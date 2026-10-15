using System.Security.Cryptography;
using System.Text.Json;

namespace Explore.Domain;

public enum ModerationRecognitionPendingState
{
    Received = 1, Computed = 2, AuthorityBound = 3, EscalationRequired = 4, LawfulDispositionRequired = 5,
    NoLongerRequired = 6
}

public enum ModerationRecognitionPendingFailure
{
    None = 0, KeyUnavailable = 1, StoreUnavailable = 2, SourceUnavailable = 3,
    IncompleteMaterial = 4, OriginalMaterialChanged = 5
}

/// <summary>
/// A received privacy request is not an erasure fact. Its original owner and finite deadline
/// survive retries. Only complete immutable computed rows produce its material receipt.
/// Authority binding records settlement ordering, never replacement-account permission.
/// </summary>
public sealed class ModerationRecognitionPending
{
    private ModerationRecognitionPending() { }
    private readonly List<ModerationRecognitionCaptureItem> material = [];
    public Guid IntentId { get; private set; }
    public Guid SubjectId { get; private set; }
    public int PolicyVersion { get; private set; }
    public PrivacyErasureReasonCode ReasonCode { get; private set; }
    public Guid PrivacyOwnerHumanId { get; private set; }
    public Guid RecoveryOwnerHumanId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset SettleBy { get; private set; }
    public DateTimeOffset EscalateAt { get; private set; }
    public byte[] ReceiptHash { get; private set; } = [];
    public DateTimeOffset ReceiptExpiresAt { get; private set; }
    public long Revision { get; private set; }
    public ModerationRecognitionPendingState State { get; private set; }
    public ModerationRecognitionPendingFailure Failure { get; private set; }
    public int MaterialCount { get; private set; }
    public string? MaterialReceipt { get; private set; }
    public DateTimeOffset? ComputedAt { get; private set; }
    public long? AuthoritySequence { get; private set; }
    public IReadOnlyList<ModerationRecognitionCaptureItem> Material => material.AsReadOnly();
    public bool HasCompleteMaterial => MaterialCount > 0 && MaterialCount == material.Count
        && MaterialReceipt is not null && MaterialReceipt == ComputeReceipt(material);

    public static ModerationRecognitionPending Receive(Guid intentId, Guid subjectId, int policyVersion,
        PrivacyErasureReasonCode reasonCode, Guid privacyOwner, Guid recoveryOwner,
        DateTimeOffset receivedAt, DateTimeOffset settleBy, DateTimeOffset escalateAt,
        byte[] receiptHash, DateTimeOffset receiptExpiresAt)
    {
        ModerationHostBinding.RequireUuidV7(intentId, nameof(intentId));
        if (subjectId == Guid.Empty || policyVersion < 1 || !Enum.IsDefined(reasonCode)
            || privacyOwner == Guid.Empty || recoveryOwner == Guid.Empty || privacyOwner == recoveryOwner
            || receivedAt.Offset != TimeSpan.Zero || receivedAt == DateTimeOffset.MinValue
            || settleBy.Offset != TimeSpan.Zero || settleBy <= receivedAt || settleBy == DateTimeOffset.MaxValue
            || escalateAt.Offset != TimeSpan.Zero || escalateAt <= receivedAt || escalateAt >= settleBy
            || receiptHash is not { Length: 32 } || receiptExpiresAt.Offset != TimeSpan.Zero
            || receiptExpiresAt <= receivedAt || receiptExpiresAt == DateTimeOffset.MaxValue)
            throw new ArgumentException("A received request requires original identity, owners and finite lawful deadlines.");
        return new()
        {
            IntentId = intentId, SubjectId = subjectId, PolicyVersion = policyVersion, ReasonCode = reasonCode,
            PrivacyOwnerHumanId = privacyOwner, RecoveryOwnerHumanId = recoveryOwner,
            ReceivedAt = receivedAt, SettleBy = settleBy, EscalateAt = escalateAt,
            ReceiptHash = receiptHash.ToArray(), ReceiptExpiresAt = receiptExpiresAt,
            Revision = 1, State = ModerationRecognitionPendingState.Received
        };
    }

