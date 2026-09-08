using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class ReorderFooterLinkGroupsCommandHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    FooterLinkMutationGuard mutationGuard)
    : IRequestHandler<ReorderFooterLinkGroupsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReorderFooterLinkGroupsCommand request, CancellationToken cancellationToken)
    {
        await mutationGuard.EnsureAllowedAsync(tenantContext.TenantId, cancellationToken);

        var tenantGroups = await footerLinkGroupRepository.GetByTenantIdAsync(
            tenantContext.TenantId, cancellationToken);

        var groupById = tenantGroups.ToDictionary(g => g.Id);

        await unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            for (var i = 0; i < request.OrderedGroupIds.Count; i++)
            {
                var id = request.OrderedGroupIds[i];
                if (!groupById.TryGetValue(id, out var group))
                    continue;

                group.Order = i;
                await footerLinkGroupRepository.Update(group);
            }
        }, cancellationToken);

        return BaseCommandResponse.Success(Guid.Empty);
    }
}
