using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;

namespace Explore.Blazor.Client.Tests.Pages.Admin.Instance;

public sealed class InstanceMonetizationSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IPlatformMonetizationService _service;
    private readonly IAccessibilityAnnouncerService _announcer;

    public InstanceMonetizationSectionTests()
    {
        _service = _ctx.AddMockService<IPlatformMonetizationService>();
        _announcer = _ctx.AddMockService<IAccessibilityAnnouncerService>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_WithoutEditRelation_ShowsValuesReadOnly()
    {
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(Settings());

        var cut = _ctx.RenderMudComponent<InstanceMonetizationSection>();

        cut.WaitForElement("[data-testid='monetization-read-only']");
        await Assert.That(cut.Markup).Contains("Platform support");
        await Assert.That(cut.FindAll("[data-testid='save-monetization-settings']")).IsEmpty();
    }

    [Test]
    public async Task Render_WithExactEditRelation_ShowsSaveAffordance()
    {
        var settings = Settings();
        settings._links = new Dictionary<string, HalLink>
        {
            ["edit"] = new HalLink { Href = "/api/instance/monetization", Method = "PUT" }
        };
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(settings);

        var cut = _ctx.RenderMudComponent<InstanceMonetizationSection>();

        cut.WaitForElement("[data-testid='save-monetization-settings']");
        await Assert.That(cut.FindAll("[data-testid='monetization-read-only']")).IsEmpty();
        await Assert.That(cut.Find("h2").TextContent).IsEqualTo("Monetization");
        await Assert.That(cut.FindAll("h3").Select(heading => heading.TextContent)).IsEquivalentTo([
            "Fixed charges",
            "Contribution options"
        ]);
        await _announcer.Received(1).AnnouncePoliteAsync("Monetization settings loaded.");
    }

    [Test]
    public async Task Save_WhenSuccessful_ReloadsAuthorityAndAnnouncesStatus()
    {
        var initial = EditableSettings();
        var authoritative = EditableSettings();
        authoritative.ContributionHeading = "Authoritative support";
        authoritative.FeeVersion = 8;
        authoritative.ContributionVersion = 9;
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(initial, authoritative);
        _service.UpdateAsync(Arg.Any<UpdatePlatformMonetizationSettingsDto>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = _ctx.RenderMudComponent<InstanceMonetizationSection>();
        cut.WaitForElement("[data-testid='save-monetization-settings']").Click();

        var status = cut.WaitForElement("[data-testid='monetization-save-message'][role='status']");
        await Assert.That(status.TextContent).Contains("saved");
        await Assert.That(cut.Markup).Contains("Authoritative support");
        await _service.Received(2).GetAsync(Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("Monetization settings saved.");
    }

    [Test]
    public async Task Save_WhenConflict_ReloadsAuthorityAndAnnouncesBoundedError()
    {
        var initial = EditableSettings();
        var authoritative = EditableSettings();
        authoritative.ContributionHeading = "Authoritative support";
        authoritative.FeeVersion = 8;
        authoritative.ContributionVersion = 9;
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(initial, authoritative, authoritative);
        _service.UpdateAsync(Arg.Any<UpdatePlatformMonetizationSettingsDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ApiFailure(409, "raw conflict provider detail"));
        var cut = _ctx.RenderMudComponent<InstanceMonetizationSection>();

        cut.WaitForElement("[data-testid='save-monetization-settings']").Click();

        var alert = cut.WaitForElement("[data-testid='monetization-save-message'][role='alert']");
        await Assert.That(alert.TextContent).Contains("changed elsewhere");
        await Assert.That(cut.Markup).Contains("Authoritative support");
        await Assert.That(cut.Markup).DoesNotContain("raw conflict provider detail");
        await _service.Received(2).GetAsync(Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnounceAssertiveAsync(
            "Monetization settings changed elsewhere. The latest values were reloaded.");

        _service.UpdateAsync(Arg.Any<UpdatePlatformMonetizationSettingsDto>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        cut.Find("[data-testid='save-monetization-settings']").Click();
        await _service.Received(1).UpdateAsync(
            Arg.Is<UpdatePlatformMonetizationSettingsDto>(request =>
                request.ExpectedFeeVersion == 8 && request.ExpectedContributionVersion == 9),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Save_WhenBadRequest_ShowsBoundedValidationGuidance()
    {
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(EditableSettings());
        _service.UpdateAsync(Arg.Any<UpdatePlatformMonetizationSettingsDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ApiFailure(400, "raw validation provider detail"));
        var cut = _ctx.RenderMudComponent<InstanceMonetizationSection>();

        cut.WaitForElement("[data-testid='save-monetization-settings']").Click();

        var alert = cut.WaitForElement("[data-testid='monetization-save-message'][role='alert']");
        await Assert.That(alert.TextContent).Contains("Review the monetization values and try again.");
        await Assert.That(cut.Markup).DoesNotContain("raw validation provider detail");
        await _announcer.Received(1).AnnounceAssertiveAsync("Review the monetization values and try again.");
    }

    [Test]
    public async Task Save_WhenGeneralFailure_ShowsSafeAlertWithoutReloading()
    {
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(EditableSettings());
        _service.UpdateAsync(Arg.Any<UpdatePlatformMonetizationSettingsDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("raw secret provider detail"));
        var cut = _ctx.RenderMudComponent<InstanceMonetizationSection>();

        cut.WaitForElement("[data-testid='save-monetization-settings']").Click();

        var alert = cut.WaitForElement("[data-testid='monetization-save-message'][role='alert']");
        await Assert.That(alert.TextContent).Contains("Monetization settings could not be saved.");
        await Assert.That(cut.Markup).DoesNotContain("raw secret provider detail");
        await _service.Received(1).GetAsync(Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnounceAssertiveAsync("Monetization settings could not be saved.");
    }

    [Test]
    public async Task Load_WhenGeneralFailure_FailsClosedAndAnnouncesSafeAlert()
    {
        _service.GetAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("raw load provider detail"));

        var cut = _ctx.RenderMudComponent<InstanceMonetizationSection>();

        var alert = cut.WaitForElement("[data-testid='monetization-load-error'][role='alert']");
        await Assert.That(alert.TextContent).Contains("Monetization settings could not be loaded.");
        await Assert.That(cut.Markup).DoesNotContain("raw load provider detail");
        await Assert.That(cut.FindAll("[data-testid='save-monetization-settings']")).IsEmpty();
        await _announcer.Received(1).AnnounceAssertiveAsync("Monetization settings could not be loaded.");
    }

    [Test]
    public async Task Dispose_DuringPendingLoad_CancelsRequestWithoutShowingError()
    {
        var completion = new TaskCompletionSource<HalResourceOfPlatformMonetizationSettingsDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = default;
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            observedToken = call.ArgAt<CancellationToken>(0);
            return completion.Task;
        });
        var cut = _ctx.RenderMudComponent<InstanceMonetizationSection>();
        cut.WaitForAssertion(() => Assert.That(observedToken.CanBeCanceled).IsTrue());

        cut.Instance.Dispose();
        cut.Dispose();

        await Assert.That(observedToken.IsCancellationRequested).IsTrue();
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
        completion.TrySetCanceled(observedToken);
    }

    private static HalResourceOfPlatformMonetizationSettingsDto Settings() => new()
    {
        FeeEnabled = true,
        FeeBasisPoints = 250,
        FeeVersion = 3,
        ContributionEnabled = true,
        ContributionHeading = "Platform support",
        ContributionBody = "Help keep this service available.",
        ContributionVersion = 4
    };

    private static HalResourceOfPlatformMonetizationSettingsDto EditableSettings()
    {
        var settings = Settings();
        settings._links = new Dictionary<string, HalLink>
        {
            ["edit"] = new HalLink { Href = "/api/instance/monetization", Method = "PUT" }
        };
        return settings;
    }

    private static ApiException<ProblemDetails> ApiFailure(int statusCode, string rawResponse) => new(
        statusCode == 409 ? "Conflict" : "Bad Request",
        statusCode,
        rawResponse,
        new Dictionary<string, IEnumerable<string>>(),
        new ProblemDetails { Detail = rawResponse },
        null);
}
