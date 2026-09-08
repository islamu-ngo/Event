using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;

using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<RealRuntimeApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RealRuntimeDb")]
public class EventSessionGroupRealRuntimeTests(RealRuntimeApiFixture fixture)
{
    private const string BaseUrl = "/api/eventsessiongroup";

    private readonly RealRuntimeApiFixture _fixture = fixture;

    [Test]
    public async Task Create_WithAuthenticatedRequest_ReturnsCreatedAndPersistsProgramSection()
    {
        await _fixture.ResetDatabaseAsync();

        var scenario = await SeedEventAsync("Session Group Create Event");
        var createDto = new CreateEventSessionGroupRequestDto
        {
            EventId = scenario.EventId,
            Name = "Main stage",
            Slug = "main-stage",
            Description = "Primary program section",
            Color = "#27563a",
            SortOrder = 10,
            IsPublished = true
        };
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, BaseUrl, scenario.UserId);
        request.Content = JsonContent.Create(createDto);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        await Assert.That(body.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(response.Headers.Location?.ToString()).Contains(body.Id.ToString());

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await context.EventSessionGroups
            .IgnoreQueryFilters()
            .SingleAsync(group => group.Id == body.Id);

        await Assert.That(persisted.EventId).IsEqualTo(scenario.EventId);
        await Assert.That(persisted.TenantId).IsEqualTo(scenario.TenantId);
        await Assert.That(persisted.Name).IsEqualTo(createDto.Name);
        await Assert.That(persisted.Slug).IsEqualTo(createDto.Slug);
        await Assert.That(persisted.SortOrder).IsEqualTo(createDto.SortOrder);
        await Assert.That(persisted.IsPublished).IsTrue();
    }