    public void Capture(IEnumerable<ModerationRecognitionCaptureItem> items, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        RequireTime(now);
        if (MaterialReceipt is not null || AuthoritySequence is not null || now >= SettleBy)
            throw new InvalidOperationException("Original computed material cannot be replaced or captured after its lawful deadline.");
        var rows = items.ToArray();
        if (rows.Length == 0 || rows.Any(row => row is null))
            throw new ArgumentException("A marker or empty set cannot receipt capture.");
        foreach (var row in rows) row.ValidatePersistedState();
        if (rows.Any(row => row.IntentId != IntentId || row.ErasureSubjectId != SubjectId || row.CapturedAt != now
                || row.ParentCaptureItemId is null && row.RetainUntil < SettleBy || row.DroppedAt is not null)
            || rows.Any(row => row.TrustDomainId != rows[0].TrustDomainId || row.KeyId != rows[0].KeyId
                || row.KeyGeneration != rows[0].KeyGeneration || row.KeyVerificationTag != rows[0].KeyVerificationTag
                || row.RingGeneration != rows[0].RingGeneration || row.SerializationVersion != rows[0].SerializationVersion)
            || rows.Select(row => row.Id).Distinct().Count() != rows.Length
            || rows.Select(row => (row.ProviderKind, row.Purpose, row.Digest, row.SanctionId, row.DecisionId))
                .Distinct().Count() != rows.Length)
            throw new ArgumentException("Capture requires unique complete original request-bound material.");
        var receipt = ComputeReceipt(rows);
        var revision = NextRevision();
        material.AddRange(rows);
        MaterialCount = rows.Length;
        MaterialReceipt = receipt;
        ComputedAt = now;
        Failure = ModerationRecognitionPendingFailure.None;
        State = DeadlineState(now);
        Revision = revision;
    }

