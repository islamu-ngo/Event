using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class CreateFooterLinkCommandHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    IFooterLinkRepository footerLinkRepository,
    ITenantContext tenantContext,
    IHierarchicalSettingsResolver settingsResolver,
    FooterLinkMutationGuard mutationGuard)
    : IRequestHandler<CreateFooterLinkCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CreateFooterLinkCommand request, CancellationToken cancellationToken)
    {
        bool requireHttps = await settingsResolver.ResolveAsync<bool>(
            GovernanceSettingKeys.Security.RequireHttpsExternalUrls,
            new SettingContext(),
            cancellationToken);
        var validator = new PatchFooterLinkDtoValidator(requireHttps);
        await validator.ValidateAndThrowAsync(new PatchFooterLinkDto
        {
            Label = new PatchFooterLinkLabelDto { Value = request.Label },
            Url = new PatchFooterLinkUrlDto { Value = request.Url },
            OpenInNewTab = new PatchFooterLinkOpenInNewTabDto { Value = request.OpenInNewTab }
        }, cancellationToken);

        await mutationGuard.EnsureAllowedAsync(tenantContext.TenantId, cancellationToken);

        var group = await footerLinkGroupRepository.GetById(request.GroupId);

        if (group is null || group.TenantId != tenantContext.TenantId)
            throw new NotFoundException(nameof(group), request.GroupId);

        var maxOrder = await footerLinkRepository.GetMaxOrderInGroupAsync(request.GroupId, cancellationToken);

        var link = new TenantFooterLink
        {
            FooterLinkGroupId = request.GroupId,
            Label = request.Label.Trim(),
            Url = request.Url.Trim(),
            OpenInNewTab = request.OpenInNewTab,
            Order = maxOrder + 1,
            IsActive = true,
        };

        link = await footerLinkRepository.Create(link);

        return BaseCommandResponse.Success(link.Id, "Footer link created successfully.");
    }
}
