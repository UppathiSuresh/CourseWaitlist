using CourseWaitlist.Api;

namespace CourseWaitlist.Tests;

public sealed class CourseRegistrationTests
{
    [Fact]
    public void Enroll_TakesAvailableSeatAndRecognizesDuplicate()
    {
        var course = new CourseRegistration(2, _ => true);

        Assert.Equal(EnrollmentStatus.Enrolled, course.Enroll(" anu ").Status);
        Assert.Equal(EnrollmentStatus.AlreadyEnrolled, course.Enroll("anu").Status);
        AssertState(course, 1, ["anu"], []);
    }

    [Fact]
    public void Enroll_WhenFull_WaitlistsEachLearnerOnce()
    {
        var course = new CourseRegistration(1, _ => true);
        course.Enroll("anu");

        Assert.Equal(EnrollmentStatus.Waitlisted, course.Enroll("ben").Status);
        Assert.Equal(EnrollmentStatus.AlreadyWaitlisted, course.Enroll(" ben ").Status);
        Assert.Equal(EnrollmentStatus.AlreadyEnrolled, course.Enroll("anu").Status);
        AssertState(course, 0, ["anu"], ["ben"]);
    }

    [Fact]
    public void Drop_PromotesInInsertionOrder()
    {
        var course = new CourseRegistration(1, _ => true);
        course.Enroll("anu");
        course.Enroll("chitra");
        course.Enroll("ben");

        Assert.Equal(new DropResult(DropStatus.Dropped, "chitra"), course.Drop("anu"));
        AssertState(course, 0, ["chitra"], ["ben"]);
        Assert.Equal(new DropResult(DropStatus.Dropped, "ben"), course.Drop("chitra"));
        AssertState(course, 0, ["ben"], []);
    }

    [Fact]
    public void Enroll_RejectsIneligibleLearnerWithOrWithoutCapacity()
    {
        var course = new CourseRegistration(1, id => id == "anu");

        Assert.Equal(EnrollmentStatus.PrerequisiteNotMet, course.Enroll("ben").Status);
        AssertState(course, 1, [], []);
        course.Enroll("anu");
        Assert.Equal(EnrollmentStatus.PrerequisiteNotMet, course.Enroll("ben").Status);
        AssertState(course, 0, ["anu"], []);
    }

    [Fact]
    public void Drop_RemovesIneligibleHeadAndPreservesRemainingOrder()
    {
        var prerequisiteCompletions = new HashSet<string> { "anu", "ben", "chitra", "dev" };
        var course = new CourseRegistration(1, prerequisiteCompletions.Contains);
        course.Enroll("anu");
        course.Enroll("ben");
        course.Enroll("chitra");
        course.Enroll("dev");
        prerequisiteCompletions.Remove("ben");

        Assert.Equal(new DropResult(DropStatus.Dropped, "chitra"), course.Drop("anu"));
        AssertState(course, 0, ["chitra"], ["dev"]);
        prerequisiteCompletions.Add("ben");
        Assert.Equal(EnrollmentStatus.Waitlisted, course.Enroll("ben").Status);
        AssertState(course, 0, ["chitra"], ["dev", "ben"]);
    }

    [Fact]
    public void Drop_OpensSeatWhenNoWaitlistedLearnerQualifies()
    {
        var prerequisiteCompletions = new HashSet<string> { "anu", "ben", "chitra" };
        var course = new CourseRegistration(1, prerequisiteCompletions.Contains);
        course.Enroll("anu");
        course.Enroll("ben");
        course.Enroll("chitra");
        prerequisiteCompletions.Remove("ben");
        prerequisiteCompletions.Remove("chitra");

        Assert.Equal(new DropResult(DropStatus.Dropped), course.Drop("anu"));
        AssertState(course, 1, [], []);
    }

    [Fact]
    public void Drop_UnknownWaitlistedOrAlreadyDroppedLearnerDoesNotChangeState()
    {
        var course = new CourseRegistration(1, _ => true);
        course.Enroll("anu");
        course.Enroll("ben");
        course.Enroll("chitra");

        Assert.Equal(new DropResult(DropStatus.NotEnrolled), course.Drop("missing"));
        Assert.Equal(new DropResult(DropStatus.NotEnrolled), course.Drop("ben"));
        AssertState(course, 0, ["anu"], ["ben", "chitra"]);
        Assert.Equal(new DropResult(DropStatus.Dropped, "ben"), course.Drop("anu"));
        Assert.Equal(new DropResult(DropStatus.NotEnrolled), course.Drop("anu"));
        AssertState(course, 0, ["ben"], ["chitra"]);
    }

