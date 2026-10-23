using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class ReorderFooterLinkGroupsCommandHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    FooterLinkMutationGuard mutationGuard,
    IFooterOperationExecution footerExecution)
    : ICommandHandler<ReorderFooterLinkGroupsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ReorderFooterLinkGroupsCommand request, CancellationToken cancellationToken)
    {
        var human = await footerExecution.RequireOwnedAsync(request, cancellationToken);
        await mutationGuard.EnsureAllowedAsync(tenantContext.TenantId, cancellationToken);

        var tenantGroups = await footerLinkGroupRepository.GetByTenantIdAsync(
            tenantContext.TenantId, cancellationToken);

        var groupById = tenantGroups.ToDictionary(g => g.Id);

        async Task Reorder(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            for (var i = 0; i < request.OrderedGroupIds.Count; i++)
            {
                var id = request.OrderedGroupIds[i];
                if (!groupById.TryGetValue(id, out var group))
                    continue;

                group.Order = i;
                await footerLinkGroupRepository.Update(group);
            }
        }
        if (human is null)
            await unitOfWork.ExecuteInTransactionAsync(Reorder, cancellationToken);
        else
            await Reorder(cancellationToken);

        return BaseCommandResponse.Success(Guid.Empty);
    }
}
