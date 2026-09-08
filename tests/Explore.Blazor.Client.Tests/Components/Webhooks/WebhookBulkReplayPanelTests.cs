using Explore.Blazor.Client.Components.Common;
using Explore.Blazor.Client.Components.Webhooks;
using Explore.Blazor.Client.Contracts.Services.Webhooks;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Webhooks;

public sealed class WebhookBulkReplayPanelTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IWebhookOperationsService _operations = Substitute.For<IWebhookOperationsService>();

    public WebhookBulkReplayPanelTests() => _ctx.Services.AddSingleton(_operations);

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task BulkReplayPanel_WhenHalCapabilitiesExist_PreviewsAndSchedulesGeneratedFilter()
    {
        _operations.GetBulkReplaysAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WebhookBulkReplaySnapshot
            {
                CanPreview = true,
                CanSchedule = true
            }));
        _operations.PreviewBulkReplayAsync(Arg.Any<WebhookBulkReplayFilterDto>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var filter = call.Arg<WebhookBulkReplayFilterDto>()!;
                return Task.FromResult(new WebhookBulkReplayPreviewResult(
                    true,
                    "Preview loaded.",
                    new WebhookBulkReplayPreviewDto
                    {
                        Filter = filter,
                        EligibleCount = 3,
                        EstimatedSelectedCount = 2,
                        ExcludedCount = 1,
                        ExcludedIneligibleLocalStateCount = 1,
                        MaximumItemsPerOperation = 100,
                        MaximumReservedItemsPerTenant = 500,
                        PreviewedAt = TestTime.UtcNow
                    }));
            });
        _operations.ScheduleBulkReplayAsync(
                Arg.Any<ScheduleWebhookBulkReplayRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WebhookActionResult.Succeeded("Replay scheduled.", Guid.CreateVersion7())));

        var cut = _ctx.RenderMudComponent<WebhookBulkReplayPanel>();
        cut.WaitForAssertion(() => cut.Find("button[data-testid='webhook-bulk-replay-preview']"));
        cut.Find("button[data-testid='webhook-bulk-replay-preview']").Click();
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("2", StringComparison.Ordinal) ||
                !cut.Markup.Contains("Local state ineligible", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Replay preview evidence was not rendered.");
            }
        });

        var reason = cut.FindComponents<AppTextField<string>>()
            .Single(field => HasAttribute(field, "data-testid", "webhook-bulk-replay-reason"));
        await cut.InvokeAsync(() => reason.Instance.ValueChanged.InvokeAsync("incident_recovery"));
        cut.Find("button[data-testid='webhook-bulk-replay-schedule']").Click();

        await _operations.Received(1).ScheduleBulkReplayAsync(
            Arg.Is<ScheduleWebhookBulkReplayRequestDto>(request =>
                request != null &&
                request.OperationKey.HasValue &&
                request.OperationKey != Guid.Empty &&
                request.ReasonCode == "incident_recovery" &&
                request.Filter.FromUtc.HasValue &&
                request.Filter.ToUtc > request.Filter.FromUtc &&
                request.Filter.MaxItems == 100),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BulkReplayPanel_WhenItemCancelLinkExists_CancelsWithObservedVersion()
    {
        var operation = CreateOperation();
        GeneratedHalLinkTestHelper.SetLinks(
            operation,
            ("cancel", $"/api/webhooks/bulk-replays/{operation.Id}/cancel", "POST"));
        _operations.GetBulkReplaysAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WebhookBulkReplaySnapshot { Operations = [operation] }));
        _operations.CancelBulkReplayAsync(
                operation.Id!.Value,
                Arg.Any<CancelWebhookBulkReplayRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WebhookActionResult.Succeeded("Replay cancelled.", operation.Id)));

        var dialogProvider = _ctx.Render<MudDialogProvider>();
        var cut = _ctx.RenderMudComponent<WebhookBulkReplayPanel>();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Cancel queued bulk replay']"));
        cut.Find("button[aria-label='Cancel queued bulk replay']").Click();

        var reason = dialogProvider.FindComponents<AppTextField<string>>()
            .Single(field => HasAttribute(field, "data-testid", "webhook-bulk-replay-cancel-reason"));
        await cut.InvokeAsync(() => reason.Instance.ValueChanged.InvokeAsync("duplicate_operator_request"));
        dialogProvider.FindAll("button").Single(button => button.TextContent.Trim() == "Cancel replay").Click();

        await _operations.Received(1).CancelBulkReplayAsync(
            operation.Id.Value,
            Arg.Is<CancelWebhookBulkReplayRequestDto>(request =>
                request != null &&
                request.ExpectedConcurrencyVersion == 3 &&
                request.ReasonCode == "duplicate_operator_request"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BulkReplayPanel_WhenCollectionLinksAreAbsent_HidesActionsAndExplainsAvailability()
    {
        _operations.GetBulkReplaysAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WebhookBulkReplaySnapshot()));

        var cut = _ctx.RenderMudComponent<WebhookBulkReplayPanel>();
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Replay preview is unavailable", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Safe collection capability explanation was not rendered.");
            }
        });

        await Assert.That(cut.Markup).DoesNotContain("data-testid=\"webhook-bulk-replay-preview\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("data-testid=\"webhook-bulk-replay-schedule\"", StringComparison.OrdinalIgnoreCase);
    }

    private static HalResourceOfWebhookBulkReplayOperationDto CreateOperation() =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            OperationKey = Guid.CreateVersion7(),
            StatusId = 1,
            StatusCode = "QUEUED",
            StatusName = "Queued",
            Filter = new WebhookBulkReplayFilterDto
            {
                FromUtc = TestTime.UtcNow.AddDays(-1),
                ToUtc = TestTime.UtcNow,
                MaxItems = 100
            },
            ReasonCode = "incident_recovery",
            EstimatedEligibleCount = 2,
            EstimatedSelectedCount = 2,
            EstimatedExcludedCount = 0,
            ScheduledCount = 0,
            ConcurrencyVersion = 3,
            QueuedAt = TestTime.UtcNow
        };

    private static bool HasAttribute(
        IRenderedComponent<AppTextField<string>> component,
        string name,
        string value) =>
        component.Instance.AdditionalAttributes?.TryGetValue(name, out var actual) == true &&
        string.Equals(actual?.ToString(), value, StringComparison.Ordinal);
}
