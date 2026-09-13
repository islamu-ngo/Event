using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TenantStorageSettings.Requests.Commands;

public sealed record PatchTenantStorageSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchTenantStorageSettingsDto Settings { get; init; } = new();
}
