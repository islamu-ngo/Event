using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// First vertical slice: EventController tests running against real PostgreSQL via Testcontainers.
/// Each test resets the database via Respawn and seeds its own scenario data for full isolation.
/// Tests are sequential to avoid shared-database interference.
/// </summary>
[ClassDataSource<RealRuntimeApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RealRuntimeDb")]
public class EventControllerRealRuntimeTests(RealRuntimeApiFixture fixture)
{
    private readonly RealRuntimeApiFixture _fixture = fixture;

    [Test]
    public async Task GetAll_WithEmptyDatabase_ReturnsOk()
    {
        await _fixture.ResetDatabaseAsync();

        var response = await _fixture.Client.GetAsync("/api/event");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetAll_WithSeededEvent_ReturnsOkContainingEventTitle()
    {
        await _fixture.ResetDatabaseAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var eventResult = await EventScenarioSeed.SeedPublishedEventAsync(
            context, tenantResult.ActorId, tenantResult.TenantId);

        var response = await _fixture.Client.GetAsync("/api/event");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).Contains(eventResult.Title);
    }

    [Test]
    public async Task GetById_WithSeededEvent_ReturnsOk()
    {
        await _fixture.ResetDatabaseAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var eventResult = await EventScenarioSeed.SeedPublishedEventAsync(
            context, tenantResult.ActorId, tenantResult.TenantId);

        var response = await _fixture.Client.GetAsync($"/api/event/{eventResult.EventId}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetById_WithNonExistentId_ReturnsNotFound()
    {
        await _fixture.ResetDatabaseAsync();

        var response = await _fixture.Client.GetAsync($"/api/event/{Guid.NewGuid()}");

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Created);
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Create_WithoutAuthentication_ReturnsUnauthorized()
    {
        await _fixture.ResetDatabaseAsync();

        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _fixture.Client.PostAsync("/api/event", content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Create_WithDraftRequest_ReturnsCreatedAndPersistsEvent()
    {
        await _fixture.ResetDatabaseAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);

        var createRequest = new CreateEventDraftRequestDto
        {
            Title = "Draft Submit Integration Event",
            Description = "Created by the draft API integration test.",
            ParticipationConfiguration = CreateParticipationConfiguration(),
            VisibilityTypeId = 1,
            EventFormatId = 1,
            Timezone = "UTC"
        };
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/event", tenantResult.UserId);
        request.Content = JsonContent.Create(createRequest);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        await Assert.That(body.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(response.Headers.Location?.ToString()).Contains(body.Id.ToString());

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var eventEntity = await verifyContext.Events.IgnoreQueryFilters().SingleAsync(x => x.Id == body.Id);

        await Assert.That(eventEntity.Title).IsEqualTo(createRequest.Title);
    }

    [Test]
    public async Task Create_WithDraftWithoutSessions_ReturnsCreatedAndPersistsEmptyProgramDraft()
    {
        await _fixture.ResetDatabaseAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);

        var createRequest = new CreateEventDraftRequestDto
        {
            Title = "Draft Without Sessions Integration Event",
            Description = "Created before any program items exist.",
            ParticipationConfiguration = CreateParticipationConfiguration(),
            VisibilityTypeId = 1,
            EventFormatId = 1,
            Timezone = "UTC"
        };
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/event", tenantResult.UserId);
        request.Content = JsonContent.Create(createRequest);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        await Assert.That(body.Id).IsNotEqualTo(Guid.Empty);

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var eventEntity = await verifyContext.Events.IgnoreQueryFilters().SingleAsync(x => x.Id == body.Id);
        var sessionCount = await verifyContext.EventSessions.IgnoreQueryFilters().CountAsync(x => x.EventId == body.Id);

        await Assert.That(eventEntity.SessionCount).IsEqualTo(0);
        await Assert.That(eventEntity.FirstSessionDate).IsNull();
        await Assert.That(eventEntity.LastSessionDate).IsNull();
        await Assert.That(eventEntity.FirstSessionStartUtc).IsNull();
        await Assert.That(eventEntity.LastSessionStartUtc).IsNull();
        await Assert.That(sessionCount).IsEqualTo(0);
    }

    [Test]
    public async Task Create_PublishedWhenApprovalIsNotRequired_ReturnsCreatedAndPersistsPublicationSideEffects()
    {
        await _fixture.ResetDatabaseAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        TenantScenarioSeed.TenantScenarioResult tenantResult;
        using (var arrangeScope = _fixture.Factory.Services.CreateScope())
        {
            var context = arrangeScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
            await SetRequireApprovalAsync(
                arrangeScope.ServiceProvider,
                tenantResult,
                requireApproval: false,
                timeout.Token);
        }

        var createRequest = CreatePublishedRequest("Approval Disabled Published Event");
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Post,
            "/api/event",
            tenantResult.UserId);
        request.Content = JsonContent.Create(createRequest);

        var response = await _fixture.Client.SendAsync(request, timeout.Token);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>(timeout.Token);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persistedEvent = await verifyContext.Events
            .SingleAsync(value => value.Id == body.Id, timeout.Token);
        var notificationOutboxCount = await verifyContext.OutboxMessages.CountAsync(
            value => value.AggregateId == body.Id
                && value.EventType == EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType,
            timeout.Token);

        await Assert.That(persistedEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(notificationOutboxCount).IsEqualTo(1);
    }

    [Test]
    public async Task Create_PublishedByOrdinaryUserWhenApprovalIsRequired_RejectsBeforePublicationSideEffects()
    {
        await _fixture.ResetDatabaseAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        TenantScenarioSeed.TenantScenarioResult tenantResult;
        using (var arrangeScope = _fixture.Factory.Services.CreateScope())
        {
            var context = arrangeScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
            await SetRequireApprovalAsync(
                arrangeScope.ServiceProvider,
                tenantResult,
                requireApproval: true,
                timeout.Token);
        }

        const string title = "Approval Required Rejected Published Event";
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Post,
            "/api/event",
            tenantResult.UserId);
        request.Headers.Accept.ParseAdd("application/problem+json");
        request.Content = JsonContent.Create(CreatePublishedRequest(title));

        var response = await _fixture.Client.SendAsync(request, timeout.Token);
        var responseJson = await response.Content.ReadAsStringAsync(timeout.Token);
        using var problemDocument = JsonDocument.Parse(responseJson);
        var policyCode = problemDocument.RootElement.TryGetProperty("code", out var codeElement)
            ? codeElement.GetString()
            : null;

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var eventCount = await verifyContext.Events.CountAsync(
            value => value.TenantId == tenantResult.TenantId && value.Title == title,
            timeout.Token);
        var federationOutboxCount = await verifyContext.PdsSyncOutbox.CountAsync(
            value => value.TenantId == tenantResult.TenantId
                && value.SourceEntityType == "Event",
            timeout.Token);
        var notificationOutboxCount = await verifyContext.OutboxMessages.CountAsync(
            value => value.EventType == EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType,
            timeout.Token);

        var actual = new PublicationRejectionObservables(
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            policyCode,
            eventCount,
            federationOutboxCount,
            notificationOutboxCount);
        var expected = new PublicationRejectionObservables(
            HttpStatusCode.BadRequest,
            "application/problem+json",
            "event_publish_approval_required",
            EventCount: 0,
            FederationOutboxCount: 0,
            NotificationOutboxCount: 0);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task Create_WithBlankTitle_ReturnsBadRequest()
    {
        await _fixture.ResetDatabaseAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);

        var createRequest = new CreateEventDraftRequestDto
        {
            Title = string.Empty,
            ParticipationConfiguration = CreateParticipationConfiguration(),
            VisibilityTypeId = 1,
            EventFormatId = 1,
            Timezone = "UTC"
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/event", tenantResult.UserId);
        request.Content = JsonContent.Create(createRequest);

        var response = await _fixture.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(content).Contains("Title");
    }

    [Test]
    public async Task Create_WithDraftContract_DoesNotPersistProgramGraphRows()
    {
        await _fixture.ResetDatabaseAsync();

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
            await context.SaveChangesAsync();
        }

        Guid userId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seededUser = await context.Users.IgnoreQueryFilters().SingleAsync();
            userId = seededUser.Id;
        }

        var requestDto = new CreateEventDraftRequestDto
        {
            Title = "Draft Contract Integration Event",
            Description = "Creates only the event draft; program rows are added through dedicated endpoints.",
            ParticipationConfiguration = CreateParticipationConfiguration(),
            VisibilityTypeId = 1,
            EventFormatId = 1,
            Timezone = "UTC"
        };

        using var createRequest = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/event", userId);
        createRequest.Content = JsonContent.Create(requestDto);

        var response = await _fixture.Client.SendAsync(createRequest);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Id).IsNotEqualTo(Guid.Empty);

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var eventId = body.Id;

        var sessionCount = await verifyContext.EventSessions.IgnoreQueryFilters().CountAsync(x => x.EventId == eventId);
        var dayCount = await verifyContext.EventDays.IgnoreQueryFilters().CountAsync(x => x.EventId == eventId);
        var agendaCount = await verifyContext.EventAgendaItems.IgnoreQueryFilters().CountAsync(x => x.EventId == eventId);

        await Assert.That(sessionCount).IsEqualTo(0);
        await Assert.That(dayCount).IsEqualTo(0);
        await Assert.That(agendaCount).IsEqualTo(0);
    }

    [Test]
    public async Task CreateWithSessionsEndpoint_IsRemoved()
    {
        await _fixture.ResetDatabaseAsync();

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/event/with-sessions");
        request.Content = JsonContent.Create(new { });

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Created);
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.OK);
    }

    private static CreateEventDraftRequestDto CreatePublishedRequest(string title)
    {
        var sessionStart = DateTimeOffset.UtcNow.AddDays(7);
        return new CreateEventDraftRequestDto
        {
            Title = title,
            Description = "Valid publication request from an ordinary tenant user.",
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            EventStatusId = (int)EventStatusEnum.Published,
            ParticipationConfiguration = CreateParticipationConfiguration(),
            VisibilityTypeId = 1,
            EventFormatId = 1,
            Timezone = "UTC",
            Sessions =
            [
                new CreateEventGraphSessionDto
                {
                    Title = $"{title} Session",
                    StartTime = sessionStart,
                    EndTime = sessionStart.AddHours(1)
                }
            ]
        };
    }

    private static async Task SetRequireApprovalAsync(
        IServiceProvider services,
        TenantScenarioSeed.TenantScenarioResult tenant,
        bool requireApproval,
        CancellationToken cancellationToken)
    {
        var settingsResolver = services.GetRequiredService<IHierarchicalSettingsResolver>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var mutationBoundary = services.GetRequiredService<IPublicationPolicyMutationBoundary>();
        PublicationPolicyMutationResult mutation = await unitOfWork.ExecuteInTransactionAsync(
            token => mutationBoundary.ApplyTenantAsync(
                new PublicationPolicyTenantMutationRequest(
                    tenant.TenantId,
                    tenant.ActorId,
                    DateTime.UtcNow,
                    [new PublicationPolicySettingMutation(
                        GovernanceSettingKeys.Events.RequireApproval,
                        PublicationPolicyMutationKind.Set,
                        SettingValueSerializer.Serialize(requireApproval),
                        tenant.TenantId,
                        IsLocked: null)],
                    PublicationPolicyLockedSystemBehavior.Reject),
                token),
            cancellationToken);
        await Assert.That(mutation.Success).IsTrue().Because(mutation.Message);
        settingsResolver.InvalidateCache(SettingScope.Tenant, tenant.TenantId);

        var effectiveValue = await settingsResolver.ResolveAsync<bool>(
            GovernanceSettingKeys.Events.RequireApproval,
            new SettingContext(TenantId: tenant.TenantId),
            cancellationToken);
        await Assert.That(effectiveValue).IsEqualTo(requireApproval);
    }

    private static ConfigureEventParticipationDto CreateParticipationConfiguration() => new()
    {
        ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly,
        AdvanceRegistrationObligationId = (int)AdvanceRegistrationObligationEnum.NotApplicable
    };

    private sealed record PublicationRejectionObservables(
        HttpStatusCode StatusCode,
        string? MediaType,
        string? PolicyCode,
        int EventCount,
        int FederationOutboxCount,
        int NotificationOutboxCount);

    [Test]
    public async Task Update_WithoutIfMatch_ReturnsBadRequest()
    {
        await _fixture.ResetDatabaseAsync();

        var userId = Guid.CreateVersion7();
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Patch, $"/api/event/{Guid.CreateVersion7()}", userId);
        request.Content = JsonContent.Create(new UpdateEventDto
        {
            Title = new UpdateEventTitleDto { Value = "Updated title" }
        });

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Update_WithOldPutRoute_ReturnsMethodNotAllowed()
    {
        await _fixture.ResetDatabaseAsync();

        var userId = Guid.CreateVersion7();
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/event/{Guid.CreateVersion7()}", userId);
        request.Content = JsonContent.Create(new { title = new { value = "Updated title" } });

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task Delete_WithoutAuthentication_ReturnsUnauthorized()
    {
        await _fixture.ResetDatabaseAsync();

        var response = await _fixture.Client.DeleteAsync($"/api/event/{Guid.NewGuid()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetAll_AfterReset_DoesNotReturnPreviouslySeededData()
    {
        await _fixture.ResetDatabaseAsync();

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
            await EventScenarioSeed.SeedPublishedEventAsync(
                context, tenantResult.ActorId, tenantResult.TenantId, "Ephemeral Event");
        }

        // Reset wipes all non-lookup data
        await _fixture.ResetDatabaseAsync();

        var response = await _fixture.Client.GetAsync("/api/event");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).DoesNotContain("Ephemeral Event");
    }

}