    public void BindAuthority(PrivacyErasureIntent fact, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fact);
        RequireTime(now);
        if (!HasCompleteMaterial || fact.IntentId != IntentId || fact.SubjectId != SubjectId
            || fact.SubjectKind != PrivacyErasureSubjectKind.User || fact.PolicyVersion != PolicyVersion
            || fact.ReasonCode != ReasonCode || fact.IsLegalHoldPseudonymized
            || fact.RecordedAtUtc < ComputedAt!.Value.UtcDateTime
            || fact.RecordedAtUtc > now.UtcDateTime
            || AuthoritySequence is { } sequence && sequence != fact.AuthoritySequence)
            throw new InvalidOperationException("Authority settlement requires the original complete computed receipt and exact request.");
        if (AuthoritySequence == fact.AuthoritySequence) return;
        var revision = NextRevision();
        AuthoritySequence = fact.AuthoritySequence;
        State = ModerationRecognitionPendingState.AuthorityBound;
        Failure = ModerationRecognitionPendingFailure.None;
        Revision = revision;
    }

    public void ObserveFailure(ModerationRecognitionPendingFailure failure, DateTimeOffset now)
    {
        RequireTime(now);
        if (failure == ModerationRecognitionPendingFailure.None || !Enum.IsDefined(failure))
            throw new ArgumentException("An unsettled disposition requires an explicit bounded failure.");
        if (AuthoritySequence is not null)
            throw new InvalidOperationException("A settled authority receipt cannot become an unsettled request.");
        var revision = NextRevision();
        Failure = failure;
        State = DeadlineState(now);
        Revision = revision;
    }

    /// <summary>
    /// A complete computed receipt still needs authority settlement by the original deadline.
    /// Clock observation alone advances the recovery obligation; it invents neither a failure
    /// cause nor erasure completion. Retries and an earlier clock cannot downgrade that obligation.
    /// </summary>
    public void AdvanceDeadline(DateTimeOffset now)
    {
        var next = StateAt(now);
        if (next == State) return;
        var revision = NextRevision();
        State = next;
        Revision = revision;
    }

    /// <summary>Reports an elapsed obligation without mutating the persisted receipt.</summary>
    public ModerationRecognitionPendingState StateAt(DateTimeOffset now)
    {
        RequireTime(now);
        return AuthoritySequence is not null ? State : DeadlineState(now);
    }

    /// <summary>
    /// Records an independently requalified empty current inventory. This is explicitly not a
    /// computed-material receipt; a native caller must prove no qualified contribution survives.
    /// </summary>
    public void BindNoLongerRequired(PrivacyErasureIntent fact, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fact);
        RequireTime(now);
        if (MaterialReceipt is not null || material.Count != 0 || fact.IntentId != IntentId
            || fact.SubjectId != SubjectId || fact.SubjectKind != PrivacyErasureSubjectKind.User
            || fact.PolicyVersion != PolicyVersion || fact.ReasonCode != ReasonCode || fact.IsLegalHoldPseudonymized
            || fact.RecordedAtUtc < ReceivedAt.UtcDateTime || fact.RecordedAtUtc > now.UtcDateTime
            || AuthoritySequence is { } sequence && sequence != fact.AuthoritySequence)
            throw new InvalidOperationException("An empty disposition must bind only the exact independently qualified request.");
        if (AuthoritySequence == fact.AuthoritySequence) return;
        var revision = NextRevision();
        AuthoritySequence = fact.AuthoritySequence;
        State = ModerationRecognitionPendingState.NoLongerRequired;
        Failure = ModerationRecognitionPendingFailure.None;
        Revision = revision;
    }

    public bool Authenticates(ReadOnlySpan<byte> hash, DateTimeOffset now) =>
        now >= ReceivedAt && now < ReceiptExpiresAt && hash.Length == 32
        && CryptographicOperations.FixedTimeEquals(hash, ReceiptHash);

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(IntentId, nameof(IntentId));
        if (SubjectId == Guid.Empty || PolicyVersion < 1 || !Enum.IsDefined(ReasonCode)
            || PrivacyOwnerHumanId == Guid.Empty || RecoveryOwnerHumanId == Guid.Empty
            || PrivacyOwnerHumanId == RecoveryOwnerHumanId || ReceivedAt == DateTimeOffset.MinValue
            || ReceivedAt.Offset != TimeSpan.Zero || SettleBy.Offset != TimeSpan.Zero
            || SettleBy <= ReceivedAt || SettleBy == DateTimeOffset.MaxValue
            || EscalateAt.Offset != TimeSpan.Zero || EscalateAt <= ReceivedAt || EscalateAt >= SettleBy
            || ReceiptHash is not { Length: 32 } || ReceiptExpiresAt.Offset != TimeSpan.Zero
            || ReceiptExpiresAt <= ReceivedAt || ReceiptExpiresAt == DateTimeOffset.MaxValue
            || Revision is < 1 or long.MaxValue || !Enum.IsDefined(State) || !Enum.IsDefined(Failure)
            || MaterialCount < 0 || (MaterialReceipt is null) != (MaterialCount == 0)
            || (MaterialReceipt is null) != (ComputedAt is null)
            || MaterialReceipt is { } receipt && (receipt.Length != 64 || !receipt.All(char.IsAsciiHexDigitLower))
            || ComputedAt is { } computed && (computed < ReceivedAt || computed >= SettleBy || computed.Offset != TimeSpan.Zero)
            || AuthoritySequence is <= 0)
            throw new InvalidOperationException("moderation_recognition_request_invalid");
        var validState = State switch
        {
            ModerationRecognitionPendingState.Received => MaterialReceipt is null && AuthoritySequence is null,
            ModerationRecognitionPendingState.Computed => MaterialReceipt is not null && AuthoritySequence is null,
            ModerationRecognitionPendingState.EscalationRequired or ModerationRecognitionPendingState.LawfulDispositionRequired
                => AuthoritySequence is null,
            ModerationRecognitionPendingState.AuthorityBound => MaterialReceipt is not null
                && AuthoritySequence is not null && Failure == ModerationRecognitionPendingFailure.None,
            ModerationRecognitionPendingState.NoLongerRequired => MaterialReceipt is null
                && AuthoritySequence is not null && Failure == ModerationRecognitionPendingFailure.None,
            _ => false
        };
        if (!validState) throw new InvalidOperationException("moderation_recognition_request_invalid");
    }

    private long NextRevision() => Revision is < 1 or >= long.MaxValue - 1
        ? throw new InvalidOperationException("Recognition request revision is exhausted.")
        : Revision + 1;

    private void RequireTime(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero || now < ReceivedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Recognition requires finite non-regressing UTC time.");
    }

    private ModerationRecognitionPendingState DeadlineState(DateTimeOffset now) =>
        State == ModerationRecognitionPendingState.LawfulDispositionRequired || now >= SettleBy
            ? ModerationRecognitionPendingState.LawfulDispositionRequired
            : State == ModerationRecognitionPendingState.EscalationRequired || now >= EscalateAt
                ? ModerationRecognitionPendingState.EscalationRequired
                : HasCompleteMaterial ? ModerationRecognitionPendingState.Computed : ModerationRecognitionPendingState.Received;

    internal static string ComputeItemReceipt(ModerationRecognitionCaptureItem item) => ComputeReceipt([item]);

    private static string ComputeReceipt(IEnumerable<ModerationRecognitionCaptureItem> rows) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            rows.OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.IntentId, row.ErasureSubjectId, row.SubjectId, row.SubjectSolutionId, row.SubjectInstanceId,
                row.FindingId, row.ParentCaptureItemId, row.ContinuityBindingId, row.OriginalCapturedAt,
                row.SourceId, row.SourceRevision, row.SourceGeneration,
                row.AdoptionId, row.AdoptionRevision, row.AdoptionGeneration, row.RingGeneration,
                row.TrustDomainId, row.ProviderKind, row.Purpose, row.KeyId, row.KeyGeneration,
                row.SerializationVersion, row.Digest, row.KeyVerificationTag,
                row.SanctionId, row.SanctionRevision, row.DecisionId, row.DecisionRevision,
                row.RuleOwnerId, row.RuleCode, row.RuleRevision, row.OriginalCapabilityMask, row.PurposeCapabilityMask,
                row.ScopeKind, row.ScopeSolutionId, row.ScopeInstanceId, row.ScopeTenantId, row.ScopeCommunityId,
                row.ScopeResourceId, row.ScopeEventId, row.StartsAt, row.EndsAt, row.ReviewAt, row.CapturedAt, row.RetainUntil
            }))));

    public override string ToString() => nameof(ModerationRecognitionPending);
}
