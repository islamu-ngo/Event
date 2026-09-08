using System.Collections;
using Explore.Blazor.Client.Components.Moderation;

namespace Explore.Blazor.Client.Tests.Components.Moderation;

public sealed class ModerationReportDetailPanelTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Render_WhenActionLinksMissing_HidesWorkflowAffordances()
    {
        var detail = CreateDetailResource();

        var cut = _ctx.RenderMudComponent<ModerationReportDetailPanel>(parameters => parameters
            .Add(component => component.Report, detail));

        await Assert.That(cut.Markup.Contains("Available Workflow", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains(">Triage<", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup).Contains("Reporter evidence text");
        await Assert.That(cut.Markup).Contains("Case updates consent");
        await Assert.That(cut.Markup).Contains("Follow-up contact consent");
        await Assert.That(cut.Markup).Contains("Granted");
        await Assert.That(cut.Markup).Contains("Not granted");
    }

    [Test]
    public async Task Render_WhenActionLinksPresent_ShowsOnlyHalAdvertisedWorkflowAffordances()
    {
        var detail = CreateDetailResource("triage-report", "assign-report");

        var cut = _ctx.RenderMudComponent<ModerationReportDetailPanel>(parameters => parameters
            .Add(component => component.Report, detail));

        await Assert.That(cut.Markup).Contains("Available Workflow");
        await Assert.That(cut.Markup).Contains("Triage");
        await Assert.That(cut.Markup).Contains("Assign");
        await Assert.That(cut.Markup.Contains(">Decide<", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains(">Execute<", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenActionButtonClicked_EmitsHalAdvertisedAction()
    {
        var detail = CreateDetailResource("triage-report");
        ModerationReportActionKind? requestedAction = null;

        var cut = _ctx.RenderMudComponent<ModerationReportDetailPanel>(parameters => parameters
            .Add(component => component.Report, detail)
            .Add(
                component => component.OnActionRequested,
                EventCallback.Factory.Create<ModerationReportActionKind>(
                    this,
                    action => requestedAction = action)));

        var triageButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Triage", StringComparison.Ordinal));
        await cut.InvokeAsync(() => triageButton.Click());

        await Assert.That(requestedAction).IsEqualTo(ModerationReportActionKind.Triage);
    }

    [Test]
    public async Task Render_StatusBadgesExposeAccessibleLabels()
    {
        var detail = CreateDetailResource();

        var cut = _ctx.RenderMudComponent<ModerationReportDetailPanel>(parameters => parameters
            .Add(component => component.Report, detail));

        await Assert.That(cut.Markup).Contains("aria-label=\"Report status: Submitted\"");
        await Assert.That(cut.Markup).Contains("aria-label=\"Priority: Urgent\"");
    }

    public void Dispose() => _ctx.Dispose();

    private static HalResourceOfModerationReportDetailDto CreateDetailResource(params string[] linkRels)
    {
        var eventId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var resource = new HalResourceOfModerationReportDetailDto
        {
            Id = reportId,
            EventId = eventId,
            ReporterKindId = 1,
            ReporterKindCode = "user",
            ReporterKindName = "User",
            SourceKindId = 1,
            SourceKindCode = "web",
            SourceKindName = "Web",
            StatusId = 1,
            StatusCode = "submitted",
            StatusName = "Submitted",
            PriorityId = 4,
            PriorityCode = "urgent",
            PriorityName = "Urgent",
            ReasonId = 1,
            ReasonCode = "spam",
            ReasonName = "Spam",
            ReportCaseUpdatesConsent = true,
            ReportFollowUpContactConsent = false,
            ReporterLocale = "en",
            SubmittedAtUtc = TestTime.UtcNow,
            ConcurrencyStamp = Guid.NewGuid(),
            CurrentCase = new CurrentCase
            {
                Id = caseId,
                ReportId = reportId,
                QueueCode = "safety",
                StatusId = 1,
                StatusCode = "open",
                StatusName = "Open",
                PriorityId = 4,
                PriorityCode = "urgent",
                PriorityName = "Urgent",
                CreatedAtUtc = TestTime.UtcNow,
                ConcurrencyStamp = Guid.NewGuid()
            },
            EvidenceItems =
            [
                new EvidenceItems
                {
                    Id = Guid.NewGuid(),
                    ReportId = reportId,
                    EvidenceKindId = 1,
                    EvidenceKindCode = "reporter_text",
                    EvidenceKindName = "Reporter text",
                    TextBody = "Reporter evidence text",
                    HasTextBody = true,
                    IsTextUnavailable = false,
                    ClassificationId = 1,
                    ClassificationCode = "user_submitted",
                    ClassificationName = "User submitted",
                    CreatedAtUtc = TestTime.UtcNow
                }
            ],
            Signals =
            [
                new Signals
                {
                    Id = Guid.NewGuid(),
                    ReportId = reportId,
                    EventId = eventId,
                    ProviderId = 1,
                    ProviderCode = "local",
                    ProviderName = "Local",
                    SignalType = "policy",
                    PolicyCode = "spam",
                    Score = 0.9,
                    VerdictId = 1,
                    VerdictCode = "flagged",
                    VerdictName = "Flagged",
                    RecommendedActionName = "Review",
                    CreatedAtUtc = TestTime.UtcNow
                }
            ],
            Decisions = [],
            ExternalLinks = [],
            Targets = []
        };

        return WithLinks(
            resource,
            linkRels,
            "/api/events/event/moderation/reports/report",
            "POST");
    }

    private static TResource WithLinks<TResource>(
        TResource resource,
        IEnumerable<string> linkRels,
        string href,
        string method)
    {
        var linksProperty = typeof(TResource).GetProperty("_links")
            ?? throw new InvalidOperationException($"{typeof(TResource).Name} does not expose HAL links.");
        var linkType = linksProperty.PropertyType.GetGenericArguments()[1];
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), linkType);
        var links = (IDictionary)Activator.CreateInstance(dictionaryType)!;

        foreach (var rel in linkRels)
        {
            var link = Activator.CreateInstance(linkType)!;
            linkType.GetProperty(nameof(HalLink.Href))!.SetValue(link, href);
            linkType.GetProperty(nameof(HalLink.Method))!.SetValue(link, method);
            links[rel] = link;
        }

        linksProperty.SetValue(resource, links);
        return resource;
    }
}