    [Fact]
    public void EnrollAndDrop_RejectInvalidIdsWithoutChangingState()
    {
        var course = new CourseRegistration(1, _ => true);

        foreach (var id in new[] { null, "", " ", "bad/id", "a b", new string('a', 65) })
        {
            Assert.Equal(EnrollmentStatus.InvalidLearnerId, course.Enroll(id).Status);
            Assert.Equal(DropStatus.InvalidLearnerId, course.Drop(id).Status);
        }

        AssertState(course, 1, [], []);
    }

    [Fact]
    public void Drop_LeavesEnrollmentAndQueueIntactIfEligibilityLookupFailsMidScan()
    {
        var failLookup = false;
        var course = new CourseRegistration(1, id =>
        {
            if (failLookup && id == "chitra")
            {
                throw new InvalidOperationException("Lookup failed.");
            }

            return !failLookup || id != "ben";
        });
        course.Enroll("anu");
        course.Enroll("ben");
        course.Enroll("chitra");
        failLookup = true;

        Assert.Throws<InvalidOperationException>(() => course.Drop("anu"));
        AssertState(course, 0, ["anu"], ["ben", "chitra"]);
        failLookup = false;
        Assert.Equal(new DropResult(DropStatus.Dropped, "ben"), course.Drop("anu"));
        AssertState(course, 0, ["ben"], ["chitra"]);
    }

    [Fact]
    public async Task Enroll_ConcurrentRequestsPreserveCapacityAndUniqueness()
    {
        const int learnerCount = 16;
        var course = new CourseRegistration(1, _ => true);
        var outcomes = new EnrollmentStatus[learnerCount * 2];
        var actions = Enumerable.Range(0, outcomes.Length)
            .Select(index => (Action)(() => outcomes[index] = course.Enroll($"learner-{index % learnerCount}").Status));

        await RunTogetherAsync(actions);

        var state = course.GetState();
        Assert.Equal(0, state.AvailableSeats);
        Assert.Single(outcomes, status => status == EnrollmentStatus.Enrolled);
        Assert.Equal(learnerCount - 1, outcomes.Count(status => status == EnrollmentStatus.Waitlisted));
        Assert.Single(state.Enrolled);
        Assert.Equal(learnerCount - 1, state.Waitlist.Length);
        Assert.Equal(learnerCount, state.Enrolled.Concat(state.Waitlist).Distinct().Count());
    }

    [Fact]
    public async Task Drop_ConcurrentEnrollmentCannotOvertakeTheQueue()
    {
        var course = new CourseRegistration(1, _ => true);
        course.Enroll("anu");
        course.Enroll("ben");
        DropResult? drop = null;
        EnrollmentResult? enrollment = null;

        await RunTogetherAsync([() => drop = course.Drop("anu"), () => enrollment = course.Enroll("chitra")]);

        Assert.Equal(new DropResult(DropStatus.Dropped, "ben"), drop);
        Assert.Equal(new EnrollmentResult(EnrollmentStatus.Waitlisted), enrollment);
        AssertState(course, 0, ["ben"], ["chitra"]);
    }

    [Fact]
    public void GetState_ReturnsIndependentSnapshots()
    {
        var course = new CourseRegistration(1, _ => true);
        course.Enroll("anu");
        course.Enroll("ben");
        var snapshot = course.GetState();

        snapshot.Enrolled[0] = "changed";
        snapshot.Waitlist[0] = "changed";

        AssertState(course, 0, ["anu"], ["ben"]);
    }

    private static void AssertState(CourseRegistration course, int seats, string[] enrolled, string[] waitlist)
    {
        var state = course.GetState();
        Assert.Equal(seats, state.AvailableSeats);
        Assert.Equal(state.Capacity, state.AvailableSeats + state.Enrolled.Length);
        Assert.Equal(enrolled, state.Enrolled);
        Assert.Equal(waitlist, state.Waitlist);
    }

    private static Task RunTogetherAsync(IEnumerable<Action> actions)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = actions.Select(async action =>
        {
            await start.Task;
            action();
        }).ToArray();

        start.SetResult();
        return Task.WhenAll(tasks);
    }
}
