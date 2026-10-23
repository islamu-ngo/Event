using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Features;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.API.Mcp;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ApiIntegrationTests.Features;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class McpProtocolContractTests(PostgreSqlContainerFixture postgres)
{
    private const string SensitiveMarker = "private-prompt-marker-7f29";
    private static readonly string[] ProjectedProposalToolNames = AiMcpProjectedToolFactory
        .CreateTools(AiToolContractRegistry.CreateDefault())
        .Select(tool => tool.ProtocolTool.Name)
        .ToArray();

    [Test]
    public async Task AuthenticatedClient_CanDiscoverExpectedMcpSurface()
    {
        await using var factory = await McpProtocolNativeFixture.CreateAsync(postgres);
        var mcp = new McpProtocolTestClient(factory);

        using var initialize = await mcp.InvokeAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "islamu-event-mcp-contract-tests",
                ["version"] = "1.0.0"
            }
        });
        using var tools = await mcp.InvokeAsync("tools/list");
        using var resources = await mcp.InvokeAsync("resources/list");
        using var resourceTemplates = await mcp.InvokeAsync("resources/templates/list");
        using var prompts = await mcp.InvokeAsync("prompts/list");

        await Assert.That(GetResult(initialize).TryGetProperty("protocolVersion", out _)).IsTrue();
        string[] expectedToolNames =
        [
            "list_ai_tool_contracts",
            "search_public_events",
            "get_public_event",
            "get_public_event_program_summary",
            "list_public_event_sessions",
            "list_my_events",
            "get_event_creation_context",
            "get_event_publish_readiness",
            "get_event_program_management_context",
            "get_event_custom_properties_context",
            "get_event_registrations_context",
            "get_event_team_context",
            "get_event_template_catalog_context",
            "get_event_template_sync_context",
            "get_event_session_template_sync_context",
            "propose_ai_tool_action",
            .. ProjectedProposalToolNames
        ];
        await Assert.That(expectedToolNames.All(GetNames(GetResult(tools), "tools").Contains)).IsTrue();
        await Assert.That(GetNames(GetResult(resources), "resources")).Contains("ai_conversations");
        await Assert.That(new[] { "ai_conversation_detail",
        "event_management_context" }.All(GetNames(GetResult(resourceTemplates), "resourceTemplates").Contains)).IsTrue();
        await Assert.That(new[] { "create_event_draft_with_confirmation",
        "manage_event_with_confirmation" }.All(GetNames(GetResult(prompts), "prompts").Contains)).IsTrue();
    }

    [Test]
    public async Task ToolsCall_ReturnsRedactedRegistryContractsAndProposalOnlyResults()
    {
        await using var factory = await McpProtocolNativeFixture.CreateAsync(postgres);
        using var httpClient = factory.Client;
        var userId = factory.UserId;
        var conversationId = await CreateConversationAsync(factory);
        var existingEvent = await SeedOwnedDraftEventAsync(
            factory,
            userId,
            $"MCP Existing Draft {Guid.NewGuid():N}");
        var eventCountBefore = await CountEventsAsync(factory);
        var aspectCountsBefore = await ReadAspectCountsAsync(factory);
        var existingEventBefore = await ReadEventStateAsync(factory, existingEvent.EventId);
        var mcp = new McpProtocolTestClient(factory);

        using var registryCall = await mcp.CallToolAsync("list_ai_tool_contracts", new JsonObject());
        var registryText = await GetFirstTextContent(GetResult(registryCall));
        using var registry = JsonDocument.Parse(registryText);
        await Assert.That(registry.RootElement.GetProperty("Tools").GetArrayLength()).IsGreaterThan(0);
        await Assert.That(registryText).Contains("CreateEventDraft");
        var normalizedRegistryText = registryText.ToLowerInvariant();
        await Assert.That(normalizedRegistryText).DoesNotContain("providerendpoint");
        await Assert.That(normalizedRegistryText).DoesNotContain("apikey");

        using var genericProposal = await mcp.CallToolAsync("propose_ai_tool_action", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["toolName"] = "CreateEventDraft",
            ["payloadJson"] = "{\"title\":\"Generic MCP protocol draft\",\"participationConfiguration\":{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}",
            ["summary"] = "Generic proposal smoke"
        });
        using var projectedProposal = await mcp.CallToolAsync("propose_create_event_draft", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected proposal smoke",
            ["title"] = "Projected MCP protocol draft",
            ["participationConfiguration"] = new JsonObject
            {
                ["participationHandlingModeId"] = 1,
                ["advanceRegistrationObligationId"] = 1
            }
        });
        using var projectedUpdateProposal = await mcp.CallToolAsync("propose_update_event_draft", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected update proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["expectedParticipationConfigurationConcurrencyStamp"] = existingEvent.ParticipationConfigurationConcurrencyStamp.ToString(),
            ["title"] = "Projected MCP protocol update",
            ["participationConfiguration"] = new JsonObject
            {
                ["participationHandlingModeId"] = existingEvent.ParticipationHandlingModeId,
                ["advanceRegistrationObligationId"] = existingEvent.AdvanceRegistrationObligationId
            }
        });
        using var projectedPublishProposal = await mcp.CallToolAsync("propose_publish_event", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected publish proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["readinessIsReady"] = true,
            ["readinessErrorCount"] = 0
        });
        using var projectedDeleteProposal = await mcp.CallToolAsync("propose_delete_event", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected delete proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["managementContextHasDelete"] = true,
            ["destructiveSummary"] = "Delete duplicate draft from protocol smoke.",
            ["confirmationPhrase"] = "DELETE_EVENT",
            ["acknowledgedConsequences"] = true
        });
        using var projectedIslamicAspectProposal = await mcp.CallToolAsync("propose_upsert_event_islamic_aspect", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected Islamic aspect proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["aspectKind"] = "islamic",
            ["managementContextHasEdit"] = true,
            ["genderMode"] = 0,
            ["includesQuranRecitation"] = true
        });
        using var projectedDeleteIslamicAspectProposal = await mcp.CallToolAsync("propose_delete_event_islamic_aspect", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected Islamic aspect delete proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["aspectKind"] = "islamic",
            ["managementContextHasEdit"] = true,
            ["destructiveSummary"] = "Remove stale Islamic aspect metadata.",
            ["confirmationPhrase"] = "DELETE_ISLAMIC_ASPECT",
            ["acknowledgedConsequences"] = true
        });
        using var projectedTechAspectProposal = await mcp.CallToolAsync("propose_upsert_event_tech_aspect", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected Tech aspect proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["aspectKind"] = "tech",
            ["managementContextHasEdit"] = true,
            ["skillLevel"] = 0,
            ["requiresLaptop"] = true
        });
        using var projectedDeleteTechAspectProposal = await mcp.CallToolAsync("propose_delete_event_tech_aspect", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected Tech aspect delete proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["aspectKind"] = "tech",
            ["managementContextHasEdit"] = true,
            ["destructiveSummary"] = "Remove stale Tech aspect metadata.",
            ["confirmationPhrase"] = "DELETE_TECH_ASPECT",
            ["acknowledgedConsequences"] = true
        });
        using var projectedSessionProposal = await mcp.CallToolAsync("propose_create_event_session", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected session proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["managementContextHasAddSession"] = true,
            ["title"] = "MCP protocol opening session",
            ["startTime"] = "2026-07-01T09:00:00Z",
            ["endTime"] = "2026-07-01T10:00:00Z"
        });
        using var projectedTemplateSyncProposal = await mcp.CallToolAsync("propose_apply_event_template_sync", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected template sync proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["managementContextHasEdit"] = true,
            ["baseProvenanceVersion"] = 2,
            ["plan"] = new JsonObject
            {
                ["targetTemplateVersion"] = 3,
                ["baseProvenanceVersion"] = 2,
                ["addedDefinitionKeys"] = new JsonArray("sessions.track"),
                ["modifiedDefinitionKeys"] = new JsonArray(),
                ["retiredDefinitionKeys"] = new JsonArray(),
                ["addedOptionKeys"] = new JsonArray(),
                ["modifiedOptionKeys"] = new JsonArray(),
                ["retiredOptionKeys"] = new JsonArray()
            },
            ["destructiveSummary"] = "Apply reviewed template sync changes.",
            ["confirmationPhrase"] = "APPLY_EVENT_TEMPLATE_SYNC",
            ["acknowledgedConsequences"] = true
        });

        await AssertSuccessfulToolResult(genericProposal);
        await AssertSuccessfulToolResult(projectedProposal);
        await AssertSuccessfulToolResult(projectedUpdateProposal);
        await AssertSuccessfulToolResult(projectedPublishProposal);
        await AssertSuccessfulToolResult(projectedDeleteProposal);
        await AssertSuccessfulToolResult(projectedIslamicAspectProposal);
        await AssertSuccessfulToolResult(projectedDeleteIslamicAspectProposal);
        await AssertSuccessfulToolResult(projectedTechAspectProposal);
        await AssertSuccessfulToolResult(projectedDeleteTechAspectProposal);
        await AssertSuccessfulToolResult(projectedSessionProposal);
        await AssertSuccessfulToolResult(projectedTemplateSyncProposal);
        await Assert.That((await CountEventsAsync(factory))).IsEqualTo(eventCountBefore);
        await Assert.That((await ReadAspectCountsAsync(factory))).IsEqualTo(aspectCountsBefore);
        var existingEventAfter = await ReadEventStateAsync(factory, existingEvent.EventId);
        await Assert.That(existingEventAfter).IsEqualTo(existingEventBefore);

        using var detailRequest = factory.CreateRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}");
        using var detailResponse = await httpClient.SendAsync(detailRequest);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detail = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        await Assert.That(detail.RootElement.GetProperty("proposedActions").GetArrayLength()).IsEqualTo(11);
    }

    [Test]
    public async Task ToolsCall_WhenModerationTargetAuthorizationDenied_ReturnsFailureWithoutPersistingProposal()
    {
        await using var factory = await McpProtocolNativeFixture.CreateAsync(postgres);
        using var httpClient = factory.Client;
        var userId = factory.UserId;
        var conversationId = await CreateConversationAsync(factory);
        var existingEvent = await SeedOwnedDraftEventAsync(
            factory,
            userId,
            $"MCP Moderation Denied {Guid.NewGuid():N}");
        var mcp = new McpProtocolTestClient(factory);

        using var projectedModerationProposal = await mcp.CallToolAsync("propose_heavy_moderate_event", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected moderation proposal denial smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["managementContextHasModerateHeavy"] = true,
            ["reasonCode"] = "policy-review",
            ["destructiveSummary"] = "Restrict event visibility until policy review completes.",
            ["confirmationPhrase"] = "HEAVY_MODERATE_EVENT",
            ["acknowledgedConsequences"] = true
        });

        using var descriptor = JsonDocument.Parse(await GetFirstTextContent(GetResult(projectedModerationProposal)));
        await Assert.That(descriptor.RootElement.GetProperty("Success").GetBoolean()).IsFalse();
        await Assert.That(descriptor.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("tool_authorization_denied");
        await Assert.That(factory.ModerationChecks).HasSingleItem(check =>
            check.ResourceId == existingEvent.EventId.ToString() &&
            HasEventScopedFacts(check, existingEvent.EventId, factory.TenantId));

        using var detailRequest = factory.CreateRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}");
        using var detailResponse = await httpClient.SendAsync(detailRequest);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detail = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        await Assert.That(detail.RootElement.GetProperty("proposedActions").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task ToolsCall_WhenInstanceAdminTargetsCrossTenantModeration_UsesTargetTenantAndPersistsProposal()
    {
        await using var factory = await McpProtocolNativeFixture.CreateAsync(postgres, instanceAdmin: true);
        var targetTenantId = factory.OtherTenantId;
        using var httpClient = factory.Client;
        var userId = factory.UserId;
        var conversationId = await CreateConversationAsync(factory);
        var existingEvent = await SeedOwnedDraftEventAsync(
            factory,
            userId,
            $"MCP Instance Admin Cross Tenant {Guid.NewGuid():N}",
            targetTenantId);
        var eventStateBefore = await ReadEventStateAsync(factory, existingEvent.EventId);
        var mcp = new McpProtocolTestClient(factory);

        using var projectedModerationProposal = await mcp.CallToolAsync("propose_heavy_moderate_event", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["summary"] = "Projected cross-tenant moderation proposal smoke",
            ["eventId"] = existingEvent.EventId.ToString(),
            ["expectedConcurrencyStamp"] = existingEvent.ConcurrencyStamp.ToString(),
            ["managementContextHasModerateHeavy"] = true,
            ["reasonCode"] = "policy-review",
            ["destructiveSummary"] = "Restrict event visibility until platform policy review completes.",
            ["confirmationPhrase"] = "HEAVY_MODERATE_EVENT",
            ["acknowledgedConsequences"] = true
        });

        await AssertSuccessfulToolResult(projectedModerationProposal);
        await Assert.That(factory.ModerationChecks).HasSingleItem(check =>
            check.ResourceId == existingEvent.EventId.ToString() &&
            HasEventScopedFacts(check, existingEvent.EventId, targetTenantId));
        await Assert.That((await ReadEventStateAsync(factory, existingEvent.EventId))).IsEqualTo(eventStateBefore);

        using var detailRequest = factory.CreateRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}");
        using var detailResponse = await httpClient.SendAsync(detailRequest);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detail = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        var proposedActions = detail.RootElement.GetProperty("proposedActions");
        await Assert.That(proposedActions.GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task ProtocolErrors_DoNotEchoSensitivePayloadsOrSecrets()
    {
        await using var factory = await McpProtocolNativeFixture.CreateAsync(postgres);
        var conversationId = await CreateConversationAsync(factory);
        var mcp = new McpProtocolTestClient(factory);

        using var malformed = await mcp.SendRawAsync(
            "{ \"jsonrpc\": \"2.0\", \"id\": 101, \"method\": \"tools/call\", " +
            $"\"params\": {{ \"name\": \"missing\", \"arguments\": {{ \"prompt\": \"{SensitiveMarker}\" }} }} ");
        await Assert.That(malformed.StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized);
        await AssertNoSensitiveEcho(await malformed.Content.ReadAsStringAsync());

        using var unknownTool = await mcp.CallToolAsync("unknown_tool", new JsonObject
        {
            ["prompt"] = SensitiveMarker,
            ["apiKey"] = "redacted-test-api-key"
        }, expectProtocolSuccess: false);
        await AssertJsonRpcFailureDoesNotEchoSensitiveData(unknownTool);

        using var hiddenField = await mcp.CallToolAsync("propose_create_event_draft", new JsonObject
        {
            ["conversationId"] = conversationId.ToString(),
            ["title"] = "Hidden field smoke",
            ["tenantId"] = SensitiveMarker
        });
        var hiddenDescriptor = JsonDocument.Parse(await GetFirstTextContent(GetResult(hiddenField)));
        await Assert.That(hiddenDescriptor.RootElement.GetProperty("Success").GetBoolean()).IsFalse();
        await Assert.That(hiddenDescriptor.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("invalid_tool_arguments");
        await AssertNoSensitiveEcho(hiddenDescriptor.RootElement.GetRawText());
    }

    [Test]
    public async Task McpEndpoint_WhenDisabled_ReturnsNotFound()
    {
        await using var factory = await McpProtocolNativeFixture.CreateAsync(postgres, mcpEnabled: false);
        var mcp = new McpProtocolTestClient(factory);

        using var response = await mcp.SendRawAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> CreateConversationAsync(McpProtocolNativeFixture factory)
    {
        using var request = factory.CreateRequest(
            HttpMethod.Post,
            "/api/ai/assistant/conversations");
        request.Content = JsonContent.Create(new CreateAiConversationRequestDto
        {
            Title = "MCP protocol contract"
        });

        using var response = await factory.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created)
            .Because(factory.RequestFailure?.GetType().Name ?? "The original signed caller must create the conversation.");
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        return body.Id;
    }

    private static async Task<int> CountEventsAsync(McpProtocolNativeFixture factory)
    {
        await using var dbContext = factory.CreateSetupContext();
        return await dbContext.Events.CountAsync();
    }

    private static async Task<OwnedDraftEventSeed> SeedOwnedDraftEventAsync(
        McpProtocolNativeFixture factory,
        Guid userId,
        string title,
        Guid? tenantId = null)
    {
        await using var context = factory.CreateSetupContext();
        var resolvedTenantId = tenantId ?? factory.TenantId;
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == resolvedTenantId);
        var ownerActor = await context.Actors.SingleAsync(actor => actor.UserId == userId);

        var @event = new EventBuilder()
            .WithTitle(title)
            .WithDescription($"Description for {title}")
            .WithActorId(ownerActor.Id)
            .WithTenantId(tenant.Id)
            .WithStatus(EventStatusEnum.Draft)
            .WithVisibility(VisibilityTypeEnum.Private)
            .WithSessionDates(
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)))
            .Build();
        context.Events.Add(@event);
        // Use the seeded EventOwner role and its real permission mappings. The
        // same owner still has no platform/tenant moderation authority.
        context.EventRoleAssignments.Add(EventRoleAssignment.Create(
            tenant.Id, @event.Id, userId, (int)RoleEnum.EventOwner,
            EventRoleAssignmentStatus.Active, DateTime.UtcNow, null, userId));
        await context.SaveChangesAsync();

        @event.CreatedBy = userId;
        await context.SaveChangesAsync();

        return new OwnedDraftEventSeed(
            @event.Id,
            @event.ConcurrencyStamp,
            @event.ParticipationConfiguration!.ConcurrencyStamp,
            @event.ParticipationConfiguration.ParticipationHandlingModeId,
            @event.ParticipationConfiguration.AdvanceRegistrationObligationId);
    }

    private static async Task<EventState> ReadEventStateAsync(
        McpProtocolNativeFixture factory,
        Guid eventId)
    {
        await using var dbContext = factory.CreateSetupContext();
        return await dbContext.Events.AsNoTracking()
            .Where(@event => @event.Id == eventId)
            .Select(@event => new EventState(@event.Title, @event.ConcurrencyStamp, @event.EventStatusId))
            .SingleAsync();
    }

    private static async Task<AspectCounts> ReadAspectCountsAsync(McpProtocolNativeFixture factory)
    {
        await using var dbContext = factory.CreateSetupContext();
        return new AspectCounts(
            await dbContext.EventIslamicAspects.CountAsync(),
            await dbContext.EventTechAspects.CountAsync());
    }

    private static JsonElement GetResult(JsonDocument document)
        => document.RootElement.GetProperty("result");

    private static IReadOnlyList<string> GetNames(JsonElement result, string collectionName)
    {
        if (!TryGetProperty(result, collectionName, out var collection) || collection.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return collection.EnumerateArray()
            .Select(item => TryGetProperty(item, "name", out var name) ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    private static async Task<string> GetFirstTextContent(JsonElement result)
    {
        var content = result.GetProperty("content");
        await Assert.That(content.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(content.GetArrayLength()).IsGreaterThan(0);
        return content[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private static async Task AssertSuccessfulToolResult(JsonDocument document)
    {
        var descriptor = JsonDocument.Parse(await GetFirstTextContent(GetResult(document)));
        var message = descriptor.RootElement.GetProperty("Message").GetString();
        await Assert.That(descriptor.RootElement.GetProperty("Success").GetBoolean()).IsTrue().Because(message ?? string.Empty);
        await Assert.That(descriptor.RootElement.GetProperty("Message").GetString()).Contains("Confirm");
        await Assert.That(descriptor.RootElement.GetProperty("Id").GetGuid()).IsNotEqualTo(Guid.Empty);
    }

    private static async Task AssertJsonRpcFailureDoesNotEchoSensitiveData(JsonDocument document)
    {
        await Assert.That(document.RootElement.TryGetProperty("error", out _)).IsTrue();
        await AssertNoSensitiveEcho(document.RootElement.GetRawText());
    }

    private static async Task AssertNoSensitiveEcho(string value)
    {
        await Assert.That(value).DoesNotContain(SensitiveMarker);
        var normalized = value.ToLowerInvariant();
        await Assert.That(normalized).DoesNotContain("redacted-test-api-key");
        await Assert.That(normalized).DoesNotContain("bearer");
        await Assert.That(normalized).DoesNotContain("stack trace");
        await Assert.That(value).DoesNotContain("System.");
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// A model-proposed moderation action is decided against the event the server loaded, so the check must
    /// carry that event and its owning tenant -- never identifiers echoed back from the tool payload.
    /// </summary>
    private static bool HasEventScopedFacts(AuthorizationRequest check, Guid eventId, Guid tenantId) =>
        check.Facts is EventScopedAuthorizationFacts facts &&
        facts.EventId == eventId &&
        facts.TenantId == tenantId;

    private sealed record OwnedDraftEventSeed(
        Guid EventId,
        Guid ConcurrencyStamp,
        Guid ParticipationConfigurationConcurrencyStamp,
        int ParticipationHandlingModeId,
        int AdvanceRegistrationObligationId);

    private sealed record EventState(string Title, Guid ConcurrencyStamp, int EventStatusId);

    private sealed record AspectCounts(int IslamicAspectCount, int TechAspectCount);

    private sealed class McpProtocolTestClient(McpProtocolNativeFixture factory)
    {
        private int _nextId;

        public Task<JsonDocument> InvokeAsync(string method, JsonObject? parameters = null)
            => SendJsonRpcAsync(method, parameters, expectProtocolSuccess: true);

        public Task<JsonDocument> CallToolAsync(
            string toolName,
            JsonObject arguments,
            bool expectProtocolSuccess = true)
            => SendJsonRpcAsync("tools/call", new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments
            }, expectProtocolSuccess);

        public async Task<HttpResponseMessage> SendRawAsync(string json)
        {
            using var request = CreateBaseRequest();
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return await factory.Client.SendAsync(request);
        }

        private async Task<JsonDocument> SendJsonRpcAsync(
            string method,
            JsonObject? parameters,
            bool expectProtocolSuccess)
        {
            var requestBody = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Interlocked.Increment(ref _nextId),
                ["method"] = method
            };

            if (parameters is not null)
            {
                requestBody["params"] = parameters;
            }

            using var response = await SendRawAsync(requestBody.ToJsonString());
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var document = await ReadJsonRpcDocumentAsync(response);

            if (expectProtocolSuccess)
            {
                await Assert.That(document.RootElement.TryGetProperty("error", out _)).IsFalse().Because(document.RootElement.GetRawText());
                await Assert.That(document.RootElement.TryGetProperty("result", out _)).IsTrue().Because(document.RootElement.GetRawText());
            }

            return document;
        }

        private HttpRequestMessage CreateBaseRequest()
        {
            var request = factory.CreateRequest(HttpMethod.Post, "/mcp");
            request.Headers.Add("ProtocolVersion", "2025-06-18");
            request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            return request;
        }

        private static async Task<JsonDocument> ReadJsonRpcDocumentAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            var trimmed = body.TrimStart();
            if (trimmed.StartsWith('{'))
            {
                return JsonDocument.Parse(trimmed);
            }

            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var payload = line[5..].Trim();
                if (payload.StartsWith('{'))
                {
                    return JsonDocument.Parse(payload);
                }
            }

            throw new InvalidOperationException("The MCP response did not contain a JSON-RPC message.");
        }
    }

}
