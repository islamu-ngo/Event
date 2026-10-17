using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Hateoas;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
public sealed class EventManagementMcpAuthenticatedReadTests(PostgreSqlContainerFixture postgres)
{
    private static readonly string[] ManagementActionRelations =
    [
        LinkRelations.Edit,
        LinkRelations.Delete,
        LinkRelations.Publish,
        LinkRelations.PublishReadiness,
        LinkRelations.AddSession,
        LinkRelations.SessionCreateContext,
        LinkRelations.ModerateLight,
        LinkRelations.ModerateHeavy,
        LinkRelations.Unmoderate
    ];

    [Test]
    public async Task ListMyEvents_MatchesRestOwnershipAndCapsPageSize()
    {
        await using var factory = await CreateMcpEnabledFactory();
        using var client = factory.Client;
        var userId = factory.UserId;
        var marker = Guid.NewGuid().ToString("N");
        var ownedTitle = $"MCP Owned Draft {marker}";
        var otherTitle = $"MCP Other Draft {marker}";

        await SeedOwnedEventsAsync(factory, userId, ownedTitle, otherTitle);

        using var restRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/event/my?pageNumber=1&pageSize=50");
        using var restResponse = await client.SendAsync(restRequest);
        var restText = await restResponse.Content.ReadAsStringAsync();
        var mcp = new McpProtocolClient(client);

        using var mcpResponse = await mcp.CallToolAsync("list_my_events", new JsonObject
        {
            ["pageNumber"] = 1,
            ["pageSize"] = 999
        });
        var mcpText = await GetFirstTextContent(GetResult(mcpResponse));
        using var descriptor = JsonDocument.Parse(mcpText);

        await Assert.That(restResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restText).Contains(ownedTitle);
        await Assert.That(restText).DoesNotContain(otherTitle);

        await Assert.That(mcpText).Contains(ownedTitle);
        await Assert.That(mcpText).DoesNotContain(otherTitle);
        await Assert.That(descriptor.RootElement.GetProperty("PageSize").GetInt32()).IsEqualTo(25);
        await Assert.That(descriptor.RootElement.GetProperty("PageSizeWasClamped").GetBoolean()).IsTrue();
        await Assert.That(descriptor.RootElement.GetProperty("TotalCount").GetInt32()).IsEqualTo(1);
    }

    [Test]
    public async Task GetEventCreationContext_MatchesRestPolicyAndOmitsInternalRoleData()
    {
        await using var factory = await CreateMcpEnabledFactory();
        using var client = factory.Client;
        var seed = await SeedOrganizationPublisherAsync(factory);

        using var restRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/event/creation-context");
        using var restResponse = await client.SendAsync(restRequest);
        var restText = await restResponse.Content.ReadAsStringAsync();
        var mcp = new McpProtocolClient(client);

        using var mcpResponse = await mcp.CallToolAsync("get_event_creation_context", new JsonObject());
        var mcpText = await GetFirstTextContent(GetResult(mcpResponse));
        using var descriptor = JsonDocument.Parse(mcpText);

        await Assert.That(restResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restText).Contains("AI Test Publisher Organization");
        await Assert.That(restText).Contains(seed.OrganizationId.ToString());

        await Assert.That(descriptor.RootElement.GetProperty("CanCreate").GetBoolean()).IsTrue();
        await Assert.That(descriptor.RootElement.GetProperty("AllowPersonalPublishing").GetBoolean()).IsTrue();
        await Assert.That(descriptor.RootElement.GetProperty("AllowOrganizationPublishing").GetBoolean()).IsTrue();
        await Assert.That(descriptor.RootElement.GetProperty("DefaultPublisherMode").GetString()).IsEqualTo("personal");
        await Assert.That(descriptor.RootElement.GetProperty("PublisherOptionCount").GetInt32()).IsGreaterThanOrEqualTo(2);
        await Assert.That(descriptor.RootElement.GetProperty("PublisherOptionsWereTruncated").GetBoolean()).IsFalse();

        var publisherOptions = descriptor.RootElement.GetProperty("PublisherOptions");
        var organizationOption = publisherOptions.EnumerateArray().Single(option =>
            option.GetProperty("PublisherId").ValueKind == JsonValueKind.String &&
            option.GetProperty("PublisherId").GetGuid() == seed.OrganizationId);
        await Assert.That(organizationOption.GetProperty("PublisherMode").GetString()).IsEqualTo("organization");
        await Assert.That(organizationOption.GetProperty("DisplayName").GetString()).IsEqualTo("AI Test Publisher Organization");
        await Assert.That(organizationOption.GetProperty("CanPublish").GetBoolean()).IsTrue();

        await Assert.That(mcpText).DoesNotContain("RoleId");
        await Assert.That(mcpText).DoesNotContain("TenantId");
        await Assert.That(mcpText).DoesNotContain("UserId");
    }

