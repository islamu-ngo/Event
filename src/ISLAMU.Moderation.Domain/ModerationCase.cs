namespace ISLAMU.Moderation.Domain;

public enum ModerationCaseState
{
    Open = 1,
    Closed = 2
}

/// <summary>An allegation occurrence. Opening it creates no finding or restriction.</summary>
public sealed class ModerationCase
{
    private ModerationCase(Guid id, SubjectReference subject, AuthorityScope scope, Guid occurrenceId, DateTimeOffset now)
    {
        Id = id;
        Subject = subject;
        Scope = scope;
        OccurrenceId = occurrenceId;
        CreatedAt = now.ToUniversalTime();
        UpdatedAt = CreatedAt;
        State = ModerationCaseState.Open;
    }

    public Guid Id { get; }
    public SubjectReference Subject { get; }
    public AuthorityScope Scope { get; }
    public Guid OccurrenceId { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ModerationCaseState State { get; private set; }
    public long Revision { get; private set; } = 1;

    public static ModerationCase Open(
        Guid id, SubjectReference subject, AuthorityScope scope, Guid occurrenceId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        if (id == Guid.Empty || occurrenceId == Guid.Empty || subject.TrustDomainId != scope.TrustDomainId)
        {
            throw new ArgumentException("A qualified allegation and distinct occurrence are required.", nameof(id));
        }

        return new ModerationCase(id, subject, scope, occurrenceId, now);
    }

    public void Close(long expectedRevision, DateTimeOffset now)
    {
        if (expectedRevision != Revision || State != ModerationCaseState.Open || now < UpdatedAt)
        {
            throw new InvalidOperationException("The case is not current and open.");
        }

        var nextRevision = checked(Revision + 1);
        State = ModerationCaseState.Closed;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }
}
