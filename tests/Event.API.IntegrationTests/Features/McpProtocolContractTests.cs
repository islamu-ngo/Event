using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Mcp;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Models;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Extensions;
using IWebHostBuilder = Microsoft.AspNetCore.Hosting.IWebHostBuilder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApiIntegrationTests.Features;

public sealed class McpProtocolContractTests
{
    private const string SensitiveMarker = "private-prompt-marker-7f29";
    private static readonly string[] ProjectedProposalToolNames = AiMcpProjectedToolFactory
        .CreateTools(AiToolContractRegistry.CreateDefault())
        .Select(tool => tool.ProtocolTool.Name)
        .ToArray();

    [Test]
    public async Task AuthenticatedClient_CanDiscoverExpectedMcpSurface()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var httpClient = factory.CreateClient();
        var mcp = McpProtocolTestClient.Authenticated(httpClient);

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
        await using var factory = CreateMcpEnabledFactory();
        using var httpClient = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var conversationId = await CreateConversationAsync(httpClient, userId);
        var existingEvent = await SeedOwnedDraftEventAsync(
            factory,
            userId,
            $"MCP Existing Draft {Guid.NewGuid():N}");
        var eventCountBefore = await CountEventsAsync(factory);
        var aspectCountsBefore = await ReadAspectCountsAsync(factory);
        var existingEventBefore = await ReadEventStateAsync(factory, existingEvent.EventId);
        var mcp = McpProtocolTestClient.Authenticated(httpClient, userId);

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

