using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Services.ControlPlane;

namespace Explore.Blazor.Client.Tests.Services.ControlPlane;

public sealed class ControlPlaneApiAdapterTests
{
    [Test]
    public async Task GetOverviewAsync_PreservesSummaryWarningsAndLinks()
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        var overview = new HalResourceOfControlPlaneOverviewDto
        {
            DeploymentMode = "MultiTenant",
            Version = "1.2.3",
            PublicOrigin = "https://events.example.test",
            AdminOrigin = "https://admin.example.test",
            TotalTenantCount = 3,
            ActiveTenantCount = 2,
            ProviderSummaries =
            [
                new ProviderSummaries2
                {
                    Key = "smtp",
                    DisplayName = "SMTP",
                    Configured = false,
                    Status = "Missing",
                    Message = "Configure SMTP."
                }
            ],
            Warnings =
            [
                new Warnings6
                {
                    Code = "smtp_missing",
                    Severity = "warning",
                    Message = "SMTP is not configured.",
                    Remediation = "Set SMTP settings."
                }
            ]
        };
        GeneratedHalLinkTestHelper.SetLinks(
            overview,
            (ControlPlaneLinkRelations.Self, "/api/admin/control-plane/overview", "GET"));
        controlPlaneClient.GetControlPlaneOverviewAsync(null, null, Arg.Any<CancellationToken>()).Returns(overview);
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetOverviewAsync();

