using Explore.Blazor.Client.Contracts.InstanceAdmin;
using Explore.Blazor.Client.Contracts.Services.InstanceAdmin;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using Explore.Blazor.Client.Routing.InstanceAdmin;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class InstanceAdministrationParityTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task OverviewRendersHostVersionCountsProvidersAndWarnings()
    {
        var service = _ctx.AddMockService<IInstanceOverviewService>();
        service.GetOverviewAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfInstanceOverviewDto
        {
            DeploymentMode = "MultiTenant",
            Version = "1.2.3",
            PublicOrigin = "https://events.example.test",
            AdminOrigin = "https://admin.example.test",
            TotalTenantCount = 4,
            ActiveTenantCount = 3,
            ProviderSummaries = [new() { Key = "storage", DisplayName = "Object storage", Status = "Configured", Message = "Provider ready", Configured = true }],
            Warnings = [new() { Code = "smtp_unavailable", Message = "Email unavailable", Remediation = "Configure SMTP" }],
            _links = Links(InstanceAdminLinkRelations.Tenants, InstanceAdminLinkRelations.Plans)
        });

        var cut = _ctx.Render<InstanceOverviewSection>();
        cut.WaitForElement("[aria-label='Instance summary']");

        await Assert.That(cut.Find("h2").TextContent.Trim()).IsEqualTo("Instance overview");
        var values = cut.FindAll("[aria-label='Instance summary'] strong").Select(item => item.TextContent).ToArray();
        await Assert.That(values).IsEquivalentTo(["MultiTenant", "https://events.example.test", "https://admin.example.test", "1.2.3"]);
        var cards = cut.FindAll("[aria-label='Operational status'] article");
        await Assert.That(cards[0].TextContent).Contains("4");
        await Assert.That(cards[1].TextContent).Contains("3");
        await Assert.That(cards[2].TextContent).Contains("Provider ready");
        await Assert.That(cut.Find("li").TextContent).Contains("Configure SMTP");
        await Assert.That(cut.Find("a[aria-label='Manage tenants']").GetAttribute("href")).IsEqualTo("/admin/instance/tenants");
        await cut.Find("button[aria-label='View tenant plans']").ClickAsync(new MouseEventArgs());
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri).EndsWith("/admin/instance/plans");
    }

    [Test]
    public async Task OverviewUnavailableRendersSafeOperatorFeedback()
    {
        var service = _ctx.AddMockService<IInstanceOverviewService>();
        service.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HalResourceOfInstanceOverviewDto>(new InvalidOperationException("transport-detail-sentinel")));

        var cut = _ctx.Render<InstanceOverviewSection>();
        cut.WaitForAssertion(() => cut.FindAll("h2").Single(item => item.TextContent == "Instance API unavailable"));

        await Assert.That(cut.FindAll("section").Last().TextContent).Contains("The instance API is currently unavailable.");
        await Assert.That(cut.Markup).DoesNotContain("transport-detail-sentinel");
    }

    [Test]
    public async Task SingleTenantOverviewReportsProviderStatusWithoutTenantInventory()
    {
        var service = _ctx.AddMockService<IInstanceOverviewService>();
        service.GetOverviewAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfInstanceOverviewDto
        {
            DeploymentMode = "SingleTenant",
            TotalTenantCount = 1,
            ActiveTenantCount = 1,
            ProviderSummaries = [new() { Key = "storage", DisplayName = "Object storage", Configured = true }]
        });

        var cut = _ctx.Render<InstanceOverviewSection>();
        cut.WaitForElement("[aria-label='Operational status']");

        await Assert.That(cut.FindAll("[aria-label='Operational status'] h3").Select(item => item.TextContent))
            .IsEquivalentTo(["Object storage"]);
    }

    [Test]
    public async Task OverviewNavigationRequiresServedHalAffordances()
    {
        var service = _ctx.AddMockService<IInstanceOverviewService>();
        service.GetOverviewAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfInstanceOverviewDto { DeploymentMode = "MultiTenant" });

        var cut = _ctx.Render<InstanceOverviewSection>();
        cut.WaitForElement("[aria-label='Instance summary']");

        await Assert.That(cut.FindAll("button[aria-label='View tenant plans']")).IsEmpty();
        await Assert.That(cut.FindAll("a[aria-label='Manage tenants']")).IsEmpty();
    }

    [Test]
    public async Task DomainsRenderRoutingFactsDnsGuidanceAndHalGovernanceAction()
    {
        var service = _ctx.AddMockService<IInstanceDomainService>();
        service.GetDomainsAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfInstanceDomainOverviewDto
        {
            DnsRecords = [new() { Name = "admin.example.test", Purpose = "Admin", Status = "Pending", Target = "instance.example.internal", Guidance = "Apply the DNS TXT record" }],
            _links = Links(InstanceAdminLinkRelations.Edit)
        });

        var cut = _ctx.RenderMudComponent<InstanceDomainsSection>();
        cut.WaitForElement("[role='listitem']");

        await Assert.That(cut.Find("h3").TextContent).IsEqualTo("admin.example.test");
        await Assert.That(cut.FindAll("dd").Select(item => item.TextContent)).IsEquivalentTo(["Admin", "Pending", "instance.example.internal"]);
        await Assert.That(cut.Find("[role='listitem']").TextContent).Contains("Apply the DNS TXT record");
        await Assert.That(cut.Find("a[aria-label='Edit domain governance']").GetAttribute("href"))
            .IsEqualTo(InstanceAdminRoutes.Domains);
    }

    [Test]
    public async Task OperationsRenderTelemetryWarningsRemediationAndCappedMetrics()
    {
        var service = _ctx.AddMockService<IInstanceOperationsService>();
        service.GetOperationsAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfInstanceOperationsDto
        {
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero),
            Statuses = [new() { Key = "outbox", DisplayName = "Outbox", Status = "Backlog", Message = "15 messages pending", Metrics = [new() { Key = "pending", DisplayName = "Pending", Value = 15, IsCapped = true }] }],
            Warnings = [new() { Code = "outbox_backlog", Message = "Outbox backlog detected", Remediation = "Inspect the outbox worker" }]
        });
        service.GetDeploymentModeRunbookAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfInstanceDeploymentModeRunbookDto { CurrentMode = "MultiTenant", Steps = [], TargetOptions = [] });

        var cut = _ctx.RenderMudComponent<InstanceOperationsSection>();
        cut.WaitForElement(".control-plane-operations__metrics");

        await Assert.That(cut.Find("[aria-label='Operations warnings']").TextContent).Contains("Inspect the outbox worker");
        await Assert.That(cut.Find(".control-plane-operations__metrics dd").TextContent).Contains("15");
        await Assert.That(cut.Find(".control-plane-operations__metrics dd").TextContent).Contains("capped");
        await Assert.That(cut.Find(".control-plane-operations__list h3").TextContent).IsEqualTo("Outbox");
    }

    [Test]
    public async Task DeploymentTransitionRequiresHalAndExactConfirmationAndSendsReason()
    {
        var service = _ctx.AddMockService<IInstanceOperationsService>();
        service.GetOperationsAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfInstanceOperationsDto());
        service.GetDeploymentModeRunbookAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfInstanceDeploymentModeRunbookDto
        {
            CurrentMode = "SingleTenant",
            ActiveTenantCount = 1,
            Steps = [new() { Key = "backup", Title = "Back up instance", Description = "Verify a recoverable backup" }],
            TargetOptions = [new() { TargetMode = "MultiTenant", Label = "Multi-tenant", Allowed = true, ConfirmationText = "ENABLE MULTI TENANT" }],
            _links = Links(InstanceAdminLinkRelations.TransitionToMultiTenant)
        });
        var dispatched = new TaskCompletionSource<(string Mode, string Confirmation, string? Reason)>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TransitionDeploymentModeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                dispatched.TrySetResult((call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<string?>(2)));
                return new BaseCommandResponseOfInstanceDeploymentModeTransitionDto { Success = true };
            });

        var cut = _ctx.RenderMudComponent<InstanceOperationsSection>();
        cut.WaitForElement("#deployment-mode-confirmation");
        var submit = cut.Find("button[aria-label='Run deployment mode transition runbook']");
        await Assert.That(submit.HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Find("[aria-label='Deployment mode runbook steps']").TextContent).Contains("Verify a recoverable backup");
        await cut.Find("#deployment-mode-confirmation").ChangeAsync(new ChangeEventArgs { Value = "ENABLE MULTI TENANT" });
        await cut.Find("#deployment-mode-reason").ChangeAsync(new ChangeEventArgs { Value = "Enable community hosting" });
        await Assert.That(cut.Find("button[aria-label='Run deployment mode transition runbook']").HasAttribute("disabled")).IsFalse();
        await cut.Find("button[aria-label='Run deployment mode transition runbook']").ClickAsync(new MouseEventArgs());

        var request = await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(request.Mode).IsEqualTo("MultiTenant");
        await Assert.That(request.Confirmation).IsEqualTo("ENABLE MULTI TENANT");
        await Assert.That(request.Reason).IsEqualTo("Enable community hosting");
        await Assert.That(cut.Find(".control-plane-operations__command-result").GetAttribute("role")).IsEqualTo("status");
    }

    private static IDictionary<string, HalLink> Links(params string[] relations) =>
        relations.ToDictionary(relation => relation, relation => new HalLink { Href = $"/api/admin/instance/{relation}", Method = "GET" }, StringComparer.OrdinalIgnoreCase);
}
