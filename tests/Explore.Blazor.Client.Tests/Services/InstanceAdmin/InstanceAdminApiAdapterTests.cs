using Explore.Blazor.Client.Contracts.InstanceAdmin;
using Explore.Blazor.Client.Contracts.Services.InstanceAdmin;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Services.InstanceAdmin;

namespace Explore.Blazor.Client.Tests.Services.InstanceAdmin;

public sealed class InstanceAdminApiAdapterTests
{
    [Test]
    public async Task GetOverviewAsync_PreservesSummaryWarningsAndLinks()
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        var overview = new HalResourceOfInstanceOverviewDto
        {
            DeploymentMode = "MultiTenant",
            Version = "1.2.3",
            PublicOrigin = "https://events.example.test",
            AdminOrigin = "https://admin.example.test",
            TotalTenantCount = 3,
            ActiveTenantCount = 2,
            ProviderSummaries =
            [
                new()
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
                new()
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
            (InstanceAdminLinkRelations.Self, "/api/admin/instance/overview", "GET"));
        controlPlaneClient.GetInstanceAdminOverviewAsync(null, null, Arg.Any<CancellationToken>()).Returns(overview);
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetOverviewAsync();

        await Assert.That(result).IsSameReferenceAs(overview);
        await Assert.That(result.DeploymentMode).IsEqualTo("MultiTenant");
        await Assert.That(result.AdminOrigin).IsEqualTo("https://admin.example.test");
        await Assert.That(result.ProviderSummaries!.Single().DisplayName).IsEqualTo("SMTP");
        await Assert.That(result.Warnings!.Single().Remediation).IsEqualTo("Set SMTP settings.");
        await Assert.That(result._links![InstanceAdminLinkRelations.Self].Href)
            .IsEqualTo("/api/admin/instance/overview");
    }

    [Test]
    public async Task GetTenantsAsync_PreservesEmbeddedItemsAndHalLinks()
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        var tenant = new HalResourceOfInstanceTenantListItemDto
        {
            Id = Guid.NewGuid(),
            FullName = "Central Mosque",
            Slug = "central",
            StatusName = "Active"
        };
        GeneratedHalLinkTestHelper.SetLinks(
            tenant,
            (InstanceAdminLinkRelations.Suspend, "/api/admin/instance/tenants/central/suspend", "POST"));
        var collection = new HalCollectionResourceOfInstanceTenantListItemDto
        {
            TotalCount = 1,
            _embedded = new HalCollectionEmbeddedOfInstanceTenantListItemDto { Items = [tenant] }
        };
        GeneratedHalLinkTestHelper.SetLinks(
            collection,
            (InstanceAdminLinkRelations.Create, "/api/admin/instance/tenants", "POST"));
        controlPlaneClient.GetInstanceAdminTenantsAsync(null, null, Arg.Any<CancellationToken>()).Returns(collection);
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetTenantsAsync();

