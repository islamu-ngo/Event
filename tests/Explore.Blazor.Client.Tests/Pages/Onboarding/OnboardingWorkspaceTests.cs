using Explore.Blazor.Client.Pages.Onboarding.Components;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public sealed class OnboardingWorkspaceTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Workspace_RendersSemanticSurface_WithOneProjectedHeading()
    {
        var cut = Render(DefaultSteps());

        await Assert.That(cut.FindAll("header").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("nav[aria-label='Setup progress']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("section").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("aside").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("details").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("footer").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.Find("h1").TextContent.Trim()).IsEqualTo("Configure your site");
        await Assert.That(cut.FindAll("main").Count).IsEqualTo(0);
        await Assert.That(cut.Markup.IndexOf("<h1", StringComparison.Ordinal))
            .IsLessThan(cut.Markup.IndexOf("<nav", StringComparison.Ordinal));
    }

    [Test]
    public async Task Progress_UsesOnlyVisibleSteps_ForPositionAndCurrentSemantics()
    {
        var visibleSteps = new[]
        {
            Step("Authentication", "Complete", OnboardingWorkspace.PresentationState.Complete),
            Step("Authorization", "Current step", OnboardingWorkspace.PresentationState.Current, isCurrent: true),
            Step("Readiness", "Upcoming", OnboardingWorkspace.PresentationState.Upcoming)
        };

        var cut = Render(visibleSteps);

        await Assert.That(cut.FindAll("nav ol > li").Count).IsEqualTo(3);
        await Assert.That(cut.Markup).Contains("Step 2 of 3", StringComparison.Ordinal);
        await Assert.That(cut.FindAll("nav[aria-label='Setup progress'] [aria-current='step']").Count).IsEqualTo(1);
        await Assert.That(cut.Find(".onboarding-workspace__current-step").TextContent.Trim())
            .IsEqualTo("Authorization");
    }

    [Test]
    public async Task Progress_RendersVisibleOrdinalForEveryProjectedStep()
    {
        var cut = Render(DefaultSteps());

        var ordinals = cut.FindAll(".onboarding-workspace__summary-desktop .onboarding-workspace__summary-step-number");

        await Assert.That(ordinals.Count).IsEqualTo(4);
        await Assert.That(ordinals.Select(element => element.TextContent.Trim()))
            .IsEquivalentTo(["1", "2", "3", "4"]);
        await Assert.That(ordinals.All(element => element.GetAttribute("aria-hidden") == "true")).IsTrue();
    }

    [Test]
    public async Task Progress_MainSegmentsDoNotExposeNonCurrentStepNames()
    {
        var cut = Render(DefaultSteps());
        var progress = cut.Find("nav[aria-label='Setup progress']");

        await Assert.That(progress.TextContent).Contains("Site profile", StringComparison.Ordinal);
        await Assert.That(progress.TextContent).DoesNotContain("Authentication", StringComparison.Ordinal);
        await Assert.That(progress.TextContent).DoesNotContain("Authorization", StringComparison.Ordinal);
        await Assert.That(progress.TextContent).DoesNotContain("Readiness", StringComparison.Ordinal);
        await Assert.That(progress.QuerySelectorAll("[aria-label*='Authentication']").Length).IsEqualTo(0);
        await Assert.That(progress.QuerySelectorAll("[aria-label*='Authorization']").Length).IsEqualTo(0);
        await Assert.That(progress.QuerySelectorAll("[aria-label*='Readiness']").Length).IsEqualTo(0);
    }

    [Test]
    public async Task Progress_WhenProjectionChanges_RecomputesPositionWithoutStaleState()
    {
        var cut = Render(DefaultSteps());
        var reducedSteps = new[]
        {
            Step("Authorization", "Current step", OnboardingWorkspace.PresentationState.Current, isCurrent: true),
            Step("Readiness", "Upcoming", OnboardingWorkspace.PresentationState.Upcoming)
        };

        cut.Render(parameters => parameters
            .Add(component => component.Steps, reducedSteps));

        await Assert.That(cut.FindAll("nav ol > li").Count).IsEqualTo(2);
        await Assert.That(cut.Markup).Contains("Step 1 of 2", StringComparison.Ordinal);
        await Assert.That(cut.Find(".onboarding-workspace__current-step").TextContent.Trim())
            .IsEqualTo("Authorization");
    }

    [Test]
    public async Task Progress_WhenParentProjectsNoSteps_OmitsNumberedNavigation()
    {
        var cut = Render([]);

        await Assert.That(cut.FindAll("nav[aria-label='Setup progress']").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).DoesNotContain("Step 1 of", StringComparison.Ordinal);
    }

    [Test]
    public async Task SummaryAndFooter_RenderNativeDisclosureButtonAndLink()
    {
        var cut = Render(DefaultSteps(), summaryExpanded: true);

        await Assert.That(cut.Find("details").HasAttribute("open")).IsTrue();
        await Assert.That(cut.Find("summary").TextContent.Trim()).IsEqualTo("Setup menu");
        await Assert.That(cut.Find(".onboarding-workspace__summary-desktop h2").TextContent.Trim()).IsEqualTo("Setup summary");
        await Assert.That(cut.FindAll("footer button, footer a").Count).IsEqualTo(2);
        await Assert.That(cut.Find("footer").TextContent).Contains("Continue", StringComparison.Ordinal);
        await Assert.That(cut.Find("a[href='/setup/exit']").TextContent.Trim()).IsEqualTo("Exit");
        await Assert.That(cut.Find("button").HasAttribute("role")).IsFalse();
        await Assert.That(cut.Find("a[href='/setup/exit']").HasAttribute("role")).IsFalse();
    }

    [Test]
    [Arguments(OnboardingWorkspace.PresentationState.Loading, "Checking setup status")]
    [Arguments(OnboardingWorkspace.PresentationState.Error, "Setup status could not be loaded")]
    [Arguments(OnboardingWorkspace.PresentationState.Locked, "Resolve the blocking configuration")]
    [Arguments(OnboardingWorkspace.PresentationState.Skipped, "Managed by deployment")]
    [Arguments(OnboardingWorkspace.PresentationState.Configured, "Configuration detected")]
    [Arguments(OnboardingWorkspace.PresentationState.Complete, "Setup complete")]
    [Arguments(OnboardingWorkspace.PresentationState.Current, "Current step")]
    [Arguments(OnboardingWorkspace.PresentationState.Upcoming, "Not started")]
    [Arguments(OnboardingWorkspace.PresentationState.Dirty, "Unsaved changes")]
    public async Task State_RendersVisibleTextIndependentOfColor(
        OnboardingWorkspace.PresentationState state,
        string statusText)
    {
        var cut = Render(
        [
            Step("Authentication", statusText, state, isCurrent: true)
        ], statusText: statusText);
        var status = cut.Find("[role='status']");
        var summaryStep = cut.Find(".onboarding-workspace__summary-desktop .onboarding-summary__step");
        var summaryStatus = cut.Find(".onboarding-workspace__summary-desktop .onboarding-summary__step-status");

        await Assert.That(status.TextContent.Trim()).IsEqualTo(statusText);
        await Assert.That(status.GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(summaryStep.GetAttribute("data-state")).IsEqualTo(state.ToString());
        await Assert.That(summaryStatus.TextContent.Trim()).IsEqualTo(statusText);
    }

    private IRenderedComponent<OnboardingWorkspace> Render(
        IReadOnlyList<OnboardingWorkspace.StepDescriptor> steps,
        bool summaryExpanded = false,
        string statusText = "Current step") =>
        _ctx.Render<OnboardingWorkspace>(parameters => parameters
            .Add(component => component.Steps, steps)
            .Add(component => component.ProgressLabel, "Setup progress")
            .Add(component => component.StepPositionFormatter, (current, total) => $"Step {current} of {total}")
            .Add(component => component.HeadingId, "workspace-heading")
            .Add(component => component.SummaryLabel, "Setup summary")
            .Add(component => component.ActionsLabel, "Setup actions")
            .Add(component => component.StatusText, statusText)
            .Add(component => component.SummaryExpanded, summaryExpanded)
            .Add(component => component.HeadingContent, "<h1 id='workspace-heading'>Configure your site</h1>")
            .Add(component => component.ChildContent, "<p>Focused step content</p>")
            .Add(component => component.SummaryContent, "<p>Two steps remain</p>")
            .Add(component => component.HelpContent, "<a href='/help'>Get help</a>")
            .Add(component => component.ActionsContent, "<a href='/setup/exit'>Exit</a><button type='button'>Continue</button>"));

    private static OnboardingWorkspace.StepDescriptor[] DefaultSteps() =>
    [
        Step("Authentication", "Complete", OnboardingWorkspace.PresentationState.Complete, href: "/onboarding/auth-provider"),
        Step("Site profile", "Current step", OnboardingWorkspace.PresentationState.Current, isCurrent: true),
        Step("Authorization", "Upcoming", OnboardingWorkspace.PresentationState.Upcoming),
        Step("Readiness", "Upcoming", OnboardingWorkspace.PresentationState.Upcoming)
    ];

    private static OnboardingWorkspace.StepDescriptor Step(
        string label,
        string statusText,
        OnboardingWorkspace.PresentationState state,
        bool isCurrent = false,
        string? href = null) =>
        new(label, statusText, state, isCurrent, href);
}
