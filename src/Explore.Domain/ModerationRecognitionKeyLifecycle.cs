using System.Security.Cryptography;
using System.Text.Json;

namespace Explore.Domain;

public enum ModerationRecognitionKeyState
{
    Candidate = 1, Writing = 2, Readable = 3, Retired = 4, Compromised = 5
}

/// <summary>
/// Durable material-free authorization for the exact selected ring. An authorized switch or
/// retirement advances the expected secret generation before the operator changes that authority.
/// The intervening mismatch is closed, not a fallback to the previous write generation.
/// </summary>
public sealed class ModerationRecognitionKeyLifecycle
{
    private ModerationRecognitionKeyLifecycle() { }
    private readonly List<ModerationRecognitionKeyLifecycleKey> keys = [];
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public long RingGeneration { get; private set; }
    public long Revision { get; private set; }
    public long AcknowledgementSequence { get; private set; }
    public long InitialErasureHead { get; private set; }
    public long InitialErasureRetainedFloor { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public IReadOnlyList<ModerationRecognitionKeyLifecycleKey> Keys => keys.AsReadOnly();

    public string Commitment => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { Id, RingGeneration, InitialErasureHead, InitialErasureRetainedFloor, Keys = keys.OrderBy(row => row.KeyGeneration)
            .Select(row => new { row.KeyId, row.KeyGeneration, row.VerificationTag, row.State,
                row.OriginalObligationsUntil, row.RequiredErasureHead }) })));

    public static ModerationRecognitionKeyLifecycle Initialize(Guid trustDomainId, Guid hostBindingId,
        string keyId, long generation, string verificationTag, long ringGeneration, DateTimeOffset now,
        long initialErasureHead, long initialErasureRetainedFloor)
    {
        var lifecycle = new ModerationRecognitionKeyLifecycle
        {
            Id = trustDomainId, HostBindingId = hostBindingId, RingGeneration = ringGeneration,
            Revision = 1, AcknowledgementSequence = 0, ChangedAt = now,
            InitialErasureHead = initialErasureHead, InitialErasureRetainedFloor = initialErasureRetainedFloor
        };
        lifecycle.keys.Add(ModerationRecognitionKeyLifecycleKey.Create(trustDomainId, keyId, generation,
            verificationTag, ModerationRecognitionKeyState.Writing, now));
        lifecycle.ValidatePersistedState();
        return lifecycle;
    }

    public void Stage(string keyId, long generation, string verificationTag, long ringGeneration, DateTimeOffset now)
    {
        RequireTime(now);
        if (ringGeneration == long.MaxValue || ringGeneration != RingGeneration + 1
            || generation <= keys.Max(row => row.KeyGeneration)
            || keys.Any(row => row.KeyId == keyId || row.State is ModerationRecognitionKeyState.Candidate
                or ModerationRecognitionKeyState.Compromised)
            || keys.Count(row => row.State != ModerationRecognitionKeyState.Retired) >= 16)
            throw Unavailable();
        keys.Add(ModerationRecognitionKeyLifecycleKey.Create(Id, keyId, generation,
            verificationTag, ModerationRecognitionKeyState.Candidate, now));
        Advance(ringGeneration, now);
    }

    public void AuthorizeSwitch(string keyId, long ringGeneration, KeyReplica? acknowledgement,
        Guid runtimeReplicaId, DateTimeOffset now)
    {
        RequireTime(now);
        if (ringGeneration == long.MaxValue || ringGeneration != RingGeneration + 1
            || acknowledgement?.Matches(this, runtimeReplicaId, now) != true
            || keys.Any(row => row.State == ModerationRecognitionKeyState.Compromised))
            throw Unavailable();
        var candidate = keys.SingleOrDefault(row => row.KeyId == keyId && row.State == ModerationRecognitionKeyState.Candidate)
            ?? throw Unavailable();
        keys.Single(row => row.State == ModerationRecognitionKeyState.Writing).MakeReadable(now);
        candidate.MakeWriting(now);
        Advance(ringGeneration, now);
    }

    /// <summary>
    /// The native owner supplies the complete original obligation inventory and independently
    /// acquired selected-authority/replay floors under the same capture/rotation fences.
    /// Empty or fabricated inventories are not an authorization boundary.
    /// </summary>
    public void AuthorizeRetirement(string keyId, long ringGeneration, IReadOnlyCollection<KeyObligation> obligations,
        long authoritativeErasureHead, long localReplayHead, KeyReplica? acknowledgement,
        Guid runtimeReplicaId, DateTimeOffset now)
    {
        RequireTime(now);
        var key = keys.SingleOrDefault(row => row.KeyId == keyId && row.State == ModerationRecognitionKeyState.Readable)
            ?? throw Unavailable();
        if (ringGeneration == long.MaxValue || ringGeneration != RingGeneration + 1 || authoritativeErasureHead < 0
            || localReplayHead != authoritativeErasureHead
            || key.OriginalObligationsUntil is { } until && now < until
            || authoritativeErasureHead < key.RequiredErasureHead
            || acknowledgement?.Matches(this, runtimeReplicaId, now) != true
            || obligations.Any(row => row.TrustDomainId != Id || row.KeyId != key.KeyId
                || row.KeyGeneration != key.KeyGeneration || row.VerificationTag != key.VerificationTag
                || !row.CanRetire(now, authoritativeErasureHead)))
            throw Unavailable();
        key.Retire(now);
        Advance(ringGeneration, now);
    }

    /// <summary>
    /// Retains only an unlinkable key horizon and authority watermark. Privacy-linked item and
    /// intent receipts may expire earlier; their expiry cannot shorten an original key obligation.
    /// </summary>
    public void RecordObligation(KeyObligation obligation)
    {
        ValidatePersistedState();
        obligation.ValidatePersistedState();
        var key = keys.SingleOrDefault(row => row.KeyId == obligation.KeyId
            && row.KeyGeneration == obligation.KeyGeneration && row.VerificationTag == obligation.VerificationTag
            && row.TrustDomainId == obligation.TrustDomainId && row.State != ModerationRecognitionKeyState.Retired)
            ?? throw Unavailable();
        key.RecordObligation(obligation);
    }

    public void Compromise(string keyId, DateTimeOffset now)
    {
        RequireTime(now);
        var key = keys.SingleOrDefault(row => row.KeyId == keyId
            && row.State is not (ModerationRecognitionKeyState.Retired or ModerationRecognitionKeyState.Compromised))
            ?? throw Unavailable();
        key.Compromise(now);
        Revision = checked(Revision + 1);
        ChangedAt = now;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(HostBindingId, nameof(HostBindingId));
        if (RingGeneration is < 1 or long.MaxValue || Revision is < 1 or long.MaxValue
            || AcknowledgementSequence is < 0 or long.MaxValue
            || InitialErasureHead < 0 || InitialErasureRetainedFloor < 0
            || InitialErasureRetainedFloor > InitialErasureHead
            || !Finite(ChangedAt) || keys.Count == 0
            || keys.Count(row => row.State == ModerationRecognitionKeyState.Writing) > 1
            || keys.All(row => row.State is not (ModerationRecognitionKeyState.Writing or ModerationRecognitionKeyState.Compromised))
            || keys.Count(row => row.State == ModerationRecognitionKeyState.Candidate) > 1
            || keys.Select(row => row.KeyId).Distinct(StringComparer.Ordinal).Count() != keys.Count
            || keys.Select(row => row.KeyGeneration).Distinct().Count() != keys.Count)
            throw Unavailable();
        foreach (var key in keys)
        {
            key.ValidatePersistedState();
            if (key.TrustDomainId != Id) throw Unavailable();
        }
    }

    internal static bool Finite(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
    internal static InvalidOperationException Unavailable() => new("moderation_recognition_lifecycle_unavailable");
    internal long AllocateAcknowledgement()
    {
        ValidatePersistedState();
        if (AcknowledgementSequence >= long.MaxValue - 1) throw Unavailable();
        return ++AcknowledgementSequence;
    }
    private void RequireTime(DateTimeOffset now)
    {
        ValidatePersistedState();
        if (!Finite(now) || now < ChangedAt || Revision >= long.MaxValue - 1) throw Unavailable();
    }
    private void Advance(long generation, DateTimeOffset now)
    {
        RingGeneration = generation;
        Revision = checked(Revision + 1);
        ChangedAt = now;
        ValidatePersistedState();
    }
    public override string ToString() => nameof(ModerationRecognitionKeyLifecycle);
}

