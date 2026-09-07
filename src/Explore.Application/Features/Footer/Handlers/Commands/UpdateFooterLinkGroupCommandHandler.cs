using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class UpdateFooterLinkGroupCommandHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    ITenantContext tenantContext,
    FooterLinkMutationGuard mutationGuard)
    : IRequestHandler<UpdateFooterLinkGroupCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateFooterLinkGroupCommand request, CancellationToken cancellationToken)
    {
        var validator = new PatchFooterLinkGroupDtoValidator();
        await validator.ValidateAndThrowAsync(request.Update, cancellationToken);

        await mutationGuard.EnsureAllowedAsync(tenantContext.TenantId, cancellationToken);

        var group = await footerLinkGroupRepository.GetById(request.GroupId);

        if (group is null || group.TenantId != tenantContext.TenantId)
            throw new NotFoundException(nameof(group), request.GroupId);

        if (request.Update.Title is not null)
            group.Title = request.Update.Title.Value.Trim();

        if (request.Update.IsActive?.Value is bool isActive)
            group.IsActive = isActive;

        await footerLinkGroupRepository.Update(group);

        return BaseCommandResponse.Success(group.Id, "Footer link group updated successfully.");
    }
}
