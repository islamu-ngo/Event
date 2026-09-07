namespace Explore.Application.Features.Settings.Requests.Commands;

using Explore.Application.Responses;
using Explore.Domain.Settings;
using MediatR;

/// <summary>
/// Unlocks a setting key at the specified scope (Instance or Tenant only).
/// Restores cascade resolution — previously suppressed lower-scope overrides become effective again.
/// Requires administrator privileges for the target scope.
/// </summary>
public sealed record UnlockSettingCommand : IRequest<BaseCommandResponse<Guid>>
{
    /// <summary>
    /// Fully qualified setting key to unlock. Must currently be locked at the specified scope.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The scope at which to unlock. Only Instance and Tenant are supported.
    /// </summary>
    public required SettingScope Scope { get; init; }
}
