using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Pages.Admin.Instance.ControlPlane;
using Explore.Blazor.Client.Routing.ControlPlane;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class ControlPlaneOverviewPageTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public ControlPlaneOverviewPageTests()
    {
        _ctx.Services.AddEventControlPlaneClient();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Render_WhenNoHostApiAdapterRegistered_ShowsFailClosedState()
    {
        var overviewService = Substitute.For<IControlPlaneOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HalResourceOfControlPlaneOverviewDto>(
                new InvalidOperationException("Generated API client unavailable.")));
        _ctx.Services.AddSingleton(overviewService);

        var cut = _ctx.Render<ControlPlaneOverviewPage>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Control-plane API unavailable", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected the control-plane overview to render its fail-closed state.");
            }
        });

        await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Event Control Plane");
        await Assert.That(cut.Markup).Contains("The control-plane API is currently unavailable.");
        await Assert.That(cut.Markup).DoesNotContain("Generated API client unavailable.");
    }

    [Test]
    public async Task Overview_PlansNavigationFollowsHalLinkPresence()
    {
        var overviewService = Substitute.For<IControlPlaneOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfControlPlaneOverviewDto
        {
            DeploymentMode = "MultiTenant",
            Version = "1.0.0",
            PublicOrigin = "https://events.example.test",
            AdminOrigin = "https://admin.example.test",
            _links = Links(ControlPlaneLinkRelations.Plans)
        });
        _ctx.Services.AddSingleton(overviewService);

        var cut = _ctx.Render<ControlPlaneOverviewPage>();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='View tenant plans']"));

        cut.Find("button[aria-label='View tenant plans']").Click();

        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri)
            .EndsWith(ControlPlaneRoutes.Plans);
    }

    [Test]
    public async Task Overview_WithoutPlansLinkHidesPlansNavigation()
    {
        var overviewService = Substitute.For<IControlPlaneOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfControlPlaneOverviewDto
        {
            DeploymentMode = "MultiTenant"
        });
        _ctx.Services.AddSingleton(overviewService);

        var cut = _ctx.Render<ControlPlaneOverviewPage>();
        cut.WaitForAssertion(() => cut.Find("[aria-label='Instance summary']"));

        await Assert.That(cut.FindAll("button[aria-label='View tenant plans']")).IsEmpty();
    }

    [Test]
    public async Task DomainsPage_RendersOperatorManagedDnsAndOnlySupportedHalDeepLink()
    {
        var domainService = Substitute.For<IControlPlaneDomainService>();
        domainService.GetDomainsAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfControlPlaneDomainOverviewDto
        {
            DnsRecords =
            [
                new ControlPlaneDnsRecordDto
                {
                    Name = "admin.example.test",
                    Purpose = "Admin",
                    Status = "Pending",
                    Target = "control-plane.example.internal",
                    Guidance = "DNS TXT record is pending."
                },
                new ControlPlaneDnsRecordDto
                {
                    Name = "public.example.test",
                    Purpose = "Public",
                    Status = "Verified"
                }
            ],
            _links = Links(
                ControlPlaneLinkRelations.Edit,
                ControlPlaneLinkRelations.Verify,
                ControlPlaneLinkRelations.Test)
        });
        _ctx.Services.AddSingleton(domainService);

        var cut = _ctx.Render<ControlPlaneDomainsPage>();
        cut.WaitForAssertion(() => cut.Find("a[aria-label='Edit domain governance']"));

        await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Domains");
        await Assert.That(cut.Find("a[aria-label='Edit domain governance']").GetAttribute("href"))
            .IsEqualTo("/settings/instance?section=domain");
        await Assert.That(cut.Markup).Contains("DNS verification is operator-managed.");
        await Assert.That(cut.FindAll("button[aria-label^='Verify ']")).IsEmpty();
        await Assert.That(cut.FindAll("button[aria-label^='Test ']")).IsEmpty();
        await Assert.That(cut.FindAll("button[aria-label^='Retry ']")).IsEmpty();
    }

    [Test]
    public async Task OperationsPage_WhenNoHostApiAdapterRegistered_ShowsFailClosedState()
    {
        var operationsService = Substitute.For<IControlPlaneOperationsService>();
        var unavailable = new InvalidOperationException("Generated API client unavailable.");
        operationsService.GetOperationsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HalResourceOfControlPlaneOperationsDto>(unavailable));
        operationsService.GetDeploymentModeRunbookAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HalResourceOfControlPlaneDeploymentModeRunbookDto>(unavailable));
        _ctx.Services.AddSingleton(operationsService);

        var cut = _ctx.Render<ControlPlaneOperationsPage>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Control-plane API unavailable", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected operations page to render its fail-closed state.");
            }
        });

        await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Operations");
        await Assert.That(cut.Markup).Contains("Operations data is currently unavailable.");
        await Assert.That(cut.Markup).DoesNotContain("Generated API client unavailable.");
    }

    [Test]
    public async Task OperationsPage_RendersStatusesWarningsAndMetrics()
    {
        var operationsService = Substitute.For<IControlPlaneOperationsService>();
        operationsService.GetOperationsAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfControlPlaneOperationsDto
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
                    Metrics = [new ControlPlaneOperationMetricDto { Key = "pending", DisplayName = "Pending", Value = 15, IsCapped = true }]
                }
            ],
            Warnings = [new Warnings5 { Code = "outbox_backlog", Message = "Outbox backlog detected.", Remediation = "Inspect the outbox worker." }],
            _links = Links(ControlPlaneLinkRelations.Self)
        });
        operationsService.GetDeploymentModeRunbookAsync(Arg.Any<CancellationToken>()).Returns(
            new HalResourceOfControlPlaneDeploymentModeRunbookDto
            {
                CurrentMode = "MultiTenant",
                TargetOptions = [],
                Steps = []
            });
        _ctx.Services.AddSingleton(operationsService);

        var cut = _ctx.Render<ControlPlaneOperationsPage>();
        cut.WaitForAssertion(() => cut.Find(".control-plane-operations__list"));

        await Assert.That(cut.Markup).Contains("Outbox backlog detected.");
        await Assert.That(cut.Markup).Contains("Inspect the outbox worker.");
        await Assert.That(cut.Markup).Contains("Pending");
        await Assert.That(cut.Markup).Contains("capped");
    }

    private static IDictionary<string, HalLink> Links(params string[] relations) =>
        relations.ToDictionary(
            relation => relation,
            relation => new HalLink { Href = $"/control-plane/{relation}", Method = "POST" },
            StringComparer.OrdinalIgnoreCase);
}
