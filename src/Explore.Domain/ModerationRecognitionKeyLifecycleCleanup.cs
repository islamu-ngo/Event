using System.Collections.Immutable;

namespace Explore.Domain;

/// <summary>A bounded native cleanup result; the cursor advances across deferred live/ancestor sets too.</summary>
public sealed record ModerationRecognitionKeyLifecycleCleanup(
    Guid? LastInspectedIntentId, ImmutableArray<ModerationRecognitionKeyLifecyclePurge> Purged)
{
    public override string ToString() => nameof(ModerationRecognitionKeyLifecycleCleanup);
}

public sealed record ModerationRecognitionKeyLifecycleReceiptCleanup(
    Guid? LastInspectedIntentId, int ReceiptsRemoved, int ObligationsRemoved)
{
    public override string ToString() => nameof(ModerationRecognitionKeyLifecycleReceiptCleanup);
}
