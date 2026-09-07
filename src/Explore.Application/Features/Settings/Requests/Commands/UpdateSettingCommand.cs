namespace Explore.Application.Features.Settings.Requests.Commands;

using Explore.Application.Responses;
using Explore.Domain.Settings;
using MediatR;

/// <summary>
/// Updates a single setting key at the specified scope. The scope ID (tenant/user) is derived
/// from the authenticated context — not supplied by the caller.
/// </summary>
public sealed record UpdateSettingCommand : IRequest<BaseCommandResponse<Guid>>
{
    /// <summary>
    /// Fully qualified setting key (e.g., "event_list.page_size"). Must exist in SettingRegistry.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The value to set, as a plain string. Handler validates and serializes per ValueType.
    /// Examples: "pagination" (string), "12" (integer), "true" (boolean).
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The scope at which to write the override. User = user preference, Tenant = tenant override.
    /// </summary>
    public required SettingScope Scope { get; init; }
}
