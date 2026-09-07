namespace Explore.Application.Features.Settings.Requests.Commands;

using Explore.Application.Responses;
using Explore.Domain.Settings;
using MediatR;

/// <summary>
/// Locks a setting key at the specified scope (Instance or Tenant only).
/// Lower-scope overrides remain in storage but are suppressed during resolution.
/// Requires administrator privileges for the target scope.
/// </summary>
public sealed record LockSettingCommand : IRequest<BaseCommandResponse<Guid>>
{
    /// <summary>
    /// Fully qualified setting key (e.g., "event_list.page_size"). Must be lockable per its definition.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The scope at which to lock. Only Instance and Tenant are supported.
    /// </summary>
    public required SettingScope Scope { get; init; }
}
