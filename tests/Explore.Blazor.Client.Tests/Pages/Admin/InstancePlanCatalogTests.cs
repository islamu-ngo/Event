using Blazouter.Enums;
using Blazouter.Extensions;
using Blazouter.Models;
using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Pages.Admin.Instance;
using Explore.Blazor.Client.Routing.ControlPlane;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class InstancePlanCatalogTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IControlPlanePlanCatalogService _catalog = Substitute.For<IControlPlanePlanCatalogService>();

    public InstancePlanCatalogTests()
    {
        _ctx.Services.AddSingleton(_catalog);
        _ctx.Services.AddBlazouter();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Plans_LoadingThenEmpty_RendersAccessibleStates()
    {
        var pending = new TaskCompletionSource<HalCollectionResourceOfControlPlaneTenantPlanListItemDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _catalog.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(pending.Task);

        var cut = _ctx.RenderMudComponent<InstancePlans>();

        await Assert.That(cut.Find("[role='status'][aria-live='polite']").TextContent).Contains("Loading plans");
        pending.SetResult(PlanCollection([]));
        cut.WaitForAssertion(() => cut.Markup.Contains("No tenant plans", StringComparison.Ordinal));
    }

    [Test]
    public async Task Plans_ThrownLoad_RendersSafeFailureWithoutRawException()
    {
        _catalog.GetPlansAsync(Arg.Any<CancellationToken>())
            .Returns<Task<HalCollectionResourceOfControlPlaneTenantPlanListItemDto>>(_ =>
                throw new InvalidOperationException("raw catalog credential"));

        var cut = _ctx.RenderMudComponent<InstancePlans>();

        cut.WaitForAssertion(() => cut.Find("[role='alert']"));
        await Assert.That(cut.Markup).Contains("Tenant plans are currently unavailable.");
        await Assert.That(cut.Markup).DoesNotContain("raw catalog credential");
    }

    [Test]
    public async Task Plans_RendersReadOnlyFactsAndOnlySelfLinkedNavigation()
    {
        _catalog.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(PlanCollection(
            [
                Summary("enterprise", "Enterprise", Links(ControlPlaneLinkRelations.Self)),
                Summary("community", "Community")
            ]));

        var cut = _ctx.RenderMudComponent<InstancePlans>();
        cut.WaitForAssertion(() => cut.Find("[data-plan-key='enterprise']"));

        await Assert.That(cut.FindAll("[aria-label^='View plan ']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[aria-label='View plan Enterprise']").GetAttribute("href"))
            .IsEqualTo("/admin/instance/plans/enterprise");
        await Assert.That(cut.Find("[data-plan-key='enterprise'] code").GetAttribute("dir")).IsEqualTo("ltr");
        await Assert.That(cut.Find("[data-plan-key='enterprise'] [data-plan-description]").GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(cut.Find("#plan-catalog-heading").GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(cut.Find(".instance-plans__section-heading span").GetAttribute("dir")).IsEqualTo("ltr");
        await Assert.That(cut.Markup).DoesNotContain("Create plan");
        await Assert.That(cut.FindAll("[aria-label^='Create plan']")).IsEmpty();
        await Assert.That(cut.FindAll("[aria-label^='Publish plan']")).IsEmpty();
        await Assert.That(cut.FindAll("[aria-label^='Archive plan']")).IsEmpty();
    }

    [Test]
    public async Task PlanDetail_RendersVersionsSettingsAndQuotasWithAccessibleOverflow()
    {
        _catalog.GetPlanAsync("enterprise", Arg.Any<CancellationToken>())
            .Returns(DetailWithVersion());

        var cut = _ctx.RenderMudComponent<InstancePlanDetail>(parameters => parameters.Add(p => p.Key, "enterprise"));
        cut.WaitForAssertion(() => cut.Find("[data-plan-version='3']"));

        await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Enterprise");
        await Assert.That(cut.Find("[data-plan-description]").GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(cut.Find("[data-plan-key]").GetAttribute("dir")).IsEqualTo("ltr");
        await Assert.That(cut.FindAll("[role='region'][tabindex='0']").Count).IsEqualTo(2);
        await Assert.That(cut.Find("[aria-label='Version 3 settings'] code").GetAttribute("dir")).IsEqualTo("ltr");
        await Assert.That(cut.Find("[aria-label='Version 3 quotas'] code").GetAttribute("dir")).IsEqualTo("ltr");
        await Assert.That(cut.Markup).Contains("ai.enabled");
        await Assert.That(cut.Markup).Contains("storage.bytes");
        await Assert.That(cut.FindAll("[aria-label^='Publish plan']")).IsEmpty();
        await Assert.That(cut.FindAll("[aria-label^='Archive plan']")).IsEmpty();
        await _catalog.Received(1).GetPlanAsync("enterprise", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PlanDetail_BlazouterRoute_LoadsMatchedKey()
    {
        _catalog.GetPlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(DetailWithVersion());
        var routes = new List<RouteConfig>
        {
            new()
            {
                Path = "/admin/instance/plans/:Key",
                Component = typeof(InstancePlanDetail),
                Transition = RouteTransition.None
            }
        };
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/admin/instance/plans/enterprise");

        var cut = _ctx.Render<Blazouter.Components.Router>(parameters => parameters
            .Add(component => component.Routes, routes));

        cut.WaitForAssertion(() => cut.Find("[data-plan-key]"));
        await _catalog.Received(1).GetPlanAsync("enterprise", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PlanDetail_WithoutVersions_RendersEmptyState()
    {
        _catalog.GetPlanAsync("community", Arg.Any<CancellationToken>()).Returns(new HalResourceOfControlPlaneTenantPlanDetailDto
        {
            Id = Guid.NewGuid(),
            Key = "community",
            DisplayName = "Community",
            Versions = []
        });

        var cut = _ctx.RenderMudComponent<InstancePlanDetail>(parameters => parameters.Add(p => p.Key, "community"));

        cut.WaitForAssertion(() => cut.Markup.Contains("No plan versions", StringComparison.Ordinal));
        await Assert.That(cut.Find("[role='status'] [dir='auto']").TextContent).Contains("No plan versions");
    }

    [Test]
    public async Task PlanDetail_ThrownLoad_RendersSafeFailureWithoutRawException()
    {
        _catalog.GetPlanAsync("enterprise", Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfControlPlaneTenantPlanDetailDto>>(_ =>
                throw new InvalidOperationException("raw plan database error"));

        var cut = _ctx.RenderMudComponent<InstancePlanDetail>(parameters => parameters.Add(p => p.Key, "enterprise"));

        cut.WaitForAssertion(() => cut.Find("[role='alert']"));
        await Assert.That(cut.Markup).Contains("Tenant plan details are currently unavailable.");
        await Assert.That(cut.Markup).DoesNotContain("raw plan database error");
    }

    private static HalResourceOfControlPlaneTenantPlanListItemDto Summary(
        string key,
        string name,
        IReadOnlyDictionary<string, HalLink>? links = null) => new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            DisplayName = name,
            Description = "خطة المستأجر المتاحة.",
            LatestVersionNumber = 4,
            PublishedVersionNumber = 3,
            PriceAmount = 199.95,
            CurrencyCode = "EUR",
            BillingPeriod = "monthly",
            IsActiveForProvisioning = true,
            _links = links is null ? null : new Dictionary<string, HalLink>(links)
        };

    private static HalResourceOfControlPlaneTenantPlanDetailDto DetailWithVersion() => new()
    {
        Id = Guid.NewGuid(),
        Key = "enterprise",
        DisplayName = "Enterprise",
        Description = "خطة مؤسسية متعددة المستأجرين.",
        Versions =
        [
            new ControlPlaneTenantPlanVersionDto
            {
                Id = Guid.NewGuid(),
                VersionNumber = 3,
                StatusId = 2,
                StatusCode = "Published",
                PriceAmount = 199.95,
                CurrencyCode = "EUR",
                BillingPeriod = "monthly",
                IsActiveForProvisioning = true,
                Settings = [new ControlPlaneTenantPlanSettingDto { Key = "ai.enabled", JsonValue = "true", IsLocked = true }],
                Quotas = [new ControlPlaneTenantPlanQuotaDto { Key = "storage.bytes", Limit = 10_000 }]
            }
        ]
    };

    private static HalCollectionResourceOfControlPlaneTenantPlanListItemDto PlanCollection(
        IReadOnlyCollection<HalResourceOfControlPlaneTenantPlanListItemDto> plans) => new()
        {
            TotalCount = plans.Count,
            _embedded = new HalCollectionEmbeddedOfControlPlaneTenantPlanListItemDto { Items = plans.ToArray() }
        };

    private static Dictionary<string, HalLink> Links(params string[] relations) =>
        relations.ToDictionary(
            relation => relation,
            relation => new HalLink { Href = $"/control-plane/{relation}", Method = "GET" },
            StringComparer.OrdinalIgnoreCase);
}
