using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.TenantOnboarding.Requests.Commands;

public sealed record CompleteTenantOnboardingCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required UpdateTenantPolicyRequest Settings { get; init; } = new();
    public required TenantDirectoryOperatorIdentityInputDto DirectoryOperatorIdentity { get; init; }
    public Guid? ExpectedDirectoryOperatorIdentityConcurrencyStamp { get; init; }
}
