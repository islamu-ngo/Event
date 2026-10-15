using System.Collections.Immutable;

namespace Explore.Domain;

/// <summary>A reference to independently requalified native evidence, never an assertion of success.</summary>
public sealed record ModerationMeasuredOperationsProofReference(
    Guid Id, long Revision, long WriterGeneration, DateTimeOffset MeasuredAt, DateTimeOffset ValidUntil)
{
    public bool IsCurrent(DateTimeOffset now) => Id != Guid.Empty && Id.Version == 7 && Id.Variant is >= 8 and <= 11
        && Revision is > 0 and < long.MaxValue && WriterGeneration is > 0 and < long.MaxValue
        && MeasuredAt.Offset == TimeSpan.Zero && ValidUntil.Offset == TimeSpan.Zero
        && MeasuredAt != DateTimeOffset.MinValue && ValidUntil != DateTimeOffset.MaxValue
        && MeasuredAt <= now && now < ValidUntil;
}

public sealed record ModerationMeasuredOperationsConsumer(string ConsumerId, ModerationMeasuredOperationsProofReference? Measurement);

/// <summary>
/// Immutable native observation report. Missing measurements remain explicit nulls; independently
/// accepting this report acknowledges its contents, not readiness. Native owners must requalify
/// every retained reference before IsComplete can be used.
/// </summary>
public sealed record ModerationMeasuredOperationsEvidence(
    string InventoryFingerprint, string PrimaryConfigurationDigest, string ProviderCode, string ProviderVersion,
    ImmutableArray<ModerationMeasuredOperationsConsumer> Consumers,
    ModerationMeasuredOperationsProofReference? Staffing,
    ModerationMeasuredOperationsProofReference? RecognitionPrivacy, int RecognitionPurpose,
    ModerationMeasuredOperationsProofReference? Recovery,
    ModerationMeasuredOperationsProofReference? KeyConvergence,
    ModerationMeasuredOperationsProofReference? Transport)
{
    public void Validate(DateTimeOffset capturedAt)
    {
        if (!ModerationDeploymentSource.Digest(InventoryFingerprint)
            || !ModerationDeploymentSource.Digest(PrimaryConfigurationDigest)
            || string.IsNullOrWhiteSpace(ProviderCode) || ProviderCode.Length > 32
            || string.IsNullOrWhiteSpace(ProviderVersion) || ProviderVersion.Length > 256
            || Consumers.IsDefaultOrEmpty || Consumers.Length > 256
            || Consumers.Any(row => row is null || string.IsNullOrWhiteSpace(row.ConsumerId) || row.ConsumerId.Length > 128)
            || Consumers.Select(row => row.ConsumerId).Distinct(StringComparer.Ordinal).Count() != Consumers.Length
            || !Consumers.Select(row => row.ConsumerId).SequenceEqual(
                Consumers.Select(row => row.ConsumerId).Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || RecognitionPurpose is < 0 or > 3
            || RecognitionPurpose == 0 && (RecognitionPrivacy is not null || KeyConvergence is not null)
            || References().Any(row => !row.IsCurrent(capturedAt)))
            throw new ArgumentException("Measured operations require a bounded exact inventory and finite native observations.");
    }

    public bool IsComplete(ImmutableArray<string> requiredConsumers, int modeCode, int recognitionPurpose, DateTimeOffset now) =>
        modeCode is 1 or 2 && !requiredConsumers.IsDefaultOrEmpty
        && recognitionPurpose == RecognitionPurpose
        && Consumers.Select(row => row.ConsumerId).Order(StringComparer.Ordinal)
            .SequenceEqual(requiredConsumers.Order(StringComparer.Ordinal), StringComparer.Ordinal)
        && Consumers.All(row => row.Measurement?.IsCurrent(now) == true)
        && Staffing?.IsCurrent(now) == true && Recovery?.IsCurrent(now) == true
        && (modeCode == 1 ? Transport is null : Transport?.IsCurrent(now) == true)
        && (recognitionPurpose == 0 ? RecognitionPrivacy is null && KeyConvergence is null
            : RecognitionPrivacy?.IsCurrent(now) == true && KeyConvergence?.IsCurrent(now) == true);

    public IEnumerable<ModerationMeasuredOperationsProofReference> References() =>
        Consumers.Where(row => row.Measurement is not null).Select(row => row.Measurement!)
            .Concat(new[] { Staffing, RecognitionPrivacy, Recovery, KeyConvergence, Transport }.OfType<ModerationMeasuredOperationsProofReference>());
}
