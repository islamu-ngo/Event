namespace Explore.Domain;

public enum ConfigurationManagedApplyScheduleStatus
{
    AwaitingReview = 1,
    Approved = 2,
    Applied = 3,
    Cancelled = 4,
    Stale = 5
}

public sealed class ConfigurationManagedApplySchedule
{
    private ConfigurationManagedApplySchedule()
    {
    }

    public Guid Id { get; private set; }
    public string TargetAuthorityKey { get; private set; } = string.Empty;
    public string ArtifactDigest { get; private set; } = string.Empty;
    public string TargetRevisionDigest { get; private set; } = string.Empty;
    public string ManagedPlanDigest { get; private set; } = string.Empty;
    public Guid UploadedBy { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public Guid? AppliedBy { get; private set; }
    public DateTime ApplyNotBefore { get; private set; }
    public DateTime ApplyBefore { get; private set; }
    public ConfigurationManagedApplyScheduleStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int Revision { get; private set; }

    public static ConfigurationManagedApplySchedule Create(
        Guid id,
        string targetAuthorityKey,
        string artifactDigest,
        string targetRevisionDigest,
        string managedPlanDigest,
        Guid uploadedBy,
        DateTime applyNotBefore,
        DateTime applyBefore,
        DateTime createdAt)
    {
        if (id == Guid.Empty || id.Version != 7)
            throw new ArgumentOutOfRangeException(nameof(id));
        RequireActor(uploadedBy, nameof(uploadedBy));
        RequireUtc(applyNotBefore, nameof(applyNotBefore));
        RequireUtc(applyBefore, nameof(applyBefore));
        RequireUtc(createdAt, nameof(createdAt));
        if (applyNotBefore < createdAt
            || applyBefore <= applyNotBefore
            || applyBefore > createdAt.AddDays(30))
        {
            throw new ArgumentOutOfRangeException(nameof(applyBefore));
        }
        return new ConfigurationManagedApplySchedule
        {
            Id = id,
            TargetAuthorityKey = Normalize(targetAuthorityKey),
            ArtifactDigest = Digest(artifactDigest),
            TargetRevisionDigest = Digest(targetRevisionDigest),
            ManagedPlanDigest = Digest(managedPlanDigest),
            UploadedBy = uploadedBy,
            ApplyNotBefore = applyNotBefore,
            ApplyBefore = applyBefore,
            Status = ConfigurationManagedApplyScheduleStatus.AwaitingReview,
            CreatedAt = createdAt,
            Revision = 1
        };
    }

    public void Approve(Guid reviewerUserId, DateTime occurredAt)
    {
        RequireUtc(occurredAt, nameof(occurredAt));
        RequireActor(reviewerUserId, nameof(reviewerUserId));
        if (Status != ConfigurationManagedApplyScheduleStatus.AwaitingReview
            || reviewerUserId == UploadedBy
            || occurredAt >= ApplyBefore)
        {
            throw new InvalidOperationException(
                "Managed schedule review is unavailable.");
        }
        ReviewedBy = reviewerUserId;
        Status = ConfigurationManagedApplyScheduleStatus.Approved;
        Revision = checked(Revision + 1);
    }

    public void Apply(
        Guid applierUserId,
        string currentTargetRevisionDigest,
        DateTime occurredAt)
    {
        RequireUtc(occurredAt, nameof(occurredAt));
        RequireActor(applierUserId, nameof(applierUserId));
        if (Status != ConfigurationManagedApplyScheduleStatus.Approved
            || !ReviewedBy.HasValue
            || applierUserId == UploadedBy
            || applierUserId == ReviewedBy
            || occurredAt < ApplyNotBefore
            || occurredAt >= ApplyBefore)
        {
            throw new InvalidOperationException(
                "Managed schedule apply is unavailable.");
        }
        if (!string.Equals(
                TargetRevisionDigest,
                Digest(currentTargetRevisionDigest),
                StringComparison.Ordinal))
        {
            Status = ConfigurationManagedApplyScheduleStatus.Stale;
            CompletedAt = occurredAt;
            Revision = checked(Revision + 1);
            return;
        }
        AppliedBy = applierUserId;
        Status = ConfigurationManagedApplyScheduleStatus.Applied;
        CompletedAt = occurredAt;
        Revision = checked(Revision + 1);
    }

    public void Cancel(DateTime occurredAt)
    {
        RequireUtc(occurredAt, nameof(occurredAt));
        if (Status is ConfigurationManagedApplyScheduleStatus.Applied
            or ConfigurationManagedApplyScheduleStatus.Stale)
        {
            throw new InvalidOperationException(
                "Completed managed schedules cannot be cancelled.");
        }
        Status = ConfigurationManagedApplyScheduleStatus.Cancelled;
        CompletedAt = occurredAt;
        Revision = checked(Revision + 1);
    }

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        return normalized.Length <= 200
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static string Digest(string value)
    {
        if (value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("Lowercase SHA-256 digest required.");
        }
        return value;
    }

    private static void RequireActor(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", parameterName);
    }
}
