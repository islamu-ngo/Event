namespace Explore.Application.Features.Settings.Requests.Commands;

using Explore.Application.Responses;
using Explore.Domain.Settings;
using MediatR;

/// <summary>
/// Removes the override for a setting key at the specified scope. The effective value
/// reverts to the next higher scope in the cascade (e.g., removing user override → tenant value applies).
/// </summary>
public sealed record ResetSettingCommand : IRequest<BaseCommandResponse<Guid>>
{
    /// <summary>
    /// Fully qualified setting key (e.g., "event_list.page_size"). Must exist in SettingRegistry.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The scope from which to remove the override.
    /// </summary>
    public required SettingScope Scope { get; init; }
}
