using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class EventManagementMcpPublicReadTests
{
    [Test]
    public async Task AnonymousClient_CanDiscoverPublicEventReadTools()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var client = factory.CreateClient();
        var mcp = McpProtocolClient.Anonymous(client);

        using var tools = await mcp.InvokeAsync("tools/list");
        using var resourceTemplates = await mcp.InvokeAsync("resources/templates/list");

        await Assert.That(new[] { "list_ai_tool_contracts",
        "search_public_events",
        "get_public_event",
        "get_public_event_program_summary",
        "list_public_event_sessions" }.All(GetNames(GetResult(tools), "tools").Contains)).IsTrue();
        await Assert.That(GetNames(GetResult(tools), "tools")).DoesNotContain("list_my_events");
        await Assert.That(GetNames(GetResult(tools), "tools")).DoesNotContain("get_event_creation_context");
        await Assert.That(GetNames(GetResult(tools), "tools")).DoesNotContain("get_event_publish_readiness");
        await Assert.That(GetNames(GetResult(tools), "tools")).DoesNotContain("propose_ai_tool_action");
        await Assert.That(GetNames(GetResult(tools), "tools")).DoesNotContain("propose_create_event_draft");
        await Assert.That(GetNames(GetResult(resourceTemplates), "resourceTemplates")).DoesNotContain("event_management_context");
    }

    [Test]
    public async Task SearchPublicEvents_MatchesRestVisibilityAndCapsPageSize()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var client = factory.CreateClient();
        var marker = Guid.NewGuid().ToString("N");
        var publishedTitle = $"MCP Published Event {marker}";
        var draftTitle = $"MCP Draft Event {marker}";
        var archivedTitle = $"MCP Archived Event {marker}";
        var privateTitle = $"MCP Private Event {marker}";

        await SeedEventsAsync(factory, seed =>
        {
            seed.AddEvent(publishedTitle, EventStatusEnum.Published, VisibilityTypeEnum.Public, addPublishedSession: true);
            seed.AddEvent(draftTitle, EventStatusEnum.Draft, VisibilityTypeEnum.Public);
            seed.AddEvent(archivedTitle, EventStatusEnum.Archived, VisibilityTypeEnum.Public);
            seed.AddEvent(privateTitle, EventStatusEnum.Published, VisibilityTypeEnum.Private, addPublishedSession: true);
        });

        using var restResponse = await client.GetAsync($"/api/event?searchTerm={marker}&pageNumber=1&pageSize=50");
        var restText = await restResponse.Content.ReadAsStringAsync();
        var mcp = McpProtocolClient.Anonymous(client);

        using var mcpResponse = await mcp.CallToolAsync("search_public_events", new JsonObject
        {
            ["searchTerm"] = marker,
            ["pageNumber"] = 1,
            ["pageSize"] = 999
        });
        var mcpText = await GetFirstTextContent(GetResult(mcpResponse));
        using var mcpDescriptor = JsonDocument.Parse(mcpText);

        await Assert.That(restResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restText).Contains(publishedTitle);
        await Assert.That(restText).DoesNotContain(draftTitle);
        await Assert.That(restText).DoesNotContain(archivedTitle);
        await Assert.That(restText).DoesNotContain(privateTitle);

        await Assert.That(mcpText).Contains(publishedTitle);
        await Assert.That(mcpText).DoesNotContain(draftTitle);
        await Assert.That(mcpText).DoesNotContain(archivedTitle);
        await Assert.That(mcpText).DoesNotContain(privateTitle);
        await Assert.That(mcpDescriptor.RootElement.GetProperty("PageSize").GetInt32()).IsEqualTo(25);
        await Assert.That(mcpDescriptor.RootElement.GetProperty("PageSizeWasClamped").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task GetPublicEvent_ReturnsPublishedDetailAndSafeNotFoundForHiddenEvents()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var client = factory.CreateClient();
        var marker = Guid.NewGuid().ToString("N");
        Guid publishedEventId = Guid.Empty;
        Guid draftEventId = Guid.Empty;
        Guid privateEventId = Guid.Empty;
        var publishedTitle = $"MCP Detail Published {marker}";
        var privateTitle = $"MCP Detail Private {marker}";

        await SeedEventsAsync(factory, seed =>
        {
            publishedEventId = seed.AddEvent(publishedTitle, EventStatusEnum.Published, VisibilityTypeEnum.Public);
            draftEventId = seed.AddEvent($"MCP Detail Draft {marker}", EventStatusEnum.Draft, VisibilityTypeEnum.Public);
            privateEventId = seed.AddEvent(privateTitle, EventStatusEnum.Published, VisibilityTypeEnum.Private);
        });

        using var restPublished = await client.GetAsync($"/api/event/{publishedEventId}");
        using var restDraft = await client.GetAsync($"/api/event/{draftEventId}");
        var mcp = McpProtocolClient.Anonymous(client);

        using var publishedMcpResponse = await mcp.CallToolAsync("get_public_event", new JsonObject
        {
            ["eventId"] = publishedEventId.ToString()
        });
        using var draftMcpResponse = await mcp.CallToolAsync("get_public_event", new JsonObject
        {
            ["eventId"] = draftEventId.ToString()
        });
        using var privateMcpResponse = await mcp.CallToolAsync("get_public_event", new JsonObject
        {
            ["eventId"] = privateEventId.ToString()
        });
        using var missingMcpResponse = await mcp.CallToolAsync("get_public_event", new JsonObject
        {
            ["eventId"] = Guid.CreateVersion7().ToString()
        });

        await Assert.That(restPublished.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restDraft.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        using var publishedDescriptor = JsonDocument.Parse(await GetFirstTextContent(GetResult(publishedMcpResponse)));
        await Assert.That(publishedDescriptor.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(publishedDescriptor.RootElement.GetProperty("Event").GetProperty("Title").GetString()).IsEqualTo(publishedTitle);

        using var draftDescriptor = JsonDocument.Parse(await GetFirstTextContent(GetResult(draftMcpResponse)));
        await Assert.That(draftDescriptor.RootElement.GetProperty("Found").GetBoolean()).IsFalse();
        await Assert.That(draftDescriptor.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("not_found");

        using var privateDescriptor = JsonDocument.Parse(await GetFirstTextContent(GetResult(privateMcpResponse)));
        await Assert.That(privateDescriptor.RootElement.GetProperty("Found").GetBoolean()).IsFalse();
        await Assert.That(privateDescriptor.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("not_found");
        await Assert.That(privateDescriptor.RootElement.GetRawText()).DoesNotContain(privateTitle);

        using var missingDescriptor = JsonDocument.Parse(await GetFirstTextContent(GetResult(missingMcpResponse)));
        await Assert.That(missingDescriptor.RootElement.GetProperty("Found").GetBoolean()).IsFalse();
        await Assert.That(missingDescriptor.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("not_found");
    }

    [Test]
    public async Task PublicProgramAndSessions_AreVisibleOnlyWhenEventDetailIsPubliclyVisible()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var client = factory.CreateClient();
        var marker = Guid.NewGuid().ToString("N");
        Guid publishedEventId = Guid.Empty;
        Guid draftEventId = Guid.Empty;
        Guid privateEventId = Guid.Empty;
        var publishedSessionTitle = $"MCP Published Session {marker}";
        var draftSessionTitle = $"MCP Draft Session {marker}";
        var privateSessionTitle = $"MCP Private Session {marker}";

        await SeedEventsAsync(factory, seed =>
        {
            publishedEventId = seed.AddEvent($"MCP Program Published {marker}", EventStatusEnum.Published, VisibilityTypeEnum.Public);
            draftEventId = seed.AddEvent($"MCP Program Draft {marker}", EventStatusEnum.Draft, VisibilityTypeEnum.Public);
            privateEventId = seed.AddEvent($"MCP Program Private {marker}", EventStatusEnum.Published, VisibilityTypeEnum.Private);
            seed.AddSession(publishedEventId, publishedSessionTitle);
            seed.AddSession(draftEventId, draftSessionTitle);
            seed.AddSession(privateEventId, privateSessionTitle);
        });

        using var restPublishedDetail = await client.GetAsync($"/api/event/{publishedEventId}");
        using var restDraftDetail = await client.GetAsync($"/api/event/{draftEventId}");
        using var restProgram = await client.GetAsync($"/api/event/{publishedEventId}/program-summary");
        using var restSessions = await client.GetAsync($"/api/eventsession/by-event/{publishedEventId}");
        var restProgramText = await restProgram.Content.ReadAsStringAsync();
        var restSessionsText = await restSessions.Content.ReadAsStringAsync();
        var mcp = McpProtocolClient.Anonymous(client);

        using var publishedProgramResponse = await mcp.CallToolAsync("get_public_event_program_summary", new JsonObject
        {
            ["eventId"] = publishedEventId.ToString()
        });
        using var publishedSessionsResponse = await mcp.CallToolAsync("list_public_event_sessions", new JsonObject
        {
            ["eventId"] = publishedEventId.ToString()
        });
        using var draftProgramResponse = await mcp.CallToolAsync("get_public_event_program_summary", new JsonObject
        {
            ["eventId"] = draftEventId.ToString()
        });
        using var draftSessionsResponse = await mcp.CallToolAsync("list_public_event_sessions", new JsonObject
        {
            ["eventId"] = draftEventId.ToString()
        });
        using var privateProgramResponse = await mcp.CallToolAsync("get_public_event_program_summary", new JsonObject
        {
            ["eventId"] = privateEventId.ToString()
        });
        using var privateSessionsResponse = await mcp.CallToolAsync("list_public_event_sessions", new JsonObject
        {
            ["eventId"] = privateEventId.ToString()
        });

        await Assert.That(restPublishedDetail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restDraftDetail.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(restProgram.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restProgramText).Contains(publishedSessionTitle);
        await Assert.That(restSessions.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restSessionsText).Contains(publishedSessionTitle);

        using var publishedProgram = JsonDocument.Parse(await GetFirstTextContent(GetResult(publishedProgramResponse)));
        await Assert.That(publishedProgram.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(publishedProgram.RootElement.GetProperty("Program").GetRawText()).Contains(publishedSessionTitle);

        using var publishedSessions = JsonDocument.Parse(await GetFirstTextContent(GetResult(publishedSessionsResponse)));
        await Assert.That(publishedSessions.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(publishedSessions.RootElement.GetProperty("Sessions").GetRawText()).Contains(publishedSessionTitle);

        using var draftProgram = JsonDocument.Parse(await GetFirstTextContent(GetResult(draftProgramResponse)));
        await Assert.That(draftProgram.RootElement.GetProperty("Found").GetBoolean()).IsFalse();
        await Assert.That(draftProgram.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("not_found");
        await Assert.That(draftProgram.RootElement.GetRawText()).DoesNotContain(draftSessionTitle);

        using var draftSessions = JsonDocument.Parse(await GetFirstTextContent(GetResult(draftSessionsResponse)));
        await Assert.That(draftSessions.RootElement.GetProperty("Found").GetBoolean()).IsFalse();
        await Assert.That(draftSessions.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("not_found");
        await Assert.That(draftSessions.RootElement.GetRawText()).DoesNotContain(draftSessionTitle);

        using var privateProgram = JsonDocument.Parse(await GetFirstTextContent(GetResult(privateProgramResponse)));
        await Assert.That(privateProgram.RootElement.GetProperty("Found").GetBoolean()).IsFalse();
        await Assert.That(privateProgram.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("not_found");
        await Assert.That(privateProgram.RootElement.GetRawText()).DoesNotContain(privateSessionTitle);

        using var privateSessions = JsonDocument.Parse(await GetFirstTextContent(GetResult(privateSessionsResponse)));
        await Assert.That(privateSessions.RootElement.GetProperty("Found").GetBoolean()).IsFalse();
        await Assert.That(privateSessions.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("not_found");
        await Assert.That(privateSessions.RootElement.GetRawText()).DoesNotContain(privateSessionTitle);
    }

    private static AuthenticatedWebApplicationFactory CreateMcpEnabledFactory()
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        factory.AdditionalConfiguration["Mcp:Enabled"] = "true";
        factory.AdditionalConfiguration["Mcp:EndpointPath"] = "/mcp";
        factory.AdditionalConfiguration["Mcp:Stateless"] = "true";
        factory.AdditionalConfiguration["Mcp:EnableLegacySse"] = "false";
        return factory;
    }

    private static async Task SeedEventsAsync(
        AuthenticatedWebApplicationFactory factory,
        Action<EventSeedBuilder> configure)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var seed = await EventSeedBuilder.CreateAsync(context);
        configure(seed);
        await context.SaveChangesAsync();
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

    private sealed class EventSeedBuilder
    {
        private readonly ExploreDbContext _context;
        private readonly Guid _tenantId;
        private readonly Guid _actorId;

        private EventSeedBuilder(ExploreDbContext context, Guid tenantId, Guid actorId)
        {
            _context = context;
            _tenantId = tenantId;
            _actorId = actorId;
        }

        public static async Task<EventSeedBuilder> CreateAsync(ExploreDbContext context)
        {
            var tenantId = PlatformDefaults.DefaultTenantId;
            var tenant = await context.Tenants.FindAsync(tenantId);
            if (tenant is null)
            {
                tenant = new TenantBuilder()
                    .WithId(tenantId)
                    .WithFullName("Default MCP Event Tenant")
                    .WithSlug("default-mcp-event")
                    .Build();
                context.Tenants.Add(tenant);
                await context.SaveChangesAsync();
            }

            var user = new UserBuilder().Build();
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var actor = new ActorBuilder()
                .WithUserId(user.Id)
                .WithDisplayName("Default MCP Event Actor")
                .Build();
            context.Actors.Add(actor);
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = tenant,
                UserId = user.Id,
                User = user,
                ActorId = actor.Id,
                Actor = actor,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            return new EventSeedBuilder(context, tenantId, actor.Id);
        }

        public Guid AddEvent(
            string title,
            EventStatusEnum status,
            VisibilityTypeEnum visibility,
            bool addPublishedSession = false)
        {
            var @event = new EventBuilder()
                .WithTitle(title)
                .WithDescription($"Description for {title}")
                .WithActorId(_actorId)
                .WithTenantId(_tenantId)
                .WithStatus(status)
                .WithVisibility(visibility)
                .WithSessionDates(
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)))
                .Build();

            _context.Events.Add(@event);

            if (addPublishedSession)
            {
                AddSession(@event.Id, $"{title} Session");
            }

            return @event.Id;
        }

        public Guid AddSession(Guid eventId, string title)
        {
            var start = DateTimeOffset.UtcNow.AddDays(7);
            var session = new EventSession(EventSessionStatusEnum.Published)
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                Event = null!,
                TenantId = _tenantId,
                Tenant = null!,
                Title = title,
                SortOrder = 1,
                EventSessionKindId = (int)EventSessionKindEnum.Talk,
                RegistrationModeId = 1,
                MaxAudienceAttendees = 120,
                ConcurrencyStamp = Guid.NewGuid()
            };
            session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());

            var eventEntity = _context.Events.Local.FirstOrDefault(e => e.Id == eventId);
            if (eventEntity is not null)
            {
                session.Event = eventEntity;
                eventEntity.Sessions.Add(session);
                eventEntity.RecalculateScheduleSummaryFromSessions();
            }

            _context.EventSessions.Add(session);
            return session.Id;
        }
    }

    private sealed class McpProtocolClient(HttpClient client, Guid? userId)
    {
        private int _nextId;

        public static McpProtocolClient Anonymous(HttpClient client)
            => new(client, userId: null);

        public Task<JsonDocument> InvokeAsync(string method, JsonObject? parameters = null)
            => SendJsonRpcAsync(method, parameters);

        public Task<JsonDocument> CallToolAsync(string toolName, JsonObject arguments)
            => SendJsonRpcAsync("tools/call", new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments
            });

        private async Task<JsonDocument> SendJsonRpcAsync(string method, JsonObject? parameters)
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
            await Assert.That(document.RootElement.TryGetProperty("error", out _)).IsFalse().Because(document.RootElement.GetRawText());
            await Assert.That(document.RootElement.TryGetProperty("result", out _)).IsTrue().Because(document.RootElement.GetRawText());
            return document;
        }

        private async Task<HttpResponseMessage> SendRawAsync(string json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            request.Headers.Add("ProtocolVersion", "2025-06-18");
            request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (userId.HasValue)
            {
                request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId.Value));
            }

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
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
