using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLAMU.Wire.Contracts.Learning;

public sealed record LearningContextDto : LearningResourceDto
{
    private IReadOnlyList<LearningTourProgressDto> _tours = Array.AsReadOnly(Array.Empty<LearningTourProgressDto>());
    private IReadOnlyList<LearningLessonDto> _lessons = Array.AsReadOnly(Array.Empty<LearningLessonDto>());
    private IReadOnlyList<LearningPracticeSummaryDto> _ownedPractices =
        Array.AsReadOnly(Array.Empty<LearningPracticeSummaryDto>());

    public required Guid TenantId { get; init; }
    public required Guid UserId { get; init; }
    public required string ScopeStamp { get; init; }
    public string ContextKey { get; init; } = "self";
    public Guid? EventContextId { get; init; }
    public Guid? StateId { get; init; }
    /// <summary>The shared source revision used only for tenant-source lifecycle actions.</summary>
    public required long ResourceRevision { get; init; }
    public required long LearnerRevision { get; init; }
    public Guid? SpaceId { get; init; }
    public required LearningSpaceStatus SpaceStatus { get; init; }
    public required LearningPreferencesDto Preferences { get; init; }
    public required LearningCapabilitiesDto Capabilities { get; init; }
    public LearningTourClaimDto? Claim { get; init; }

    /// <summary>At most the current and previous material version for each of four families.</summary>
    public IReadOnlyList<LearningTourProgressDto> Tours
    {
        get => _tours;
        init => _tours = LearningWireSnapshot.List(value, 8);
    }

    public IReadOnlyList<LearningLessonDto> Lessons
    {
        get => _lessons;
        init => _lessons = LearningWireSnapshot.List(value, 6);
    }

    /// <summary>Opaque owner-only inventory permits clearing an orphaned context without previewing it.</summary>
    public IReadOnlyList<LearningPracticeSummaryDto> OwnedPractices
    {
        get => _ownedPractices;
        init => _ownedPractices = LearningWireSnapshot.List(value, 120);
    }
}

public sealed record LearningPracticeSummaryDto
{
    public required Guid Id { get; init; }
    public required string ContextKey { get; init; }
    public required string RecipeKey { get; init; }
    public required int MaterialVersion { get; init; }
    public required long Revision { get; init; }
    public bool PreviewAvailable { get; init; }
}

public sealed record LearningPreferencesDto
{
    public bool SuppressAutomaticTours { get; init; }
    public bool HideLearning { get; init; }
    public bool PinLearning { get; init; }
}

/// <summary>Server-evaluated learning facts; HAL links remain the action-affordance authority.</summary>
public sealed record LearningCapabilitiesDto
{
    public bool CanLearn { get; init; }
    public bool CanClaimTours { get; init; }
    public bool CanSimulate { get; init; }
    public bool CanClearPractice { get; init; }
    public bool CanManageSpace { get; init; }
    public LearningDisplayTextDto? UnavailableReason { get; init; }
}

public sealed record LearningTourProgressDto
{
    public required LearningTourFamily Family { get; init; }
    public required int MaterialVersion { get; init; }
    public LearningTourOutcome? Outcome { get; init; }
    public required int StepIndex { get; init; }
    public bool IsAvailable { get; init; }
    public LearningDisplayTextDto? UnavailableReason { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LearningUpdatePreferencesRequest
{
    public required string ExpectedScopeStamp { get; init; }
    public required long ExpectedRevision { get; init; }
    public required bool SuppressAutomaticTours { get; init; }
    public required bool HideLearning { get; init; }
    public required bool PinLearning { get; init; }
}

/// <summary>Preserves the native HAL extension-data shape without exposing a mutable caller alias.</summary>
public abstract record LearningResourceDto
{
    [JsonInclude]
    [JsonExtensionData]
    internal Dictionary<string, JsonElement> JsonAdditionalProperties { get; init; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public ImmutableDictionary<string, object> AdditionalProperties
    {
        get => JsonAdditionalProperties.ToImmutableDictionary(
            property => property.Key, property => (object)property.Value, StringComparer.Ordinal);
        init => JsonAdditionalProperties = value.ToDictionary(
            property => property.Key,
            property => JsonSerializer.SerializeToElement(property.Value, property.Value?.GetType() ?? typeof(object)),
            StringComparer.Ordinal);
    }
}

internal static class LearningWireSnapshot
{
    internal static IReadOnlyList<T> List<T>(IReadOnlyList<T> source, int maximum)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count > maximum)
            throw new ArgumentOutOfRangeException(nameof(source), "Learning collection exceeds its finite bound.");
        return Array.AsReadOnly(source.ToArray());
    }
}
