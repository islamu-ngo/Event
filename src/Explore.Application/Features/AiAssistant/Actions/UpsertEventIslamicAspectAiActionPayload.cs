namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class UpsertEventIslamicAspectAiActionPayload
{
    public Guid? EventId { get; init; }

    public Guid? ExpectedConcurrencyStamp { get; init; }

    public string? AspectKind { get; init; }

    public bool? ManagementContextHasEdit { get; init; }

    public int? MadhabId { get; init; }

    public int? ReferencePrayer { get; init; }

    public int? PrayerTimeOffset { get; init; }

    public int? GenderMode { get; init; }

    public bool IncludesQuranRecitation { get; init; }

    public int? PrimaryLanguageId { get; init; }
}
