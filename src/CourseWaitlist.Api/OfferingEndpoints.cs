using Microsoft.AspNetCore.Http.HttpResults;

namespace CourseWaitlist.Api;

internal static class OfferingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var offering = endpoints.MapGroup("/offering").WithTags("Course offering");

        offering.MapGet("", (CourseRegistration course) => TypedResults.Ok(course.GetState()))
            .WithName("GetOffering")
            .WithSummary("Get available seats, enrolled learners, and the FIFO waitlist.");

        offering.MapPost("/enrollments", Enroll)
            .WithName("RequestEnrollment")
            .WithSummary("Enroll an eligible learner or add them to the waitlist.")
            .WithDescription(
                "Send a JSON object with learnerId. Existing enrollment and waitlist requests preserve state. " +
                "Missing or malformed JSON returns 400 as Problem Details. An invalid learnerId returns 400 as an enrollment result.")
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        offering.MapDelete("/enrollments/{learnerId}", Drop)
            .WithName("DropEnrollment")
            .WithSummary("Drop an enrollment and promote the first eligible waitlisted learner.")
            .WithDescription("Returns 404 if the learner has no active enrollment.");
    }

    private static Results<Ok<EnrollmentResult>, BadRequest<EnrollmentResult>, UnprocessableEntity<EnrollmentResult>>
        Enroll(EnrollRequest request, CourseRegistration course)
    {
        var result = course.Enroll(request.LearnerId);
        return result.Status switch
        {
            EnrollmentStatus.Enrolled or EnrollmentStatus.Waitlisted
                or EnrollmentStatus.AlreadyEnrolled or EnrollmentStatus.AlreadyWaitlisted => TypedResults.Ok(result),
            EnrollmentStatus.InvalidLearnerId => TypedResults.BadRequest(result),
            EnrollmentStatus.PrerequisiteNotMet => TypedResults.UnprocessableEntity(result),
            _ => throw new InvalidOperationException("Unexpected enrollment status.")
        };
    }

    private static Results<Ok<DropResult>, BadRequest<DropResult>, NotFound<DropResult>>
        Drop(string learnerId, CourseRegistration course)
    {
        var result = course.Drop(learnerId);
        return result.Status switch
        {
            DropStatus.Dropped => TypedResults.Ok(result),
            DropStatus.InvalidLearnerId => TypedResults.BadRequest(result),
            DropStatus.NotEnrolled => TypedResults.NotFound(result),
            _ => throw new InvalidOperationException("Unexpected drop status.")
        };
    }

    internal sealed record EnrollRequest(string? LearnerId);
}
