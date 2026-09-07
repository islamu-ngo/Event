using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.TenantStorageSettings.Requests.Commands;

public sealed record PatchTenantStorageSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchTenantStorageSettingsDto Settings { get; init; } = new();
}
