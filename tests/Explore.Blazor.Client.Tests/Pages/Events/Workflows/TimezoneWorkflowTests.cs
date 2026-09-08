using Explore.Blazor.Client.Pages.Events.Workflows;

namespace Explore.Blazor.Client.Tests.Pages.Events.Workflows;

public class TimezoneWorkflowTests
{
    [Test]
    public async Task InitializeFromId_UsesRequestedTimezone_WhenFound()
    {
        var workflow = new TimezoneWorkflow();
        var expected = TimeZoneInfo.Local;

        workflow.InitializeFromId(expected.Id);

        await Assert.That(workflow.SelectedTimezone.Id).IsEqualTo(expected.Id);
    }

    [Test]
    public async Task InitializeFromId_FallsBackToLocal_WhenMissing()
    {
        var workflow = new TimezoneWorkflow();

        workflow.InitializeFromId("Mars/Phobos");

        await Assert.That(workflow.SelectedTimezone.Id).IsEqualTo(TimeZoneInfo.Local.Id);
    }

    [Test]
    public async Task Select_UpdatesSelectedTimezone_WhenValueProvided()
    {
        var workflow = new TimezoneWorkflow();
        var target = TimeZoneInfo.GetSystemTimeZones().First(timezone => timezone.Id != workflow.SelectedTimezone.Id);

        workflow.Select(target);

        await Assert.That(workflow.SelectedTimezone.Id).IsEqualTo(target.Id);
    }

    [Test]
    public async Task SearchAsync_ReturnsMatches_ByIdAndDisplayName()
    {
        var workflow = new TimezoneWorkflow();
        var sample = TimeZoneInfo.Local;
        var searchTerm = sample.Id.Split('/').LastOrDefault() ?? sample.Id;

        var results = (await workflow.SearchAsync(searchTerm)).ToList();

        await Assert.That(results.Any(timezone => timezone.Id == sample.Id)).IsTrue();
    }

    [Test]
    public async Task SelectedTimezoneDisplay_UsesGmtPrefix()
    {
        var workflow = new TimezoneWorkflow();

        workflow.InitializeFromId(TimeZoneInfo.Local.Id);

        await Assert.That(workflow.SelectedTimezoneDisplay.StartsWith("GMT", StringComparison.Ordinal)).IsTrue();
    }
}
