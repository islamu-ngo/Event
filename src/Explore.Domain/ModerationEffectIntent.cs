namespace Explore.Domain;

/// <summary>A qualified committed change awaiting native consequence planning; delivery and evidence custody are separate.</summary>
public sealed class ModerationEffectIntent
{
    private ModerationEffectIntent()
    {
    }

    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid DecisionId { get; private set; }
    public long DecisionRevision { get; private set; }
    public Guid? SanctionId { get; private set; }
    public long? SanctionRevision { get; private set; }
    public Guid? SupersededSanctionId { get; private set; }
    public long? SupersededSanctionRevision { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public int ScopeKind { get; private set; }
    public Guid ScopeId { get; private set; }
    public int SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public int OperationKind { get; private set; }
    public long PolicyRevision { get; private set; }
    public long QualificationRevision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long Revision { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTimeOffset? LeaseStartedAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    // These coordinates belong to this existing lifecycle ledger, not a new retained record.
    // They confer neither disclosure nor notification nor consequence-completion authority,
    // and must never extend the independently governed lifetime of this ledger.
    public Guid? OriginalNoticeCleanupId { get; private set; }
    public Guid? OriginalNoticeHostBindingId { get; private set; }
    public Guid? OriginalNoticeWriterTenantId { get; private set; }
    public DateTimeOffset? OriginalNoticeRetainUntil { get; private set; }
    public DateTimeOffset? OriginalNoticePurgedAt { get; private set; }

    // This exact completed transition shares the existing effect ledger's lifetime.
    // It retains no original private snapshot, proof digest or longer custody purpose.
    public Guid? OriginalCompletionId { get; private set; }
    public Guid? OriginalCompletionHostBindingId { get; private set; }
    public Guid? OriginalCompletionWriterTenantId { get; private set; }
    public long? OriginalCompletionRevision { get; private set; }

    public static ModerationEffectIntent Create(
        Guid id, Guid operationId, Guid decisionId, long decisionRevision, Guid? sanctionId, long? sanctionRevision,
        Guid trustDomainId, int scopeKind, Guid scopeId, int subjectKind, Guid subjectId, int operationKind,
        long policyRevision, long qualificationRevision, DateTimeOffset now,
        Guid? supersededSanctionId, long? supersededSanctionRevision)
    {
        if (id == Guid.Empty || operationId == Guid.Empty || decisionId == Guid.Empty
            || trustDomainId == Guid.Empty || scopeId == Guid.Empty || subjectId == Guid.Empty
            || decisionRevision < 1 || policyRevision < 1 || qualificationRevision < 1
            || sanctionId == Guid.Empty || (sanctionId is null) != (sanctionRevision is null)
            || (sanctionRevision is not null && sanctionRevision < 1)
            || supersededSanctionId == Guid.Empty || (supersededSanctionId is null) != (supersededSanctionRevision is null)
            || (supersededSanctionRevision is not null && supersededSanctionRevision < 1)
            || (operationKind == 10) != (supersededSanctionId is not null)
            || supersededSanctionId is not null && (sanctionId is null || supersededSanctionId == sanctionId)
            || scopeKind is < 1 or > 6 || subjectKind is < 1 or > 4 || operationKind is < 1 or > 10)
        {
            throw new ArgumentException("A durable effect requires an exact qualified changed aggregate.", nameof(id));
        }

        return new ModerationEffectIntent
        {
            Id = id,
            OperationId = operationId,
            DecisionId = decisionId,
            DecisionRevision = decisionRevision,
            SanctionId = sanctionId,
            SanctionRevision = sanctionRevision,
            SupersededSanctionId = supersededSanctionId,
            SupersededSanctionRevision = supersededSanctionRevision,
            TrustDomainId = trustDomainId,
            ScopeKind = scopeKind,
            ScopeId = scopeId,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            OperationKind = operationKind,
            PolicyRevision = policyRevision,
            QualificationRevision = qualificationRevision,
            CreatedAt = now.ToUniversalTime(),
            Revision = 1
        };
    }

    /// <summary>Claims an uncompleted intent, including recovery at an expired owner's boundary.</summary>
    public void Claim(Guid leaseId, long expectedRevision, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        ValidatePersistedState();
        if (leaseId == Guid.Empty || leaseId.Version != 7 || leaseId.Variant is < 8 or > 11 || expiresAt <= now)
            throw new ArgumentException("A claim requires a native fencing identity and a positive lease.", nameof(leaseId));
        if (Revision != expectedRevision || CompletedAt is not null || now < CreatedAt
            || LeaseId == leaseId || LeaseExpiresAt > now)
            throw new InvalidOperationException("The effect intent is already owned or no longer current.");

        var nextRevision = checked(Revision + 1);
        LeaseId = leaseId;
        LeaseStartedAt = now.ToUniversalTime();
        LeaseExpiresAt = expiresAt.ToUniversalTime();
        Revision = nextRevision;
    }

    /// <summary>Completes only the currently owned, unexpired lease; delivery is a separate receipt.</summary>
    public void Complete(Guid leaseId, long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || CompletedAt is not null || leaseId != LeaseId || LeaseId is null
            || now < LeaseStartedAt || now >= LeaseExpiresAt)
        {
            throw new InvalidOperationException("The effect lease is no longer current.");
        }

        var nextRevision = checked(Revision + 1);
        CompletedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    /// <summary>
    /// Records the exact original owner's completed transition in the same write.
    /// This permits later acknowledgement, never completion of unfinished work.
    /// </summary>
    public void RecordOriginalCompletionAcknowledgement(ModerationOriginalEffectCompletionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidatePersistedState();
        receipt.ValidatePersistedState();
        if (OriginalCompletionId is not null || OriginalNoticeCleanupId is not null
            || !receipt.MatchesCompletion(this))
            throw new InvalidOperationException("Acknowledgement requires this exact original completion transition.");
        OriginalCompletionId = receipt.Id;
        OriginalCompletionHostBindingId = receipt.HostBindingId;
        OriginalCompletionWriterTenantId = receipt.TenantId;
        OriginalCompletionRevision = Revision;
    }

    /// <summary>
    /// Recognizes only an already committed original transition under its original host/writer.
    /// The current native reader must independently qualify maintenance access.
    /// </summary>
    public bool HasOriginalCompletionAcknowledgement(Guid hostBindingId, Guid writerTenantId)
    {
        ValidatePersistedState();
        return OriginalCompletionId is not null && OriginalCompletionHostBindingId == hostBindingId
            && OriginalCompletionWriterTenantId == writerTenantId;
    }

    /// <summary>Releases this exact owner without marking work complete, even after its lease expires.</summary>
    public void Release(Guid leaseId, long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || CompletedAt is not null || leaseId != LeaseId || LeaseId is null
            || now < LeaseStartedAt)
            throw new InvalidOperationException("The effect lease is no longer current.");
        var nextRevision = checked(Revision + 1);
        LeaseId = null;
        LeaseStartedAt = null;
        LeaseExpiresAt = null;
        Revision = nextRevision;
    }

    /// <summary>
    /// Records removal only after the native cleanup owner proved the complete original atomic
    /// write and staged all governed copies for deletion in the same transaction. The source is
    /// checked here; original typed capture, recipient set and operation receipt are checked by
    /// that persistence owner. No payload, hash, reviewer or target is copied into this ledger.
    /// </summary>
    public void RecordOriginalNoticeCleanup(
        ModerationNoticeSource source, Guid hostBindingId, Guid writerTenantId,
        Guid cleanupId, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidatePersistedState();
        foreach (var id in new[] { hostBindingId, writerTenantId, cleanupId })
            ModerationHostBinding.RequireUuidV7(id, nameof(cleanupId));
        if (OriginalNoticeCleanupId is not null || Revision != expectedRevision
            || !source.Matches(this) || source.RetainUntil is not { } ceiling
            || source.TenantId is { } sourceTenant && sourceTenant != writerTenantId
            || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue || now < ceiling)
            throw new InvalidOperationException("Cleanup requires the exact original source at its immutable privacy ceiling.");
        var nextRevision = checked(Revision + 1);
        OriginalNoticeCleanupId = cleanupId;
        OriginalNoticeHostBindingId = hostBindingId;
        OriginalNoticeWriterTenantId = writerTenantId;
        OriginalNoticeRetainUntil = ceiling;
        OriginalNoticePurgedAt = now;
        Revision = nextRevision;
    }

    /// <summary>
    /// Only substitutes for original capture existence in exact accepted-operation replay.
    /// It never proves actual notification, expired contestation or completed consequences.
    /// </summary>
    public bool HasOriginalNoticeCleanup(Guid hostBindingId, Guid writerTenantId, DateTimeOffset now)
    {
        ValidatePersistedState();
        return OriginalNoticeCleanupId is not null && OriginalNoticeHostBindingId == hostBindingId
            && OriginalNoticeWriterTenantId == writerTenantId && now >= OriginalNoticePurgedAt;
    }

    public void ValidatePersistedState()
    {
        if (Revision < 1 || CreatedAt.Offset != TimeSpan.Zero
            || (LeaseId is null) != (LeaseStartedAt is null) || (LeaseId is null) != (LeaseExpiresAt is null)
            || LeaseId is { } lease && (lease == Guid.Empty || lease.Version != 7 || lease.Variant is < 8 or > 11)
            || LeaseStartedAt is { } started && (started.Offset != TimeSpan.Zero || started < CreatedAt)
            || LeaseExpiresAt is { } expires && (expires.Offset != TimeSpan.Zero || expires <= LeaseStartedAt)
            || CompletedAt is { } completed && (completed.Offset != TimeSpan.Zero || LeaseId is null
                || completed < LeaseStartedAt || completed >= LeaseExpiresAt))
            throw new InvalidOperationException("The persisted effect lease is inconsistent.");
        if ((OriginalNoticeCleanupId is null) != (OriginalNoticeHostBindingId is null)
            || (OriginalNoticeCleanupId is null) != (OriginalNoticeWriterTenantId is null)
            || (OriginalNoticeCleanupId is null) != (OriginalNoticeRetainUntil is null)
            || (OriginalNoticeCleanupId is null) != (OriginalNoticePurgedAt is null)
            || OriginalNoticeRetainUntil is { } ceiling && (ceiling.Offset != TimeSpan.Zero
                || ceiling == DateTimeOffset.MaxValue || ceiling <= CreatedAt)
            || OriginalNoticePurgedAt is { } purged && (purged.Offset != TimeSpan.Zero
                || purged == DateTimeOffset.MaxValue || purged < OriginalNoticeRetainUntil || Revision < 2))
            throw new InvalidOperationException("The original notice cleanup coordinates are incomplete.");
        if (OriginalNoticeCleanupId is { } cleanupId)
        {
            ModerationHostBinding.RequireUuidV7(cleanupId, nameof(OriginalNoticeCleanupId));
            ModerationHostBinding.RequireUuidV7(OriginalNoticeHostBindingId!.Value, nameof(OriginalNoticeHostBindingId));
            ModerationHostBinding.RequireUuidV7(OriginalNoticeWriterTenantId!.Value, nameof(OriginalNoticeWriterTenantId));
        }
        if ((OriginalCompletionId is null) != (OriginalCompletionHostBindingId is null)
            || (OriginalCompletionId is null) != (OriginalCompletionWriterTenantId is null)
            || (OriginalCompletionId is null) != (OriginalCompletionRevision is null)
            || OriginalCompletionRevision is { } completedRevision
                && (CompletedAt is null || completedRevision < 3 || completedRevision > Revision))
            throw new InvalidOperationException("The original completion acknowledgement is inconsistent.");
        if (OriginalCompletionId is { } completionId)
        {
            ModerationHostBinding.RequireUuidV7(completionId, nameof(OriginalCompletionId));
            ModerationHostBinding.RequireUuidV7(OriginalCompletionHostBindingId!.Value, nameof(OriginalCompletionHostBindingId));
            ModerationHostBinding.RequireUuidV7(OriginalCompletionWriterTenantId!.Value, nameof(OriginalCompletionWriterTenantId));
        }
    }

    public override string ToString() => nameof(ModerationEffectIntent);
}
