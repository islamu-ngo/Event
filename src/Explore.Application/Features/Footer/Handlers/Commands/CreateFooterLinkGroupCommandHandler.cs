using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class CreateFooterLinkGroupCommandHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    FooterLinkMutationGuard mutationGuard)
    : IRequestHandler<CreateFooterLinkGroupCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CreateFooterLinkGroupCommand request, CancellationToken cancellationToken)
    {
        await mutationGuard.EnsureAllowedAsync(request.TenantId, cancellationToken);

        var maxOrder = await footerLinkGroupRepository.GetMaxOrderAsync(request.TenantId, cancellationToken);

        var group = new TenantFooterLinkGroup
        {
            TenantId = request.TenantId,
            Title = request.Title,
            Order = maxOrder + 1,
            IsActive = true,
        };

        group = await footerLinkGroupRepository.Create(group);

        return BaseCommandResponse.Success(group.Id, "Footer link group created successfully.");
    }
}
