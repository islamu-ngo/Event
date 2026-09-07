using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class UpdateFooterLinkCommandHandler(
    IFooterLinkRepository footerLinkRepository,
    ITenantContext tenantContext,
    IHierarchicalSettingsResolver settingsResolver,
    FooterLinkMutationGuard mutationGuard)
    : IRequestHandler<UpdateFooterLinkCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateFooterLinkCommand request, CancellationToken cancellationToken)
    {
        bool requireHttps = await settingsResolver.ResolveAsync<bool>(
            GovernanceSettingKeys.Security.RequireHttpsExternalUrls,
            new SettingContext(),
            cancellationToken);
        var validator = new PatchFooterLinkDtoValidator(requireHttps);
        await validator.ValidateAndThrowAsync(request.Update, cancellationToken);

        await mutationGuard.EnsureAllowedAsync(tenantContext.TenantId, cancellationToken);

        var link = await footerLinkRepository.GetByIdForTenantAsync(
            request.LinkId,
            tenantContext.TenantId,
            cancellationToken);
        if (link is null)
            throw new NotFoundException(nameof(link), request.LinkId);

        if (request.Update.Label is not null)
            link.Label = request.Update.Label.Value.Trim();

        if (request.Update.Url is not null)
            link.Url = request.Update.Url.Value.Trim();

        if (request.Update.OpenInNewTab?.Value is bool openInNewTab)
            link.OpenInNewTab = openInNewTab;

        if (request.Update.IsActive?.Value is bool isActive)
            link.IsActive = isActive;

        await footerLinkRepository.Update(link);

        return BaseCommandResponse.Success(link.Id, "Footer link updated successfully.");
    }
}
