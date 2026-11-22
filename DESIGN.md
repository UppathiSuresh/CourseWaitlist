# Design notes

## Service boundary

One `CourseRegistration` instance holds the enrollments and waitlist. The HTTP handlers translate requests and results. The demo starts with two empty seats and a fixed set of learner IDs that have completed the prerequisite.

## HTTP contract

| Method and path | Outcomes |
| --- | --- |
| `GET /` | `302` to `/offering`. |
| `GET /offering` | `200` with `capacity`, `availableSeats`, `enrolled`, and `waitlist`. Enrollment IDs are sorted for display. The waitlist is in queue order. |
| `POST /offering/enrollments` | A JSON object with `learnerId`. `200` with `enrolled`, `waitlisted`, `alreadyEnrolled`, or `alreadyWaitlisted`. `400` with `invalidLearnerId` for an invalid or omitted ID. `422` with `prerequisiteNotMet` for a valid ID without prerequisite completion. |
| `DELETE /offering/enrollments/{learnerId}` | `200` with `dropped` and `promotedLearnerId`, which is null if nobody is promoted. `400` with `invalidLearnerId` for an invalid ID. `404` with `notEnrolled` if the learner has no active enrollment. |
| `GET /openapi/v1.json` | The generated OpenAPI document in Development. |

Application outcomes use a JSON `status` string. IDs are trimmed, case-sensitive, and limited to 1–64 ASCII letters, digits, hyphens, or underscores. A missing or malformed JSON body returns `400`. An unsupported request media type returns `415`. Request-format failures use `application/problem+json` for clients that accept it. Unexpected errors return a generic `500` Problem Details response with diagnostic information in server logs.

## Seat and queue rules

- Available seats equal capacity minus the number of enrolled learners. Each ID can appear at most once across the two collections.
- Repeating an enrollment request returns the current status without moving the learner in the queue or consuming another seat.
- When a seat opens, the service rechecks eligibility in queue order. It removes ineligible entries it passes and promotes the first eligible learner. If nobody qualifies, the seat stays open. A removed learner can reapply at the back of the queue if they regain eligibility.
- A drop applies to an active enrollment. Requesting a drop for someone who is waiting, unknown, or already dropped leaves both collections intact.

## Why one lock

If dropping and promoting were separate operations, a new enrollment could grab the seat in between. Both run under the same lock as enrollment and snapshot reads. When requests overlap, queue order is the order in which they are inserted under that lock.

The service finds the promotion candidate before changing either collection. If the prerequisite lookup fails halfway through the scan, the enrolled learner is still enrolled and nobody has lost their place. The lookup is local and synchronous. The waitlist is a list, so checking for a duplicate or scanning for promotion is linear in queue length. For one offering, the list is straightforward to inspect and test.

## Deployment limits

Restarting the process clears the data. Running multiple instances would require shared storage, uniqueness constraints, and a transaction joining the capacity check, queue update, and promotion. A deployed service would also need authenticated learner identity and authorization for writes and snapshot access.

A delete targets the learner's current enrollment. If a learner re-enrolls before a delayed retry arrives, that retry could drop the new enrollment. An enrollment ID or version check would distinguish the two.

## Verification

The [unit tests](tests/CourseWaitlist.Tests/CourseRegistrationTests.cs) cover the seat and queue rules, including eligibility changes between joining and promotion. The [HTTP tests](tests/CourseWaitlist.Tests/OfferingApiTests.cs) send requests through ASP.NET Core. They check contention for the last seat, the final snapshot, and how malformed requests and an unexpected lookup failure appear to a caller.
