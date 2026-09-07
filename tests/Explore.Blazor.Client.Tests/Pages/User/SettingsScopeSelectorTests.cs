using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Pages.User.Components;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Pages.User;

public sealed class SettingsScopeSelectorTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IUiShellContextService _shellContextService;
    private readonly IShellPreferencesService _shellPreferencesService;

    public SettingsScopeSelectorTests()
    {
        _shellContextService = _ctx.AddMockService<IUiShellContextService>();
        _shellPreferencesService = _ctx.AddMockService<IShellPreferencesService>();
        _shellPreferencesService.LoadAsync(Arg.Any<UiShellContextDto>(), Arg.Any<CancellationToken>())
            .Returns(new ShellPreferenceState(WorkspaceKey.Events.Value, null, "/settings/personal"));
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Selector_RendersOnlyPersonalAndServerAuthorizedScopes()
    {
        var organizationId = Guid.NewGuid();
        _shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto
            {
                SettingsScopes =
                [
                    new SettingsScopeDto { Scope = "Organization", ScopeId = organizationId, DisplayName = "Community" },
                    new SettingsScopeDto { Scope = "Tenant", DisplayName = "Tenant" }
                ]
            });
        var cut = _ctx.Render<SettingsScopeSelector>();

        cut.WaitForState(() => cut.Markup.Contains("Community", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("href=\"/settings/personal\"");
        await Assert.That(cut.Markup).Contains($"href=\"/settings/organization/{organizationId}\"");
        await Assert.That(cut.Markup).Contains("href=\"/settings/admin\"");
        await Assert.That(cut.Markup).DoesNotContain("/settings/instance");
        await _shellContextService.Received(1).GetCachedContextAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SelectorWithNoAuthorizedAdministrativeScopesRendersNothing()
    {
        _shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto { SettingsScopes = [] });

        var cut = RenderSelector();

        await _shellContextService.Received(1).GetCachedContextAsync(Arg.Any<CancellationToken>());
        await Assert.That(cut.FindAll("nav[aria-label='Settings scopes']").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).DoesNotContain("href=\"/settings/personal\"");
    }

    [Test]
    public async Task Selector_SingleTenantDualAuthority_UsesOneInstanceAdminDestination()
    {
        _shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto
            {
                DeploymentMode = "SingleTenant",
                SettingsScopes =
                [
                    new SettingsScopeDto { Scope = "Tenant", DisplayName = "Tenant" },
                    new SettingsScopeDto { Scope = "Instance", DisplayName = "Instance" }
                ]
            });

        var cut = RenderSelector();

        cut.WaitForState(() => cut.Markup.Contains("Admin Settings", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("href=\"/settings/instance\"");
        await Assert.That(cut.Markup).DoesNotContain("/settings/admin");
    }

    [Test]
    public async Task CompactSelector_TenantAdmin_UsesCurrentAdminRoute()
    {
        _ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/admin");
        _shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto
            {
                DeploymentMode = "SingleTenant",
                SettingsScopes =
                [
                    new SettingsScopeDto { Scope = "Tenant", DisplayName = "Tenant" }
                ]
            });

        var cut = RenderSelector(compact: true);

        cut.WaitForState(() => cut.FindAll("select[aria-label='Settings scope']").Count == 1);
        var select = cut.Find("select[aria-label='Settings scope']");
        await Assert.That(select.GetAttribute("value")).IsEqualTo("/settings/admin");
        await Assert.That(cut.Markup).Contains("Admin Settings");
    }

    [Test]
    public async Task Selector_LastAuthorizedAdministrativeScope_IsOrderedFirstAfterPersonal()
    {
        var organizationId = Guid.CreateVersion7();
        var groupId = Guid.CreateVersion7();
        _shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto
            {
                SettingsScopes =
                [
                    new SettingsScopeDto { Scope = "Organization", ScopeId = organizationId, DisplayName = "Organization" },
                    new SettingsScopeDto { Scope = "Group", ScopeId = groupId, DisplayName = "Group" }
                ]
            });
        _shellPreferencesService.LoadAsync(Arg.Any<UiShellContextDto>(), Arg.Any<CancellationToken>())
            .Returns(new ShellPreferenceState(WorkspaceKey.Events.Value, null, $"/settings/group/{groupId}"));

        var cut = RenderSelector();

        cut.WaitForState(() => cut.Markup.Contains($"/settings/group/{groupId}", StringComparison.Ordinal));
        await Assert.That(cut.Markup.IndexOf($"/settings/group/{groupId}", StringComparison.Ordinal))
            .IsLessThan(cut.Markup.IndexOf($"/settings/organization/{organizationId}", StringComparison.Ordinal));
    }

    private IRenderedComponent<SettingsScopeSelector> RenderSelector(bool compact = false) =>
        _ctx.Render<SettingsScopeSelector>(parameters =>
            parameters.Add(component => component.Compact, compact));
}
