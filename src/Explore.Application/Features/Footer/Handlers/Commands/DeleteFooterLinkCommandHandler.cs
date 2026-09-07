using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class DeleteFooterLinkCommandHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    IFooterLinkRepository footerLinkRepository,
    ITenantContext tenantContext,
    FooterLinkMutationGuard mutationGuard)
    : IRequestHandler<DeleteFooterLinkCommand, bool>
{
    public async Task<bool> Handle(
        DeleteFooterLinkCommand request, CancellationToken cancellationToken)
    {
        await mutationGuard.EnsureAllowedAsync(tenantContext.TenantId, cancellationToken);

        var link = await footerLinkRepository.GetById(request.LinkId);
        if (link is null)
            throw new NotFoundException(nameof(link), request.LinkId);

        var group = await footerLinkGroupRepository.GetById(link.FooterLinkGroupId);
        if (group is null || group.TenantId != tenantContext.TenantId)
            throw new NotFoundException(nameof(group), link.FooterLinkGroupId);

        await footerLinkRepository.Delete(link);

        return true;
    }
}
