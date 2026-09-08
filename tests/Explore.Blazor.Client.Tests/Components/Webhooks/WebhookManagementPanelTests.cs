using Explore.Blazor.Client.Components.Common;
using Explore.Blazor.Client.Components.Webhooks;
using Explore.Blazor.Client.Contracts.Services.Webhooks;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Webhooks;

public sealed class WebhookManagementPanelTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IWebhookManagementService _webhookService;
    private readonly IWebhookOperationsService _webhookOperations;

    public WebhookManagementPanelTests()
    {
        _ctx = new BlazorTestContext();
        _webhookService = Substitute.For<IWebhookManagementService>();
        _webhookOperations = Substitute.For<IWebhookOperationsService>();
        _ctx.Services.AddSingleton(_webhookService);
        _ctx.Services.AddSingleton(_webhookOperations);
        _webhookOperations.GetProviderPublicationsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WebhookProviderPublicationSnapshot()));
        _webhookOperations.GetBulkReplaysAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WebhookBulkReplaySnapshot()));
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task WebhookManagementPanel_WhenHalLinksExist_RendersAccessibleActionAffordances()
    {
        var snapshot = CreateSnapshot(includeActionLinks: true);
        _webhookService.GetSnapshotAsync(Arg.Any<WebhookOwnerSelection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Webhook status summary", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Webhook status summary was not rendered.");
            }
        });

        await Assert.That(cut.Markup).Contains("aria-label=\"Refresh webhooks\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("aria-label=\"Open provider portal\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("aria-label=\"Update endpoint\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("aria-label=\"Rotate signing secret\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("aria-label=\"Send test webhook\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("aria-label=\"Archive endpoint\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("aria-label=\"View delivery attempts\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("aria-label=\"Retry delivery\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("4 of 12 capabilities available", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task WebhookManagementPanel_WhenHalLinksAreAbsent_HidesMutationAffordances()
    {
        var snapshot = CreateSnapshot(includeActionLinks: false);
        _webhookService.GetSnapshotAsync(Arg.Any<WebhookOwnerSelection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Webhook status summary", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Webhook status summary was not rendered.");
            }
        });

        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Open provider portal\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Update endpoint\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Rotate signing secret\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Send test webhook\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Archive endpoint\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"View delivery attempts\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Retry delivery\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Pause webhook endpoint\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Resume webhook endpoint\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"View webhook payload\"", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Delivery control is managed by the configured provider mode.", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task WebhookManagementPanel_LocalHalActions_LoadPayloadAndPauseWithObservedVersion()
    {
        var snapshot = CreateSnapshot(includeActionLinks: true);
        var endpoint = snapshot.Endpoints.Single();
        var message = snapshot.Messages.Single();
        endpoint.ProviderModeCode = "LOCAL";
        endpoint.ProviderModeName = "Local";
        endpoint.DeliveryStateVersion = 4;
        GeneratedHalLinkTestHelper.SetLinks(
            endpoint,
            ("pause", $"/api/webhooks/endpoints/{endpoint.Id}/pause", "POST"));
        GeneratedHalLinkTestHelper.SetLinks(
            message,
            ("payload", $"/api/webhooks/messages/{message.Id}/payload", "GET"));

        _webhookService.GetSnapshotAsync(Arg.Any<WebhookOwnerSelection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));
        _webhookOperations.GetMessagePayloadAsync(message.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WebhookPayloadResult(
                true,
                "Payload loaded.",
                new WebhookMessagePayloadDto
                {
                    MessageId = message.Id,
                    ContentType = "application/json",
                    ContentEncoding = "utf-8",
                    PayloadBase64 = Convert.ToBase64String("{\"event\":\"published\"}"u8),
                    PayloadHash = message.PayloadHash,
                    PayloadByteLength = 21,
                    PayloadRetentionUntil = TestTime.UtcNow.AddDays(1),
                    RetrievedAt = TestTime.UtcNow
                })));
        _webhookOperations.PauseEndpointAsync(
                endpoint.Id!.Value,
                Arg.Any<PauseWebhookEndpointRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WebhookActionResult.Succeeded("Endpoint paused.", endpoint.Id)));

        var dialogProvider = _ctx.Render<MudDialogProvider>();
        var cut = RenderPanel();
        cut.WaitForAssertion(() =>
        {
            cut.Find("button[aria-label='Pause webhook endpoint']");
            cut.Find("button[aria-label='View webhook payload']");
        });

        cut.Find("button[aria-label='View webhook payload']").Click();
        dialogProvider.WaitForAssertion(() =>
        {
            if (!dialogProvider.Markup.Contains("published", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Decoded payload was not rendered.");
            }
        });
        await _webhookOperations.Received(1).GetMessagePayloadAsync(message.Id.Value, Arg.Any<CancellationToken>());

        dialogProvider.FindAll("button").Single(button => button.TextContent.Trim() == "Close").Click();
        dialogProvider.WaitForAssertion(() =>
        {
            if (dialogProvider.Markup.Contains("Webhook Payload", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Payload dialog has not closed.");
            }
        });
        cut.Find("button[aria-label='Pause webhook endpoint']").Click();
        IRenderedComponent<AppTextField<string>>? reasonField = null;
        dialogProvider.WaitForAssertion(() =>
            reasonField = dialogProvider.FindComponents<AppTextField<string>>()
                .Single(field => HasAdditionalAttribute(field, "data-testid", "webhook-endpoint-control-reason")));
        await cut.InvokeAsync(() => reasonField!.Instance.ValueChanged.InvokeAsync("operator_maintenance"));
        dialogProvider.FindAll("button").Single(button => button.TextContent.Trim() == "Pause").Click();

        await _webhookOperations.Received(1).PauseEndpointAsync(
            endpoint.Id.Value,
            Arg.Is<PauseWebhookEndpointRequestDto>(request =>
                request != null &&
                request.ExpectedDeliveryStateVersion == 4 &&
                request.ReasonCode == "operator_maintenance"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WebhookManagementPanel_WhenCapabilityAuthorityIsUnavailable_ShowsSafeExplanation()
    {
        var snapshot = CreateSnapshot(includeActionLinks: false, capabilityAuthorityAvailable: false);
        _webhookService.GetSnapshotAsync(Arg.Any<WebhookOwnerSelection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Capability authority unavailable", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Capability authority status was not rendered.");
            }
        });

        await Assert.That(cut.Markup).Contains("Provider binding verification is required.", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("webhook_provider_binding_unverified", StringComparison.Ordinal);
        await Assert.That(cut.Markup).DoesNotContain("aria-label=\"Open provider portal\"", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task WebhookManagementPanel_TestRetryPortalAndAttemptActions_InvokeServiceLayer()
    {
        var snapshot = CreateSnapshot(includeActionLinks: true);
        var endpointId = snapshot.Endpoints.Single().Id!.Value;
        var consumerId = snapshot.Consumers.Single().Id!.Value;
        var messageId = snapshot.Messages.Single().Id!.Value;
        var attemptId = snapshot.DeliveryAttempts.Single().Id!.Value;

        _webhookService.GetSnapshotAsync(Arg.Any<WebhookOwnerSelection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));
        _webhookService.TestEndpointAsync(endpointId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WebhookActionResult.Succeeded("Test webhook queued.")));
        _webhookService.RetryDeliveryAttemptAsync(attemptId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WebhookActionResult.Succeeded("Retry scheduled.")));
        _webhookService.GetDeliveryAttemptsAsync(
                Arg.Any<WebhookOwnerSelection>(),
                messageId,
                null,
                100,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot.DeliveryAttempts));
        _webhookService.OpenProviderPortalAsync(consumerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WebhookPortalResult(true, "Portal ready.", "https://svix.example.test/app-portal")));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("aria-label=\"Send test webhook\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Endpoint test action was not rendered.");
            }
        });

        cut.Find("button[aria-label='Send test webhook']").Click();
        cut.Find("button[aria-label='View delivery attempts']").Click();
        cut.Find("button[aria-label='Retry delivery']").Click();
        cut.Find("button[aria-label='Open provider portal']").Click();

        cut.WaitForAssertion(() =>
            _webhookService.Received(1).TestEndpointAsync(endpointId, Arg.Any<CancellationToken>()));
        await _webhookService.Received(1).GetDeliveryAttemptsAsync(
            WebhookOwnerSelection.Instance,
            messageId,
            null,
            100,
            Arg.Any<CancellationToken>());
        await _webhookService.Received(1).RetryDeliveryAttemptAsync(attemptId, Arg.Any<CancellationToken>());
        await _webhookService.Received(1).OpenProviderPortalAsync(consumerId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WebhookManagementPanel_UpdateEndpoint_RequiresAndForwardsExplicitPendingWorkDecision()
    {
        var snapshot = CreateSnapshot(includeActionLinks: true);
        var endpoint = snapshot.Endpoints.Single();
        var endpointId = endpoint.Id!.Value;

        _webhookService.GetSnapshotAsync(Arg.Any<WebhookOwnerSelection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));
        _webhookService.UpdateEndpointAsync(
                endpointId,
                Arg.Any<UpdateWebhookEndpointRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WebhookActionResult.Succeeded("Endpoint updated.", endpointId)));

        var dialogProvider = _ctx.Render<MudDialogProvider>();
        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Update endpoint']"));

        cut.Find("button[aria-label='Update endpoint']").Click();

        dialogProvider.WaitForAssertion(() =>
        {
            if (!dialogProvider.Markup.Contains("Pending delivery work", StringComparison.OrdinalIgnoreCase)
                || !dialogProvider.Markup.Contains("Preserve existing snapshots", StringComparison.OrdinalIgnoreCase)
                || !dialogProvider.Markup.Contains("Migrate eligible pending deliveries", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The explicit pending-work decision controls were not rendered.");
            }

            var save = dialogProvider.FindAll("button").Single(button => button.TextContent.Trim() == "Save");
            if (!save.HasAttribute("disabled"))
            {
                throw new InvalidOperationException("Update must remain disabled before an explicit decision and reason.");
            }
        });

        var fields = dialogProvider.FindComponents<AppTextField<string>>();
        var urlField = fields.Single(field => HasAdditionalAttribute(field, "Label", "Endpoint URL"));
        var reasonField = fields.Single(field => HasAdditionalAttribute(field, "data-testid", "webhook-update-pending-reason"));
        var decisionGroup = dialogProvider.FindComponent<MudRadioGroup<int>>();
        var acknowledgement = dialogProvider.FindComponents<MudCheckBox<bool>>()
            .Single(checkBox => checkBox.Markup.Contains("provider publications", StringComparison.OrdinalIgnoreCase));

        await cut.InvokeAsync(() => urlField.Instance.ValueChanged.InvokeAsync("https://hooks.example.test/events"));
        await cut.InvokeAsync(() => decisionGroup.Instance.ValueChanged.InvokeAsync(WebhookPendingWorkDecisionIds.MigrateEligible));
        await cut.InvokeAsync(() => reasonField.Instance.ValueChanged.InvokeAsync("Adopt the rotated integration route"));
        await cut.InvokeAsync(() => acknowledgement.Instance.ValueChanged.InvokeAsync(true));

        dialogProvider.FindAll("button").Single(button => button.TextContent.Trim() == "Save").Click();

        await _webhookService.Received(1).UpdateEndpointAsync(
            endpointId,
            Arg.Is<UpdateWebhookEndpointRequestDto>(request =>
                request.Governance.ExpectedConfigurationVersion == endpoint.ConfigurationVersion
                && request.Governance.PendingWorkDecisionId == WebhookPendingWorkDecisionIds.MigrateEligible
                && request.Governance.PendingWorkReason == "Adopt the rotated integration route"
                && request.Governance.AcknowledgeUncertainProviderPublications == true
                && request.Destination!.Url == "https://hooks.example.test/events"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WebhookManagementPanel_RotateSecret_RequiresAndForwardsExplicitPendingWorkDecision()
    {
        var snapshot = CreateSnapshot(includeActionLinks: true);
        var endpoint = snapshot.Endpoints.Single();
        var endpointId = endpoint.Id!.Value;

        _webhookService.GetSnapshotAsync(Arg.Any<WebhookOwnerSelection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));
        _webhookService.RotateEndpointSecretAsync(
                endpointId,
                Arg.Any<RotateWebhookEndpointSecretRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WebhookActionResult.Succeeded("Signing secret rotated.", endpointId)));

        var dialogProvider = _ctx.Render<MudDialogProvider>();
        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Rotate signing secret']"));

        cut.Find("button[aria-label='Rotate signing secret']").Click();

        dialogProvider.WaitForAssertion(() =>
        {
            var rotate = dialogProvider.FindAll("button").Single(button => button.TextContent.Trim() == "Rotate");
            if (!rotate.HasAttribute("disabled"))
            {
                throw new InvalidOperationException("Rotation must remain disabled before an explicit decision and reason.");
            }
        });

        var fields = dialogProvider.FindComponents<AppTextField<string>>();
        var secretField = fields.Single(field => HasAdditionalAttribute(field, "Label", "New secret reference"));
        var reasonField = fields.Single(field => HasAdditionalAttribute(field, "data-testid", "webhook-rotate-pending-reason"));
        var decisionGroup = dialogProvider.FindComponent<MudRadioGroup<int>>();
        var acknowledgement = dialogProvider.FindComponents<MudCheckBox<bool>>()
            .Single(checkBox => checkBox.Markup.Contains("provider publications", StringComparison.OrdinalIgnoreCase));

        await cut.InvokeAsync(() => secretField.Instance.ValueChanged.InvokeAsync("webhook:endpoint:v3"));
        await cut.InvokeAsync(() => decisionGroup.Instance.ValueChanged.InvokeAsync(WebhookPendingWorkDecisionIds.PreserveExisting));
        await cut.InvokeAsync(() => reasonField.Instance.ValueChanged.InvokeAsync("Keep queued deliveries on their original credential"));
        await cut.InvokeAsync(() => acknowledgement.Instance.ValueChanged.InvokeAsync(true));

        dialogProvider.FindAll("button").Single(button => button.TextContent.Trim() == "Rotate").Click();

        await _webhookService.Received(1).RotateEndpointSecretAsync(
            endpointId,
            Arg.Is<RotateWebhookEndpointSecretRequestDto>(request =>
                request.ExpectedConfigurationVersion == endpoint.ConfigurationVersion
                && request.PendingWorkDecisionId == WebhookPendingWorkDecisionIds.PreserveExisting
                && request.PendingWorkReason == "Keep queued deliveries on their original credential"
                && request.AcknowledgeUncertainProviderPublications == true
                && request.NewSecretRef == "webhook:endpoint:v3"
                && request.PreviousSecretValidForSeconds == 86400),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WebhookManagementPanel_CreateConsumer_UsesFixedOrganizationOwnerWithoutKindSelector()
    {
        var organizationId = Guid.CreateVersion7();
        var owner = WebhookOwnerSelection.ForOrganization(organizationId);
        _webhookService.GetSnapshotAsync(owner, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateSnapshot(includeActionLinks: true)));
        _webhookService.CreateConsumerAsync(
                Arg.Any<CreateWebhookConsumerRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WebhookActionResult.Succeeded("Consumer created.")));

        var dialogProvider = _ctx.Render<MudDialogProvider>();
        var cut = RenderPanel(owner);
        cut.WaitForAssertion(() =>
        {
            if (!cut.FindAll("button").Any(button => button.TextContent.Trim() == "Create Consumer"))
            {
                throw new InvalidOperationException("The create-consumer affordance was not rendered.");
            }
        });

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Create Consumer").Click();
        IRenderedComponent<AppTextField<string>>? nameField = null;
        dialogProvider.WaitForAssertion(() =>
        {
            nameField = dialogProvider.FindComponents<AppTextField<string>>()
                .Single(field => HasAdditionalAttribute(field, "Label", "Name"));
            if (!dialogProvider.Markup.Contains("Owner scope", StringComparison.Ordinal)
                || !dialogProvider.Markup.Contains("Organization", StringComparison.Ordinal)
                || dialogProvider.Markup.Contains("System integration", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The fixed organization scope was not rendered safely.");
            }
        });

        await cut.InvokeAsync(() => nameField!.Instance.ValueChanged.InvokeAsync("Organization events"));
        dialogProvider.FindAll("button").Single(button => button.TextContent.Trim() == "Create").Click();

        await _webhookService.Received(1).CreateConsumerAsync(
            Arg.Is<CreateWebhookConsumerRequestDto>(request =>
                request.ConsumerKindId == (int)WebhookOwnerKind.Organization
                && request.OwnerId == organizationId
                && request.Name == "Organization events"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WebhookManagementPanel_WithoutCollectionHalCapabilities_HidesSensitiveTabs()
    {
        _webhookService.GetSnapshotAsync(WebhookOwnerSelection.User, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateSnapshot(includeActionLinks: false)));

        var denied = RenderPanel(WebhookOwnerSelection.User);
        denied.WaitForAssertion(() => denied.Find("[aria-label='Webhook status summary']"));
        await Assert.That(denied.FindComponents<WebhookProviderPublicationsPanel>().Count).IsEqualTo(0);
        await Assert.That(denied.FindComponents<WebhookBulkReplayPanel>().Count).IsEqualTo(0);
    }

    [Test]
    public async Task WebhookManagementPanel_WithCollectionHalCapabilities_RendersSensitiveTabs()
    {
        _webhookService.GetSnapshotAsync(WebhookOwnerSelection.Tenant, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateSnapshot(
                includeActionLinks: false,
                canViewProviderPublications: true,
                canUseBulkReplay: true)));

        var allowed = RenderPanel(WebhookOwnerSelection.Tenant);
        allowed.WaitForAssertion(() =>
        {
            if (allowed.FindComponents<WebhookProviderPublicationsPanel>().Count != 1
                || allowed.FindComponents<WebhookBulkReplayPanel>().Count != 1)
            {
                throw new InvalidOperationException("HAL-authorized sensitive tabs were not rendered.");
            }
        });
        await Assert.That(allowed.FindComponents<WebhookProviderPublicationsPanel>().Count).IsEqualTo(1);
        await Assert.That(allowed.FindComponents<WebhookBulkReplayPanel>().Count).IsEqualTo(1);
    }

    [Test]
    public async Task WebhookManagementPanel_ResponsiveMarkupPreservesAccessibleMobileAndDesktopViews()
    {
        _webhookService.GetSnapshotAsync(WebhookOwnerSelection.Instance, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateSnapshot(includeActionLinks: true)));
        var cut = RenderPanel();

        cut.WaitForAssertion(() => cut.Find("[aria-label='Instance webhook management']"));
        await Assert.That(cut.Find("[aria-label='Instance webhook management']").GetAttribute("aria-busy"))
            .IsEqualTo("false");
        await Assert.That(cut.FindAll("section.webhook-management__mobile-card[aria-label]").Count)
            .IsEqualTo(2);
        await Assert.That(cut.FindAll("button[aria-label='View delivery attempts']").Count)
            .IsEqualTo(2);

    }

    private IRenderedComponent<WebhookManagementPanel> RenderPanel(
        WebhookOwnerSelection? owner = null) =>
        _ctx.RenderMudComponent<WebhookManagementPanel>(parameters => parameters
            .Add(component => component.Owner, owner ?? WebhookOwnerSelection.Instance));

    private static WebhookManagementSnapshot CreateSnapshot(
        bool includeActionLinks,
        bool capabilityAuthorityAvailable = true,
        bool canViewProviderPublications = false,
        bool canUseBulkReplay = false)
    {
        var tenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var endpointId = Guid.CreateVersion7();
        var eventTypeId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var attemptId = Guid.CreateVersion7();

        var consumer = new HalResourceOfWebhookConsumerDto
        {
            Id = consumerId,
            TenantId = tenantId,
            Name = "Operations bridge",
            ConsumerKindId = 5,
            ConsumerKindName = "Instance",
            ProviderModeId = 3,
            ProviderModeName = "Svix",
            ProviderCapabilityAuthorityAvailable = capabilityAuthorityAvailable,
            CapabilityResolutionVersion = "selfhost-v1.96.1-v1",
            CapabilityUnavailableReasonCode = capabilityAuthorityAvailable
                ? null
                : "webhook_provider_binding_unverified",
            ProviderCapabilities = CreateProviderCapabilities(capabilityAuthorityAvailable),
            StatusId = 1,
            StatusName = "Active"
        };

        var endpoint = new HalResourceOfWebhookEndpointDto
        {
            Id = endpointId,
            TenantId = tenantId,
            ConsumerId = consumerId,
            ConsumerName = consumer.Name,
            ProviderModeId = 3,
            ProviderModeCode = "SVIX",
            ProviderModeName = "Svix",
            DestinationHost = "integrator.example.test",
            Description = "Integration endpoint",
            StatusId = 1,
            StatusCode = "ACTIVE",
            StatusName = "Active",
            SecretVersion = 2,
            ConfigurationVersion = 7,
            DeliveryStateVersion = 2,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            RateLimitPerMinute = 60,
            Subscriptions =
            [
                new Subscriptions
                {
                    Id = Guid.CreateVersion7(),
                    EventTypeId = eventTypeId,
                    EventTypeName = "event.published",
                    EventTypeGroupName = "event",
                    IsEnabled = true,
                    CreatedAt = TestTime.UtcNow
                }
            ]
        };

        var message = new HalResourceOfWebhookMessageDto
        {
            Id = messageId,
            TenantId = tenantId,
            EventType = "event.published",
            EventId = "evt_018f",
            AggregateKind = "Event",
            AggregateId = Guid.CreateVersion7(),
            ConsumerId = consumerId,
            ConsumerName = consumer.Name,
            PayloadHash = "sha256:1234567890abcdef",
            PayloadRetentionUntil = TestTime.UtcNow.AddDays(14),
            CreatedAt = TestTime.UtcNow
        };

        var attempt = new HalResourceOfWebhookDeliveryAttemptDto
        {
            Id = attemptId,
            TenantId = tenantId,
            MessageId = messageId,
            MessageEventType = message.EventType,
            EndpointId = endpointId,
            EndpointStatusCode = "ACTIVE",
            EndpointStatusName = "Active",
            AttemptNumber = 2,
            OutcomeId = 4,
            OutcomeCode = "FAILED",
            OutcomeName = "Failed",
            ScheduledAt = TestTime.UtcNow.AddMinutes(-5),
            HttpStatusCode = 500,
            FailureCategory = "http_non_success",
            DurationMs = 1200,
            CreatedAt = TestTime.UtcNow.AddMinutes(-5)
        };

        if (includeActionLinks)
        {
            GeneratedHalLinkTestHelper.SetLinks(
                consumer,
                ("open-provider-portal", $"/api/webhooks/consumers/{consumerId}/svix/app-portal", "POST"));
            GeneratedHalLinkTestHelper.SetLinks(
                endpoint,
                ("update", $"/api/webhooks/endpoints/{endpointId}", "PUT"),
                ("rotate-secret", $"/api/webhooks/endpoints/{endpointId}/rotate-secret", "POST"),
                ("test", $"/api/webhooks/endpoints/{endpointId}/test", "POST"),
                ("delete", $"/api/webhooks/endpoints/{endpointId}", "DELETE"));
            GeneratedHalLinkTestHelper.SetLinks(
                message,
                ("delivery-attempts", $"/api/webhooks/delivery-attempts?messageId={messageId}", "GET"));
            GeneratedHalLinkTestHelper.SetLinks(
                attempt,
                ("retry", $"/api/webhooks/delivery-attempts/{attemptId}/retry", "POST"));
        }

        return new WebhookManagementSnapshot
        {
            EventTypes =
            [
                new WebhookEventTypeDto
                {
                    Id = eventTypeId,
                    Name = "event.published",
                    GroupName = "event",
                    Description = "Event becomes publicly visible.",
                    SchemaVersion = 1,
                    IsPublic = true,
                    IsEnabled = true,
                    PayloadRetentionDays = 14,
                    SchemaJson = "{}",
                    ExamplePayloadJson = "{}"
                }
            ],
            Consumers = [consumer],
            Endpoints = [endpoint],
            Messages = [message],
            DeliveryAttempts = [attempt],
            CanCreateConsumer = true,
            CanCreateEndpoint = true,
            CanViewProviderPublications = canViewProviderPublications,
            CanUseBulkReplay = canUseBulkReplay
        };
    }

    private static List<ProviderCapabilities> CreateProviderCapabilities(bool authorityAvailable)
    {
        var capabilities = new (int Id, string Code, string Name, bool IsProven)[]
        {
            (1, "ENDPOINT_MANAGEMENT", "Endpoint management", true),
            (2, "ATTEMPT_INSPECTION", "Attempt inspection", false),
            (4, "MESSAGE_REPLAY", "Message replay", false),
            (8, "PAYLOAD_INSPECTION", "Payload inspection", true),
            (16, "APP_PORTAL", "App portal", true),
            (32, "EVENT_CATALOG", "Event catalog", true),
            (64, "RETENTION_CONTROL", "Retention control", false),
            (128, "APPLICATION_THROTTLING", "Application throttling", false),
            (256, "ENDPOINT_THROTTLING", "Endpoint throttling", false),
            (512, "TRANSFORMATIONS", "Transformations", false),
            (1024, "ORDERING", "Ordering", false),
            (2048, "CALLBACKS", "Callbacks", false)
        };

        return capabilities
            .Select(capability => new ProviderCapabilities
            {
                CapabilityId = capability.Id,
                CapabilityCode = capability.Code,
                CapabilityName = capability.Name,
                IsAvailable = authorityAvailable && capability.IsProven,
                AvailableFromProviderCodes = authorityAvailable && capability.IsProven ? ["SVIX"] : [],
                UnavailableReasonCode = authorityAvailable && capability.IsProven
                    ? null
                    : authorityAvailable
                        ? "webhook_provider_capability_unproven"
                        : "webhook_provider_binding_unverified"
            })
            .ToList();
    }

    private static bool HasAdditionalAttribute<T>(
        IRenderedComponent<T> component,
        string name,
        string expectedValue)
        where T : IComponent =>
        component.Instance is AppTextField<string> field
        && field.AdditionalAttributes?.TryGetValue(name, out var value) == true
        && string.Equals(value?.ToString(), expectedValue, StringComparison.Ordinal);

}
