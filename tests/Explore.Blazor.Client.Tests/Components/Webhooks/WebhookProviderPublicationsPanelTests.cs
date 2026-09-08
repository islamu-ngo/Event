using Explore.Blazor.Client.Components.Common;
using Explore.Blazor.Client.Components.Webhooks;
using Explore.Blazor.Client.Contracts.Services.Webhooks;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Webhooks;

public sealed class WebhookProviderPublicationsPanelTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IWebhookOperationsService _operations = Substitute.For<IWebhookOperationsService>();

    public WebhookProviderPublicationsPanelTests() => _ctx.Services.AddSingleton(_operations);

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task ProviderPublicationsPanel_WhenHalLinksExist_ReconcilesWithObservedVersion()
    {
        var publication = CreatePublication("MANUAL_RECONCILIATION", "Manual reconciliation");
        GeneratedHalLinkTestHelper.SetLinks(
            publication,
            ("reconcile", $"/api/webhooks/provider-publications/{publication.Id}/reconcile", "POST"),
            ("abandon", $"/api/webhooks/provider-publications/{publication.Id}/abandon", "POST"));
        _operations.GetProviderPublicationsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WebhookProviderPublicationSnapshot { Publications = [publication] }));
        _operations.ReconcileProviderPublicationAsync(
                publication.Id!.Value,
                Arg.Any<ReconcileWebhookProviderPublicationRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WebhookActionResult.Succeeded("Reconciled.", publication.Id)));

        var dialogProvider = _ctx.Render<MudDialogProvider>();
        var cut = _ctx.RenderMudComponent<WebhookProviderPublicationsPanel>();
        cut.WaitForAssertion(() =>
        {
            cut.Find("button[aria-label='Reconcile provider publication']");
            cut.Find("button[aria-label='Abandon provider publication']");
        });

        cut.Find("button[aria-label='Reconcile provider publication']").Click();
        var fields = dialogProvider.FindComponents<AppTextField<string>>();
        var providerId = fields.Single(field => HasAttribute(field, "data-testid", "webhook-publication-provider-message-id"));
        var reason = fields.Single(field => HasAttribute(field, "data-testid", "webhook-publication-reconcile-reason"));
        await cut.InvokeAsync(() => providerId.Instance.ValueChanged.InvokeAsync("msg_selfhosted_42"));
        await cut.InvokeAsync(() => reason.Instance.ValueChanged.InvokeAsync("verified_provider_evidence"));
        dialogProvider.FindAll("button").Single(button => button.TextContent.Trim() == "Reconcile").Click();

        await _operations.Received(1).ReconcileProviderPublicationAsync(
            publication.Id.Value,
            Arg.Is<ReconcileWebhookProviderPublicationRequestDto>(request =>
                request != null &&
                request.ExpectedConcurrencyVersion == 7 &&
                request.ExternalProviderMessageId == "msg_selfhosted_42" &&
                request.ReasonCode == "verified_provider_evidence"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProviderPublicationsPanel_WhenHalLinksAreAbsent_HidesActionsAndExplainsState()
    {
        var publication = CreatePublication("PROVIDER_QUEUED", "Provider queued");
        _operations.GetProviderPublicationsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WebhookProviderPublicationSnapshot { Publications = [publication] }));

        var cut = _ctx.RenderMudComponent<WebhookProviderPublicationsPanel>();
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("The provider accepted this publication.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Safe provider state explanation was not rendered.");
            }
        });

        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Reconcile provider publication\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Abandon provider publication\"", StringComparison.OrdinalIgnoreCase);
    }

    private static HalResourceOfWebhookProviderPublicationDto CreatePublication(string statusCode, string statusName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            WebhookMessageId = Guid.CreateVersion7(),
            WebhookConsumerId = Guid.CreateVersion7(),
            WebhookDeliveryPlanSnapshotId = Guid.CreateVersion7(),
            ProviderKindId = 2,
            ProviderKindCode = "SVIX",
            ProviderKindName = "Svix",
            ModeSnapshotId = 3,
            ModeSnapshotCode = "SVIX",
            ModeSnapshotName = "Svix",
            StatusId = 5,
            StatusCode = statusCode,
            StatusName = statusName,
            ProviderVersion = "1.96.1",
            ProviderEventId = "evt_42",
            RequestHash = "sha256:request",
            ProviderEnvironment = "self-hosted",
            ConcurrencyVersion = 7,
            EventContractVersion = 1,
            ProviderConfigurationVersion = "selfhost-v1.96.1-v1",
            RetentionPolicyVersion = "retention-v1",
            PreparedAt = TestTime.UtcNow.AddMinutes(-5),
            CreatedAt = TestTime.UtcNow.AddMinutes(-5),
            UpdatedAt = TestTime.UtcNow
        };

    private static bool HasAttribute(
        IRenderedComponent<AppTextField<string>> component,
        string name,
        string value) =>
        component.Instance.AdditionalAttributes?.TryGetValue(name, out var actual) == true &&
        string.Equals(actual?.ToString(), value, StringComparison.Ordinal);
}
