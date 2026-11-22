namespace CourseWaitlist.Api;

public sealed class CourseRegistration(int capacity, Func<string, bool> meetsPrerequisite)
{
    private readonly int _capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly Func<string, bool> _meetsPrerequisite = meetsPrerequisite
        ?? throw new ArgumentNullException(nameof(meetsPrerequisite));
    private readonly Lock _gate = new();
    private readonly HashSet<string> _enrolled = new(StringComparer.Ordinal);
    private readonly List<string> _waitlist = [];

    public EnrollmentResult Enroll(string? learnerId)
    {
        var id = NormalizeLearnerId(learnerId);
        if (id is null)
        {
            return new(EnrollmentStatus.InvalidLearnerId);
        }

        lock (_gate)
        {
            if (_enrolled.Contains(id))
            {
                return new(EnrollmentStatus.AlreadyEnrolled);
            }

            if (_waitlist.Contains(id))
            {
                return new(EnrollmentStatus.AlreadyWaitlisted);
            }

            if (!_meetsPrerequisite(id))
            {
                return new(EnrollmentStatus.PrerequisiteNotMet);
            }

            if (_enrolled.Count < _capacity)
            {
                _enrolled.Add(id);
                return new(EnrollmentStatus.Enrolled);
            }

            _waitlist.Add(id);
            return new(EnrollmentStatus.Waitlisted);
        }
    }

    public DropResult Drop(string? learnerId)
    {
        var id = NormalizeLearnerId(learnerId);
        if (id is null)
        {
            return new(DropStatus.InvalidLearnerId);
        }

        lock (_gate)
        {
            if (!_enrolled.Contains(id))
            {
                return new(DropStatus.NotEnrolled);
            }

            // Evaluate eligibility before mutating state so a failed lookup leaves the drop intact.
            var nextIndex = _waitlist.FindIndex(idOnWaitlist => _meetsPrerequisite(idOnWaitlist));
            var promotedId = nextIndex >= 0 ? _waitlist[nextIndex] : null;

            _enrolled.Remove(id);
            if (promotedId is null)
            {
                _waitlist.Clear();
            }
            else
            {
                // Remove the promoted learner and any ineligible entries encountered before them.
                _waitlist.RemoveRange(0, nextIndex + 1);
                _enrolled.Add(promotedId);
            }

            return new(DropStatus.Dropped, promotedId);
        }
    }

    public OfferingState GetState()
    {
        lock (_gate)
        {
            return new(_capacity, _capacity - _enrolled.Count,
                [.. _enrolled.Order(StringComparer.Ordinal)], [.. _waitlist]);
        }
    }

    private static string? NormalizeLearnerId(string? learnerId)
    {
        var id = learnerId?.Trim();
        if (id is null || id.Length is < 1 or > 64)
        {
            return null;
        }

        return id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? id
            : null;
    }
}

public enum EnrollmentStatus
{
    Enrolled,
    Waitlisted,
    AlreadyEnrolled,
    AlreadyWaitlisted,
    InvalidLearnerId,
    PrerequisiteNotMet
}

public enum DropStatus
{
    Dropped,
    InvalidLearnerId,
    NotEnrolled
}

public sealed record EnrollmentResult(EnrollmentStatus Status);
public sealed record DropResult(DropStatus Status, string? PromotedLearnerId = null);
public sealed record OfferingState(int Capacity, int AvailableSeats, string[] Enrolled, string[] Waitlist);
