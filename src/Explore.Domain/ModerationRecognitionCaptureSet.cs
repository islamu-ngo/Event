using System.Collections.Immutable;

namespace Explore.Domain;

/// <summary>
/// Snapshots complete computed items for one original erasure operation and writing ring.
/// The native capture owner separately proves exhaustive eligible contribution/provider coverage,
/// current governance and durability. This is not an erasure settlement or matching permission.
/// </summary>
public sealed class ModerationRecognitionCaptureSet
{
    public ModerationRecognitionCaptureSet(Guid erasureIntentId, Guid trustDomainId, long ringGeneration,
        DateTimeOffset capturedAt, DateTimeOffset settleBy,
        IEnumerable<ModerationRecognitionCapturedMaterial> material)
    {
        ModerationHostBinding.RequireUuidV7(erasureIntentId, nameof(erasureIntentId));
        ModerationHostBinding.RequireUuidV7(trustDomainId, nameof(trustDomainId));
        ArgumentNullException.ThrowIfNull(material);
        if (ringGeneration is < 1 or long.MaxValue
            || capturedAt.Offset != TimeSpan.Zero || capturedAt == DateTimeOffset.MinValue
            || settleBy.Offset != TimeSpan.Zero || settleBy <= capturedAt || settleBy == DateTimeOffset.MaxValue)
            throw new ArgumentException("Computed capture requires an exact original operation, finite ring generation and settlement deadline.");
        var items = material.ToImmutableArray();
        if (items.IsEmpty || items.Any(item => item is null))
            throw new ArgumentException("Recognition capture cannot receipt an empty or partially computed set.", nameof(material));
        var original = items[0];
        if (items.Any(item => item.Token.TrustDomainId != trustDomainId
                || item.Token.KeyId != original.Token.KeyId || item.Token.KeyGeneration != original.Token.KeyGeneration
                || item.Token.SerializationVersion != original.Token.SerializationVersion
                || item.KeyVerificationTag != original.KeyVerificationTag
                || item.Contribution.CapturedAt != capturedAt || settleBy > item.Contribution.RetainUntil)
            || items.Select(item => (item.Token.ProviderKind, item.Token.Purpose, item.Token.Digest,
                    item.Contribution.SanctionId, item.Contribution.DecisionId))
                .Distinct().Count() != items.Length)
            throw new ArgumentException("Computed capture requires unique original contributions, one exact writing material identity and finite original retention.");
        ErasureIntentId = erasureIntentId;
        TrustDomainId = trustDomainId;
        RingGeneration = ringGeneration;
        CapturedAt = capturedAt;
        SettleBy = settleBy;
        Material = items;
    }

    public Guid ErasureIntentId { get; }
    public Guid TrustDomainId { get; }
    public long RingGeneration { get; }
    public DateTimeOffset CapturedAt { get; }
    public DateTimeOffset SettleBy { get; }
    public ImmutableArray<ModerationRecognitionCapturedMaterial> Material { get; }
    public override string ToString() => nameof(ModerationRecognitionCaptureSet);
}
