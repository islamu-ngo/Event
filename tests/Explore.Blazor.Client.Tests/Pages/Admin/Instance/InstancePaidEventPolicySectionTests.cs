using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.PaidEventPolicies;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;

namespace Explore.Blazor.Client.Tests.Pages.Admin.Instance;

public sealed class InstancePaidEventPolicySectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IPaidEventPolicyService _service;
    private readonly IAccessibilityAnnouncerService _announcer;

    public InstancePaidEventPolicySectionTests()
    {
        _service = _ctx.AddMockService<IPaidEventPolicyService>();
        _announcer = _ctx.AddMockService<IAccessibilityAnnouncerService>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task RenderWithoutEditRelationShowsReadOnlyNamedPolicy()
    {
        _service.GetInstanceAsync(Arg.Any<CancellationToken>()).Returns(Policy());

        var cut = _ctx.RenderMudComponent<InstancePaidEventPolicySection>();

        cut.WaitForElement("[data-testid='paid-policy-read-only']");
        await Assert.That(cut.FindAll("[data-testid='save-instance-paid-policy']")).IsEmpty();
        await Assert.That(cut.Find("h3").TextContent).IsEqualTo("Paid events");
        await Assert.That(cut.Markup).Contains("Organizations");
        await Assert.That(cut.Markup).Contains("Card dispute rights are not waived");
        await Assert.That(cut.Markup).DoesNotContain("ExternalAccountId");
    }

    [Test]
    public async Task ExactEditRelationAllowsSaveAndReloadsAuthority()
    {
        HalResourceOfPaidEventPolicyDto initial = Policy(editable: true);
        HalResourceOfPaidEventPolicyDto authoritative = Policy(editable: true);
        authoritative.DefaultCurrencyCode = "USD";
        _service.GetInstanceAsync(Arg.Any<CancellationToken>()).Returns(initial, authoritative);
        _service.UpdateInstanceAsync(Arg.Any<RevisePaidEventPolicyDto>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = _ctx.RenderMudComponent<InstancePaidEventPolicySection>();
        cut.WaitForElement("[data-testid='save-instance-paid-policy']").Click();

        var status = cut.WaitForElement("[data-testid='paid-policy-message'][role='status']");
        await Assert.That(status.TextContent).Contains("saved");
        await _service.Received(1).UpdateInstanceAsync(
            Arg.Is<RevisePaidEventPolicyDto>(request =>
                request.RefundProtectionIds!.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7 })
                && request.AllowedOrganizerKindIds!.Contains(2)),
            Arg.Any<CancellationToken>());
        await _service.Received(2).GetInstanceAsync(Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("Paid-event ceiling saved.");
    }

    [Test]
    public async Task EmptyOrganizerSelectionBlocksWriteWithBoundedGuidance()
    {
        HalResourceOfPaidEventPolicyDto resource = Policy(editable: true);
        resource.AllowedOrganizerKindIds = [];
        _service.GetInstanceAsync(Arg.Any<CancellationToken>()).Returns(resource);

        var cut = _ctx.RenderMudComponent<InstancePaidEventPolicySection>();
        cut.WaitForElement("[data-testid='save-instance-paid-policy']").Click();

        var alert = cut.WaitForElement("[data-testid='paid-policy-message'][role='alert']");
        await Assert.That(alert.TextContent).Contains("Select at least one eligible organizer kind.");
        await _service.DidNotReceive().UpdateInstanceAsync(Arg.Any<RevisePaidEventPolicyDto>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConflictReloadsAuthorityWithoutLeakingRawDetail()
    {
        HalResourceOfPaidEventPolicyDto initial = Policy(editable: true);
        HalResourceOfPaidEventPolicyDto authoritative = Policy(editable: true);
        authoritative.DefaultCurrencyCode = "USD";
        _service.GetInstanceAsync(Arg.Any<CancellationToken>()).Returns(initial, authoritative);
        _service.UpdateInstanceAsync(Arg.Any<RevisePaidEventPolicyDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ApiFailure(409, "raw provider conflict"));

        var cut = _ctx.RenderMudComponent<InstancePaidEventPolicySection>();
        cut.WaitForElement("[data-testid='save-instance-paid-policy']").Click();

        var alert = cut.WaitForElement("[data-testid='paid-policy-message'][role='alert']");
        await Assert.That(alert.TextContent).Contains("changed elsewhere");
        await Assert.That(cut.Markup).DoesNotContain("raw provider conflict");
        await _service.Received(2).GetInstanceAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisposeDuringPendingLoadCancelsWithoutErrorAnnouncement()
    {
        var completion = new TaskCompletionSource<HalResourceOfPaidEventPolicyDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = default;
        _service.GetInstanceAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            observedToken = call.ArgAt<CancellationToken>(0);
            return completion.Task;
        });
        var cut = _ctx.RenderMudComponent<InstancePaidEventPolicySection>();
        cut.WaitForState(() => observedToken.CanBeCanceled);

        cut.Instance.Dispose();
        cut.Dispose();

        await Assert.That(observedToken.IsCancellationRequested).IsTrue();
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
        completion.TrySetCanceled(observedToken);
    }

    private static HalResourceOfPaidEventPolicyDto Policy(bool editable = false)
    {
        var resource = new HalResourceOfPaidEventPolicyDto
        {
            VersionNumber = 1,
            IsActive = true,
            IsPaymentsEnabled = true,
            RequiresLocalVerification = true,
            AllowedOrganizerKindIds = [1, 2, 4],
            AllowedCurrencyCodes = ["EUR", "USD"],
            DefaultCurrencyCode = "EUR",
            RefundProtectionIds = [1, 2, 3, 4, 5, 6, 7],
            CurrencyRiskLimits =
            [
                new PaidEventPolicyCurrencyRiskLimitDto
                {
                    CurrencyCode = "EUR",
                    PerEventSalesCeilingMinor = 500_000,
                    RollingOrganizerSalesCeilingMinor = 1_000_000,
                    HighValueReviewThresholdMinor = 250_000
                },
                new PaidEventPolicyCurrencyRiskLimitDto { CurrencyCode = "USD" }
            ],
            RequiresFirstPaidEventReview = true,
            FarFutureReviewThresholdDays = 180
        };
        if (editable)
        {
            resource._links = new Dictionary<string, HalLink>
            {
                ["edit"] = new() { Href = "/api/instance/settings/paid-event-policy", Method = "PUT" }
            };
        }
        return resource;
    }

    private static ApiException<ProblemDetails> ApiFailure(int statusCode, string rawResponse) => new(
        "Conflict",
        statusCode,
        rawResponse,
        new Dictionary<string, IEnumerable<string>>(),
        new ProblemDetails { Detail = rawResponse },
        null);
}
