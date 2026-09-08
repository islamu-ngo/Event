using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Pages.Admin.Instance;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class InstanceTenantsSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IControlPlaneTenantService _tenantService = Substitute.For<IControlPlaneTenantService>();
    private readonly IAccessibilityFocusService _focusService = Substitute.For<IAccessibilityFocusService>();

    public InstanceTenantsSectionTests()
    {
        _ctx.Services.AddSingleton(_tenantService);
        _ctx.Services.AddSingleton(_focusService);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task WithoutHalLinks_HidesEveryLifecycleAction_EvenForAdminClaims()
    {
        _ctx.SetAuthenticatedUserWithClaims(
            Guid.NewGuid(),
            "Instance Admin",
            new Claim("explore:admin:instance", "true"));
        var tenant = Tenant("Quiet Mosque", "quiet-mosque", "Active");
        ReturnTenants(tenant);

        var cut = RenderSection();

        cut.WaitForAssertion(() => cut.Find("td").TextContent.Contains("Quiet Mosque", StringComparison.Ordinal));
        await Assert.That(cut.Markup).DoesNotContain("Activate");
        await Assert.That(cut.Markup).DoesNotContain("Suspend");
        await Assert.That(cut.Markup).DoesNotContain("Archive");
        await Assert.That(cut.Markup).DoesNotContain("Reactivate");
        await Assert.That(cut.Markup).DoesNotContain("Schedule purge");
        await Assert.That(cut.Markup).DoesNotContain("Create tenant");
        var focusTarget = cut.Find($"#instance-tenants-actions-{tenant.Id:N}");
        await Assert.That(focusTarget.GetAttribute("role")).IsEqualTo("group");
        await Assert.That(focusTarget.GetAttribute("tabindex")).IsEqualTo("-1");
        await Assert.That(focusTarget.GetAttribute("aria-label")).IsEqualTo("Tenant lifecycle controls for Quiet Mosque");
    }

    [Test]
    public async Task StateSpecificHalLinks_ShowAllSupportedLifecycleActions()
    {
        var provisioningTenant = Tenant("Provisioning Mosque", "provisioning", "Provisioning", ControlPlaneLinkRelations.Activate);
        ReturnTenants(
            provisioningTenant,
            Tenant("Active Mosque", "active", "Active", ControlPlaneLinkRelations.Suspend),
            Tenant("Suspended Mosque", "suspended", "Suspended", ControlPlaneLinkRelations.Reactivate),
            Tenant("Archivable Mosque", "archivable", "Active", ControlPlaneLinkRelations.Archive),
            Tenant("Archived Mosque", "archived", "Archived", ControlPlaneLinkRelations.SchedulePurge));

        var cut = RenderSection();

        cut.WaitForAssertion(() => cut.Find("button[aria-label='Activate Provisioning Mosque']"));
        await Assert.That(cut.Find("button[aria-label='Suspend Active Mosque']")).IsNotNull();
        await Assert.That(cut.Find("button[aria-label='Reactivate Suspended Mosque']")).IsNotNull();
        await Assert.That(cut.Find("button[aria-label='Archive Archivable Mosque']")).IsNotNull();
        await Assert.That(cut.Find("button[aria-label='Schedule purge for Archived Mosque']")).IsNotNull();
        await Assert.That(cut.FindAll("[role='group']").Count).IsEqualTo(5);
        await Assert.That(cut.Find(".settings-section-header .mud-typography-body2").GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(cut.Find(".instance-tenants__count").GetAttribute("dir")).IsEqualTo("auto");
        var focusTarget = cut.Find($"#instance-tenants-actions-{provisioningTenant.Id:N}");
        await Assert.That(focusTarget.GetAttribute("role")).IsEqualTo("group");
        await Assert.That(focusTarget.GetAttribute("tabindex")).IsEqualTo("-1");
        await Assert.That(focusTarget.GetAttribute("aria-label")).IsEqualTo("Tenant lifecycle controls for Provisioning Mosque");
    }

    [Test]
    public async Task ConfigurationNavigation_RequiresItemHalLink()
    {
        var configurable = Tenant("Configurable Mosque", "configurable", "Active", ControlPlaneLinkRelations.Configuration);
        var hidden = Tenant("Hidden Mosque", "hidden", "Active");
        ReturnTenants(configurable, hidden);

        var cut = RenderSection();

        cut.WaitForAssertion(() => cut.Find("a[aria-label='Configure Configurable Mosque']"));
        await Assert.That(cut.Find("a[aria-label='Configure Configurable Mosque']").GetAttribute("href"))
            .IsEqualTo($"/admin/instance/tenants/{configurable.Id}/configuration");
        await Assert.That(cut.FindAll("a[aria-label^='Configure ']").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).DoesNotContain("Configure Hidden Mosque");
    }

    [Test]
    public async Task LoadingResult_ThenEmptyResult_RenderMigrationStates()
    {
        var pending = new TaskCompletionSource<HalCollectionResourceOfControlPlaneTenantListItemDto>();
        _tenantService.GetTenantsAsync(Arg.Any<CancellationToken>()).Returns(pending.Task);

        var loading = RenderSection();
        await Assert.That(loading.Find("[role='status']").TextContent).Contains("Loading tenants");

        pending.SetResult(TenantCollection([]));
        loading.WaitForAssertion(() => loading.Markup.Contains("No tenants were returned", StringComparison.Ordinal));
    }

    [Test]
    public async Task ApiFailure_RendersSafeProblemMessage()
    {
        _tenantService.GetTenantsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<HalCollectionResourceOfControlPlaneTenantListItemDto>>(_ =>
                throw new ApiException("forbidden", 403, null, new Dictionary<string, IEnumerable<string>>(), null));
        var failed = RenderSection();
        failed.WaitForAssertion(() => failed.Find("[role='alert']"));
        await Assert.That(failed.Markup).Contains("Tenant data is currently unavailable.");
        await Assert.That(failed.Markup).DoesNotContain("forbidden");
    }

    [Test]
    public async Task ThrownLoad_RendersSafeProblemWithoutRawException()
    {
        _tenantService.GetTenantsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<HalCollectionResourceOfControlPlaneTenantListItemDto>>(_ => throw new InvalidOperationException("raw load secret"));
        var thrown = RenderSection();
        thrown.WaitForAssertion(() => thrown.Find("[role='alert']"));
        await Assert.That(thrown.Markup).Contains("Tenant data is currently unavailable.");
        await Assert.That(thrown.Markup).DoesNotContain("raw load secret");
    }

    [Test]
    public async Task CreateAffordance_RequiresCollectionHalLink_ValidInputAndManagesFocus()
    {
        ReturnTenantsWithCollectionLinks([Tenant("Central Mosque", "central", "Active")],
            ControlPlaneLinkRelations.Create);
        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Create tenant']"));
        await Assert.That(cut.Find("button[aria-label='Create tenant']").Id).IsEqualTo("instance-tenants-create-trigger");

        cut.Find("button[aria-label='Create tenant']").Click();
        cut.WaitForAssertion(() => cut.Find("input[aria-label='Tenant name']"));

        await _focusService.Received(1).SaveFocusAsync();
        cut.WaitForAssertion(() => _focusService.Received(1).FocusByIdAsync("instance-tenants-create-name"));
        await Assert.That(cut.Find("section[aria-labelledby='instance-tenants-create-title']").GetAttribute("role")).IsEqualTo("region");
        var assignSelf = cut.Find("input[aria-label='Assign me as administrator of this tenant']");
        await Assert.That(assignSelf.GetAttribute("type")).IsEqualTo("checkbox");
        await Assert.That(assignSelf.HasAttribute("checked")).IsFalse();
        await Assert.That(cut.Find("label[for='instance-tenants-create-assign-self']")).IsNotNull();

        var submit = () => cut.Find("button[aria-label='Submit tenant creation']");
        await Assert.That(submit().HasAttribute("disabled")).IsTrue();
        cut.Find("input[aria-label='Tenant name']").Change("New Mosque");
        cut.Find("input[aria-label='Tenant slug']").Change("Invalid Slug");
        await Assert.That(submit().HasAttribute("disabled")).IsTrue();
        cut.Find("input[aria-label='Tenant slug']").Change("new-mosque");
        await Assert.That(submit().HasAttribute("disabled")).IsFalse();

        cut.Find("button[aria-label='Cancel tenant creation']").Click();
        await _focusService.Received(1).RestoreFocusAsync("#instance-tenants-create-trigger");
        await Assert.That(cut.Markup).DoesNotContain("Submit tenant creation");
    }

    [Test]
    public async Task CreateSuccess_SubmitsTrimmedTypedRequestReloadsAndRestoresFocus()
    {
        ReturnTenantsWithCollectionLinks([], ControlPlaneLinkRelations.Create);
        _tenantService.CreateTenantAsync(
                Arg.Is<CreateTenantDto>(request =>
                    request != null
                    && request.FullName == "New Mosque"
                    && request.Slug == "new-mosque"
                    && request.AssignCurrentUserAsTenantAdmin == true),
                Arg.Any<CancellationToken>())
            .Returns(CreateResult(true, "Tenant created."));
        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Create tenant']"));

        cut.Find("button[aria-label='Create tenant']").Click();
        cut.Find("input[aria-label='Tenant name']").Change("  New Mosque  ");
        cut.Find("input[aria-label='Tenant slug']").Change("  new-mosque  ");
        cut.Find("input[aria-label='Assign me as administrator of this tenant']").Change(true);
        cut.Find("button[aria-label='Submit tenant creation']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[role='status']").Any(element => element.TextContent.Contains("Tenant created.", StringComparison.Ordinal)));
        await _tenantService.Received(1).CreateTenantAsync(
            Arg.Is<CreateTenantDto>(request =>
                request != null
                && request.FullName == "New Mosque"
                && request.Slug == "new-mosque"
                && request.AssignCurrentUserAsTenantAdmin == true),
            Arg.Any<CancellationToken>());
        await _tenantService.Received(2).GetTenantsAsync(Arg.Any<CancellationToken>());
        await _focusService.Received(1).RestoreFocusAsync("#instance-tenants-create-trigger");
        await Assert.That(cut.Markup).DoesNotContain("Submit tenant creation");
        cut.Find("button[aria-label='Create tenant']").Click();
        await Assert.That(cut.Find("input[aria-label='Assign me as administrator of this tenant']").HasAttribute("checked")).IsFalse();
    }

    [Test]
    public async Task CreateFailure_ShowsSafeResultKeepsFormAndDoesNotReload()
    {
        ReturnTenantsWithCollectionLinks([], ControlPlaneLinkRelations.Create);
        var pending = new TaskCompletionSource<BaseCommandResponseOfGuid>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tenantService.CreateTenantAsync(Arg.Any<CreateTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Create tenant']"));

        cut.Find("button[aria-label='Create tenant']").Click();
        cut.Find("input[aria-label='Tenant name']").Change("New Mosque");
        cut.Find("input[aria-label='Tenant slug']").Change("new-mosque");
        cut.Find("input[aria-label='Assign me as administrator of this tenant']").Change(true);
        var submitTask = cut.Find("button[aria-label='Submit tenant creation']")
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            if (!cut.Find("input[aria-label='Assign me as administrator of this tenant']").HasAttribute("disabled"))
            {
                throw new InvalidOperationException("Expected assign-self checkbox to disable while creating.");
            }
        });
        pending.SetResult(CreateResult(false, "A tenant with this slug already exists.", "control_plane_validation_failed"));
        await submitTask;

        cut.WaitForAssertion(() => cut.Find("[role='alert']").TextContent.Contains("already exists", StringComparison.Ordinal));
        await _tenantService.Received(1).CreateTenantAsync(
            Arg.Is<CreateTenantDto>(request =>
                request != null
                && request.FullName == "New Mosque"
                && request.Slug == "new-mosque"
                && request.AssignCurrentUserAsTenantAdmin == true),
            Arg.Any<CancellationToken>());
        await _tenantService.Received(1).GetTenantsAsync(Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).Contains("Submit tenant creation");
        await Assert.That(cut.Find("input[aria-label='Assign me as administrator of this tenant']").HasAttribute("checked")).IsTrue();
        await Assert.That(cut.Find("input[aria-label='Assign me as administrator of this tenant']").HasAttribute("disabled")).IsFalse();
        await _focusService.DidNotReceive().RestoreFocusAsync(Arg.Any<string?>());
    }

    [Test]
    public async Task ActivateSuccess_CallsLifecycleServiceAndReloads()
    {
        var tenant = Tenant("Provisioning Mosque", "provisioning", "Provisioning", ControlPlaneLinkRelations.Activate);
        ReturnTenants(tenant);
        _tenantService.ActivateTenantAsync(TenantId(tenant), null, Arg.Any<CancellationToken>())
            .Returns(LifecycleResult(true, "Tenant activated."));

        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Activate Provisioning Mosque']"));

        cut.Find("button[aria-label='Activate Provisioning Mosque']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[role='status']").Any(element => element.TextContent.Contains("Tenant activated.", StringComparison.Ordinal)));

        await _tenantService.Received(1).ActivateTenantAsync(TenantId(tenant), null, Arg.Any<CancellationToken>());
        await _tenantService.Received(2).GetTenantsAsync(Arg.Any<CancellationToken>());
        await _focusService.Received(1).SaveFocusAsync();
        await _focusService.Received(1).RestoreFocusAsync($"#instance-tenants-actions-{tenant.Id:N}");
    }

    [Test]
    public async Task SuspendReasonFlow_RequiresReasonAndReloadsAfterTrimmedReasonSucceeds()
    {
        var tenant = Tenant("Active Mosque", "active", "Active", ControlPlaneLinkRelations.Suspend);
        ReturnTenants(tenant);
        _tenantService.SuspendTenantAsync(TenantId(tenant), "maintenance window", Arg.Any<CancellationToken>())
            .Returns(LifecycleResult(true, "Tenant suspended."));

        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Suspend Active Mosque']"));
        cut.Find("button[aria-label='Suspend Active Mosque']").Click();

        await _focusService.Received(1).SaveFocusAsync();
        cut.WaitForAssertion(() => _focusService.Received(1).FocusByIdAsync("instance-tenants-lifecycle-reason"));

        var confirm = () => cut.Find("button[aria-label='Confirm suspend for Active Mosque']");
        await Assert.That(confirm().HasAttribute("disabled")).IsTrue();
        cut.Find("input[aria-label='Lifecycle reason']").Change("   ");
        await Assert.That(confirm().HasAttribute("disabled")).IsTrue();
        cut.Find("input[aria-label='Lifecycle reason']").Change("  maintenance window  ");
        await Assert.That(confirm().HasAttribute("disabled")).IsFalse();
        confirm().Click();

        cut.WaitForAssertion(() => cut.FindAll("[role='status']").Any(element => element.TextContent.Contains("Tenant suspended.", StringComparison.Ordinal)));
        await _tenantService.Received(1).SuspendTenantAsync(TenantId(tenant), "maintenance window", Arg.Any<CancellationToken>());
        await _tenantService.Received(2).GetTenantsAsync(Arg.Any<CancellationToken>());
        await _focusService.Received(1).RestoreFocusAsync($"#instance-tenants-actions-{tenant.Id:N}");
    }

    [Test]
    public async Task ArchiveReasonFlow_FailureUsesTrimmedReasonAndDoesNotReload()
    {
        var tenant = Tenant("Active Mosque", "active", "Active", ControlPlaneLinkRelations.Archive);
        ReturnTenants(tenant);
        _tenantService.ArchiveTenantAsync(TenantId(tenant), "contract ended", Arg.Any<CancellationToken>())
            .Returns(LifecycleResult(false, "Tenant cannot be archived.", "control_plane_conflict"));

        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Archive Active Mosque']"));
        cut.Find("button[aria-label='Archive Active Mosque']").Click();
        cut.Find("input[aria-label='Lifecycle reason']").Change("  contract ended  ");
        cut.Find("button[aria-label='Confirm archive for Active Mosque']").Click();

        cut.WaitForAssertion(() => cut.Find("[role='alert']").TextContent.Contains("Tenant cannot be archived.", StringComparison.Ordinal));
        await _tenantService.Received(1).ArchiveTenantAsync(TenantId(tenant), "contract ended", Arg.Any<CancellationToken>());
        await _tenantService.Received(1).GetTenantsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PurgeRequiresNonblankReasonAndExactCaseSensitiveSlug()
    {
        ReturnTenants(Tenant("Archived Mosque", "Archived-Mosque", "Archived", ControlPlaneLinkRelations.SchedulePurge));
        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Schedule purge for Archived Mosque']"));

        cut.Find("button[aria-label='Schedule purge for Archived Mosque']").Click();
        await _focusService.Received(1).SaveFocusAsync();
        cut.WaitForAssertion(() => _focusService.Received(1).FocusByIdAsync("instance-tenants-purge-reason"));
        var confirm = () => cut.Find("button[aria-label='Confirm purge for Archived Mosque']");

        await Assert.That(confirm().HasAttribute("disabled")).IsTrue();
        cut.Find("input[aria-label='Purge reason']").Change("   ");
        cut.Find("input[aria-label='Purge confirmation']").Change("Archived-Mosque");
        await Assert.That(confirm().HasAttribute("disabled")).IsTrue();
        cut.Find("input[aria-label='Purge reason']").Change("retention complete");
        cut.Find("input[aria-label='Purge confirmation']").Change("archived-mosque");
        await Assert.That(confirm().HasAttribute("disabled")).IsTrue();
        cut.Find("input[aria-label='Purge confirmation']").Change("Archived-Mosque");
        await Assert.That(confirm().HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task PurgeWithEmptyTenantSlug_RemainsDisabled()
    {
        ReturnTenants(Tenant("Malformed Tenant", string.Empty, "Archived", ControlPlaneLinkRelations.SchedulePurge));
        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Schedule purge for Malformed Tenant']"));

        cut.Find("button[aria-label='Schedule purge for Malformed Tenant']").Click();
        cut.Find("input[aria-label='Purge reason']").Change("retention complete");
        cut.Find("input[aria-label='Purge confirmation']").Change(string.Empty);

        await Assert.That(cut.Find("button[aria-label='Confirm purge for Malformed Tenant']").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task ConfirmedPurge_CallsSchedulePurgeAndReloadsOnlyAfterSuccess()
    {
        var tenant = Tenant("Archived Mosque", "archived-mosque", "Archived", ControlPlaneLinkRelations.SchedulePurge);
        ReturnTenants(tenant);
        _tenantService.ScheduleTenantPurgeAsync(
                TenantId(tenant),
                "retention complete",
                tenant.Slug!,
                Arg.Any<CancellationToken>())
            .Returns(LifecycleResult(true, "Purge scheduled."));

        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Schedule purge for Archived Mosque']"));
        cut.Find("button[aria-label='Schedule purge for Archived Mosque']").Click();
        cut.Find("input[aria-label='Purge reason']").Change("retention complete");
        cut.Find("input[aria-label='Purge confirmation']").Change(tenant.Slug);
        cut.Find("button[aria-label='Confirm purge for Archived Mosque']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[role='status']").Any(element => element.TextContent.Contains("Purge scheduled.", StringComparison.Ordinal)));

        await _tenantService.Received(1).ScheduleTenantPurgeAsync(
            TenantId(tenant),
            "retention complete",
            tenant.Slug!,
            Arg.Any<CancellationToken>());
        await _tenantService.Received(2).GetTenantsAsync(Arg.Any<CancellationToken>());
        await _focusService.Received(1).RestoreFocusAsync($"#instance-tenants-actions-{tenant.Id:N}");
    }

    [Test]
    public async Task LifecycleFailure_ShowsSafeErrorAndDoesNotReload()
    {
        var tenant = Tenant("Suspended Mosque", "suspended", "Suspended", ControlPlaneLinkRelations.Reactivate);
        ReturnTenants(tenant);
        _tenantService.ReactivateTenantAsync(TenantId(tenant), null, Arg.Any<CancellationToken>())
            .Returns(LifecycleResult(false, "Reactivation is currently blocked.", "control_plane_conflict"));

        var cut = RenderSection();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Reactivate Suspended Mosque']"));
        cut.Find("button[aria-label='Reactivate Suspended Mosque']").Click();
        cut.WaitForAssertion(() => cut.Find("[role='alert']").TextContent.Contains("Reactivation is currently blocked.", StringComparison.Ordinal));

        await _tenantService.Received(1).ReactivateTenantAsync(TenantId(tenant), null, Arg.Any<CancellationToken>());
        await _tenantService.Received(1).GetTenantsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublicPage_RendersHeadingAndTenantSection()
    {
        ReturnTenants(Tenant("Central Mosque", "central", "Active"));

        var cut = _ctx.Render<InstanceTenants>();

        cut.WaitForAssertion(() => cut.Find("h1").TextContent.Equals("Tenant Management", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("Central Mosque");
    }

    private IRenderedComponent<InstanceTenantsSection> RenderSection() => _ctx.RenderMudComponent<InstanceTenantsSection>();

    private void ReturnTenants(params HalResourceOfControlPlaneTenantListItemDto[] tenants) =>
        _tenantService.GetTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(TenantCollection(tenants));

    private void ReturnTenantsWithCollectionLinks(
        IReadOnlyList<HalResourceOfControlPlaneTenantListItemDto> tenants,
        params string[] relations) =>
        _tenantService.GetTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(TenantCollection(tenants, Links(relations)));

    private static HalResourceOfControlPlaneTenantListItemDto Tenant(
        string name,
        string slug,
        string status,
        params string[] relations) => new()
        {
            Id = Guid.NewGuid(),
            FullName = name,
            Slug = slug,
            StatusCode = status,
            StatusName = status,
            _links = Links(relations)
        };

    private static HalCollectionResourceOfControlPlaneTenantListItemDto TenantCollection(
        IReadOnlyCollection<HalResourceOfControlPlaneTenantListItemDto> tenants,
        IReadOnlyDictionary<string, HalLink>? links = null) => new()
        {
            TotalCount = tenants.Count,
            _embedded = new HalCollectionEmbeddedOfControlPlaneTenantListItemDto { Items = tenants.ToArray() },
            _links = links is null ? null : new Dictionary<string, HalLink>(links)
        };

    private static Guid TenantId(HalResourceOfControlPlaneTenantListItemDto tenant) => tenant.Id.GetValueOrDefault();

    private static BaseCommandResponseOfGuid CreateResult(bool success, string message, string? failureCode = null) => new()
    {
        Success = success,
        Message = message,
        FailureCode = failureCode
    };

    private static BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto LifecycleResult(
        bool success,
        string message,
        string? failureCode = null) => new()
        {
            Success = success,
            Message = message,
            FailureCode = failureCode
        };

    private static Dictionary<string, HalLink> Links(params string[] relations) =>
        relations.ToDictionary(
            relation => relation,
            relation => new HalLink { Href = $"/api/admin/control-plane/tenants/{relation}", Method = "POST" },
            StringComparer.OrdinalIgnoreCase);
}
