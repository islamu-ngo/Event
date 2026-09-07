using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.PaidEventPolicies;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantPaidEventPolicySectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IPaidEventPolicyService _service;
    private readonly IAccessibilityAnnouncerService _announcer;

    public TenantPaidEventPolicySectionTests()
    {
        _service = _ctx.AddMockService<IPaidEventPolicyService>();
        _announcer = _ctx.AddMockService<IAccessibilityAnnouncerService>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task EmptyTenantIdFailsClosedWithoutServiceCall()
    {
        var cut = _ctx.RenderMudComponent<TenantPaidEventPolicySection>();

        cut.WaitForElement("[data-testid='tenant-paid-policy-load-error']");
        await _service.DidNotReceive().GetTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await Assert.That(cut.FindAll("[data-testid='save-tenant-paid-policy']")).IsEmpty();
    }

    [Test]
    public async Task MissingCeilingOrEffectivePolicyFailsClosed()
    {
        var tenantId = Guid.CreateVersion7();
        _service.GetTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new HalResourceOfTenantPaidEventPolicyConfigurationDto
        {
        });

        var cut = Render(tenantId);

        var alert = cut.WaitForElement("[data-testid='tenant-paid-policy-load-error']");
        await Assert.That(alert.TextContent).Contains("could not be loaded");
        await Assert.That(cut.FindAll("[data-testid='save-tenant-paid-policy']")).IsEmpty();
    }

    [Test]
    public async Task ReadOnlyConfigurationShowsOnlyCeilingBackedChoicesAndNamedFloors()
    {
        var tenantId = Guid.CreateVersion7();
        _service.GetTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(Configuration(tenantId, editable: false));

        var cut = Render(tenantId);

        cut.WaitForElement("[data-testid='tenant-paid-policy-read-only']");
        await Assert.That(cut.Markup).Contains("Organizations");
        await Assert.That(cut.Markup).DoesNotContain(">Groups<");
        await Assert.That(cut.Markup).Contains("EUR");
        await Assert.That(cut.Markup).DoesNotContain(">USD<");
        await Assert.That(cut.Markup).Contains("Card dispute rights are not waived");
        await Assert.That(cut.FindAll("[data-testid='save-tenant-paid-policy']")).IsEmpty();
    }

    [Test]
    public async Task AuthorityFactsExplainManifestAndSovereignBoundariesWithoutAddingActions()
    {
        var tenantId = Guid.CreateVersion7();
        HalResourceOfTenantPaidEventPolicyConfigurationDto configuration =
            Configuration(
                tenantId,
                editable: false,
                effectiveValuesInherited: true,
                hasTenantNarrowing: false);
        _service.GetTenantAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(configuration);

        var cut = Render(tenantId);

        var authority = cut.WaitForElement(
            "[data-testid='tenant-paid-policy-authority']");
        await Assert.That(authority.QuerySelector(
            "#tenant-paid-policy-authority-title")?.TagName).IsEqualTo("H3");
        await Assert.That(authority.QuerySelector(
            "#tenant-paid-policy-manifest-title")?.TagName).IsEqualTo("H4");
        await Assert.That(authority.QuerySelector(
            "#tenant-paid-policy-sovereign-title")?.TagName).IsEqualTo("H4");
        await Assert.That(authority.QuerySelector(
            "[data-testid='tenant-paid-policy-authority-source']")
            ?.GetAttribute("data-source")).IsEqualTo("inherited");
        await Assert.That(authority.QuerySelector(
            "[data-testid='tenant-paid-policy-manifest-facts']")).IsNotNull();
        await Assert.That(authority.QuerySelector(
            "[data-testid='tenant-paid-policy-sovereign-facts']")).IsNotNull();
        foreach (string code in configuration.Authority!.SovereignLockedFields ?? [])
        {
            await Assert.That(authority.QuerySelectorAll("*").Any(element =>
                string.Equals(
                    element.TextContent.Trim(),
                    code,
                    StringComparison.Ordinal))).IsFalse();
        }
        await Assert.That(cut.FindAll("[data-testid='save-tenant-paid-policy']")).IsEmpty();
    }

    [Test]
    public async Task ExactEditRelationSavesNarrowedGeneratedRequestAndReloads()
    {
        var tenantId = Guid.CreateVersion7();
        HalResourceOfTenantPaidEventPolicyConfigurationDto configuration = Configuration(tenantId, editable: true);
        _service.GetTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(configuration, configuration);
        _service.UpdateTenantAsync(tenantId, Arg.Any<RevisePaidEventPolicyDto>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = Render(tenantId);
        cut.WaitForElement("[data-testid='save-tenant-paid-policy']").Click();

        cut.WaitForElement("[data-testid='tenant-paid-policy-message'][role='status']");
        await _service.Received(1).UpdateTenantAsync(
            tenantId,
            Arg.Is<RevisePaidEventPolicyDto>(request =>
                request.AllowedOrganizerKindIds!.SequenceEqual(new[] { 2 })
                && request.AllowedCurrencyCodes!.SequenceEqual(new[] { "EUR" })
                && request.RefundProtectionIds!.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7 })),
            Arg.Any<CancellationToken>());
        await _service.Received(2).GetTenantAsync(tenantId, Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("Tenant paid-event policy saved.");
    }

    [Test]
    public async Task InvalidBroaderOverrideBlocksServiceCallWithSafeMessage()
    {
        var tenantId = Guid.CreateVersion7();
        HalResourceOfTenantPaidEventPolicyConfigurationDto configuration = Configuration(
            tenantId,
            editable: true,
            tenantOrganizerKindIds: [2, 4]);
        _service.GetTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(configuration);

        var cut = Render(tenantId);
        cut.WaitForElement("[data-testid='save-tenant-paid-policy']").Click();

        var alert = cut.WaitForElement("[data-testid='tenant-paid-policy-message'][role='alert']");
        await Assert.That(alert.TextContent).Contains("organizer kinds must stay inside");
        await _service.DidNotReceive().UpdateTenantAsync(Arg.Any<Guid>(), Arg.Any<RevisePaidEventPolicyDto>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisposeDuringPendingLoadCancelsWithoutErrorAnnouncement()
    {
        var tenantId = Guid.CreateVersion7();
        var completion = new TaskCompletionSource<HalResourceOfTenantPaidEventPolicyConfigurationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = default;
        _service.GetTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(call =>
        {
            observedToken = call.ArgAt<CancellationToken>(1);
            return completion.Task;
        });
        var cut = Render(tenantId);
        cut.WaitForState(() => observedToken.CanBeCanceled);

        cut.Instance.Dispose();
        cut.Dispose();

        await Assert.That(observedToken.IsCancellationRequested).IsTrue();
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
        completion.TrySetCanceled(observedToken);
    }

    private IRenderedComponent<TenantPaidEventPolicySection> Render(Guid tenantId) =>
        _ctx.RenderMudComponent<TenantPaidEventPolicySection>(parameters => parameters
            .Add(component => component.TenantId, tenantId));

    private static HalResourceOfTenantPaidEventPolicyConfigurationDto Configuration(
        Guid tenantId,
        bool editable,
        bool effectiveValuesInherited = false,
        bool hasTenantNarrowing = true,
        int[]? tenantOrganizerKindIds = null)
    {
        PaidEventPolicyDto ceiling = Policy([2], ["EUR"]);
        PaidEventPolicyDto tenant = Policy(tenantOrganizerKindIds ?? [2], ["EUR"]);
        return new HalResourceOfTenantPaidEventPolicyConfigurationDto
        {
            ActiveInstanceCeiling = ceiling,
            ActiveTenantOverride = tenant,
            EffectivePolicy = tenant,
            Authority = new PaidEventPolicyAuthorityDto
            {
                InstancePolicyVersion = 2,
                EffectiveValuesInherited = effectiveValuesInherited,
                HasTenantNarrowing = hasTenantNarrowing,
                ManifestOwnedFields =
                [
                    "allowedCurrencyCodes",
                    "allowedOrganizerKindIds",
                    "currencyRiskLimits",
                    "defaultCurrencyCode",
                    "farFutureReviewThresholdDays",
                    "isPaymentsEnabled",
                    "refundProtectionIds",
                    "requiresFirstPaidEventReview",
                    "requiresLocalVerification"
                ],
                SovereignLockedFields =
                [
                    "buyerAcceptance",
                    "chargeType",
                    "connectedAccounts",
                    "disputeHandling",
                    "liability",
                    "negativeBalances",
                    "officialOrigin",
                    "officialStatus",
                    "operatorIdentity",
                    "providerCredentials",
                    "providerHandoff",
                    "providerProfiles",
                    "reconciliation",
                    "refundExecution",
                    "saleControl"
                ]
            },
            _links = editable
                ? new Dictionary<string, HalLink>
                {
                    ["edit"] = new() { Href = $"/api/tenants/{tenantId}/settings/paid-event-policy", Method = "PUT" }
                }
                : null
        };
    }

    private static PaidEventPolicyDto Policy(int[] organizerKindIds, string[] currencies) => new()
    {
        IsPaymentsEnabled = true,
        RequiresLocalVerification = true,
        AllowedOrganizerKindIds = organizerKindIds,
        AllowedCurrencyCodes = currencies,
        DefaultCurrencyCode = currencies[0],
        RefundProtectionIds = [1, 2, 3, 4, 5, 6, 7],
        CurrencyRiskLimits =
        [
            new PaidEventPolicyCurrencyRiskLimitDto
            {
                CurrencyCode = currencies[0],
                PerEventSalesCeilingMinor = 500_000,
                RollingOrganizerSalesCeilingMinor = 1_000_000,
                HighValueReviewThresholdMinor = 250_000
            }
        ],
        RequiresFirstPaidEventReview = true,
        FarFutureReviewThresholdDays = 180
    };
}
