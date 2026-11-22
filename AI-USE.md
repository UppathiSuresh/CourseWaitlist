# AI-use record

I used Codex to work through the assessment, implement the API, and put together the tests. I ran the app in Visual Studio, reported the 404 on the startup page, and used Postman to try the enrollment endpoint.

## What I asked Codex

These excerpts cover the engineering prompts.

- “which is good service or command line” — Codex suggested ASP.NET Core and built one in-memory offering. The registration rules stayed in `CourseRegistration`, where they can be tested directly.
- “I ran this app in visual studio getting the above error” — Codex checked the startup log and the running routes. It added the root redirect after confirming that `/offering` already returned the correct JSON.
- “can you run and create few records, I want to test all the apis once” — Codex sent requests with the sample IDs and confirmed that dropping Anu promoted Chitra ahead of Dev.
- “make it production grade” — The next pass added typed responses, OpenAPI, Problem Details, and HTTP integration tests. The scope stayed at one offering with in-memory data.

## Decisions and checks

The promotion rule removes ineligible entries it passes. Someone who regains eligibility can reapply at the back. Tests check that rule and show that an eligibility failure halfway through scanning leaves both the enrollment and the queue intact.

The first pass used a small test runner after NuGet failed to restore xUnit. Codex replaced it with xUnit and `WebApplicationFactory` once restore worked. The API kept its existing enrollment and drop outcome strings. Framework errors such as malformed JSON got Problem Details responses. The package audit flagged an indirect OpenAPI dependency, so Codex pinned the patched version and checked the generated contract again.

Codex ran `dotnet test -c Release` from a clean extraction and all 21 tests passed. It also ran the README walkthrough against the local server. The final snapshot showed Ben and Chitra enrolled, Dev waiting, and zero seats available. The NuGet audit, including transitive packages, reported no known vulnerable packages at the time of the check.
