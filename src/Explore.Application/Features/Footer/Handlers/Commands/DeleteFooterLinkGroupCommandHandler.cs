using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class DeleteFooterLinkGroupCommandHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    IFooterLinkRepository footerLinkRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    FooterLinkMutationGuard mutationGuard)
    : IRequestHandler<DeleteFooterLinkGroupCommand, bool>
{
    public async Task<bool> Handle(
        DeleteFooterLinkGroupCommand request, CancellationToken cancellationToken)
    {
        await mutationGuard.EnsureAllowedAsync(tenantContext.TenantId, cancellationToken);

        var group = await footerLinkGroupRepository.GetById(request.GroupId);

        if (group is null || group.TenantId != tenantContext.TenantId)
            throw new NotFoundException(nameof(group), request.GroupId);

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await footerLinkRepository.DeleteByGroupIdAsync(request.GroupId, ct);
            await footerLinkGroupRepository.Delete(group);
        }, cancellationToken);

        return true;
    }
}
