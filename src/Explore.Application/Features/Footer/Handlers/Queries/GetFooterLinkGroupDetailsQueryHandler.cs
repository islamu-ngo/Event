using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Footer;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Footer.Handlers.Queries;

public sealed class GetFooterLinkGroupDetailsQueryHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    ITenantContext tenantContext,
    IHumanCommitScopePreparation humanScope,
    IFooterOperationExecution footerExecution)
    : IQueryHandler<GetFooterLinkGroupDetailsQuery, FooterLinkGroupDetailsDto>
{
    public async Task<FooterLinkGroupDetailsDto> QueryAsync(
        GetFooterLinkGroupDetailsQuery request, CancellationToken cancellationToken)
    {
        await humanScope.PrepareAsync(cancellationToken);
        await footerExecution.RequireOwnedAsync(request, cancellationToken);
        var group = await footerLinkGroupRepository.GetWithLinksAsync(request.GroupId, cancellationToken);

        if (group is null || group.TenantId != tenantContext.TenantId)
            throw new NotFoundException(nameof(group), request.GroupId);

        return FooterMapper.ToDetail(group);
    }
}
