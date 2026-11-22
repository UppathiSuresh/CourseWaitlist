using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using CourseWaitlist.Api;

var builder = WebApplication.CreateBuilder(args);

// Demo prerequisite records are owned by the service, never supplied in an enrollment request.
var prerequisiteCompletions = new[] { "anu", "ben", "chitra", "dev" }
    .ToFrozenSet(StringComparer.Ordinal);
builder.Services.AddSingleton(new CourseRegistration(2, prerequisiteCompletions.Contains));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => TypedResults.Redirect("/offering")).ExcludeFromDescription();
OfferingEndpoints.Map(app);
app.Run();