    [Test]
    public async Task Update_WithAuthenticatedRequest_ReturnsOkAndPersistsProgramSectionChanges()
    {
        await _fixture.ResetDatabaseAsync();

        var scenario = await SeedEventAsync("Session Group Update Event");
        var sectionId = await SeedProgramSectionAsync(scenario.EventId, scenario.TenantId, "Workshop rooms");
        Guid concurrencyStamp;
        await using (var concurrencyScope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var concurrencyContext = concurrencyScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            concurrencyStamp = await concurrencyContext.EventSessionGroups
                .IgnoreQueryFilters()
                .Where(group => group.Id == sectionId)
                .Select(group => group.ConcurrencyStamp)
                .SingleAsync();
        }
        var updateDto = new UpdateEventSessionGroupRequestDto
        {
            Metadata = new UpdateEventSessionGroupMetadataDto
            {
                Name = "Workshop track",
                Slug = new Explore.Application.Models.Common.OptionalUpdate<string?>(true, "workshop-track"),
                Description = new Explore.Application.Models.Common.OptionalUpdate<string?>(true, "Hands-on sessions"),
                Color = new Explore.Application.Models.Common.OptionalUpdate<string?>(true, "#915f2d")
            },
            Ordering = new UpdateEventSessionGroupOrderingDto { SortOrder = 25 },
            Publication = new UpdateEventSessionGroupPublicationDto { IsPublished = true }
        };
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Patch, $"{BaseUrl}/{sectionId}", scenario.UserId);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{concurrencyStamp}\"");
        request.Content = JsonContent.Create(updateDto);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        await Assert.That(body.Id).IsEqualTo(sectionId);

        await using var persistenceScope = _fixture.Factory.Services.CreateAsyncScope();
        var persistenceContext = persistenceScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await persistenceContext.EventSessionGroups
            .IgnoreQueryFilters()
            .SingleAsync(group => group.Id == sectionId);

        await Assert.That(persisted.Name).IsEqualTo(updateDto.Metadata.Name);
        await Assert.That(persisted.Slug).IsEqualTo(updateDto.Metadata.Slug.Value);
        await Assert.That(persisted.Description).IsEqualTo(updateDto.Metadata.Description.Value);
        await Assert.That(persisted.Color).IsEqualTo(updateDto.Metadata.Color.Value);
        await Assert.That(persisted.SortOrder).IsEqualTo(updateDto.Ordering.SortOrder.GetValueOrDefault());
        await Assert.That(persisted.IsPublished).IsTrue();
    }

    [Test]
    public async Task Create_WhenSlugAlreadyExists_ReturnsValidationProblemDetails()
    {
        await _fixture.ResetDatabaseAsync();

        var scenario = await SeedEventAsync("Session Group Duplicate Slug Event");
        await SeedProgramSectionAsync(scenario.EventId, scenario.TenantId, "Main stage");
        var createDto = new CreateEventSessionGroupRequestDto
        {
            EventId = scenario.EventId,
            Name = "Duplicate main stage",
            Slug = "MAIN-STAGE",
            SortOrder = 20,
            IsPublished = true
        };
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, BaseUrl, scenario.UserId);
        request.Content = JsonContent.Create(createDto);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var isValidContentType = contentType is "application/problem+json" or "application/json";
        await Assert.That(isValidContentType).IsTrue();

        await using var bodyStream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(bodyStream);
        var root = json.RootElement;
        await Assert.That(root.GetProperty("title").GetString()).IsEqualTo("Program validation failed");
        await Assert.That(root.TryGetProperty("traceId", out var traceId)).IsTrue();
        await Assert.That(traceId.GetString()).IsNotNull();
        await Assert.That(root.TryGetProperty("timestamp", out var timestamp)).IsTrue();
        await Assert.That(timestamp.GetString()).IsNotNull();
        await Assert.That(root.TryGetProperty("correlationId", out var correlationId)).IsTrue();
        await Assert.That(correlationId.GetString()).IsNotNull();
        await Assert.That(root.GetProperty("errors").GetProperty("program")[0].GetString())
            .IsEqualTo("Slug must be unique within the event.");
    }

    [Test]
    public async Task Delete_WithAuthenticatedRequest_ReturnsNoContentAndSoftDeletesProgramSection()
    {
        await _fixture.ResetDatabaseAsync();

        var scenario = await SeedEventAsync("Session Group Delete Event");
        var sectionId = await SeedProgramSectionAsync(scenario.EventId, scenario.TenantId, "Temporary section");
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"{BaseUrl}/{sectionId}?eventId={scenario.EventId}",
            scenario.UserId);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await context.EventSessionGroups
            .IgnoreQueryFilters()
            .SingleAsync(group => group.Id == sectionId);

        await Assert.That(persisted.IsDeleted).IsTrue();
        await Assert.That(persisted.DeletedAt).IsNotNull();
    }

    [Test]
    public async Task AssignSession_WithAuthenticatedRequest_ReturnsOkAndPersistsProgramSectionAssignment()
    {
        await _fixture.ResetDatabaseAsync();

        var scenario = await SeedEventAsync("Session Group Assign Event");
        var sectionId = await SeedProgramSectionAsync(scenario.EventId, scenario.TenantId, "Assignment section");
        var sessionId = await SeedSessionAsync(scenario.EventId, scenario.TenantId, "Assignment talk");
        var assignDto = new AssignSessionToGroupRequestDto
        {
            EventId = scenario.EventId,
            EventSessionGroupId = sectionId,
            EventSessionId = sessionId,
            IsPrimary = true,
            SortOrder = 7
        };
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"{BaseUrl}/{sectionId}/sessions",
            scenario.UserId);
        request.Content = JsonContent.Create(assignDto);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        await Assert.That(body.Id).IsNotEqualTo(Guid.Empty);

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await context.EventSessionGroupSessions
            .IgnoreQueryFilters()
            .SingleAsync(assignment => assignment.EventSessionGroupId == sectionId
                && assignment.EventSessionId == sessionId);

        await Assert.That(persisted.EventId).IsEqualTo(scenario.EventId);
        await Assert.That(persisted.TenantId).IsEqualTo(scenario.TenantId);
        await Assert.That(persisted.IsPrimary).IsTrue();
        await Assert.That(persisted.SortOrder).IsEqualTo(assignDto.SortOrder);
        await Assert.That(persisted.IsDeleted).IsFalse();
    }

    [Test]
    public async Task UnassignSession_WithAuthenticatedRequest_ReturnsNoContentAndSoftDeletesProgramSectionAssignment()
    {
        await _fixture.ResetDatabaseAsync();

        var scenario = await SeedEventAsync("Session Group Unassign Event");
        var sectionId = await SeedProgramSectionAsync(scenario.EventId, scenario.TenantId, "Unassign section");
        var sessionId = await SeedSessionAsync(scenario.EventId, scenario.TenantId, "Unassign talk");
        await SeedAssignmentAsync(scenario.EventId, scenario.TenantId, sectionId, sessionId, isPrimary: true, sortOrder: 3);
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"{BaseUrl}/{sectionId}/sessions/{sessionId}?eventId={scenario.EventId}",
            scenario.UserId);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await context.EventSessionGroupSessions
            .IgnoreQueryFilters()
            .SingleAsync(assignment => assignment.EventSessionGroupId == sectionId
                && assignment.EventSessionId == sessionId);

        await Assert.That(persisted.IsDeleted).IsTrue();
        await Assert.That(persisted.DeletedAt).IsNotNull();
    }

    private async Task<SessionGroupScenario> SeedEventAsync(string eventTitle)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var @event = await EventScenarioSeed.SeedPublishedEventAsync(
            context,
            tenant.ActorId,
            tenant.TenantId,
            eventTitle);

        return new SessionGroupScenario(tenant.TenantId, tenant.UserId, @event.EventId);
    }

    private async Task<Guid> SeedProgramSectionAsync(Guid eventId, Guid tenantId, string name)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var group = new EventSessionGroup
        {
            EventId = eventId,
            Event = null!,
            Name = name,
            Slug = name.ToLowerInvariant().Replace(' ', '-'),
            SortOrder = 5,
            IsPublished = true,
            TenantId = tenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.NewGuid()
        };

        context.EventSessionGroups.Add(group);
        await context.SaveChangesAsync();
        return group.Id;
    }

    private async Task<Guid> SeedSessionAsync(Guid eventId, Guid tenantId, string title)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var session = new EventSession
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            TenantId = tenantId,
            Tenant = null!,
            Title = title,
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddHours(1),
            ConcurrencyStamp = Guid.NewGuid()
        };

        context.EventSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private async Task SeedAssignmentAsync(
        Guid eventId,
        Guid tenantId,
        Guid sectionId,
        Guid sessionId,
        bool isPrimary,
        int sortOrder)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var assignment = new EventSessionGroupSession
        {
            EventId = eventId,
            Event = null!,
            EventSessionGroupId = sectionId,
            EventSessionGroup = null!,
            EventSessionId = sessionId,
            EventSession = null!,
            IsPrimary = isPrimary,
            SortOrder = sortOrder,
            TenantId = tenantId,
            Tenant = null!
        };

        context.EventSessionGroupSessions.Add(assignment);
        await context.SaveChangesAsync();
    }

    private sealed record SessionGroupScenario(Guid TenantId, Guid UserId, Guid EventId);
}
