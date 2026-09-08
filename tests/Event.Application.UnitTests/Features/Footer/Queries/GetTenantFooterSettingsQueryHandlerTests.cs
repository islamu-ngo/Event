using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Handlers.Queries;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Footer.Queries;

public sealed class GetTenantFooterSettingsQueryHandlerTests
{
    [Test]
    public async Task Handle_WhenMultiTenant_ReturnsCurrentTenantScalarsAndFiveGovernedLockStates()
    {
        var tenantId = Guid.NewGuid();
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var tenantContext = Substitute.For<ITenantContext>();
        var deploymentModeProvider = Substitute.For<IDeploymentModeProvider>();
        tenantContext.TenantId.Returns(tenantId);
        deploymentModeProvider.IsSingleTenantAsync(Arg.Any<CancellationToken>()).Returns(false);
        var socialLinks = new List<FooterSocialLinkDto>
        {
            new() { Platform = "github", Url = "https://github.com/islamu", Label = "GitHub" }
        };
        var group = CreateSettingsGroup(socialLinks);
        settingsResolver.ResolveGroupAsync<FooterSettingGroup>(
                Arg.Is<SettingContext>(context => context != null && context.TenantId == tenantId),
                Arg.Any<CancellationToken>())
            .Returns(group);
        var handler = new GetTenantFooterSettingsQueryHandler(
            settingsResolver,
            tenantContext,
            deploymentModeProvider);

        var result = await handler.Handle(new GetTenantFooterSettingsQuery(), CancellationToken.None);

        await Assert.That(result.TenantId).IsEqualTo(tenantId);
        await Assert.That(result.Enabled).IsFalse();
        await Assert.That(result.Template).IsEqualTo("community");
        await Assert.That(result.ShowDescription).IsFalse();
        await Assert.That(result.DescriptionText).IsEqualTo("Tenant description");
        await Assert.That(result.ShowSocialLinks).IsTrue();
        await Assert.That(result.SocialLinks.Count).IsEqualTo(1);
        await Assert.That(result.CopyrightText).IsEqualTo("Tenant copyright");
        await Assert.That(result.ShowCookieSettingsLink).IsFalse();
        await Assert.That(result.LockTenantTemplate).IsTrue();
        await Assert.That(result.LockTenantDescription).IsTrue();
        await Assert.That(result.LockTenantLinkGroups).IsTrue();
        await Assert.That(result.LockTenantSocialLinks).IsFalse();
        await Assert.That(result.LockTenantCopyright).IsTrue();
        await Assert.That(typeof(TenantFooterSettingsDto).GetProperty("LinkGroups")).IsNull();
    }

    [Test]
    public async Task Handle_WhenSingleTenant_IgnoresRawLinkGroupLock()
    {
        var tenantId = Guid.NewGuid();
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var tenantContext = Substitute.For<ITenantContext>();
        var deploymentModeProvider = Substitute.For<IDeploymentModeProvider>();
        tenantContext.TenantId.Returns(tenantId);
        deploymentModeProvider.IsSingleTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        settingsResolver.ResolveGroupAsync<FooterSettingGroup>(
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSettingsGroup([]));
        var handler = new GetTenantFooterSettingsQueryHandler(
            settingsResolver,
            tenantContext,
            deploymentModeProvider);

        var result = await handler.Handle(new GetTenantFooterSettingsQuery(), CancellationToken.None);

        await Assert.That(result.LockTenantLinkGroups).IsFalse();
    }

    private static FooterSettingGroup CreateSettingsGroup(IReadOnlyList<FooterSocialLinkDto> socialLinks)
    {
        var values = new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.Footer.Enabled] = Resolved(false),
            [GovernanceSettingKeys.Footer.Template] = Resolved("community"),
            [GovernanceSettingKeys.Footer.ShowDescription] = Resolved(false),
            [GovernanceSettingKeys.Footer.DescriptionText] = Resolved("Tenant description"),
            [GovernanceSettingKeys.Footer.ShowSocialLinks] = Resolved(true),
            [GovernanceSettingKeys.Footer.SocialLinks] = Resolved(socialLinks),
            [GovernanceSettingKeys.Footer.CopyrightText] = Resolved("Tenant copyright"),
            [GovernanceSettingKeys.Footer.ShowCookieSettingsLink] = Resolved(false),
            [GovernanceSettingKeys.Footer.LockTenantTemplate] = Resolved(true),
            [GovernanceSettingKeys.Footer.LockTenantDescription] = Resolved(true),
            [GovernanceSettingKeys.Footer.LockTenantLinkGroups] = Resolved(true),
            [GovernanceSettingKeys.Footer.LockTenantSocialLinks] = Resolved(false),
            [GovernanceSettingKeys.Footer.LockTenantCopyright] = Resolved(true)
        };
        var group = new FooterSettingGroup();
        group.Populate(values);
        return group;
    }

    private static ResolvedSetting Resolved<T>(T value) => new()
    {
        Value = SettingValueSerializer.Serialize(value)
    };
}
