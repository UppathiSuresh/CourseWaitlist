# Course Waitlist

A .NET 10 Minimal API for one course offering. The demo has two seats. Eligible learners take a free seat or join the waitlist. When someone drops, the first learner in line who still meets the prerequisite takes the seat.

## Run locally

From the solution directory, run the following with the .NET 10 SDK. The first restore needs access to NuGet.

```sh
dotnet test -c Release
dotnet run --project src/CourseWaitlist.Api --no-launch-profile -- --urls http://127.0.0.1:5080 --environment Development
```

Open `http://127.0.0.1:5080/offering` for the current seat count and queue. To try the API in Postman, import the OpenAPI document at `http://127.0.0.1:5080/openapi/v1.json` while the app is running in Development.

## Try the waitlist

Start with a fresh instance. Anu and Ben take the two seats, then Chitra and Dev join the waitlist in that order. Dropping Anu should promote Chitra and leave Dev first in line. Run the [sample HTTP requests](src/CourseWaitlist.Api/CourseWaitlist.Api.http) in order, or use PowerShell from a second terminal.

```powershell
$offering = 'http://127.0.0.1:5080/offering'
foreach ($learner in 'anu', 'ben', 'chitra', 'dev') {
    Invoke-RestMethod "$offering/enrollments" -Method Post -ContentType 'application/json' `
        -Body (@{ learnerId = $learner } | ConvertTo-Json -Compress)
}
Invoke-RestMethod "$offering/enrollments/anu" -Method Delete
Invoke-RestMethod $offering
```

The final snapshot should show Ben and Chitra enrolled, Dev waiting, and zero available seats. Restart the app to reset the demo. If you start it with Visual Studio, use the base URL shown in the startup log.

## Review the implementation

The enrollment rules live in [CourseRegistration.cs](src/CourseWaitlist.Api/CourseRegistration.cs). The key test is what happens when a new request arrives just as a seat opens. The drop and promotion share a lock so the new request cannot take the seat ahead of the waitlist. See the [design notes](DESIGN.md) and [AI-use record](AI-USE.md) for the tradeoffs and verification.
