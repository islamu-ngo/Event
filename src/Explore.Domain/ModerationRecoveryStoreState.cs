namespace Explore.Domain;

/// <summary>
/// Writer-owned coordinates for the canonical moderation store. A position records an atomic
/// native mutation boundary, not a backup, replay payload, reconciliation or activation verdict.
/// </summary>
public sealed class ModerationRecoveryStoreState
{
    private ModerationRecoveryStoreState()
    {
    }

    public Guid Id { get; private set; }
    public long Revision { get; private set; }
    public long Generation { get; private set; }
    public long Position { get; private set; }

    public static ModerationRecoveryStoreState Start(Guid id)
    {
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        return new() { Id = id, Revision = 1, Generation = 1, Position = 0 };
    }

    public void Advance(long expectedRevision, long expectedPosition)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || Position != expectedPosition)
            throw new InvalidOperationException("Canonical recovery advancement requires the exact current coordinates.");
        var revision = checked(Revision + 1);
        var position = checked(Position + 1);
        Revision = revision;
        Position = position;
    }

    /// <summary>
    /// Changes the lineage only at the exact owned recovery boundary. Persistence must retain the
    /// writer fence and authenticate the recovery executor; this transition grants no permission.
    /// </summary>
    public void BeginRecoveryGeneration(long expectedRevision, long expectedGeneration, long expectedPosition)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || Generation != expectedGeneration || Position != expectedPosition)
            throw new InvalidOperationException("Recovery lineage requires the exact current store coordinates.");
        var revision = checked(Revision + 1);
        var generation = checked(Generation + 1);
        Revision = revision;
        Generation = generation;
    }

    public void ValidatePersistedState()
    {
        if (Id == Guid.Empty || Id.Version != 7 || Revision < 1 || Generation < 1 || Position < 0
            || Generation > Revision || Position >= Revision || Generation > Revision - Position)
            throw new InvalidOperationException("The canonical moderation recovery coordinates are invalid.");
    }
}
