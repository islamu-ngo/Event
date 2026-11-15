using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using Explore.Domain.Constants;
using System.Security.Cryptography;
using System.Text.Json;

namespace Explore.Application.Features.Footer.Handlers.Queries;

public sealed class GetFooterConfigQueryHandler(
    IHierarchicalSettingsResolver settingsResolver,
    IFooterLinkGroupRepository footerLinkGroupRepository,
    ITenantContext tenantContext,
    IVisitorTenantReadAccessPolicy visitorRead,
    ITenantDirectoryOperatorReadinessEvaluator directoryReadiness,
    IInstanceOperatorIdentityReadinessEvaluator instanceReadiness,
    ITypedSettingsDocumentResolver documents)
    : IQueryHandler<GetFooterConfigQuery, FooterConfigDto>
{
    public async Task<FooterConfigDto> QueryAsync(
        GetFooterConfigQuery request, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;

        var instance = await instanceReadiness.EvaluateAsync(
            InstanceOperatorIdentityCapability.PublicDisclosure, cancellationToken);
        var result = new FooterConfigDto
        {
            Settings = new FooterSettingsDto { Enabled = false, Template = "minimal", ShowCookieSettingsLink = true },
            InstanceOperator = instance.IsReady && instance.Identity is not null
                ? PublicOperatorIdentityMapper.MapInstanceOperator(instance.Identity) : null
        };
        if (await visitorRead.CheckAsync(tenantId, cancellationToken) != VisitorTenantReadAccessDecision.Allowed)
            return WithRevision(result);

        var settingGroup = await settingsResolver.ResolveGroupAsync<FooterSettingGroup>(
            new SettingContext(TenantId: tenantId), cancellationToken);
        var groups = await footerLinkGroupRepository.GetResolvedGroupsForTenantAsync(tenantId, cancellationToken);
        var branding = await documents.ResolveTenantDocumentAsync<BrandingSettings>(
            new SettingsResolutionContext(tenantId, RequestedDocuments: [SettingsDocumentKeys.Tenant.Branding]),
            SettingsDocumentKeys.Tenant.Branding, cancellationToken);
        var directory = await directoryReadiness.EvaluateAsync(
            tenantId, TenantDirectoryOperatorIdentityCapability.PublicDisclosure, cancellationToken);
        var settingContext = new SettingContext(TenantId: tenantId);
        bool showCommunityGuidelines =
            await settingsResolver.ResolveAsync<bool>(GovernanceSettingKeys.Events.UserSubmissionEnabled, settingContext, cancellationToken)
            || await settingsResolver.ResolveAsync<bool>(GovernanceSettingKeys.Events.OrganizationSubmissionEnabled, settingContext, cancellationToken)
            || await settingsResolver.ResolveAsync<bool>(GovernanceSettingKeys.Events.GroupSubmissionEnabled, settingContext, cancellationToken);

        var settings = new FooterSettingsDto
        {
            Enabled = settingGroup.Enabled,
            Template = settingGroup.Template,
            ShowDescription = settingGroup.ShowDescription,
            DescriptionText = settingGroup.DescriptionText,
            ShowSocialLinks = settingGroup.ShowSocialLinks,
            SocialLinks = settingGroup.SocialLinks.AsReadOnly(),
            CopyrightText = settingGroup.CopyrightText,
            ShowCookieSettingsLink = settingGroup.ShowCookieSettingsLink,
        };

        return WithRevision(result with
        {
            TenantId = tenantId,
            BrandDisplayName = branding?.Payload.DisplayName ?? string.Empty,
            BrandLogoUrl = branding?.Payload.LogoUrl ?? string.Empty,
            ShowCommunityGuidelinesLink = showCommunityGuidelines,
            DirectoryOperator = directory.IsReady && directory.Identity is not null
                && directory.DocumentRevision is { } revision && revision != Guid.Empty
                    ? PublicOperatorIdentityMapper.MapDirectoryOperator(directory.Identity, revision) : null,
            Settings = settings,
            LinkGroups = groups.Select(FooterMapper.ToPublicGroup).ToList(),
        });
    }

    private static FooterConfigDto WithRevision(FooterConfigDto footer) =>
        footer with { Revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(footer))) };
}