        await Assert.That(result).IsSameReferenceAs(overview);
        await Assert.That(result.DeploymentMode).IsEqualTo("MultiTenant");
        await Assert.That(result.AdminOrigin).IsEqualTo("https://admin.example.test");
        await Assert.That(result.ProviderSummaries!.Single().DisplayName).IsEqualTo("SMTP");
        await Assert.That(result.Warnings!.Single().Remediation).IsEqualTo("Set SMTP settings.");
        await Assert.That(result._links![ControlPlaneLinkRelations.Self].Href)
            .IsEqualTo("/api/admin/control-plane/overview");
    }

    [Test]
    public async Task GetTenantsAsync_PreservesEmbeddedItemsAndHalLinks()
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        var tenant = new HalResourceOfControlPlaneTenantListItemDto
        {
            Id = Guid.NewGuid(),
            FullName = "Central Mosque",
            Slug = "central",
            StatusName = "Active"
        };
        GeneratedHalLinkTestHelper.SetLinks(
            tenant,
            (ControlPlaneLinkRelations.Suspend, "/api/admin/control-plane/tenants/central/suspend", "POST"));
        var collection = new HalCollectionResourceOfControlPlaneTenantListItemDto
        {
            TotalCount = 1,
            _embedded = new HalCollectionEmbeddedOfControlPlaneTenantListItemDto { Items = [tenant] }
        };
        GeneratedHalLinkTestHelper.SetLinks(
            collection,
            (ControlPlaneLinkRelations.Create, "/api/admin/control-plane/tenants", "POST"));
        controlPlaneClient.GetControlPlaneTenantsAsync(null, null, Arg.Any<CancellationToken>()).Returns(collection);
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetTenantsAsync();

        await Assert.That(result).IsSameReferenceAs(collection);
        await Assert.That(result.TotalCount).IsEqualTo(1);
        await Assert.That(result._links!.ContainsKey(ControlPlaneLinkRelations.Create)).IsTrue();
        var item = result._embedded!.Items!.Single();
        await Assert.That(item.FullName).IsEqualTo("Central Mosque");
        await Assert.That(item._links!.ContainsKey(ControlPlaneLinkRelations.Suspend)).IsTrue();
    }

    [Test]
    public async Task GetPlansAsync_PreservesCatalogItemsPricingAndHalLinks()
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        var plan = new HalResourceOfControlPlaneTenantPlanListItemDto
        {
            Id = Guid.NewGuid(),
            Key = "enterprise",
            DisplayName = "Enterprise",
            Description = "Enterprise tenant plan.",
            LatestVersionNumber = 4,
            PublishedVersionNumber = 3,
            PriceAmount = 199.95,
            CurrencyCode = "EUR",
            BillingPeriod = "monthly",
            IsActiveForProvisioning = true
        };
        GeneratedHalLinkTestHelper.SetLinks(
            plan,
            (ControlPlaneLinkRelations.Self, "/api/admin/control-plane/plans/enterprise", "GET"));
        var collection = new HalCollectionResourceOfControlPlaneTenantPlanListItemDto
        {
            TotalCount = 1,
            _embedded = new HalCollectionEmbeddedOfControlPlaneTenantPlanListItemDto { Items = [plan] }
        };
        GeneratedHalLinkTestHelper.SetLinks(
            collection,
            (ControlPlaneLinkRelations.Self, "/api/admin/control-plane/plans", "GET"));
        tenantPlanClient.GetControlPlaneTenantPlansAsync(null, null, Arg.Any<CancellationToken>()).Returns(collection);
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetPlansAsync();

        await Assert.That(result).IsSameReferenceAs(collection);
        await Assert.That(result.TotalCount).IsEqualTo(1);
        await Assert.That(result._links!.ContainsKey(ControlPlaneLinkRelations.Self)).IsTrue();
        var item = result._embedded!.Items!.Single();
        await Assert.That(item.Key).IsEqualTo("enterprise");
        await Assert.That(item.PriceAmount).IsEqualTo(199.95);
        await Assert.That(item.PublishedVersionNumber).IsEqualTo(3);
        await Assert.That(item._links!.ContainsKey(ControlPlaneLinkRelations.Self)).IsTrue();
    }

    [Test]
    public async Task GetPlanAsync_PreservesVersionsSettingsQuotasAndLinks()
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        var planId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var detail = new HalResourceOfControlPlaneTenantPlanDetailDto
        {
            Id = planId,
            Key = "enterprise",
            DisplayName = "Enterprise",
            Description = "Enterprise tenant plan.",
            Versions =
            [
                new ControlPlaneTenantPlanVersionDto
                {
                    Id = versionId,
                    VersionNumber = 3,
                    StatusId = 2,
                    StatusCode = "Published",
                    PriceAmount = 199.95,
                    CurrencyCode = "EUR",
                    BillingPeriod = "monthly",
                    IsActiveForProvisioning = true,
                    Settings =
                    [
                        new ControlPlaneTenantPlanSettingDto
                        {
                            Key = "ai.enabled",
                            JsonValue = "true",
                            IsLocked = true
                        }
                    ],
                    Quotas = [new ControlPlaneTenantPlanQuotaDto { Key = "storage.bytes", Limit = 10_000 }]
                }
            ]
        };
        GeneratedHalLinkTestHelper.SetLinks(
            detail,
            (ControlPlaneLinkRelations.Self, "/api/admin/control-plane/plans/enterprise", "GET"));
        tenantPlanClient.GetControlPlaneTenantPlanByKeyAsync("enterprise", null, null, Arg.Any<CancellationToken>())
            .Returns(detail);
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetPlanAsync("enterprise");

        await Assert.That(result).IsSameReferenceAs(detail);
        await Assert.That(result.Id).IsEqualTo(planId);
        await Assert.That(result._links!.ContainsKey(ControlPlaneLinkRelations.Self)).IsTrue();
        var version = result.Versions!.Single();
        await Assert.That(version.Id).IsEqualTo(versionId);
        await Assert.That(version.Settings!.Single().Key).IsEqualTo("ai.enabled");
        await Assert.That(version.Settings!.Single().IsLocked).IsTrue();
        await Assert.That(version.Quotas!.Single().Limit).IsEqualTo(10_000);
    }

    [Test]
    public async Task GetPlansAsync_WhenApiReturnsForbidden_PropagatesGeneratedApiException()
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        tenantPlanClient.GetControlPlaneTenantPlansAsync(null, null, Arg.Any<CancellationToken>())
            .Returns<Task<HalCollectionResourceOfControlPlaneTenantPlanListItemDto>>(_ => throw CreateApiException(403));
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        await Assert.ThrowsAsync<ApiException>(async () => await adapter.GetPlansAsync());
    }

    [Test]
    public async Task GetPlanAsync_WhenCancelled_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        tenantPlanClient.GetControlPlaneTenantPlanByKeyAsync("enterprise", null, null, source.Token)
            .Returns<Task<HalResourceOfControlPlaneTenantPlanDetailDto>>(_ =>
                throw new OperationCanceledException(source.Token));
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await adapter.GetPlanAsync("enterprise", source.Token));
    }

    [Test]
    public async Task AddSharedApplicationServices_RegistersPlanCatalogInterfaceToScopedAdapter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IControlPlaneClient>());
        services.AddSingleton(Substitute.For<IControlPlaneDeploymentModeClient>());
        services.AddSingleton(Substitute.For<IControlPlaneTenantConfigurationClient>());
        services.AddSingleton(Substitute.For<IControlPlaneTenantLifecycleClient>());
        services.AddSingleton(Substitute.For<IControlPlaneTenantPlanClient>());
        services.AddSharedApplicationServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IControlPlanePlanCatalogService>();
        var adapter = scope.ServiceProvider.GetRequiredService<ControlPlaneApiAdapter>();

        await Assert.That(service).IsSameReferenceAs(adapter);
    }

    [Test]
    public async Task GetDomainsAsync_PreservesDnsRecords()
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        var domains = new HalResourceOfControlPlaneDomainOverviewDto
        {
            DnsRecords =
            [
                new ControlPlaneDnsRecordDto
                {
                    Name = "admin.example.test",
                    Purpose = "Admin host",
                    Status = "Pending",
                    Target = "control-plane.example.internal",
                    Guidance = "Create a CNAME record."
                }
            ]
        };
        GeneratedHalLinkTestHelper.SetLinks(
            domains,
            (ControlPlaneLinkRelations.Self, "/api/admin/control-plane/domains", "GET"));
        controlPlaneClient.GetControlPlaneDomainsAsync(null, null, Arg.Any<CancellationToken>()).Returns(domains);
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetDomainsAsync();

        await Assert.That(result).IsSameReferenceAs(domains);
        var record = result.DnsRecords!.Single();
        await Assert.That(record.Name).IsEqualTo("admin.example.test");
        await Assert.That(record.Purpose).IsEqualTo("Admin host");
        await Assert.That(record.Guidance).IsEqualTo("Create a CNAME record.");
        await Assert.That(result._links!.ContainsKey(ControlPlaneLinkRelations.Self)).IsTrue();
    }

    [Test]
    public async Task GetOperationsAsync_PreservesStatusesWarningsMetricsAndLinks()
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        var operations = new HalResourceOfControlPlaneOperationsDto
        {
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero),
            Statuses =
            [
                new ControlPlaneOperationStatusDto
                {
                    Key = "outbox",
                    DisplayName = "Outbox",
                    Status = "Backlog",
                    Severity = "warning",
                    Message = "15 messages are pending.",
                    Metrics =
                    [
                        new ControlPlaneOperationMetricDto
                        {
                            Key = "pending",
                            DisplayName = "Pending",
                            Value = 15,
                            IsCapped = true
                        }
                    ]
                }
            ],
            Warnings =
            [
                new Warnings5
                {
                    Code = "outbox_backlog",
                    Severity = "warning",
                    Message = "Outbox backlog detected.",
                    Remediation = "Inspect the outbox worker."
                }
            ]
        };
        GeneratedHalLinkTestHelper.SetLinks(
            operations,
            (ControlPlaneLinkRelations.Self, "/api/admin/control-plane/operations", "GET"));
        controlPlaneClient.GetControlPlaneOperationsAsync(null, null, Arg.Any<CancellationToken>()).Returns(operations);
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetOperationsAsync();

        await Assert.That(result).IsSameReferenceAs(operations);
        await Assert.That(result.Statuses!.Single().DisplayName).IsEqualTo("Outbox");
        await Assert.That(result.Statuses!.Single().Metrics!.Single().IsCapped).IsTrue();
        await Assert.That(result.Warnings!.Single().Remediation).IsEqualTo("Inspect the outbox worker.");
        await Assert.That(result._links!.ContainsKey(ControlPlaneLinkRelations.Self)).IsTrue();
    }

    [Test]
    public async Task GetOverviewAsync_WhenApiReturnsForbidden_PropagatesGeneratedApiException()
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        controlPlaneClient.GetControlPlaneOverviewAsync(null, null, Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfControlPlaneOverviewDto>>(_ => throw CreateApiException(403));
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        await Assert.ThrowsAsync<ApiException>(async () => await adapter.GetOverviewAsync());
    }

    [Test]
    public async Task ScheduleTenantPurgeAsync_SendsReasonAndConfirmationText()
    {
        var tenantId = Guid.NewGuid();
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        tenantLifecycleClient.ScheduleControlPlaneTenantPurgeAsync(
                tenantId,
                null,
                null,
                Arg.Is<ControlPlaneTenantLifecycleTransitionRequestDto>(request =>
                    request.Reason == "cleanup" && request.ConfirmationText == "central"),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto
            {
                Success = true,
                Message = "Purge scheduled."
            });
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.ScheduleTenantPurgeAsync(tenantId, "cleanup", "central");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Purge scheduled.");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateTenantAsync_PassesGeneratedRequest(bool assignCurrentUserAsTenantAdmin)
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        var request = new CreateTenantDto
        {
            FullName = "New Mosque",
            Slug = "new-mosque",
            IsActive = false,
            AssignCurrentUserAsTenantAdmin = assignCurrentUserAsTenantAdmin
        };
        tenantLifecycleClient.CreateControlPlaneTenantAsync(request, null, null, Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = true,
                Message = "Tenant created successfully."
            });
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.CreateTenantAsync(request);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Tenant created successfully.");
        await tenantLifecycleClient.Received(1).CreateControlPlaneTenantAsync(
            Arg.Is<CreateTenantDto>(actual => ReferenceEquals(actual, request)),
            null,
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SuspendTenantAsync_WhenApiReturnsConflict_PropagatesGeneratedApiException()
    {
        var controlPlaneClient = Substitute.For<IControlPlaneClient>();
        var deploymentModeClient = Substitute.For<IControlPlaneDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IControlPlaneTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IControlPlaneTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IControlPlaneTenantPlanClient>();
        tenantLifecycleClient.SuspendControlPlaneTenantAsync(
                Arg.Any<Guid>(),
                null,
                null,
                Arg.Any<ControlPlaneTenantLifecycleTransitionRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto>>(_ => throw CreateApiException(409));
        var adapter = new ControlPlaneApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        await Assert.ThrowsAsync<ApiException>(async () =>
            await adapter.SuspendTenantAsync(Guid.NewGuid(), "maintenance"));
    }

    private static ApiException CreateApiException(int statusCode) =>
        new(
            "Control-plane API error",
            statusCode,
            "response",
            new Dictionary<string, IEnumerable<string>>(),
            null);
}