        using var detailRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            userId);
        using var detailResponse = await httpClient.SendAsync(detailRequest);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detail = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        await Assert.That(detail.RootElement.GetProperty("proposedActions").GetArrayLength()).IsEqualTo(11);
    }

    [Test]
    public async Task ToolsCall_WhenModerationTargetAuthorizationDenied_ReturnsFailureWithoutPersistingProposal()
    {
        var deniedChecks = new List<AuthorizationRequest>();
        var authorizationProvider = new StubAuthorizationProvider
        {
            CheckPredicate = check =>
            {
                if (check.ResourceKind == ResourceKinds.Event &&
                    check.Action == AuthorizationActions.Events.ModerateHeavy)
                {
                    deniedChecks.Add(check);
                    return false;
                }

                return true;
            }
        };
        await using var factory = CreateMcpEnabledFactory(authorizationProvider);
        using var httpClient = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var conversationId = await CreateConversationAsync(httpClient, userId);
        var existingEvent = await SeedOwnedDraftEventAsync(
            factory,
            userId,
            $"MCP Moderation Denied {Guid.NewGuid():N}");
        var mcp = McpProtocolTestClient.Authenticated(httpClient, userId);

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
        await Assert.That(deniedChecks).HasSingleItem(check =>
            check.ResourceId == existingEvent.EventId.ToString() &&
            HasEventScopedFacts(check, existingEvent.EventId, PlatformDefaults.DefaultTenantId));

        using var detailRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            userId);
        using var detailResponse = await httpClient.SendAsync(detailRequest);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detail = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        await Assert.That(detail.RootElement.GetProperty("proposedActions").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task ToolsCall_WhenInstanceAdminTargetsCrossTenantModeration_UsesTargetTenantAndPersistsProposal()
    {
        var targetTenantId = Guid.CreateVersion7();
        var moderationChecks = new List<AuthorizationRequest>();
        var authorizationProvider = new StubAuthorizationProvider
        {
            CheckPredicate = check =>
            {
                if (check.ResourceKind == ResourceKinds.Event &&
                    check.Action == AuthorizationActions.Events.ModerateHeavy)
                {
                    moderationChecks.Add(check);
                    return check.Facts is EventScopedAuthorizationFacts facts && facts.TenantId == targetTenantId;
                }

                return true;
            }
        };
        await using var factory = CreateMcpEnabledFactory(authorizationProvider);
        using var httpClient = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var instanceAdminHeader = TestAuthHandler.CreateInstanceAdminHeaderValue(userId);
        var conversationId = await CreateConversationAsync(httpClient, userId, instanceAdminHeader);
        var existingEvent = await SeedOwnedDraftEventAsync(
            factory,
            userId,
            $"MCP Instance Admin Cross Tenant {Guid.NewGuid():N}",
            targetTenantId);
        var eventStateBefore = await ReadEventStateAsync(factory, existingEvent.EventId);
        var mcp = McpProtocolTestClient.AuthenticatedWithHeader(httpClient, userId, instanceAdminHeader);

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
        await Assert.That(moderationChecks).HasSingleItem(check =>
            check.ResourceId == existingEvent.EventId.ToString() &&
            HasEventScopedFacts(check, existingEvent.EventId, targetTenantId));
        await Assert.That((await ReadEventStateAsync(factory, existingEvent.EventId))).IsEqualTo(eventStateBefore);

        using var detailRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            userId,
            instanceAdminHeader);
        using var detailResponse = await httpClient.SendAsync(detailRequest);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detail = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        var proposedActions = detail.RootElement.GetProperty("proposedActions");
        await Assert.That(proposedActions.GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task ProtocolErrors_DoNotEchoSensitivePayloadsOrSecrets()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var httpClient = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var conversationId = await CreateConversationAsync(httpClient, userId);
        var mcp = McpProtocolTestClient.Authenticated(httpClient, userId);

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
        await using var factory = CreateFactory(mcpEnabled: false);
        using var httpClient = factory.CreateClient();
        var mcp = McpProtocolTestClient.Authenticated(httpClient);

        using var response = await mcp.SendRawAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private static McpProtocolContractFactory CreateMcpEnabledFactory(
        StubAuthorizationProvider? authorizationProvider = null)
        => CreateFactory(mcpEnabled: true, authorizationProvider);

    private static McpProtocolContractFactory CreateFactory(
        bool mcpEnabled,
        StubAuthorizationProvider? authorizationProvider = null)
        => new(mcpEnabled, authorizationProvider);

    private static async Task<Guid> CreateConversationAsync(
        HttpClient client,
        Guid userId,
        string? authHeaderValue = null)
    {
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            "/api/ai/assistant/conversations",
            userId,
            authHeaderValue);
        request.Content = JsonContent.Create(new CreateAiConversationRequestDto
        {
            Title = "MCP protocol contract"
        });

        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        return body.Id;
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string url,
        Guid userId,
        string? authHeaderValue = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, authHeaderValue ?? TestAuthHandler.CreateAuthHeaderValue(userId));
        return request;
    }

    private static async Task<int> CountEventsAsync(McpProtocolContractFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return await dbContext.Events.CountAsync();
    }

    private static async Task<OwnedDraftEventSeed> SeedOwnedDraftEventAsync(
        McpProtocolContractFactory factory,
        Guid userId,
        string title,
        Guid? tenantId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var resolvedTenantId = tenantId ?? PlatformDefaults.DefaultTenantId;
        var tenant = await context.Tenants.FindAsync(resolvedTenantId);
        if (tenant is null)
        {
            tenant = new TenantBuilder()
                .WithId(resolvedTenantId)
                .WithFullName("Default MCP Protocol Event Tenant")
                .WithSlug($"mcp-protocol-event-{resolvedTenantId:N}"[..48])
                .Build();
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        var owner = new UserBuilder()
            .WithId(userId)
            .Build();
        var existingOwner = await context.Users.FindAsync(userId);
        if (existingOwner is null)
        {
            context.Users.Add(owner);
            await context.SaveChangesAsync();
        }
        else
        {
            owner = existingOwner;
        }

        var ownerActor = await context.Actors
            .SingleOrDefaultAsync(actor => actor.UserId == owner.Id);
        if (ownerActor is null)
        {
            ownerActor = new ActorBuilder()
                .WithUserId(owner.Id)
                .WithDisplayName("MCP Protocol Owner")
                .Build();
            context.Actors.Add(ownerActor);
            await context.SaveChangesAsync();
        }

        if (!await context.TenantUsers.AnyAsync(candidate =>
                candidate.TenantId == tenant.Id
                && candidate.UserId == owner.Id
                && candidate.ActorId == ownerActor.Id))
        {
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                UserId = owner.Id,
                User = owner,
                ActorId = ownerActor.Id,
                Actor = ownerActor,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

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
        McpProtocolContractFactory factory,
        Guid eventId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return await Explore.Persistence.QueryFilters.QueryFilterExtensions.IgnoreTenantFilter(
                dbContext.Events.AsNoTracking(),
                Explore.Persistence.QueryFilters.TenantFilterBypassReasons.EventAuthorizationTargetResolution)
            .Where(@event => @event.Id == eventId)
            .Select(@event => new EventState(@event.Title, @event.ConcurrencyStamp, @event.EventStatusId))
            .SingleAsync();
    }

    private static async Task<AspectCounts> ReadAspectCountsAsync(McpProtocolContractFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
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

    private sealed class McpProtocolTestClient(HttpClient client, Guid userId)
    {
        private int _nextId;
        private string? _authHeaderValue;

        public static McpProtocolTestClient Authenticated(HttpClient client, Guid? userId = null)
            => new(client, userId ?? Guid.CreateVersion7());

        public static McpProtocolTestClient AuthenticatedWithHeader(
            HttpClient client,
            Guid userId,
            string authHeaderValue)
            => new(client, userId)
            {
                _authHeaderValue = authHeaderValue
            };

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
            var request = CreateBaseRequest();
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
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
            var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            request.Headers.Add(TestAuthHandler.AuthHeaderName, _authHeaderValue ?? TestAuthHandler.CreateAuthHeaderValue(userId));
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

    private sealed class McpProtocolContractFactory(
        bool mcpEnabled,
        StubAuthorizationProvider? authorizationProvider) : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            AuthorizationProviderOverride = authorizationProvider ?? new StubAuthorizationProvider();
            AdditionalConfiguration["Mcp:Enabled"] = mcpEnabled ? "true" : "false";
            AdditionalConfiguration["Mcp:EndpointPath"] = "/mcp";
            AdditionalConfiguration["Mcp:Stateless"] = "true";
            AdditionalConfiguration["Mcp:EnableLegacySse"] = "false";
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHierarchicalSettingsResolver>();
                services.AddSingleton<IHierarchicalSettingsResolver>(new FixedAiSettingsResolver(CreateEnabledAiSettings()));

                services.RemoveAll<IAiConversationRepository>();
                services.AddSingleton<InMemoryAiConversationStore>();
                services.AddScoped<IAiConversationRepository, InMemoryAiConversationRepository>();
            });
        }
    }


    private sealed class InMemoryAiConversationStore
    {
        public Dictionary<Guid, AiConversation> Conversations { get; } = [];
        public List<AiToolExecution> ToolExecutions { get; } = [];
    }

    private sealed class InMemoryAiConversationRepository(
        InMemoryAiConversationStore store,
        ITenantContext tenantContext) : IAiConversationRepository
    {
        private IEnumerable<AiConversation> TenantConversations
            => store.Conversations.Values.Where(conversation => conversation.TenantId == tenantContext.TenantId);

        public Task<AiConversation?> GetById(Guid id)
            => Task.FromResult(TenantConversations.FirstOrDefault(conversation => conversation.Id == id));

        public Task<IReadOnlyList<AiConversation>> GetAll()
            => Task.FromResult<IReadOnlyList<AiConversation>>(TenantConversations.ToArray());

        public Task<(IReadOnlyList<AiConversation> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize)
        {
            var conversations = TenantConversations.ToArray();
            var items = conversations
                .Skip(Math.Max(0, pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToArray();

            return Task.FromResult<(
                IReadOnlyList<AiConversation> Items,
                int TotalCount)>((items, conversations.Length));
        }

        public Task<bool> Exists(Guid id)
            => Task.FromResult(TenantConversations.Any(conversation => conversation.Id == id));

        public Task<AiConversation> Create(AiConversation entity)
        {
            store.Conversations[entity.Id] = entity;
            return Task.FromResult(entity);
        }

        public Task Update(AiConversation entity)
        {
            store.Conversations[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task Delete(AiConversation entity)
        {
            store.Conversations.Remove(entity.Id);
            return Task.CompletedTask;
        }

        public Task<int> HardDeleteUserConversationGraphAsync(Guid subjectId, CancellationToken cancellationToken)
        {
            var deletedConversations = TenantConversations
                .Where(conversation => conversation.UserId == subjectId)
                .ToArray();
            var deletedActionIds = deletedConversations
                .SelectMany(conversation => conversation.ProposedActions)
                .Select(action => action.Id)
                .ToHashSet();

            foreach (AiConversation conversation in deletedConversations)
            {
                store.Conversations.Remove(conversation.Id);
            }

            if (deletedActionIds.Count > 0)
            {
                store.ToolExecutions.RemoveAll(execution => deletedActionIds.Contains(execution.ProposedActionId));
            }

            return Task.FromResult(deletedConversations.Length);
        }

        public Task<AiConversation?> GetByIdWithDetailsAsync(Guid conversationId, CancellationToken cancellationToken)
            => GetById(conversationId);

        public Task<AiConversation?> GetByIdForUpdateAsync(Guid conversationId, CancellationToken cancellationToken)
            => GetById(conversationId);

        public Task<IReadOnlyList<AiConversation>> ListRecentForUserAsync(
            Guid userId,
            int limit,
            CancellationToken cancellationToken)
        {
            var conversations = TenantConversations
                .Where(conversation => conversation.UserId == userId)
                .OrderByDescending(conversation => conversation.UpdatedAt ?? conversation.CreatedAt)
                .ThenByDescending(conversation => conversation.Id)
                .Take(Math.Max(0, limit))
                .ToArray();

            return Task.FromResult<IReadOnlyList<AiConversation>>(conversations);
        }

        public Task<int> CountUserMessagesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken)
        {
            var count = TenantConversations
                .SelectMany(conversation => conversation.Messages)
                .Count(message => message.Role == AiMessageRole.User &&
                    message.CreatedBy == userId &&
                    message.CreatedAt >= sinceUtc);

            return Task.FromResult(count);
        }

        public Task<int> CountTenantMessagesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken)
        {
            var count = TenantConversations
                .SelectMany(conversation => conversation.Messages)
                .Count(message => message.Role == AiMessageRole.User && message.CreatedAt >= sinceUtc);

            return Task.FromResult(count);
        }

        public Task<int> ReleaseStaleRunningConversationsForUserAsync(
            Guid userId,
            DateTime staleBeforeUtc,
            string failureCode,
            string failureMessage,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            var staleConversations = TenantConversations
                .Where(conversation => conversation.UserId == userId)
                .Where(conversation => conversation.Status == AiConversationStatus.Running)
                .Where(conversation => conversation.Runs.Any(run =>
                    run.Status is AiRunStatus.Queued or AiRunStatus.InProgress
                    && (run.StartedAt ?? run.QueuedAt) <= staleBeforeUtc))
                .Where(conversation => !conversation.Runs.Any(run =>
                    run.Status is AiRunStatus.Queued or AiRunStatus.InProgress
                    && (run.StartedAt ?? run.QueuedAt) > staleBeforeUtc))
                .ToList();

            foreach (var conversation in staleConversations)
            {
                foreach (var run in conversation.Runs.Where(run =>
                    run.Status is AiRunStatus.Queued or AiRunStatus.InProgress
                    && (run.StartedAt ?? run.QueuedAt) <= staleBeforeUtc))
                {
                    run.Fail(failureCode, failureMessage, utcNow);
                }

                conversation.Activate(utcNow);
            }

            return Task.FromResult(staleConversations.Count);
        }

        public Task<int> CountRunningConversationsForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            var count = TenantConversations
                .Count(conversation => conversation.UserId == userId && conversation.Status == AiConversationStatus.Running);

            return Task.FromResult(count);
        }


        public Task<AiProposedAction?> GetProposedActionForUpdateAsync(Guid proposedActionId, CancellationToken cancellationToken)
        {
            var action = TenantConversations
                .SelectMany(conversation => conversation.ProposedActions)
                .FirstOrDefault(candidate => candidate.Id == proposedActionId);

            return Task.FromResult(action);
        }

        public Task UpdateProposedActionAsync(AiProposedAction proposedAction, CancellationToken cancellationToken)
        {
            var existingAction = TenantConversations
                .SelectMany(conversation => conversation.ProposedActions)
                .FirstOrDefault(candidate => candidate.Id == proposedAction.Id);

            if (existingAction is null)
            {
                return Task.CompletedTask;
            }

            existingAction.Status = proposedAction.Status;
            existingAction.ConfirmedBy = proposedAction.ConfirmedBy;
            existingAction.ConfirmedAt = proposedAction.ConfirmedAt;
            existingAction.RejectedBy = proposedAction.RejectedBy;
            existingAction.RejectedAt = proposedAction.RejectedAt;
            existingAction.ResultResourceId = proposedAction.ResultResourceId;
            existingAction.FailureCode = proposedAction.FailureCode;
            existingAction.FailureMessage = proposedAction.FailureMessage;

            return Task.CompletedTask;
        }

        public Task CreateToolExecutionAsync(AiToolExecution toolExecution, CancellationToken cancellationToken)
        {
            store.ToolExecutions.Add(toolExecution);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiToolExecution>> ListToolExecutionsForProposedActionAsync(
            Guid proposedActionId,
            CancellationToken cancellationToken)
        {
            var executions = store.ToolExecutions
                .Where(execution => execution.TenantId == tenantContext.TenantId && execution.ProposedActionId == proposedActionId)
                .OrderByDescending(execution => execution.StartedAt)
                .ThenByDescending(execution => execution.Id)
                .ToArray();

            return Task.FromResult<IReadOnlyList<AiToolExecution>>(executions);
        }

        public Task<AiRetentionCleanupResult> RedactExpiredConversationsAsync(
            DateTime cutoffUtc,
            int retentionDays,
            DateTime utcNow,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            var eligibleConversations = TenantConversations
                .Count(conversation => (conversation.UpdatedAt ?? conversation.CreatedAt) <= cutoffUtc);

            return Task.FromResult(new AiRetentionCleanupResult(
                cutoffUtc,
                retentionDays,
                eligibleConversations,
                RedactedConversations: 0,
                RedactedMessages: 0,
                RedactedRuns: 0,
                RedactedReferences: 0,
                RedactedProposedActions: 0,
                RedactedToolExecutions: 0,
                DryRun: dryRun));
        }
    }

    private sealed class FixedAiSettingsResolver(AiAssistantSettingGroup settings) : IHierarchicalSettingsResolver
    {
        public Task<T?> ResolveAsync<T>(string key, SettingContext context, CancellationToken ct = default)
            => Task.FromResult(default(T));

        public Task<ResolvedSetting?> ResolveWithMetadataAsync(string key, SettingContext context, CancellationToken ct = default)
            => Task.FromResult<ResolvedSetting?>(null);

        public Task<IReadOnlyList<ResolvedSetting>> ResolveBatchAsync(
            IEnumerable<string> keys,
            SettingContext context,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResolvedSetting>>([]);

        public Task<TGroup> ResolveGroupAsync<TGroup>(SettingContext context, CancellationToken ct = default)
            where TGroup : ISettingGroup, new()
        {
            if (typeof(TGroup) == typeof(AiAssistantSettingGroup))
            {
                return Task.FromResult((TGroup)(object)settings);
            }

            return Task.FromResult(new TGroup());
        }

        public Task SetValueAsync(
            string key,
            string value,
            SettingScope scope,
            Guid scopeId,
            Guid actorId,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveOverrideAsync(
            string key,
            SettingScope scope,
            Guid scopeId,
            Guid actorId,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task LockAsync(
            string key,
            SettingScope scope,
            Guid scopeId,
            Guid actorId,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UnlockAsync(
            string key,
            SettingScope scope,
            Guid scopeId,
            Guid actorId,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public void InvalidateCache(SettingScope? scope = null, Guid? scopeId = null)
        {
        }

        public void InvalidateOrganizationCache(Guid tenantId, Guid organizationId)
        {
        }

        public void InvalidateUserCache(Guid tenantId, Guid userId)
        {
        }
    }

    private static AiAssistantSettingGroup CreateEnabledAiSettings()
    {
        var values = new Dictionary<string, object?>
        {
            [GovernanceSettingKeys.AiAssistant.Enabled] = true,
            [GovernanceSettingKeys.AiAssistant.Provider] = AiProviderDefaults.ProviderFake,
            [GovernanceSettingKeys.AiAssistant.DailyMessageLimit] = 50,
            [GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled] = true
        };

        var resolved = values.ToDictionary(
            pair => pair.Key,
            pair => new ResolvedSetting
            {
                Key = pair.Key,
                Value = JsonSerializer.Serialize(pair.Value),
                Source = SettingSource.SystemDefault,
                IsLocked = false
            });

        var group = new AiAssistantSettingGroup();
        group.Populate(resolved);
        return group;
    }
}
