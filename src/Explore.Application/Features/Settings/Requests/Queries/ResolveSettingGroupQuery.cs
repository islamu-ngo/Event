namespace Explore.Application.Features.Settings.Requests.Queries;

using Explore.Application.DTOs.Settings;
using Explore.Domain.Settings;
using MediatR;

/// <summary>
/// Resolves all settings for a category through the hierarchical cascade at the requested scope.
/// The scope determines context depth: User sees full cascade, Tenant sees instance+tenant only.
/// </summary>
public sealed record ResolveSettingGroupQuery : IRequest<SettingGroupResponseDto>
{
    /// <summary>
    /// Setting category (e.g., "EventList", "Appearance"). Must exist in SettingRegistry.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// The scope at which to resolve and evaluate editability.
    /// User = full cascade; Tenant = instance+tenant; Instance = instance only.
    /// </summary>
    public required SettingScope Scope { get; init; }

    private IReadOnlyCollection<string>? _includedKeys;

    public IReadOnlyCollection<string>? IncludedKeys
    {
        get => _includedKeys;
        init => _includedKeys = value is null
            ? null
            : Array.AsReadOnly(value.Distinct(StringComparer.Ordinal).ToArray());
    }
}
