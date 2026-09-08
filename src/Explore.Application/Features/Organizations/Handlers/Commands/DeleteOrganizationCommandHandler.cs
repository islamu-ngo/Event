using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Organizations.Handlers.Commands;

public sealed class DeleteOrganizationCommandHandler(
    IOrganizationRepository organizationRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    HybridCache cache) : IRequestHandler<DeleteOrganizationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DeleteOrganizationCommand request, CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetById(request.Id);
        if (organization is null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Organization not found."],
                "Organization not found.");
        }

        if (!Guid.TryParse(request.UserId, out var requesterUserId))
        {
            throw new UnauthorizedAccessException("User is not authenticated.");
        }

        var requesterMember = await organizationMemberRepository.GetByOrganizationAndUser(request.Id, requesterUserId);
        if (requesterMember is null || requesterMember.RoleId != (int)RoleEnum.OrgAdmin)
        {
            throw new AuthorizationException(ResourceKinds.Organization, AuthorizationActions.Delete);
        }

        await organizationRepository.Delete(organization);
        await cache.RemoveAsync($"organization:detail:{organization.Id}", cancellationToken);

        return BaseCommandResponse.Success(organization.Id, "Organization deleted successfully.");
    }
}