public sealed class ModerationRecognitionKeyLifecycleKey
{
    private ModerationRecognitionKeyLifecycleKey() { }
    public Guid TrustDomainId { get; private set; }
    public string KeyId { get; private set; } = string.Empty;
    public long KeyGeneration { get; private set; }
    public string VerificationTag { get; private set; } = string.Empty;
    public ModerationRecognitionKeyState State { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public DateTimeOffset? OriginalObligationsUntil { get; private set; }
    public long RequiredErasureHead { get; private set; }

    internal static ModerationRecognitionKeyLifecycleKey Create(Guid trustDomainId, string keyId,
        long generation, string tag, ModerationRecognitionKeyState state, DateTimeOffset now)
    {
        var key = new ModerationRecognitionKeyLifecycleKey
        {
            TrustDomainId = trustDomainId, KeyId = keyId, KeyGeneration = generation,
            VerificationTag = tag, State = state, ChangedAt = now,
            OriginalObligationsUntil = null, RequiredErasureHead = 0
        };
        key.ValidatePersistedState();
        return key;
    }
    internal void MakeReadable(DateTimeOffset now) { State = ModerationRecognitionKeyState.Readable; ChangedAt = now; }
    internal void MakeWriting(DateTimeOffset now) { State = ModerationRecognitionKeyState.Writing; ChangedAt = now; }
    internal void Retire(DateTimeOffset now) { State = ModerationRecognitionKeyState.Retired; ChangedAt = now; }
    internal void Compromise(DateTimeOffset now) { State = ModerationRecognitionKeyState.Compromised; ChangedAt = now; }
    internal void RecordObligation(KeyObligation obligation)
    {
        if (OriginalObligationsUntil is null || OriginalObligationsUntil < obligation.ReadableUntil)
            OriginalObligationsUntil = obligation.ReadableUntil;
        if (obligation.AuthoritySequence is { } sequence && sequence > RequiredErasureHead)
            RequiredErasureHead = sequence;
    }
    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(TrustDomainId, nameof(TrustDomainId));
        PrivacyIdentityFingerprint.ValidateKeyId(KeyId);
        PrivacyIdentityFingerprint.ValidateDigest(VerificationTag);
        if (KeyGeneration is < 1 or long.MaxValue || !Enum.IsDefined(State)
            || RequiredErasureHead < 0
            || OriginalObligationsUntil is { } until && !ModerationRecognitionKeyLifecycle.Finite(until)
            || RequiredErasureHead > 0 && OriginalObligationsUntil is null
            || !ModerationRecognitionKeyLifecycle.Finite(ChangedAt))
            throw ModerationRecognitionKeyLifecycle.Unavailable();
    }
    public override string ToString() => nameof(ModerationRecognitionKeyLifecycleKey);
}
