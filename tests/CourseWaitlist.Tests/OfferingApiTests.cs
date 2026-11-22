using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CourseWaitlist.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CourseWaitlist.Tests;

public sealed class OfferingApiTests
{
    private const string OfferingPath = "/offering";
    private const string EnrollmentPath = $"{OfferingPath}/enrollments";

    [Fact]
    public async Task Root_RedirectsToOfferingSnapshot()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(OfferingPath, response.Headers.Location?.OriginalString);
        var state = await ReadStateAsync(client);
        Assert.Equal(2, state.Capacity);
        Assert.Equal(2, state.AvailableSeats);
        Assert.Empty(state.Enrolled);
        Assert.Empty(state.Waitlist);
    }

    [Fact]
    public async Task EnrollmentAndDrop_PreserveQueueOrderAndIdempotentOutcomes()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        await EnrollAsync(client, "anu", HttpStatusCode.OK, "enrolled");
        await EnrollAsync(client, "ben", HttpStatusCode.OK, "enrolled");
        await EnrollAsync(client, "chitra", HttpStatusCode.OK, "waitlisted");
        await EnrollAsync(client, "dev", HttpStatusCode.OK, "waitlisted");
        await EnrollAsync(client, "anu", HttpStatusCode.OK, "alreadyEnrolled");
        await EnrollAsync(client, "chitra", HttpStatusCode.OK, "alreadyWaitlisted");

        using var waitlistDrop = await client.DeleteAsync($"{EnrollmentPath}/dev");
        await AssertOutcomeAsync(waitlistDrop, HttpStatusCode.NotFound, "notEnrolled");
        using var drop = await client.DeleteAsync($"{EnrollmentPath}/anu");
        var result = await AssertOutcomeAsync(drop, HttpStatusCode.OK, "dropped");
        Assert.Equal("chitra", result.GetProperty("promotedLearnerId").GetString());
        using var repeatedDrop = await client.DeleteAsync($"{EnrollmentPath}/anu");
        await AssertOutcomeAsync(repeatedDrop, HttpStatusCode.NotFound, "notEnrolled");

        var state = await ReadStateAsync(client);
        Assert.Equal(0, state.AvailableSeats);
        Assert.Equal(new[] { "ben", "chitra" }, state.Enrolled);
        Assert.Equal(new[] { "dev" }, state.Waitlist);
    }

    [Fact]
    public async Task Enrollment_InvalidIdsAndPrerequisiteFailureReturnStableOutcomes()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var missingLearner = await client.PostAsJsonAsync(EnrollmentPath, new { });
        await AssertOutcomeAsync(missingLearner, HttpStatusCode.BadRequest, "invalidLearnerId");
        await EnrollAsync(client, "bad/id", HttpStatusCode.BadRequest, "invalidLearnerId");
        await EnrollAsync(client, "unknown", HttpStatusCode.UnprocessableEntity, "prerequisiteNotMet");
        using var invalidDrop = await client.DeleteAsync($"{EnrollmentPath}/bad%20id");
        await AssertOutcomeAsync(invalidDrop, HttpStatusCode.BadRequest, "invalidLearnerId");

        var state = await ReadStateAsync(client);
        Assert.Equal(2, state.AvailableSeats);
        Assert.Empty(state.Enrolled);
        Assert.Empty(state.Waitlist);
    }

    [Fact]
    public async Task Enrollment_ConcurrentRequestsShareTheSameSeatAndQueueState()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        await EnrollAsync(client, "anu", HttpStatusCode.OK, "enrolled");
        string[] requests = ["ben", "chitra", "dev", "ben", "chitra", "dev"];
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = requests.Select(async learner =>
        {
            await start.Task;
            using var response = await client.PostAsJsonAsync(EnrollmentPath, new { learnerId = learner });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            return result.GetProperty("status").GetString();
        }).ToArray();

        start.SetResult();
        var outcomes = await Task.WhenAll(operations);

        Assert.Single(outcomes, outcome => outcome == "enrolled");
        Assert.Single(outcomes, outcome => outcome == "alreadyEnrolled");
        Assert.Equal(2, outcomes.Count(outcome => outcome == "waitlisted"));
        Assert.Equal(2, outcomes.Count(outcome => outcome == "alreadyWaitlisted"));
        var state = await ReadStateAsync(client);
        Assert.Equal(0, state.AvailableSeats);
        Assert.Equal(2, state.Enrolled.Length);
        Assert.Equal(2, state.Waitlist.Length);
        Assert.Equal(new[] { "anu", "ben", "chitra", "dev" },
            state.Enrolled.Concat(state.Waitlist).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null, "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{}", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    public async Task Enrollment_InvalidRequestBodyReturnsProblemDetails(
        string? body, string contentType, HttpStatusCode expectedStatus)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType);

        using var response = await client.PostAsync(EnrollmentPath, content);

        await AssertProblemAsync(response, expectedStatus);
        var state = await ReadStateAsync(client);
        Assert.Equal(2, state.AvailableSeats);
        Assert.Empty(state.Enrolled);
        Assert.Empty(state.Waitlist);
    }

    [Fact]
    public async Task Drop_UnexpectedFailureReturnsGenericProblemAndPreservesEnrollment()
    {
        const string internalMessage = "Internal eligibility lookup failure";
        var failLookup = false;
        var course = new CourseRegistration(1, _ =>
            failLookup ? throw new InvalidOperationException(internalMessage) : true);
        course.Enroll("anu");
        course.Enroll("ben");
        failLookup = true;

        using var defaultFactory = new WebApplicationFactory<Program>();
        using var factory = defaultFactory.WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.ConfigureServices(services =>
            {
                services.RemoveAll<CourseRegistration>();
                services.AddSingleton(course);
            });
        });
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"{EnrollmentPath}/anu");

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain(internalMessage, await response.Content.ReadAsStringAsync());
        var state = await ReadStateAsync(client);
        Assert.Equal(0, state.AvailableSeats);
        Assert.Equal(new[] { "anu" }, state.Enrolled);
        Assert.Equal(new[] { "ben" }, state.Waitlist);
    }

    [Fact]
    public async Task OpenApi_DescribesTheRegistrationContract()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty(OfferingPath).TryGetProperty("get", out _));
        var enroll = paths.GetProperty(EnrollmentPath).GetProperty("post");
        Assert.True(enroll.GetProperty("requestBody").GetProperty("required").GetBoolean());
        AssertResponses(enroll, "200", "400", "415", "422");
        var drop = paths.GetProperty($"{EnrollmentPath}/{{learnerId}}").GetProperty("delete");
        AssertResponses(drop, "200", "400", "404");
    }

    private static async Task EnrollAsync(HttpClient client, string learner, HttpStatusCode code, string status)
    {
        using var response = await client.PostAsJsonAsync(EnrollmentPath, new { learnerId = learner });
        await AssertOutcomeAsync(response, code, status);
    }

    private static async Task<JsonElement> AssertOutcomeAsync(HttpResponseMessage response, HttpStatusCode code, string status)
    {
        Assert.Equal(code, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, result.GetProperty("status").GetString());
        return result;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode code)
    {
        Assert.Equal(code, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)code, problem.GetProperty("status").GetInt32());
    }

    private static async Task<OfferingState> ReadStateAsync(HttpClient client) =>
        Assert.IsType<OfferingState>(await client.GetFromJsonAsync<OfferingState>(OfferingPath));

    private static void AssertResponses(JsonElement operation, params string[] statusCodes)
    {
        var responses = operation.GetProperty("responses");
        foreach (var statusCode in statusCodes)
        {
            Assert.True(responses.TryGetProperty(statusCode, out _), $"Missing OpenAPI response {statusCode}.");
        }
    }
}