        await Assert.That(result).IsSameReferenceAs(collection);
        await Assert.That(result.TotalCount).IsEqualTo(1);
        await Assert.That(result._links!.ContainsKey(InstanceAdminLinkRelations.Create)).IsTrue();
        var item = result._embedded!.Items!.Single();
        await Assert.That(item.FullName).IsEqualTo("Central Mosque");
        await Assert.That(item._links!.ContainsKey(InstanceAdminLinkRelations.Suspend)).IsTrue();
    }

    [Test]
    public async Task GetPlansAsync_PreservesCatalogItemsPricingAndHalLinks()
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        var plan = new HalResourceOfInstanceTenantPlanListItemDto
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
            (InstanceAdminLinkRelations.Self, "/api/admin/instance/plans/enterprise", "GET"));
        var collection = new HalCollectionResourceOfInstanceTenantPlanListItemDto
        {
            TotalCount = 1,
            _embedded = new HalCollectionEmbeddedOfInstanceTenantPlanListItemDto { Items = [plan] }
        };
        GeneratedHalLinkTestHelper.SetLinks(
            collection,
            (InstanceAdminLinkRelations.Self, "/api/admin/instance/plans", "GET"));
        tenantPlanClient.GetInstanceAdminTenantPlansAsync(null, null, Arg.Any<CancellationToken>()).Returns(collection);
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetPlansAsync();

        await Assert.That(result).IsSameReferenceAs(collection);
        await Assert.That(result.TotalCount).IsEqualTo(1);
        await Assert.That(result._links!.ContainsKey(InstanceAdminLinkRelations.Self)).IsTrue();
        var item = result._embedded!.Items!.Single();
        await Assert.That(item.Key).IsEqualTo("enterprise");
        await Assert.That(item.PriceAmount).IsEqualTo(199.95);
        await Assert.That(item.PublishedVersionNumber).IsEqualTo(3);
        await Assert.That(item._links!.ContainsKey(InstanceAdminLinkRelations.Self)).IsTrue();
    }

    [Test]
    public async Task GetPlanAsync_PreservesVersionsSettingsQuotasAndLinks()
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        var planId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var detail = new HalResourceOfInstanceTenantPlanDetailDto
        {
            Id = planId,
            Key = "enterprise",
            DisplayName = "Enterprise",
            Description = "Enterprise tenant plan.",
            Versions =
            [
                new InstanceTenantPlanVersionDto
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
                        new InstanceTenantPlanSettingDto
                        {
                            Key = "ai.enabled",
                            JsonValue = "true",
                            IsLocked = true
                        }
                    ],
                    Quotas = [new InstanceTenantPlanQuotaDto { Key = "storage.bytes", Limit = 10_000 }]
                }
            ]
        };
        GeneratedHalLinkTestHelper.SetLinks(
            detail,
            (InstanceAdminLinkRelations.Self, "/api/admin/instance/plans/enterprise", "GET"));
        tenantPlanClient.GetInstanceAdminTenantPlanByKeyAsync("enterprise", null, null, Arg.Any<CancellationToken>())
            .Returns(detail);
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetPlanAsync("enterprise");

        await Assert.That(result).IsSameReferenceAs(detail);
        await Assert.That(result.Id).IsEqualTo(planId);
        await Assert.That(result._links!.ContainsKey(InstanceAdminLinkRelations.Self)).IsTrue();
        var version = result.Versions!.Single();
        await Assert.That(version.Id).IsEqualTo(versionId);
        await Assert.That(version.Settings!.Single().Key).IsEqualTo("ai.enabled");
        await Assert.That(version.Settings!.Single().IsLocked).IsTrue();
        await Assert.That(version.Quotas!.Single().Limit).IsEqualTo(10_000);
    }

    [Test]
    public async Task GetPlansAsync_WhenApiReturnsForbidden_PropagatesGeneratedApiException()
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        tenantPlanClient.GetInstanceAdminTenantPlansAsync(null, null, Arg.Any<CancellationToken>())
            .Returns<Task<HalCollectionResourceOfInstanceTenantPlanListItemDto>>(_ => throw CreateApiException(403));
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        await Assert.ThrowsAsync<ApiException>(async () => await adapter.GetPlansAsync());
    }

    [Test]
    public async Task GetPlanAsync_WhenCancelled_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        tenantPlanClient.GetInstanceAdminTenantPlanByKeyAsync("enterprise", null, null, source.Token)
            .Returns<Task<HalResourceOfInstanceTenantPlanDetailDto>>(_ =>
                throw new OperationCanceledException(source.Token));
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await adapter.GetPlanAsync("enterprise", source.Token));
    }

    [Test]
    public async Task AddSharedApplicationServices_RegistersPlanCatalogInterfaceToScopedAdapter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IInstanceAdminClient>());
        services.AddSingleton(Substitute.For<IInstanceDeploymentModeClient>());
        services.AddSingleton(Substitute.For<IInstanceTenantConfigurationClient>());
        services.AddSingleton(Substitute.For<IInstanceTenantLifecycleClient>());
        services.AddSingleton(Substitute.For<IInstanceTenantPlanClient>());
        services.AddSharedApplicationServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IInstancePlanCatalogService>();
        var adapter = scope.ServiceProvider.GetRequiredService<InstanceAdminApiAdapter>();

        await Assert.That(service).IsSameReferenceAs(adapter);
    }

    [Test]
    public async Task GetDomainsAsync_PreservesDnsRecords()
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        var domains = new HalResourceOfInstanceDomainOverviewDto
        {
            DnsRecords =
            [
                new InstanceDnsRecordDto
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
            (InstanceAdminLinkRelations.Self, "/api/admin/instance/domains", "GET"));
        controlPlaneClient.GetInstanceAdminDomainsAsync(null, null, Arg.Any<CancellationToken>()).Returns(domains);
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetDomainsAsync();

        await Assert.That(result).IsSameReferenceAs(domains);
        var record = result.DnsRecords!.Single();
        await Assert.That(record.Name).IsEqualTo("admin.example.test");
        await Assert.That(record.Purpose).IsEqualTo("Admin host");
        await Assert.That(record.Guidance).IsEqualTo("Create a CNAME record.");
        await Assert.That(result._links!.ContainsKey(InstanceAdminLinkRelations.Self)).IsTrue();
    }

    [Test]
    public async Task GetOperationsAsync_PreservesStatusesWarningsMetricsAndLinks()
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        var operations = new HalResourceOfInstanceOperationsDto
        {
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero),
            Statuses =
            [
                new InstanceOperationStatusDto
                {
                    Key = "outbox",
                    DisplayName = "Outbox",
                    Status = "Backlog",
                    Severity = "warning",
                    Message = "15 messages are pending.",
                    Metrics =
                    [
                        new InstanceOperationMetricDto
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
                new()
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
            (InstanceAdminLinkRelations.Self, "/api/admin/instance/operations", "GET"));
        controlPlaneClient.GetInstanceAdminOperationsAsync(null, null, Arg.Any<CancellationToken>()).Returns(operations);
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.GetOperationsAsync();

        await Assert.That(result).IsSameReferenceAs(operations);
        await Assert.That(result.Statuses!.Single().DisplayName).IsEqualTo("Outbox");
        await Assert.That(result.Statuses!.Single().Metrics!.Single().IsCapped).IsTrue();
        await Assert.That(result.Warnings!.Single().Remediation).IsEqualTo("Inspect the outbox worker.");
        await Assert.That(result._links!.ContainsKey(InstanceAdminLinkRelations.Self)).IsTrue();
    }

    [Test]
    public async Task GetOverviewAsync_WhenApiReturnsForbidden_PropagatesGeneratedApiException()
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        controlPlaneClient.GetInstanceAdminOverviewAsync(null, null, Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfInstanceOverviewDto>>(_ => throw CreateApiException(403));
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        await Assert.ThrowsAsync<ApiException>(async () => await adapter.GetOverviewAsync());
    }

    [Test]
    public async Task ScheduleTenantPurgeAsync_SendsReasonAndConfirmationText()
    {
        var tenantId = Guid.NewGuid();
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        tenantLifecycleClient.ScheduleInstanceAdminTenantPurgeAsync(
                tenantId,
                null,
                null,
                Arg.Is<InstanceTenantLifecycleTransitionRequestDto>(request =>
                    request.Reason == "cleanup" && request.ConfirmationText == "central"),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfInstanceTenantLifecycleTransitionDto
            {
                Success = true,
                Message = "Purge scheduled."
            });
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.ScheduleTenantPurgeAsync(tenantId, "cleanup", "central");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Purge scheduled.");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateTenantAsync_PassesGeneratedRequest(bool assignCurrentUserAsTenantAdmin)
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        var request = new CreateTenantDto
        {
            FullName = "New Mosque",
            Slug = "new-mosque",
            IsActive = false,
            AssignCurrentUserAsTenantAdmin = assignCurrentUserAsTenantAdmin
        };
        tenantLifecycleClient.CreateInstanceAdminTenantAsync(request, null, null, Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = true,
                Message = "Tenant created successfully."
            });
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

        var result = await adapter.CreateTenantAsync(request);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Tenant created successfully.");
        await tenantLifecycleClient.Received(1).CreateInstanceAdminTenantAsync(
            Arg.Is<CreateTenantDto>(actual => ReferenceEquals(actual, request)),
            null,
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SuspendTenantAsync_WhenApiReturnsConflict_PropagatesGeneratedApiException()
    {
        var controlPlaneClient = Substitute.For<IInstanceAdminClient>();
        var deploymentModeClient = Substitute.For<IInstanceDeploymentModeClient>();
        var tenantConfigurationClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var tenantLifecycleClient = Substitute.For<IInstanceTenantLifecycleClient>();
        var tenantPlanClient = Substitute.For<IInstanceTenantPlanClient>();
        tenantLifecycleClient.SuspendInstanceAdminTenantAsync(
                Arg.Any<Guid>(),
                null,
                null,
                Arg.Any<InstanceTenantLifecycleTransitionRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto>>(_ => throw CreateApiException(409));
        var adapter = new InstanceAdminApiAdapter(controlPlaneClient, deploymentModeClient, tenantConfigurationClient, tenantLifecycleClient, tenantPlanClient);

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