    [Test]
    public async Task EventManagementContext_DerivesActionAvailabilityFromRestHalLinks()
    {
        await using var factory = await CreateMcpEnabledFactory();
        using var client = factory.Client;
        var userId = factory.UserId;
        var title = $"MCP Managed Draft {Guid.NewGuid():N}";
        var seed = await SeedOwnedDraftEventAsync(factory, userId, title);

        using var restRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/event/{seed.EventId}/management-detail");
        using var restResponse = await client.SendAsync(restRequest);
        using var restDocument = await JsonDocument.ParseAsync(await restResponse.Content.ReadAsStreamAsync());
        var restLinks = ReadRestLinks(restDocument.RootElement);
        var mcp = new McpProtocolClient(client);

        using var resourceResponse = await mcp.ReadResourceAsync(
            $"islamu-event://events/{seed.EventId}/management-context");
        var resourceText = await GetFirstResourceText(GetResult(resourceResponse));
        using var descriptor = JsonDocument.Parse(resourceText);

        await Assert.That(restResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restLinks.Keys).Contains(LinkRelations.Edit);

        await Assert.That(descriptor.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(descriptor.RootElement.GetProperty("EventId").GetGuid()).IsEqualTo(seed.EventId);

        var context = descriptor.RootElement.GetProperty("Context");
        await Assert.That(context.GetProperty("EventId").GetGuid()).IsEqualTo(seed.EventId);
        await Assert.That(context.GetProperty("ConcurrencyStamp").GetGuid()).IsEqualTo(seed.ConcurrencyStamp);
        await Assert.That(context.GetProperty("Title").GetString()).IsEqualTo(title);
        await Assert.That(context.GetProperty("PublishReadinessAvailable").GetBoolean()).IsEqualTo(restLinks.ContainsKey(LinkRelations.PublishReadiness));
        await Assert.That(context.GetProperty("PublishReadiness").ValueKind).IsNotEqualTo(JsonValueKind.Null);
        await Assert.That(context.GetProperty("PublishReadiness").GetProperty("EventId").GetGuid()).IsEqualTo(seed.EventId);

        var actions = context.GetProperty("Actions")
            .EnumerateArray()
            .ToDictionary(action => action.GetProperty("Rel").GetString()!, StringComparer.Ordinal);

        foreach (var relation in ManagementActionRelations)
        {
            await Assert.That(actions).ContainsKey(relation);
            var action = actions[relation];
            var isAvailableInRest = restLinks.TryGetValue(relation, out var restLink);
            await Assert.That(action.GetProperty("Available").GetBoolean()).IsEqualTo(isAvailableInRest).Because(relation);

            if (isAvailableInRest)
            {
                await Assert.That(action.GetProperty("Href").GetString()).IsEqualTo(restLink.Href).Because(relation);
                await Assert.That(action.GetProperty("Method").GetString()).IsEqualTo(restLink.Method).Because(relation);
                continue;
            }

            await Assert.That(action.GetProperty("Href").ValueKind).IsEqualTo(JsonValueKind.Null).Because(relation);
            await Assert.That(action.GetProperty("Method").ValueKind).IsEqualTo(JsonValueKind.Null).Because(relation);
        }

        await Assert.That(resourceText).DoesNotContain("TenantId");
        await Assert.That(resourceText).DoesNotContain("UserId");
        await Assert.That(resourceText).DoesNotContain("RoleId");
    }

    [Test]
    public async Task GetEventPublishReadiness_MatchesRestReadinessWhenHalAffordanceIsAvailable()
    {
        await using var factory = await CreateMcpEnabledFactory();
        using var client = factory.Client;
        var userId = factory.UserId;
        var title = $"MCP Readiness Draft {Guid.NewGuid():N}";
        var seed = await SeedOwnedDraftEventAsync(factory, userId, title);

        using var restDetailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/event/{seed.EventId}/management-detail");
        using var restDetailResponse = await client.SendAsync(restDetailRequest);
        using var restDetailDocument = await JsonDocument.ParseAsync(await restDetailResponse.Content.ReadAsStreamAsync());
        var restLinks = ReadRestLinks(restDetailDocument.RootElement);

        using var restReadinessRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/event/{seed.EventId}/publish-readiness");
        using var restReadinessResponse = await client.SendAsync(restReadinessRequest);
        using var restReadiness = await JsonDocument.ParseAsync(await restReadinessResponse.Content.ReadAsStreamAsync());
        var mcp = new McpProtocolClient(client);

        using var mcpResponse = await mcp.CallToolAsync("get_event_publish_readiness", new JsonObject
        {
            ["eventId"] = seed.EventId.ToString()
        });
        var mcpText = await GetFirstTextContent(GetResult(mcpResponse));
        using var descriptor = JsonDocument.Parse(mcpText);

        await Assert.That(restDetailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restLinks).ContainsKey(LinkRelations.PublishReadiness);
        await Assert.That(restReadinessResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var restErrors = restReadiness.RootElement.GetProperty("errors");
        var readiness = descriptor.RootElement.GetProperty("PublishReadiness");
        await Assert.That(descriptor.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(descriptor.RootElement.GetProperty("Available").GetBoolean()).IsTrue();
        await Assert.That(descriptor.RootElement.GetProperty("FailureCode").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(readiness.GetProperty("EventId").GetGuid()).IsEqualTo(seed.EventId);
        await Assert.That(readiness.GetProperty("IsReady").GetBoolean()).IsEqualTo(restReadiness.RootElement.GetProperty("isReady").GetBoolean());
        await Assert.That(readiness.GetProperty("ErrorCount").GetInt32()).IsEqualTo(restErrors.GetArrayLength());
        await Assert.That(readiness.GetProperty("Errors").GetArrayLength()).IsLessThanOrEqualTo(25);

        if (restErrors.GetArrayLength() > 0)
        {
            var restCodes = restErrors
                .EnumerateArray()
                .Select(error => error.GetProperty("code").GetString())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToArray();
            var mcpCodes = readiness.GetProperty("Errors")
                .EnumerateArray()
                .Select(error => error.GetProperty("Code").GetString())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToArray();

            await Assert.That(mcpCodes.All(restCodes.Contains)).IsTrue();
        }

        await Assert.That(mcpText).DoesNotContain("TenantId");
        await Assert.That(mcpText).DoesNotContain("UserId");
        await Assert.That(mcpText).DoesNotContain("RoleId");
    }

    [Test]
    public async Task GetEventPublishReadiness_ReturnsUnavailableWhenHalAffordanceIsAbsent()
    {
        await using var factory = await CreateMcpEnabledFactory();
        using var client = factory.Client;
        var userId = factory.UserId;
        var title = $"MCP Readiness Published {Guid.NewGuid():N}";
        var seed = await SeedOwnedDraftEventAsync(factory, userId, title);
        await PublishEventAsync(factory, seed.EventId);

        using var restDetailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/event/{seed.EventId}/management-detail");
        using var restDetailResponse = await client.SendAsync(restDetailRequest);
        using var restDetailDocument = await JsonDocument.ParseAsync(await restDetailResponse.Content.ReadAsStreamAsync());
        var restLinks = ReadRestLinks(restDetailDocument.RootElement);
        var mcp = new McpProtocolClient(client);

        using var mcpResponse = await mcp.CallToolAsync("get_event_publish_readiness", new JsonObject
        {
            ["eventId"] = seed.EventId.ToString()
        });
        using var descriptor = JsonDocument.Parse(await GetFirstTextContent(GetResult(mcpResponse)));

        await Assert.That(restDetailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restLinks).DoesNotContainKey(LinkRelations.PublishReadiness);
        await Assert.That(descriptor.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(descriptor.RootElement.GetProperty("Available").GetBoolean()).IsFalse();
        await Assert.That(descriptor.RootElement.GetProperty("FailureCode").GetString()).IsEqualTo("not_available");
        await Assert.That(descriptor.RootElement.GetProperty("PublishReadiness").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task AuthenticatedSubResourceReadTools_ReturnBoundedHalGatedContexts()
    {
        await using var factory = await CreateMcpEnabledFactory();
        using var client = factory.Client;
        var userId = factory.UserId;
        var title = $"MCP Subresource Draft {Guid.NewGuid():N}";
        var seed = await SeedOwnedDraftEventAsync(factory, userId, title);
        var sessionId = await SeedManagedSubResourcesAsync(factory, seed.EventId);
        var mcp = new McpProtocolClient(client);

        using var programResponse = await mcp.CallToolAsync("get_event_program_management_context", new JsonObject
        {
            ["eventId"] = seed.EventId.ToString()
        });
        var programText = await GetFirstTextContent(GetResult(programResponse));
        using var program = JsonDocument.Parse(programText);
        var programContext = program.RootElement.GetProperty("Context");

        await Assert.That(program.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(program.RootElement.GetProperty("Available").GetBoolean()).IsTrue();
        await Assert.That(programContext.GetProperty("EventId").GetGuid()).IsEqualTo(seed.EventId);
        await Assert.That(programContext.GetProperty("SessionCount").GetInt32()).IsEqualTo(1);
        await Assert.That(programContext.GetProperty("SessionGroupCount").GetInt32()).IsEqualTo(1);
        await Assert.That(programContext.GetProperty("DayCount").GetInt32()).IsEqualTo(1);
        await Assert.That(programContext.GetProperty("AgendaItemCount").GetInt32()).IsEqualTo(1);
        await Assert.That(programText).Contains("MCP Management Session");
        await Assert.That(programText).Contains("MCP Management Track");
        await Assert.That(programText).DoesNotContain("LocationName");
        await Assert.That(programText).DoesNotContain("RoomName");
        await Assert.That(programText).DoesNotContain("TenantId");
        await Assert.That(programText).DoesNotContain("UserId");

        using var customPropertiesResponse = await mcp.CallToolAsync("get_event_custom_properties_context", new JsonObject
        {
            ["eventId"] = seed.EventId.ToString(),
            ["pageSize"] = 999
        });
        var customPropertiesText = await GetFirstTextContent(GetResult(customPropertiesResponse));
        using var customProperties = JsonDocument.Parse(customPropertiesText);
        var customPropertiesContext = customProperties.RootElement.GetProperty("Context");

        await Assert.That(customProperties.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(customProperties.RootElement.GetProperty("Available").GetBoolean()).IsTrue();
        await Assert.That(customPropertiesContext.GetProperty("PageSize").GetInt32()).IsEqualTo(25);
        await Assert.That(customPropertiesContext.GetProperty("PageSizeWasClamped").GetBoolean()).IsTrue();
        await Assert.That(customPropertiesContext.GetProperty("TotalDefinitionCount").GetInt32()).IsEqualTo(1);
        await Assert.That(customPropertiesContext.GetProperty("ValueCount").GetInt32()).IsEqualTo(1);
        await Assert.That(customPropertiesText).Contains("MCP Track Notes");
        await Assert.That(customPropertiesText).Contains("Bring notebook");
        await Assert.That(customPropertiesText).DoesNotContain("TenantId");
        await Assert.That(customPropertiesText).DoesNotContain("UserId");

        using var registrationsResponse = await mcp.CallToolAsync("get_event_registrations_context", new JsonObject
        {
            ["eventId"] = seed.EventId.ToString(),
            ["pageSize"] = 999
        });
        var registrationsText = await GetFirstTextContent(GetResult(registrationsResponse));
        using var registrations = JsonDocument.Parse(registrationsText);
        var registrationsContext = registrations.RootElement.GetProperty("Context");

        await Assert.That(registrations.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(registrations.RootElement.GetProperty("Available").GetBoolean()).IsTrue();
        await Assert.That(registrationsContext.GetProperty("PageSize").GetInt32()).IsEqualTo(100);
        await Assert.That(registrationsContext.GetProperty("PageSizeWasClamped").GetBoolean()).IsTrue();
        await Assert.That(registrationsContext.GetProperty("TotalRegistrationCount").GetInt32()).IsEqualTo(1);
        var registration = registrationsContext.GetProperty("Registrations").EnumerateArray().Single();
        await Assert.That(registration.GetProperty("EventId").GetGuid()).IsEqualTo(seed.EventId);
        await Assert.That(registration.GetProperty("StatusCode").GetString()).IsEqualTo("DRAFT");
        await Assert.That(registration.GetProperty("CurrencyCode").GetString()).IsEqualTo("USD");
        await Assert.That(registration.GetProperty("TotalDueMinor").GetInt64()).IsEqualTo(0);
        await Assert.That(registrationsText).DoesNotContain("MCP Management Session");
        await Assert.That(registrationsText).DoesNotContain("Approved");
        await Assert.That(registrationsText).DoesNotContain("MCP Management Attendee");
        await Assert.That(registrationsText).DoesNotContain("TenantId");
        await Assert.That(registrationsText).DoesNotContain("UserId");
        await Assert.That(registrationsText).DoesNotContain("UserFullName");
        await Assert.That(registrationsText).DoesNotContain("UserEmail");

        using var teamResponse = await mcp.CallToolAsync("get_event_team_context", new JsonObject
        {
            ["eventId"] = seed.EventId.ToString()
        });
        var teamText = await GetFirstTextContent(GetResult(teamResponse));
        using var team = JsonDocument.Parse(teamText);

        await Assert.That(team.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(team.RootElement.GetProperty("Available").GetBoolean()).IsTrue();
        await Assert.That(team.RootElement.GetProperty("Context").GetProperty("CurrentUserPermissions")
            .GetProperty("EventId").GetGuid()).IsEqualTo(seed.EventId);
        await Assert.That(teamText).DoesNotContain("TenantId");
        await Assert.That(teamText).DoesNotContain("UserId");

        using var templateCatalogResponse = await mcp.CallToolAsync("get_event_template_catalog_context", new JsonObject
        {
            ["eventId"] = seed.EventId.ToString(),
            ["pageSize"] = 999
        });
        var templateCatalogText = await GetFirstTextContent(GetResult(templateCatalogResponse));
        using var templateCatalog = JsonDocument.Parse(templateCatalogText);

        await Assert.That(templateCatalog.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(templateCatalog.RootElement.GetProperty("Available").GetBoolean()).IsTrue();
        await Assert.That(templateCatalog.RootElement.GetProperty("Context").GetProperty("PageSize").GetInt32()).IsEqualTo(25);
        await Assert.That(templateCatalogText).DoesNotContain("TenantId");
        await Assert.That(templateCatalogText).DoesNotContain("UserId");

        using var templateSyncResponse = await mcp.CallToolAsync("get_event_template_sync_context", new JsonObject
        {
            ["eventId"] = seed.EventId.ToString()
        });
        var templateSyncText = await GetFirstTextContent(GetResult(templateSyncResponse));
        using var templateSync = JsonDocument.Parse(templateSyncText);

        await Assert.That(templateSync.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(templateSync.RootElement.GetProperty("Available").GetBoolean()).IsTrue();
        await Assert.That(templateSync.RootElement.GetProperty("Context").GetProperty("DiffAvailable").GetBoolean()).IsFalse();
        await Assert.That(templateSync.RootElement.GetProperty("Context").GetProperty("DiffFailureCode").GetString()).IsEqualTo("not_requested");
        await Assert.That(templateSyncText).DoesNotContain("TenantId");
        await Assert.That(templateSyncText).DoesNotContain("UserId");

        using var sessionTemplateSyncResponse = await mcp.CallToolAsync("get_event_session_template_sync_context", new JsonObject
        {
            ["eventId"] = seed.EventId.ToString(),
            ["sessionId"] = sessionId.ToString()
        });
        var sessionTemplateSyncText = await GetFirstTextContent(GetResult(sessionTemplateSyncResponse));
        using var sessionTemplateSync = JsonDocument.Parse(sessionTemplateSyncText);

        await Assert.That(sessionTemplateSync.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(sessionTemplateSync.RootElement.GetProperty("Available").GetBoolean()).IsTrue();
        await Assert.That(sessionTemplateSync.RootElement.GetProperty("Context").GetProperty("SessionId").GetGuid()).IsEqualTo(sessionId);
        await Assert.That(sessionTemplateSyncText).DoesNotContain("TenantId");
        await Assert.That(sessionTemplateSyncText).DoesNotContain("UserId");
    }

    private Task<EventManagementMcpAuthenticatedFixture> CreateMcpEnabledFactory()
        => EventManagementMcpAuthenticatedFixture.CreateAsync(postgres);

    private static async Task SeedOwnedEventsAsync(
        EventManagementMcpAuthenticatedFixture factory,
        Guid userId,
        string ownedTitle,
        string otherTitle)
    {
        await using var context = factory.CreateMigratorContext();
        var tenant = await context.Tenants.SingleAsync(value => value.Id == factory.TenantId);
        var owner = await context.Users.SingleAsync(value => value.Id == userId);
        var otherUser = new UserBuilder().Build();
        context.Users.Add(otherUser);
        await context.SaveChangesAsync();

        var ownerActor = await context.Actors.SingleAsync(value => value.UserId == owner.Id);
        var otherActor = new ActorBuilder()
            .WithUserId(otherUser.Id)
            .WithDisplayName("MCP Authenticated Other")
            .Build();
        context.Actors.Add(otherActor);
        context.TenantUsers.Add(CreateActiveTenantUser(tenant, otherUser, otherActor));
        await context.SaveChangesAsync();

        context.Events.AddRange(
            new EventBuilder()
                .WithTitle(ownedTitle)
                .WithDescription($"Description for {ownedTitle}")
                .WithActorId(ownerActor.Id)
                .WithTenantId(tenant.Id)
                .WithStatus(EventStatusEnum.Draft)
                .WithVisibility(VisibilityTypeEnum.Private)
                .WithSessionDates(
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)),
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)))
                .Build(),
            new EventBuilder()
                .WithTitle(otherTitle)
                .WithDescription($"Description for {otherTitle}")
                .WithActorId(otherActor.Id)
                .WithTenantId(tenant.Id)
                .WithStatus(EventStatusEnum.Draft)
                .WithVisibility(VisibilityTypeEnum.Private)
                .WithSessionDates(
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)))
                .Build());
        await context.SaveChangesAsync();
    }

    private static async Task<OwnedDraftEventSeed> SeedOwnedDraftEventAsync(
        EventManagementMcpAuthenticatedFixture factory,
        Guid userId,
        string title)
    {
        await using var context = factory.CreateMigratorContext();
        var tenant = await context.Tenants.SingleAsync(value => value.Id == factory.TenantId);
        var ownerActor = await context.Actors.SingleAsync(actor => actor.UserId == userId);

        var @event = new EventBuilder()
            .WithTitle(title)
            .WithDescription($"Description for {title}")
            .WithActorId(ownerActor.Id)
            .WithTenantId(tenant.Id)
            .WithStatus(EventStatusEnum.Draft)
            .WithVisibility(VisibilityTypeEnum.Private)
            .WithSessionDates(
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6)),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6)))
            .Build();
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        @event.CreatedBy = userId;
        await context.SaveChangesAsync();

        return new OwnedDraftEventSeed(@event.Id, @event.ConcurrencyStamp);
    }

    private static TenantUser CreateActiveTenantUser(Tenant tenant, User user, Actor actor) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant.Id,
        Tenant = tenant,
        UserId = user.Id,
        User = user,
        ActorId = actor.Id,
        Actor = actor,
        StatusId = (int)TenantUserStatusEnum.Active,
        JoinedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    private static async Task PublishEventAsync(
        EventManagementMcpAuthenticatedFixture factory,
        Guid eventId)
    {
        await using var context = factory.CreateMigratorContext();
        var @event = await context.Events.FindAsync(eventId);
        await Assert.That(@event).IsNotNull();
        @event!.Publish(DateTime.UtcNow);
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> SeedManagedSubResourcesAsync(
        EventManagementMcpAuthenticatedFixture factory,
        Guid eventId)
    {
        await using var context = factory.CreateMigratorContext();
        var @event = await context.Events.FindAsync(eventId);
        await Assert.That(@event).IsNotNull();
        @event!.Timezone = "UTC";
        @event.OrganizerActorId = @event.ActorId;
        var tenantId = @event.TenantId;
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var calculator = new EventScheduleProjectionCalculator();

        var day = new EventDay
        {
            EventId = eventId,
            Event = null!,
            LocalDate = DateOnly.FromDateTime(start.UtcDateTime),
            Label = "MCP Management Day",
            SortOrder = 1,
            IsPublished = true,
            AllowsDayScopeRegistration = true,
            TenantId = tenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.NewGuid()
        };
        context.EventDays.Add(day);

        var sessionGroup = new EventSessionGroup
        {
            EventId = eventId,
            Event = null!,
            Name = "MCP Management Track",
            Slug = "mcp-management-track",
            Color = "#2255aa",
            SortOrder = 1,
            IsPublished = true,
            TenantId = tenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.NewGuid()
        };
        context.EventSessionGroups.Add(sessionGroup);

        var session = new EventSession
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            TenantId = tenantId,
            Tenant = null!,
            Title = "MCP Management Session",
            SortOrder = 1,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            RegistrationModeId = 1,
            MaxAudienceAttendees = 80,
            ConcurrencyStamp = Guid.NewGuid()
        };
        session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", calculator);
        context.EventSessions.Add(session);

        var agendaItem = new EventAgendaItem
        {
            EventId = eventId,
            Event = null!,
            EventDay = day,
            Title = "MCP Management Break",
            SortOrder = 1,
            TenantId = tenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.NewGuid()
        };
        agendaItem.Reschedule(UtcInstantRange.Create(start.AddHours(1), start.AddHours(2)), "UTC", calculator);
        context.EventAgendaItems.Add(agendaItem);

        var definition = new EventCustomPropertyDefinition
        {
            EventId = eventId,
            Event = null!,
            TenantId = tenantId,
            Tenant = null!,
            Namespace = "mcp.management",
            Key = "track_notes",
            DisplayName = "MCP Track Notes",
            PropertyType = PropertyType.Text,
            IsActive = true,
            ExposureLevel = ExposureLevel.OrganizerOnly,
            SortOrder = 1,
            InstantiatedAt = DateTimeOffset.UtcNow,
            ConcurrencyStamp = Guid.NewGuid()
        };
        context.EventCustomPropertyDefinitions.Add(definition);
        await context.SaveChangesAsync();

        context.EventSessionGroupSessions.Add(new EventSessionGroupSession
        {
            EventId = eventId,
            Event = null!,
            EventSessionGroupId = sessionGroup.Id,
            EventSessionGroup = null!,
            EventSessionId = session.Id,
            EventSession = null!,
            IsPrimary = true,
            SortOrder = 1,
            TenantId = tenantId,
            Tenant = null!
        });
        context.EventCustomPropertyValues.Add(new EventCustomPropertyValue
        {
            EventCustomPropertyDefinitionId = definition.Id,
            Definition = null,
            EventId = eventId,
            Event = null,
            TenantId = tenantId,
            Tenant = null,
            Ordinal = 0,
            TextValue = "Bring notebook",
            ConcurrencyStamp = Guid.NewGuid()
        });
        var attendee = new UserBuilder()
            .WithFirstName("MCP Management")
            .WithLastName("Attendee")
            .Build();
        DateTime registrationTime = DateTime.UtcNow;
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        RegistrationOrder order = RegistrationOrder.Create(
            tenantId,
            eventId,
            attendee.Id,
            purchaserActorId: null,
            BookingPartyTypeEnum.Individual,
            catalog.Id,
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            registrationWorkflowVersionId: null,
            guestAccessTokenHash: null,
            "USD",
            registrationTime,
            expiresAt: null);
        RegistrationParticipant participant = RegistrationParticipant.Create(
            tenantId, order.Id, attendee.Id, ParticipantTypeEnum.Adult, guardian: null);
        context.Users.Add(attendee);
        context.EventTicketCatalogVersions.Add(catalog);
        context.RegistrationOrders.Add(order);
        context.RegistrationParticipants.Add(participant);
        context.EventRegistrations.Add(new EventRegistration
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            EventSessionId = session.Id,
            EventSession = session,
            LinkedUserId = attendee.Id,
            LinkedUser = attendee,
            RegistrationOrderId = order.Id,
            RegistrationOrder = order,
            RegistrationParticipantId = participant.Id,
            RegistrationParticipant = participant,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            TenantId = tenantId,
            Tenant = null!
        });
        await context.SaveChangesAsync();

        return session.Id;
    }

    private static async Task<TenantScenarioSeed.TenantOrganizationScenarioResult> SeedOrganizationPublisherAsync(
        EventManagementMcpAuthenticatedFixture factory)
    {
        await using var context = factory.CreateMigratorContext();
        var tenant = await context.Tenants.SingleAsync(value => value.Id == factory.TenantId);
        var user = await context.Users.SingleAsync(value => value.Id == factory.UserId);
        var actor = await context.Actors.SingleAsync(value => value.UserId == user.Id);
        var permission = await context.Permissions.SingleAsync(value => value.MasterCode == PermissionCodes.EventCreate);
        if (!await context.RolePermissions.AnyAsync(value =>
                value.RoleId == (int)RoleEnum.OrgAdmin && value.PermissionId == permission.Id))
        {
            context.RolePermissions.Add(new RolePermission
            {
                RoleId = (int)RoleEnum.OrgAdmin,
                Role = null!,
                PermissionId = permission.Id,
                Permission = permission,
                GrantedAt = DateTime.UtcNow
            });
        }
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "AI Test Publisher Organization" },
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var placement = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = true,
            IsOrganizerEligible = true,
            ApprovedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var organizationActor = new ActorBuilder()
            .WithActorType(ActorTypeEnum.Organization)
            .WithDisplayName("AI Test Publisher Organization")
            .Build();
        organizationActor.OrganizationId = organization.Id;
        context.AddRange(organization, placement, organizationActor, new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            OrganizationTenantId = placement.Id,
            OrganizationTenant = placement,
            UserId = user.Id,
            User = user,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return new(tenant.Id, user.Id, actor.Id, organization.Id, organizationActor.Id);
    }

    private static JsonElement GetResult(JsonDocument document)
        => document.RootElement.GetProperty("result");

    private static async Task<string> GetFirstTextContent(JsonElement result)
    {
        var content = result.GetProperty("content");
        await Assert.That(content.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(content.GetArrayLength()).IsGreaterThan(0);
        return content[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private static async Task<string> GetFirstResourceText(JsonElement result)
    {
        var contents = result.GetProperty("contents");
        await Assert.That(contents.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(contents.GetArrayLength()).IsGreaterThan(0);
        return contents[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private static Dictionary<string, RestHalLink> ReadRestLinks(JsonElement resource)
    {
        var links = resource.GetProperty("_links");
        return links.EnumerateObject().ToDictionary(
            property => property.Name,
            property => new RestHalLink(
                ReadNullableString(property.Value, "href"),
                ReadNullableString(property.Value, "method")),
            StringComparer.Ordinal);
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record OwnedDraftEventSeed(Guid EventId, Guid ConcurrencyStamp);

    private sealed record RestHalLink(string? Href, string? Method);

    private sealed class McpProtocolClient(HttpClient client)
    {
        private int _nextId;

        public Task<JsonDocument> CallToolAsync(string toolName, JsonObject arguments)
            => SendJsonRpcAsync("tools/call", new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments
            });

        public Task<JsonDocument> ReadResourceAsync(string uri)
            => SendJsonRpcAsync("resources/read", new JsonObject
            {
                ["uri"] = uri
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
