using Explore.Blazor.Client.Pages.Onboarding.Components;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public sealed class OnboardingTaskListTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Items_RenderInOneSemanticOrderedList_WithVisibleTaskMetadata()
    {
        // Arrange
        var items = new[]
        {
            CreateItem(
                title: "Configure identity",
                description: "Connect the platform identity provider.",
                isRequired: true,
                requirementText: "Required",
                status: OnboardingTaskList.TaskStatus.Blocked,
                statusText: "Needs configuration",
                authorityScope: "Authority: instance administrator"),
            CreateItem(
                title: "Customize branding",
                description: "Apply the organization visual identity after launch.",
                isRequired: false,
                requirementText: "Optional",
                status: OnboardingTaskList.TaskStatus.Available,
                statusText: "Available after launch",
                authorityScope: "Authority: tenant administrator")
        };

        // Act
        var cut = Render(items);

        // Assert
        await Assert.That(cut.FindAll("ol").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("ol > li").Count).IsEqualTo(2);
        await Assert.That(cut.Find("ol").GetAttribute("aria-label")).IsEqualTo("Onboarding tasks");
        await Assert.That(cut.Markup).Contains("Configure identity", StringComparison.Ordinal);
        await Assert.That(cut.Markup).Contains("Connect the platform identity provider.", StringComparison.Ordinal);
        await Assert.That(cut.Markup).Contains("Required", StringComparison.Ordinal);
        await Assert.That(cut.Markup).Contains("Optional", StringComparison.Ordinal);
        await Assert.That(cut.Markup).Contains("Needs configuration", StringComparison.Ordinal);
        await Assert.That(cut.Markup).Contains("Available after launch", StringComparison.Ordinal);
        await Assert.That(cut.Markup).Contains("Authority: instance administrator", StringComparison.Ordinal);
        await Assert.That(cut.Markup).Contains("Authority: tenant administrator", StringComparison.Ordinal);
    }

    [Test]
    public async Task Item_WithNavigationAction_RendersAccessibleNativeLink()
    {
        // Arrange
        var item = CreateItem(
            href: "/admin/identity",
            actionLabel: "Configure",
            actionAccessibleName: "Configure identity provider");

        // Act
        var cut = Render(item);
        var action = cut.Find("a");

        // Assert
        await Assert.That(action.GetAttribute("href")).IsEqualTo("/admin/identity");
        await Assert.That(action.GetAttribute("aria-label")).IsEqualTo("Configure identity provider");
        await Assert.That(action.TextContent.Trim()).IsEqualTo("Configure");
        await Assert.That(action.HasAttribute("role")).IsFalse();
    }

    [Test]
    public async Task Item_WithoutHref_DoesNotRenderNavigationAction()
    {
        // Arrange
        var item = CreateItem(actionLabel: "Configure");

        // Act
        var cut = Render(item);

        // Assert
        await Assert.That(cut.FindAll("a").Count).IsEqualTo(0);
    }

    [Test]
    public async Task Status_UsesAtomicPoliteLiveRegionSemantics()
    {
        // Arrange
        var item = CreateItem(
            status: OnboardingTaskList.TaskStatus.Completed,
            statusText: "Configuration complete");

        // Act
        var cut = Render(item);
        var status = cut.Find("[role='status']");

        // Assert
        await Assert.That(status.GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(status.GetAttribute("aria-atomic")).IsEqualTo("true");
        await Assert.That(status.GetAttribute("data-status")).IsEqualTo("Completed");
        await Assert.That(status.TextContent.Trim()).IsEqualTo("Configuration complete");
    }

    private IRenderedComponent<OnboardingTaskList> Render(params OnboardingTaskList.TaskItem[] items) =>
        _ctx.Render<OnboardingTaskList>(parameters => parameters
            .Add(component => component.AccessibleLabel, "Onboarding tasks")
            .Add(component => component.Items, items));

    private static OnboardingTaskList.TaskItem CreateItem(
        string title = "Configure identity",
        string description = "Connect the platform identity provider.",
        bool isRequired = true,
        string requirementText = "Required",
        OnboardingTaskList.TaskStatus status = OnboardingTaskList.TaskStatus.NotStarted,
        string statusText = "Not started",
        string authorityScope = "Authority: instance administrator",
        string? href = null,
        string? actionLabel = null,
        string? actionAccessibleName = null) =>
        new(
            title,
            description,
            isRequired,
            requirementText,
            status,
            statusText,
            authorityScope,
            href,
            actionLabel,
            actionAccessibleName);
}
