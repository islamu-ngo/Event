using System.Collections;
using System.Reflection;
using Explore.Blazor.Client.Components.Moderation;
using Explore.Blazor.Client.Contracts.Services.EventReporting;

namespace Explore.Blazor.Client.Tests.Components.Moderation;

public sealed class ModerationReportQueuePageTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Render_WhenQueueRowOpened_InvokesDetailReadOnDemand()
    {
        var eventId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var service = Substitute.For<IEventReportModerationService>();
        service.GetQueueAsync(eventId, Arg.Any<ModerationReportQueueQueryState>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationReportQueuePageResult(
                [CreateQueueResource(eventId, reportId)],
                1,
                20,
                1,
                1,
                false,
                false));
        service.GetDetailAsync(eventId, reportId, Arg.Any<CancellationToken>())
            .Returns(CreateDetailResource(eventId, reportId));

        _ctx.Services.AddSingleton(service);

        var cut = _ctx.RenderMudComponent<ModerationReportQueuePage>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForState(() => cut.Markup.Contains("Spam", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        await service.DidNotReceive().GetDetailAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await cut.InvokeAsync(() =>
            cut.Find($"button[aria-label='Open Report {reportId.ToString("N")[..8]}']").Click());
        await service.Received(1).GetDetailAsync(eventId, reportId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteActionAsync_WhenDialogResultMissingRequiredData_DoesNotCallActionService()
    {
        var eventId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var service = Substitute.For<IEventReportModerationService>();
        service.GetQueueAsync(eventId, Arg.Any<ModerationReportQueueQueryState>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationReportQueuePageResult([], 1, 20, 0, 0, false, false));
        _ctx.Services.AddSingleton(service);
        var cut = _ctx.RenderMudComponent<ModerationReportQueuePage>(parameters => parameters
            .Add(component => component.EventId, eventId));
        cut.WaitForState(() => cut.Instance is not null, TimeSpan.FromSeconds(3));
        SetPrivateField(
            cut.Instance,
            "_selectedDetail",
            CreateDetailResource(eventId, reportId, "assign-report"));

        await InvokePrivateTaskAsync(
            cut.Instance,
            "ExecuteActionAsync",
            new ModerationReportActionDialogResult(
                ModerationReportActionKind.Assign,
                QueueCode: null,
                Priority: null,
                AssigneeUserId: null,
                DecisionKind: null,
                ReasonCode: null,
                SafeNote: null,
                DuplicateGroupId: null,
                DecisionId: null,
                CorrelationId: null));

        await service.DidNotReceive().AssignAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<AssignModerationReportRequestDto>(),
            Arg.Any<CancellationToken>());
        await Assert.That(GetPrivateField<string?>(cut.Instance, "_detailErrorMessage"))
            .IsEqualTo("Assignee user id is required.");
    }

    public void Dispose() => _ctx.Dispose();

    private static HalResourceOfModerationReportQueueItemDto CreateQueueResource(Guid eventId, Guid reportId)
        => HalLinkTestFactory.WithLinks(new HalResourceOfModerationReportQueueItemDto
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
            SubmittedAtUtc = TestTime.UtcNow,
            CurrentCase = new CurrentCase2
            {
                Id = Guid.NewGuid(),
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
            DecisionCount = 0,
            SignalCount = 1,
            ExternalLinkCount = 0
        }, new HalLinkTestLink("self", "/api/events/event/moderation/reports/report", "GET"));

    private static HalResourceOfModerationReportDetailDto CreateDetailResource(
        Guid eventId,
        Guid reportId,
        params string[] linkRels)
    {
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
            SubmittedAtUtc = TestTime.UtcNow,
            ConcurrencyStamp = Guid.NewGuid(),
            CurrentCase = new CurrentCase
            {
                Id = Guid.NewGuid(),
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
            Decisions = [],
            Signals = [],
            ExternalLinks = [],
            Targets = []
        };

        return WithLinks(
            resource,
            linkRels.Prepend("self").Distinct(StringComparer.Ordinal),
            "/api/events/event/moderation/reports/report",
            "GET");
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

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");

        return (T?)field.GetValue(instance)
            ?? throw new InvalidOperationException($"Field {fieldName} returned null.");
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");

        field.SetValue(instance, value);
    }

    private static async Task InvokePrivateTaskAsync(object instance, string methodName, params object?[] parameters)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");

        var task = method.Invoke(instance, parameters) as Task
            ?? throw new InvalidOperationException($"Method {methodName} did not return a task.");
        await task;
    }
}
