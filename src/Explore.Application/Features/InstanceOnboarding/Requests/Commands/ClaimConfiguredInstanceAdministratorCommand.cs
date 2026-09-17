using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

[AuthorizeConfiguredAdministratorClaim]
public sealed record ClaimConfiguredInstanceAdministratorCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required ProviderAccountKey AuthenticatedAccount { get; init; }
    public Guid UserId { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public bool? EmailVerified { get; init; }
}
