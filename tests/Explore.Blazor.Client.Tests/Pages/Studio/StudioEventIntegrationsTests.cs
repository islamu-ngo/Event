using System.Text.Json;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Pages.Studio;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioEventIntegrationsTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IRegistrationProviderIntegrationService _service;
    private readonly IAccessibilityFocusService _focus;
    private readonly EventDto _event = new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        Title = "Provider event"
    };

    public StudioEventIntegrationsTests()
    {
        _service = _ctx.AddMockService<IRegistrationProviderIntegrationService>();
        _focus = _ctx.AddMockService<IAccessibilityFocusService>();
        EmptyResponses();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task LoadingEmptyAndAccessibleStructure_RenderExpectedStates()
    {
        WithEventLinks("manage-registration-channels", "view-registration-provider-health");

        var cut = Render();

        cut.WaitForElement("[data-testid='studio-event-integrations']");
        await Assert.That(cut.FindAll("h3").Count).IsGreaterThanOrEqualTo(5);
        await Assert.That(cut.Markup).Contains("No provider connections yet.");
        await Assert.That(cut.Markup).Contains("No parked reconciliation items.");
    }

    [Test]
    public async Task LoadError_RendersAlertAndDoesNotExposeMutations()
    {
        WithEventLinks("manage-registration-channels");
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var cut = Render();

        cut.WaitForElement("[data-testid='integrations-error']");
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("could not be loaded");
        await Assert.That(cut.FindAll("[data-testid='connection-form']")).IsEmpty();
    }

    [Test]
    public async Task HealthOnly_RendersReadModeWithoutMutationForms()
    {
        WithEventLinks("view-registration-provider-health");
        _service.GetHealthAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Health(Item(new { id = Guid.CreateVersion7(), name = "Listmonk", status = "Healthy" }))));

        var cut = Render();

        cut.WaitForElement("[data-testid='integrations-health-only']");
        await Assert.That(cut.Find("[data-testid='integration-health-row']").TextContent).Contains("Healthy");
        await Assert.That(cut.FindAll("[data-testid='connection-form']")).IsEmpty();
        await _service.DidNotReceiveWithAnyArgs().GetConnectionsAsync(default, default);
    }

    [Test]
    public async Task HealthFetch_RequiresExactViewHealthEventRelation()
    {
        WithEventLinks("manage-registration-channels");

        var cut = Render();

        cut.WaitForElement("[data-testid='studio-event-integrations']");
        await Assert.That(cut.FindAll("[data-testid='integration-health-row']")).IsEmpty();
        await _service.DidNotReceiveWithAnyArgs().GetHealthAsync(default, default);
    }

    [Test]
    public async Task HalActions_CreateConnectionRequiresCollectionCreateLinkAndValidation()
    {
        WithEventLinks("manage-registration-channels");
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(collectionLinks: ["provider-create"])));
        var cut = Render();
        cut.WaitForElement("[data-testid='connection-form']");

        await cut.Find("form[data-testid='connection-form']").SubmitAsync();
        await Assert.That(cut.Find("[data-testid='integrations-validation']").TextContent).Contains("required fields");
        await _service.DidNotReceiveWithAnyArgs().CreateConnectionAsync(default, default, default!, default!);

        var inputs = cut.Find("form[data-testid='connection-form']").QuerySelectorAll("input");
        inputs[0].Change("Listmonk");
        inputs[1].Change("1");
        inputs[2].Change("2");
        Guid apiSecretBindingId = Guid.CreateVersion7();
        Guid webhookSecretBindingId = Guid.CreateVersion7();
        SetPrivateDraft(cut.Instance, "_connectionDraft", new Dictionary<string, object?>
        {
            ["ProviderCode"] = "MICROSOFT_FORMS",
            ["ProviderDeploymentCode"] = "MICROSOFT_365",
            ["ApiVersion"] = "POWER_AUTOMATE_V1",
            ["AdapterPolicyVersion"] = "ISLAMU_EVENT_MICROSOFT_FORMS_V1",
            ["ConformanceEvidenceRevision"] = "2026-08-11",
            ["ManagementApiBaseUrl"] = "https://forms.office.com",
            ["PublicBaseUrl"] = "https://forms.office.com/Pages/ResponsePage.aspx",
            ["ProviderWorkspaceId"] = "microsoft-365",
            ["ApiTokenSecretBindingId"] = apiSecretBindingId,
            ["WebhookSecretBindingId"] = webhookSecretBindingId
        });
        await cut.Find("form[data-testid='connection-form']").SubmitAsync();

        await _service.Received(1).CreateConnectionAsync(
            _event.TenantId.Value,
            _event.Id.Value,
            Arg.Is<HalLink>(link => link.Method == "POST" && link.Href.Contains("provider-create")),
            Arg.Is<RegistrationProviderConnectionRequestDto>(request => request.Name == "Listmonk" && request.ProviderKindId == 1 && request.DeploymentKindId == 2 && request.ApiTokenSecretBindingId == apiSecretBindingId && request.WebhookSecretBindingId == webhookSecretBindingId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PollReconciliation_UsesHealthCallbackCheckpointInsteadOfUnboundedDefault()
    {
        WithEventLinks("manage-registration-channels", "view-registration-provider-health");
        Guid bindingId = Guid.CreateVersion7();
        var checkpoint = new DateTimeOffset(2026, 8, 9, 10, 30, 0, TimeSpan.Zero);
        _service.GetHealthAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Health(Item(new { id = bindingId, bindingId, lastCallbackAt = checkpoint }, "poll"))));

        var cut = Render();
        cut.WaitForElement("[data-testid='integration-health-row']");
        cut.FindAll("button").Single(button => button.TextContent.Contains("Poll reconciliation")).Click();

        await _service.Received(1).PollReconciliationAsync(
            _event.TenantId.Value,
            _event.Id.Value,
            bindingId,
            Arg.Any<HalLink>(),
            Arg.Is<DateTimeOffset?>(value => value == checkpoint),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RetryAndResolve_UseOnlyQueueItemHalLinks()
    {
        WithEventLinks("manage-registration-channels");
        var submissionId = Guid.CreateVersion7();
        _service.GetQueueAsync(_event.TenantId!.Value, _event.Id!.Value, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Queue(Item(new { id = Guid.CreateVersion7(), submissionId, reason = "drift" }, "retry", "resolve"))));
        var cut = Render();
        cut.WaitForElement("[data-testid='integration-queue-row']");

        cut.FindAll("button").Single(button => button.TextContent.Contains("Retry")).Click();
        cut.FindAll("button").Single(button => button.TextContent.Contains("Resolve")).Click();

        await _service.Received(1).RetryQueueItemAsync(_event.TenantId.Value, _event.Id.Value, Arg.Any<HalLink>(), Arg.Is<RetryRegistrationProviderParkedItemRequestDto>(request => request.SubmissionId == submissionId), Arg.Any<CancellationToken>());
        await _service.Received(1).ResolveQueueItemAsync(_event.TenantId.Value, _event.Id.Value, Arg.Any<HalLink>(), Arg.Is<ResolveRegistrationProviderQueueItemRequestDto>(request => request.SubmissionId == submissionId), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Channels_HideSwitchActionsWithoutEventOrItemHalLinks()
    {
        var workflowId = Guid.CreateVersion7();
        var requirementId = Guid.CreateVersion7();
        var channelId = Guid.CreateVersion7();
        _service.GetChannelsAsync(_event.TenantId!.Value, _event.Id!.Value, workflowId, requirementId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Channels(Item(new { id = channelId, ordinal = 1, isNative = true }))));

        var cut = Render();

        await Assert.That(cut.FindAll("[data-testid='channel-scope-form']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='channel-edit-form']")).IsEmpty();
        await _service.DidNotReceiveWithAnyArgs().GetChannelsAsync(default, default, default, default);
        await _service.DidNotReceiveWithAnyArgs().UpdateChannelAsync(default, default, default, default, default, default!, default!, default);
    }

    [Test]
    public async Task FullWritePaths_UseItemAndCollectionHalLinks()
    {
        WithEventLinks("manage-registration-channels", "view-registration-provider-health");
        var connectionId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var webhookSecretBindingId = Guid.CreateVersion7();
        var workflowId = Guid.CreateVersion7();
        var requirementId = Guid.CreateVersion7();
        var channelId = Guid.CreateVersion7();
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(Item(new
            {
                id = connectionId,
                name = "Listmonk",
                providerKindId = 1,
                deploymentKindId = 2,
                providerCode = "MICROSOFT_FORMS",
                providerDeploymentCode = "MICROSOFT_365",
                apiVersion = "POWER_AUTOMATE_V1",
                adapterPolicyVersion = "ISLAMU_EVENT_MICROSOFT_FORMS_V1",
                conformanceEvidenceRevision = "2026-08-11",
                managementApiBaseUrl = "https://forms.office.com",
                publicBaseUrl = "https://forms.office.com/Pages/ResponsePage.aspx",
                providerWorkspaceId = "microsoft-365",
                apiTokenSecretBindingId = Guid.CreateVersion7(),
                webhookSecretBindingId = Guid.CreateVersion7(),
                approvedOrigins = new[] { "https://forms.example" }
            }, "edit", "origins", "delete"), "provider-create")));
        _service.GetBindingsAsync(_event.TenantId.Value, _event.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Bindings(Item(new
            {
                id = bindingId,
                connectionId,
                formId = Guid.CreateVersion7(),
                formVersionId = Guid.CreateVersion7(),
                revisionHash = "abc",
                driftClassId = 1,
                presentationModeId = 1,
                collectionModeId = 2,
                completionModeId = 3,
                trustLevelId = 4,
                fieldMappings = new[] { new { platformFieldKey = "email", providerFieldKey = "Email", isRequired = true } },
                optionMappings = new[] { new { platformFieldKey = "ticket", platformOptionKey = "vip", providerOptionKey = "VIP" } }
            }, "edit", "mappings", "publish", "delete"), "provider-create", "manual-import")));
        _service.GetChannelsAsync(_event.TenantId.Value, _event.Id.Value, workflowId, requirementId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Channels(Item(new { id = channelId, ordinal = 1, isNative = false, registrationProviderBindingId = bindingId }, "edit", "delete"), "provider-create")));

        var cut = Render();
        cut.WaitForElement("[data-testid='connection-edit-form']");

        await cut.Find("form[data-testid='connection-edit-form']").SubmitAsync();
        cut.FindAll("button").Single(button => button.TextContent.Contains("Replace origins")).Click();
        cut.WaitForAssertion(() => _service.Received(1).ReplaceApprovedOriginsAsync(_event.TenantId.Value, _event.Id.Value, connectionId, Arg.Any<HalLink>(), Arg.Any<ReplaceRegistrationProviderApprovedOriginsRequestDto>(), Arg.Any<CancellationToken>()));
        cut.FindAll("button").Single(button => button.TextContent.Contains("Save mappings")).Click();
        cut.FindAll("button").Single(button => button.TextContent.Contains("Publish binding")).Click();
        FillGuidForm(cut, "channel-scope-form", workflowId, requirementId);
        await cut.Find("form[data-testid='channel-scope-form']").SubmitAsync();
        cut.WaitForElement("[data-testid='channel-edit-form']");
        await cut.Find("form[data-testid='channel-edit-form']").SubmitAsync();
        cut.FindAll("button").Single(button => button.TextContent.Contains("Delete channel")).Click();

        await _service.Received(1).UpdateConnectionAsync(_event.TenantId.Value, _event.Id.Value, connectionId, Arg.Is<HalLink>(link => link.Method == "PUT"), Arg.Is<RegistrationProviderConnectionRequestDto>(request => request.Name == "Listmonk"), Arg.Any<CancellationToken>());
        await _service.Received(1).ReplaceApprovedOriginsAsync(_event.TenantId.Value, _event.Id.Value, connectionId, Arg.Is<HalLink>(link => link.Method == "PUT"), Arg.Is<ReplaceRegistrationProviderApprovedOriginsRequestDto>(request => request.Origins!.Single() == "https://forms.example"), Arg.Any<CancellationToken>());
        await _service.Received(1).ReplaceMappingsAsync(_event.TenantId.Value, _event.Id.Value, bindingId, Arg.Is<HalLink>(link => link.Method == "PUT"), Arg.Is<ReplaceRegistrationProviderMappingsRequestDto>(request => request.FieldMappings!.Single().PlatformFieldKey == "email" && request.OptionMappings!.Single().ProviderOptionKey == "VIP"), Arg.Any<CancellationToken>());
        await _service.Received(1).PublishBindingAsync(_event.TenantId.Value, _event.Id.Value, bindingId, Arg.Is<HalLink>(link => link.Method == "POST"), Arg.Any<CancellationToken>());
        await _service.Received(1).UpdateChannelAsync(_event.TenantId.Value, _event.Id.Value, workflowId, requirementId, channelId, Arg.Is<HalLink>(link => link.Method == "PUT"), Arg.Is<RegistrationChannelRequestDto>(request => request.Ordinal == 1 && request.RegistrationProviderBindingId == bindingId), Arg.Any<CancellationToken>());
        await _service.Received(1).DeleteChannelAsync(_event.TenantId.Value, _event.Id.Value, workflowId, requirementId, channelId, Arg.Is<HalLink>(link => link.Method == "DELETE"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BindingEdit_SubmitsRequiredStructuredDtoWithoutDecorativeExtras()
    {
        WithEventLinks("manage-registration-channels");
        var connectionId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var webhookSecretBindingId = Guid.CreateVersion7();
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(Item(new { id = connectionId, name = "Forms", providerKindId = 1, deploymentKindId = 2 }))));
        _service.GetBindingsAsync(_event.TenantId.Value, _event.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Bindings(Item(new { id = bindingId, connectionId, formId = Guid.CreateVersion7(), formVersionId = Guid.CreateVersion7(), providerSurveyId = "form-123", providerSurveyRevisionId = "revision-1", providerWebhookId = "POWER_AUTOMATE_V1", webhookSecretBindingId, presentationModeId = 1, collectionModeId = 2, completionModeId = 3, trustLevelId = 4 }, "edit"))));
        var cut = Render();
        cut.WaitForElement("[data-testid='binding-edit-form']");
        SetDictionaryDraft(cut.Instance, "_bindingEdits", bindingId, new Dictionary<string, object?>
        {
            ["ConnectionId"] = connectionId,
            ["FormId"] = Guid.CreateVersion7(),
            ["FormVersionId"] = Guid.CreateVersion7(),
            ["PresentationModeId"] = 1,
            ["CollectionModeId"] = 2,
            ["CompletionModeId"] = 3,
            ["TrustLevelId"] = 4
        });

        await cut.Find("form[data-testid='binding-edit-form']").SubmitAsync();

        await _service.Received(1).UpdateBindingAsync(_event.TenantId.Value, _event.Id.Value, bindingId, Arg.Is<HalLink>(link => link.Method == "PUT"), Arg.Is<RegistrationProviderBindingRequestDto>(request => request.ConnectionId == connectionId && request.FormId != null && request.FormVersionId != null && request.ProviderSurveyId == "form-123" && request.ProviderSurveyRevisionId == "revision-1" && request.ProviderWebhookId == "POWER_AUTOMATE_V1" && request.WebhookSecretBindingId == webhookSecretBindingId && request.PresentationModeId == 1 && request.CollectionModeId == 2 && request.CompletionModeId == 3 && request.TrustLevelId == 4 && request.AdditionalProperties.Count == 0), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BindingCreate_SubmitsOnlyValidStructuredDto()
    {
        WithEventLinks("manage-registration-channels");
        var connectionId = Guid.CreateVersion7();
        var webhookSecretBindingId = Guid.CreateVersion7();
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(Item(new { id = connectionId, name = "Forms", providerKindId = 1, deploymentKindId = 2 }), "provider-create")));
        _service.GetBindingsAsync(_event.TenantId.Value, _event.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Bindings(collectionLinks: ["provider-create"])));
        var cut = Render();
        cut.WaitForElement("[data-testid='binding-form']");
        SetPrivateDraft(cut.Instance, "_bindingDraft", new Dictionary<string, object?>
        {
            ["ConnectionId"] = connectionId,
            ["FormId"] = Guid.CreateVersion7(),
            ["FormVersionId"] = Guid.CreateVersion7(),
            ["ProviderSurveyId"] = "form-123",
            ["ProviderSurveyRevisionId"] = "revision-1",
            ["ProviderWebhookId"] = "POWER_AUTOMATE_V1",
            ["WebhookSecretBindingId"] = webhookSecretBindingId,
            ["PresentationModeId"] = 1,
            ["CollectionModeId"] = 2,
            ["CompletionModeId"] = 3,
            ["TrustLevelId"] = 4
        });

        await cut.Find("form[data-testid='binding-form']").SubmitAsync();

        await _service.Received(1).CreateBindingAsync(_event.TenantId.Value, _event.Id.Value, Arg.Any<HalLink>(), Arg.Is<RegistrationProviderBindingRequestDto>(request =>
            request.ConnectionId == connectionId && request.FormId != null && request.FormVersionId != null && request.ProviderSurveyId == "form-123" && request.ProviderSurveyRevisionId == "revision-1" && request.ProviderWebhookId == "POWER_AUTOMATE_V1" && request.WebhookSecretBindingId == webhookSecretBindingId && request.PresentationModeId == 1 && request.CollectionModeId == 2 && request.CompletionModeId == 3 && request.TrustLevelId == 4 && request.AdditionalProperties.Count == 0), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConnectionCreate_PersistsEnteredApprovedOriginsThroughCreatedItemLink()
    {
        WithEventLinks("manage-registration-channels");
        var connectionId = Guid.CreateVersion7();
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(collectionLinks: ["provider-create"])));
        _service.CreateConnectionAsync(_event.TenantId.Value, _event.Id.Value, Arg.Any<HalLink>(), Arg.Any<RegistrationProviderConnectionRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = connectionId, Success = true }));
        _service.GetConnectionAsync(_event.TenantId.Value, _event.Id.Value, connectionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ConnectionResource("origins")));
        var cut = Render();
        cut.WaitForElement("[data-testid='connection-form']");
        SetPrivateDraft(cut.Instance, "_connectionDraft", ValidConnectionDraft("Forms", "https://forms.example\nhttps://checkout.example"));

        await cut.Find("form[data-testid='connection-form']").SubmitAsync();

        await _service.Received(1).GetConnectionAsync(_event.TenantId.Value, _event.Id.Value, connectionId, Arg.Any<CancellationToken>());
        await _service.Received(1).ReplaceApprovedOriginsAsync(_event.TenantId.Value, _event.Id.Value, connectionId, Arg.Is<HalLink>(link => link.Method == "PUT"), Arg.Is<ReplaceRegistrationProviderApprovedOriginsRequestDto>(request => request.Origins!.SequenceEqual(new[] { "https://forms.example", "https://checkout.example" })), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConnectionCreate_WithMixedInvalidOrigins_RejectsBeforeServiceCall()
    {
        WithEventLinks("manage-registration-channels");
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(collectionLinks: ["provider-create"])));
        var cut = Render();
        cut.WaitForElement("[data-testid='connection-form']");
        SetPrivateDraft(cut.Instance, "_connectionDraft", ValidConnectionDraft("Forms", "https://forms.example\nnot-a-url"));

        await cut.Find("form[data-testid='connection-form']").SubmitAsync();

        await Assert.That(cut.Find("[data-testid='integrations-validation']").TextContent).Contains("absolute URLs");
        await _ctx.Services.GetRequiredService<IAccessibilityAnnouncerService>().Received(1).AnnounceAssertiveAsync(Arg.Is<string>(message => message.Contains("absolute URLs")));
        await _focus.Received(1).RestoreFocusAsync("[data-testid='integrations-validation']");
        await _service.DidNotReceiveWithAnyArgs().CreateConnectionAsync(default, default, default!, default!);
    }

    [Test]
    public async Task TwoConnectionRows_DoNotShareEditDraftState()
    {
        WithEventLinks("manage-registration-channels");
        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(new[]
            {
                Item(new { id = firstId, name = "First", providerKindId = 1, deploymentKindId = 2, providerCode = "MICROSOFT_FORMS", providerDeploymentCode = "MICROSOFT_365", apiVersion = "POWER_AUTOMATE_V1", adapterPolicyVersion = "ISLAMU_EVENT_MICROSOFT_FORMS_V1", conformanceEvidenceRevision = "2026-08-11", managementApiBaseUrl = "https://forms.office.com", publicBaseUrl = "https://forms.office.com/Pages/ResponsePage.aspx", providerWorkspaceId = "microsoft-365", apiTokenSecretBindingId = Guid.CreateVersion7(), webhookSecretBindingId = Guid.CreateVersion7() }, "edit"),
                Item(new { id = secondId, name = "Second", providerKindId = 1, deploymentKindId = 2, providerCode = "MICROSOFT_FORMS", providerDeploymentCode = "MICROSOFT_365", apiVersion = "POWER_AUTOMATE_V1", adapterPolicyVersion = "ISLAMU_EVENT_MICROSOFT_FORMS_V1", conformanceEvidenceRevision = "2026-08-11", managementApiBaseUrl = "https://forms.office.com", publicBaseUrl = "https://forms.office.com/Pages/ResponsePage.aspx", providerWorkspaceId = "microsoft-365", apiTokenSecretBindingId = Guid.CreateVersion7(), webhookSecretBindingId = Guid.CreateVersion7() }, "edit")
            })));
        var cut = Render();
        cut.WaitForElement("[data-testid='connection-edit-form']");
        cut.FindAll("form[data-testid='connection-edit-form']")[0].QuerySelector("input")!.Change("Changed first");

        await cut.FindAll("form[data-testid='connection-edit-form']")[1].SubmitAsync();

        await _service.Received(1).UpdateConnectionAsync(_event.TenantId.Value, _event.Id.Value, secondId, Arg.Any<HalLink>(), Arg.Is<RegistrationProviderConnectionRequestDto>(request => request.Name == "Second"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConnectionUpdate_MissingSecretBindings_DoesNotCallService()
    {
        WithEventLinks("manage-registration-channels");
        var connectionId = Guid.CreateVersion7();
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(Item(new { id = connectionId, name = "Missing secrets", providerKindId = 1, deploymentKindId = 2, providerCode = "MICROSOFT_FORMS", providerDeploymentCode = "MICROSOFT_365", apiVersion = "POWER_AUTOMATE_V1", adapterPolicyVersion = "ISLAMU_EVENT_MICROSOFT_FORMS_V1", conformanceEvidenceRevision = "2026-08-11", managementApiBaseUrl = "https://forms.office.com", publicBaseUrl = "https://forms.office.com/Pages/ResponsePage.aspx", providerWorkspaceId = "microsoft-365" }, "edit"))));
        var cut = Render();
        cut.WaitForElement("[data-testid='connection-edit-form']");

        await cut.Find("form[data-testid='connection-edit-form']").SubmitAsync();

        await _service.DidNotReceiveWithAnyArgs().UpdateConnectionAsync(default, default, default, default!, default!);
    }

    [Test]
    public async Task PublishedBindingWithoutMappingsLink_HidesStructuredMappingEditor()
    {
        WithEventLinks("manage-registration-channels");
        _service.GetBindingsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Bindings(Item(new { id = Guid.CreateVersion7(), connectionId = Guid.CreateVersion7(), formId = Guid.CreateVersion7(), formVersionId = Guid.CreateVersion7(), presentationModeId = 1, collectionModeId = 2, completionModeId = 3, trustLevelId = 4 }, "edit"))));

        var cut = Render();

        cut.WaitForElement("[data-testid='binding-edit-form']");
        await Assert.That(cut.FindAll("[data-testid='binding-mappings-editor']")).IsEmpty();
    }

    [Test]
    public async Task MissingItemLinks_HideWriteControlsAndDoNotCallService()
    {
        WithEventLinks("manage-registration-channels");
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(Item(new { id = Guid.CreateVersion7(), name = "Read only", providerKindId = 1, deploymentKindId = 2 }))));

        var cut = Render();

        cut.WaitForElement("[data-testid='integration-connection-row']");
        await Assert.That(cut.FindAll("[data-testid='connection-edit-form']")).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("Replace origins");
        await Assert.That(cut.Markup).DoesNotContain("Delete connection");
        await _service.DidNotReceiveWithAnyArgs().UpdateConnectionAsync(default, default, default, default!, default!);
    }

    [Test]
    public async Task ValidationFailure_AnnouncesAndRestoresFocusBeforeServiceCall()
    {
        WithEventLinks("manage-registration-channels");
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(collectionLinks: ["provider-create"])));
        var cut = Render();
        cut.WaitForElement("[data-testid='connection-form']");

        await cut.Find("form[data-testid='connection-form']").SubmitAsync();

        await Assert.That(cut.Find("[data-testid='integrations-validation']").TextContent).Contains("required fields");
        await _ctx.Services.GetRequiredService<IAccessibilityAnnouncerService>().Received(1).AnnounceAssertiveAsync(Arg.Is<string>(message => message.Contains("required fields")));
        await _focus.Received(1).RestoreFocusAsync("[data-testid='integrations-validation']");
        await _service.DidNotReceiveWithAnyArgs().CreateConnectionAsync(default, default, default!, default!);
    }

    [Test]
    public async Task EventChange_CancelsStaleLoadAndIgnoresOldResponse()
    {
        WithEventLinks("manage-registration-channels");
        CancellationToken firstToken = default;
        var staleLoad =
            new TaskCompletionSource<HalCollectionResourceOfRegistrationProviderConnectionDto>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        _service.GetConnectionsAsync(
                _event.TenantId!.Value,
                _event.Id!.Value,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                firstToken = call.Arg<CancellationToken>();
                firstToken.Register(() => staleLoad.TrySetResult(
                    Connections(Item(new
                    {
                        id = Guid.CreateVersion7(),
                        name = "Stale",
                    }))));
                return staleLoad.Task;
            });
        var cut = Render();
        cut.WaitForElement("[data-testid='integrations-loading']");

        var next = new EventDto { Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), Title = "Next" };
        next.AdditionalProperties["_links"] = _event.AdditionalProperties["_links"];
        _service.GetConnectionsAsync(next.TenantId.Value, next.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(Item(new { id = Guid.CreateVersion7(), name = "Fresh", providerKindId = 1, deploymentKindId = 2 }))));
        cut.Render(parameters => parameters.Add(component => component.Event, next));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Fresh"));
        await Assert.That(firstToken.IsCancellationRequested).IsTrue();
        await Assert.That(cut.Markup).DoesNotContain("Stale");
    }

    [Test]
    public async Task EventChange_FromManagedToHealthOnly_ClearsQueueChannelsAndRowDrafts()
    {
        WithEventLinks("manage-registration-channels", "view-registration-provider-health");
        var workflowId = Guid.CreateVersion7();
        var requirementId = Guid.CreateVersion7();
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Connections(Item(new { id = Guid.CreateVersion7(), name = "Old connection", providerKindId = 1, deploymentKindId = 2 }, "edit"))));
        _service.GetQueueAsync(_event.TenantId.Value, _event.Id.Value, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Queue(Item(new { id = Guid.CreateVersion7(), reason = "Old queue leak", processingGeneration = 1 }, "retry"))));
        _service.GetChannelsAsync(_event.TenantId.Value, _event.Id.Value, workflowId, requirementId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Channels(Item(new { id = Guid.CreateVersion7(), ordinal = 7, isNative = true }, "edit"))));
        var cut = Render();
        cut.WaitForElement("[data-testid='integration-connection-row']");
        FillGuidForm(cut, "channel-scope-form", workflowId, requirementId);
        await cut.Find("form[data-testid='channel-scope-form']").SubmitAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Old queue leak"));
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Order 7"));

        var next = new EventDto { Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), Title = "Health only" };
        AddEventLinks(next, "view-registration-provider-health");
        _service.GetHealthAsync(next.TenantId.Value, next.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Health(Item(new { id = Guid.CreateVersion7(), name = "New health", status = "Healthy" }))));
        cut.Render(parameters => parameters.Add(component => component.Event, next));

        cut.WaitForElement("[data-testid='integrations-health-only']");
        await Assert.That(cut.Markup).Contains("New health");
        await Assert.That(cut.Markup).DoesNotContain("Old connection");
        await Assert.That(cut.Markup).DoesNotContain("Old queue leak");
        await Assert.That(cut.Markup).DoesNotContain("Order 7");
    }

    [Test]
    public async Task AccessibilityRendersNamedControls()
    {
        WithEventLinks("manage-registration-channels");
        var cut = Render();
        cut.WaitForElement("[data-testid='studio-event-integrations']");

        await Assert.That(cut.FindAll("label").Count).IsGreaterThan(0);
    }

    private IRenderedComponent<StudioEventIntegrations> Render() => _ctx.RenderMudComponent<StudioEventIntegrations>(parameters => parameters.Add(component => component.Event, _event));

    private void WithEventLinks(params string[] rels)
    {
        AddEventLinks(_event, rels);
    }

    private static void AddEventLinks(EventDto evt, params string[] rels)
    {
        evt.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            rels.ToDictionary(rel => rel, rel => (object)new { href = $"/api/events/{evt.Id}/{rel}", method = "GET" }));
    }

    private void EmptyResponses()
    {
        _service.GetConnectionsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Connections()));
        _service.GetBindingsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Bindings()));
        _service.GetHealthAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Health()));
        _service.GetQueueAsync(_event.TenantId!.Value, _event.Id!.Value, 50, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Queue()));
        _service.GetChannelsAsync(_event.TenantId.Value, _event.Id.Value, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Channels()));
        _service.GetConnectionAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ConnectionResource("origins")));
        _service.CreateConnectionAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<HalLink>(), Arg.Any<RegistrationProviderConnectionRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.UpdateConnectionAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<RegistrationProviderConnectionRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.DeleteConnectionAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.ReplaceApprovedOriginsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<ReplaceRegistrationProviderApprovedOriginsRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.CreateBindingAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<HalLink>(), Arg.Any<RegistrationProviderBindingRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.UpdateBindingAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<RegistrationProviderBindingRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.ReplaceMappingsAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<ReplaceRegistrationProviderMappingsRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.DeleteBindingAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.PublishBindingAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.CreateChannelAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<RegistrationChannelRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.UpdateChannelAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<RegistrationChannelRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.DeleteChannelAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.RetryQueueItemAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<HalLink>(), Arg.Any<RetryRegistrationProviderParkedItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _service.ResolveQueueItemAsync(_event.TenantId!.Value, _event.Id!.Value, Arg.Any<HalLink>(), Arg.Any<ResolveRegistrationProviderQueueItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
    }

    private static HalCollectionResourceOfRegistrationProviderConnectionDto Connections(object? item = null, params string[] collectionLinks) => new()
    {
        _links = Links(collectionLinks),
        _embedded = new HalCollectionEmbeddedOfRegistrationProviderConnectionDto { Items = Items<HalResourceOfRegistrationProviderConnectionDto>(item) }
    };

    private static HalResourceOfRegistrationProviderConnectionDto ConnectionResource(params string[] rels) => new()
    {
        AdditionalProperties = { ["_links"] = Links(rels)! }
    };

    private static HalCollectionResourceOfRegistrationProviderBindingDto Bindings(object? item = null, params string[] collectionLinks) => new()
    {
        _links = Links(collectionLinks),
        _embedded = new HalCollectionEmbeddedOfRegistrationProviderBindingDto { Items = Items<HalResourceOfRegistrationProviderBindingDto>(item) }
    };

    private static HalCollectionResourceOfRegistrationChannelDto Channels(object? item = null, params string[] collectionLinks) => new()
    {
        _links = Links(collectionLinks),
        _embedded = new HalCollectionEmbeddedOfRegistrationChannelDto { Items = Items<HalResourceOfRegistrationChannelDto>(item) }
    };

    private static HalCollectionResourceOfRegistrationProviderBindingHealthDto Health(object? item = null) => new()
    {
        _embedded = new HalCollectionEmbeddedOfRegistrationProviderBindingHealthDto { Items = Items<HalResourceOfRegistrationProviderBindingHealthDto>(item) }
    };

    private static HalCollectionResourceOfRegistrationProviderParkedQueueItemDto Queue(object? item = null, params string[] collectionLinks) => new()
    {
        _links = Links(collectionLinks),
        _embedded = new HalCollectionEmbeddedOfRegistrationProviderParkedQueueItemDto { Items = Items<HalResourceOfRegistrationProviderParkedQueueItemDto>(item) }
    };

    private static List<T> Items<T>(object? item)
    {
        if (item is null)
        {
            return [];
        }

        if (item is IEnumerable<JsonElement> items)
        {
            return items.Select(ToItem<T>).ToList();
        }

        return [ToItem<T>((JsonElement)item)];
    }

    private static T ToItem<T>(JsonElement item) => item.Deserialize<T>()!;

    private static Dictionary<string, object?> ValidConnectionDraft(string name, string? originsText = null) => new()
    {
        ["Name"] = name,
        ["ProviderKindId"] = 1,
        ["DeploymentKindId"] = 2,
        ["ProviderCode"] = "MICROSOFT_FORMS",
        ["ProviderDeploymentCode"] = "MICROSOFT_365",
        ["ApiVersion"] = "POWER_AUTOMATE_V1",
        ["AdapterPolicyVersion"] = "ISLAMU_EVENT_MICROSOFT_FORMS_V1",
        ["ConformanceEvidenceRevision"] = "2026-08-11",
        ["ManagementApiBaseUrl"] = "https://forms.office.com",
        ["PublicBaseUrl"] = "https://forms.office.com/Pages/ResponsePage.aspx",
        ["ProviderWorkspaceId"] = "microsoft-365",
        ["ApiTokenSecretBindingId"] = Guid.CreateVersion7(),
        ["WebhookSecretBindingId"] = Guid.CreateVersion7(),
        ["OriginsText"] = originsText
    };

    private static Dictionary<string, HalLink>? Links(params string[] rels) => rels.Length == 0
        ? null
        : rels.ToDictionary(rel => rel, rel => new HalLink { Href = $"/api/{rel}", Method = MethodFor(rel) }, StringComparer.OrdinalIgnoreCase);

    private static void FillGuidForm(IRenderedComponent<StudioEventIntegrations> cut, string testId, params Guid[] values)
    {
        var inputs = cut.Find($"form[data-testid='{testId}']").QuerySelectorAll("input");
        for (var i = 0; i < values.Length; i++) inputs[i].Change(values[i].ToString("D"));
    }

    private static void SetPrivateDraft(object component, string fieldName, IReadOnlyDictionary<string, object?> values)
    {
        var draft = component.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(component)!;
        foreach (var (name, value) in values)
        {
            draft.GetType().GetProperty(name)!.SetValue(draft, value);
        }
    }

    private static void SetDictionaryDraft(object component, string fieldName, Guid key, IReadOnlyDictionary<string, object?> values)
    {
        var dictionary = (System.Collections.IDictionary)component.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(component)!;
        var draft = dictionary[key]!;
        foreach (var (name, value) in values)
        {
            draft.GetType().GetProperty(name)!.SetValue(draft, value);
        }
    }

    private static JsonElement Item(object value, params string[] rels)
    {
        var json = JsonSerializer.SerializeToElement(value);
        using var document = JsonDocument.Parse(json.GetRawText());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }
            writer.WritePropertyName("_links");
            JsonSerializer.Serialize(writer, rels.ToDictionary(rel => rel, rel => new { href = $"/api/{rel}", method = MethodFor(rel) }));
            writer.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static string MethodFor(string rel) => rel switch
    {
        "edit" or "origins" or "mappings" => "PUT",
        "delete" => "DELETE",
        _ => "POST"
    };
}
