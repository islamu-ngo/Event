using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Models;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventLocations.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Seed;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Features;

[Category("EventLocationPrivacy")]
[Category("EventLocationController")]
public sealed class EventLocationControllerTests
{
    [Test]
    public async Task PurposeSpecificRoutes_DeclareExactAuthorizationAndCacheBoundaries()
    {
        Type? controller = typeof(EventController).Assembly.GetType(
            "Explore.API.Controllers.EventLocationController",
            throwOnError: false);

        await Assert.That(controller).IsNotNull()
            .Because("ELP-405 requires an additive EventLocation controller");
        await Assert.That(controller!.GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo("api/events/{eventId:guid}/locations");

        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .ToDictionary(method => method.Name, StringComparer.Ordinal);

        AssertPublic(actions, "GetPublic", HttpMethods.Get, "");
        AssertPrivate(actions, "GetMyAccess", HttpMethods.Get, "my-access");
        AssertPrivate(actions, "GetManagement", HttpMethods.Get, "{eventLocationId:guid}/management");
        AssertPrivate(actions, "UpdateDisclosure", HttpMethods.Patch, "{eventLocationId:guid}/disclosure");

        AuthorizeResourceAttribute? updateAuthorization =
            typeof(UpdateEventLocationPolicyCommand).GetCustomAttribute<AuthorizeResourceAttribute>();
        await Assert.That(updateAuthorization?.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(updateAuthorization?.Action).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(typeof(UpdateEventLocationPolicyCommand)))
            .IsTrue();
    }

    [Test]
    public async Task DisclosureUpdate_ForwardsRouteIdsTokensAndOptionalGroupsIndependently()
    {
        var eventId = Guid.CreateVersion7();
        var eventLocationId = Guid.CreateVersion7();
        var fieldsStamp = Guid.CreateVersion7();
        var audienceStamp = Guid.CreateVersion7();
        var fields = new UpdateEventLocationDisclosureFieldsDto { ShowVenueName = true };
        var audience = new UpdateEventLocationDisclosureAudienceDto
        {
            FullDetailsAudienceId = (int)LocationDisclosureAudienceEnum.ConfirmedParticipant,
            RevealFullDetailsFromUtc = OptionalUpdate<DateTime?>.Set(DateTime.UtcNow)
        };
        var mediator = new EventLocationMediatorStub();
        var controller = new EventLocationController(mediator, null!);

        await controller.UpdateDisclosure(eventId, eventLocationId, new UpdateEventLocationDisclosureDto
        {
            ExpectedPolicyVersion = 4,
            ExpectedConcurrencyStamp = fieldsStamp,
            Fields = fields
        });
        await controller.UpdateDisclosure(eventId, eventLocationId, new UpdateEventLocationDisclosureDto
        {
            ExpectedPolicyVersion = 5,
            ExpectedConcurrencyStamp = audienceStamp,
            Audience = audience
        });

        UpdateEventLocationPolicyCommand fieldsCommand = mediator.Requests[0];
        await Assert.That(fieldsCommand.EventId).IsEqualTo(eventId);
        await Assert.That(fieldsCommand.EventLocationId).IsEqualTo(eventLocationId);
        await Assert.That(fieldsCommand.ExpectedPolicyVersion).IsEqualTo(4);
        await Assert.That(fieldsCommand.ExpectedConcurrencyStamp).IsEqualTo(fieldsStamp);
        await Assert.That(fieldsCommand.Fields).IsSameReferenceAs(fields);
        await Assert.That(fieldsCommand.Audience).IsNull();

        UpdateEventLocationPolicyCommand audienceCommand = mediator.Requests[1];
        await Assert.That(audienceCommand.EventId).IsEqualTo(eventId);
        await Assert.That(audienceCommand.EventLocationId).IsEqualTo(eventLocationId);
        await Assert.That(audienceCommand.ExpectedPolicyVersion).IsEqualTo(5);
        await Assert.That(audienceCommand.ExpectedConcurrencyStamp).IsEqualTo(audienceStamp);
        await Assert.That(audienceCommand.Fields).IsNull();
        await Assert.That(audienceCommand.Audience).IsSameReferenceAs(audience);
    }

    private static void AssertPublic(
        IReadOnlyDictionary<string, MethodInfo> actions,
        string actionName,
        string method,
        string? template)
    {
        MethodInfo action = GetAction(actions, actionName);
        AssertMethod(action, method, template);
        if (GetEffectiveAttribute<AllowAnonymousAttribute>(action) is null
            || GetEffectiveAttribute<AuthorizeAttribute>(action) is not null
            || GetEffectiveAttribute<OutputCacheAttribute>(action) is not null
            || GetEffectiveAttribute<PrivateNoStoreAttribute>(action) is not null
            || GetEffectiveAttribute<EndpointClassificationAttribute>(action)?.Class != EndpointClass.Public)
        {
            throw new InvalidOperationException(
                $"{actionName} must be anonymous, Public, and free of shared/private cache attributes.");
        }
    }

    private static void AssertPrivate(
        IReadOnlyDictionary<string, MethodInfo> actions,
        string actionName,
        string method,
        string template)
    {
        MethodInfo action = GetAction(actions, actionName);
        AssertMethod(action, method, template);
        if (GetEffectiveAttribute<AuthorizeAttribute>(action) is null
            || GetEffectiveAttribute<AllowAnonymousAttribute>(action) is not null
            || GetEffectiveAttribute<OutputCacheAttribute>(action) is not null
            || GetEffectiveAttribute<PrivateNoStoreAttribute>(action) is null
            || GetEffectiveAttribute<EndpointClassificationAttribute>(action)?.Class != EndpointClass.Authenticated)
        {
            throw new InvalidOperationException(
                $"{actionName} must be authenticated, private/no-store, and free of shared output caching.");
        }
    }

    private static MethodInfo GetAction(IReadOnlyDictionary<string, MethodInfo> actions, string actionName) =>
        actions.TryGetValue(actionName, out MethodInfo? action)
            ? action
            : throw new InvalidOperationException($"Missing EventLocationController.{actionName}.");

    private static void AssertMethod(MethodInfo action, string method, string? template)
    {
        HttpMethodAttribute attribute = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Single();
        if (!attribute.HttpMethods.SequenceEqual([method], StringComparer.OrdinalIgnoreCase)
            || !string.Equals(attribute.Template, template, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{action.Name} expected {method} '{template}', got " +
                $"{string.Join(',', attribute.HttpMethods)} '{attribute.Template}'.");
        }
    }

    private static TAttribute? GetEffectiveAttribute<TAttribute>(MethodInfo action)
        where TAttribute : Attribute =>
        action.GetCustomAttribute<TAttribute>(inherit: true)
        ?? action.DeclaringType?.GetCustomAttribute<TAttribute>(inherit: true);

    private sealed class EventLocationMediatorStub : IMediator
    {
        public List<UpdateEventLocationPolicyCommand> Requests { get; } = [];

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((UpdateEventLocationPolicyCommand)(object)request);
            object response = BaseCommandResponse.Success(
                Guid.CreateVersion7(),
                "Disclosure updated.");
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

[Category("EventLocationPrivacy")]
[Category("EventLocationController")]
[ClassDataSource<EventLocationRouteRuntimeFixture>(Shared = SharedType.PerClass)]
[NotInParallel("RealRuntimeDb")]
public sealed class EventLocationControllerRuntimeTests(EventLocationRouteRuntimeFixture fixture)
{
    [Test]
    public async Task PublicRoute_IsPrincipalInvariantAndNeverExposesPhysicalIdentity()
    {
        EventLocationRouteScenario scenario = await SeedScenarioAsync();
        string route = $"/api/events/{scenario.EventId}/locations";

        using HttpResponseMessage anonymous = await fixture.Client.GetAsync(route);
        byte[] anonymousBody = await anonymous.Content.ReadAsByteArrayAsync();
        using HttpRequestMessage authenticatedRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            route,
            scenario.UserId);
        using HttpResponseMessage authenticated = await fixture.Client.SendAsync(authenticatedRequest);
        byte[] authenticatedBody = await authenticated.Content.ReadAsByteArrayAsync();

        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(authenticated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(authenticatedBody).IsEquivalentTo(anonymousBody);

        using JsonDocument document = JsonDocument.Parse(anonymousBody);
        JsonElement item = document.RootElement.EnumerateArray().Single();
        await Assert.That(item.GetProperty("eventLocationId").GetGuid())
            .IsEqualTo(scenario.EventLocationId);
        await Assert.That(item.TryGetProperty("locationId", out _)).IsFalse();

        string json = document.RootElement.GetRawText();
        await Assert.That(json).DoesNotContain(scenario.LocationId.ToString("D"));
        await Assert.That(json).DoesNotContain(scenario.Address);
        await Assert.That(json).DoesNotContain(scenario.Postcode);

        using HttpResponseMessage legacyRoute = await fixture.Client.GetAsync(
            $"/api/events/{scenario.EventId}/locations/public");
        await Assert.That(legacyRoute.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PrivateRoutes_RequireAuthenticationAndEmitPrivateNoStore()
    {
        EventLocationRouteScenario scenario = await SeedScenarioAsync();
        string attendeeRoute = $"/api/events/{scenario.EventId}/locations/my-access";
        string managementRoute =
            $"/api/events/{scenario.EventId}/locations/{scenario.EventLocationId}/management";

        using HttpResponseMessage anonymousAttendee = await fixture.Client.GetAsync(attendeeRoute);
        using HttpResponseMessage anonymousManagement = await fixture.Client.GetAsync(managementRoute);
        using HttpRequestMessage attendeeRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            attendeeRoute,
            scenario.UserId);
        using HttpResponseMessage attendee = await fixture.Client.SendAsync(attendeeRequest);
        using HttpRequestMessage managementRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            managementRoute,
            scenario.UserId);
        using HttpResponseMessage management = await fixture.Client.SendAsync(managementRequest);
        string attendeeBody = await attendee.Content.ReadAsStringAsync();
        string managementBody = await management.Content.ReadAsStringAsync();

        await Assert.That(anonymousAttendee.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(anonymousManagement.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(attendee.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(attendee.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(attendee.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(attendeeBody).Contains(scenario.Address);
        await Assert.That(management.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(management.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(management.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(managementBody).Contains(scenario.Address);

        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        int allowedAudits = await context.EventLocationExactReadAudits.CountAsync(item =>
            item.EventLocationId == scenario.EventLocationId && item.WasAuthorized);
        await Assert.That(allowedAudits).IsEqualTo(1);
    }

    [Test]
    public async Task PrivateRoutes_HideUncoveredAndCrossEventAssociations()
    {
        EventLocationRouteScenario scenario = await SeedScenarioAsync();
        string attendeeRoute = $"/api/events/{scenario.EventId}/locations/my-access";
        using HttpRequestMessage uncoveredRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            attendeeRoute,
            Guid.CreateVersion7());
        using HttpResponseMessage uncovered = await fixture.Client.SendAsync(uncoveredRequest);
        string uncoveredBody = await uncovered.Content.ReadAsStringAsync();

        string crossEventRoute =
            $"/api/events/{Guid.CreateVersion7()}/locations/{scenario.EventLocationId}/management";
        using HttpRequestMessage crossEventRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            crossEventRoute,
            scenario.UserId);
        using HttpResponseMessage crossEvent = await fixture.Client.SendAsync(crossEventRequest);

        await Assert.That(uncovered.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(uncovered.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(uncovered.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(uncoveredBody).IsEqualTo("[]");
        await Assert.That(crossEvent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DisclosureUpdate_UsesBothConcurrencyTokensAndReturnsStableConflictProblem()
    {
        EventLocationRouteScenario scenario = await SeedScenarioAsync();
        string route = $"/api/events/{scenario.EventId}/locations/{scenario.EventLocationId}/disclosure";
        var body = new UpdateEventLocationDisclosureDto
        {
            ExpectedPolicyVersion = scenario.PolicyVersion,
            ExpectedConcurrencyStamp = scenario.ConcurrencyStamp,
            Fields = new UpdateEventLocationDisclosureFieldsDto
            {
                ShowVenueName = true,
                ShowCity = true,
                ShowCountry = true,
                ShowRoomName = false,
                ShowStreetAddress = false,
                ShowPostcode = false,
                ShowCoordinates = false
            },
            Audience = new UpdateEventLocationDisclosureAudienceDto
            {
                FullDetailsAudienceId = (int)LocationDisclosureAudienceEnum.ConfirmedParticipant,
                RevealFullDetailsFromUtc = OptionalUpdate<DateTime?>.Set(DateTime.UtcNow.AddMinutes(-1))
            }
        };

        using HttpRequestMessage updateRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Patch,
            route,
            scenario.UserId);
        updateRequest.Content = JsonContent.Create(body);
        using HttpResponseMessage update = await fixture.Client.SendAsync(updateRequest);

        using HttpRequestMessage staleRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Patch,
            route,
            scenario.UserId);
        staleRequest.Content = JsonContent.Create(body);
        using HttpResponseMessage stale = await fixture.Client.SendAsync(staleRequest);
        string staleBody = await stale.Content.ReadAsStringAsync();

        await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(update.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(update.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(stale.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        using JsonDocument staleProblem = JsonDocument.Parse(staleBody);
        await Assert.That(staleProblem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("concurrent_update");
    }

    [Test]
    public async Task ProblemResponses_UseRfc7807MediaTypeForNotFoundAndValidation()
    {
        EventLocationRouteScenario scenario = await SeedScenarioAsync();

        using HttpResponseMessage unknownPublic = await fixture.Client.GetAsync(
            $"/api/events/{Guid.CreateVersion7()}/locations");

        string crossEventRoute =
            $"/api/events/{Guid.CreateVersion7()}/locations/{scenario.EventLocationId}/management";
        using HttpRequestMessage crossEventRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            crossEventRoute,
            scenario.UserId);
        using HttpResponseMessage crossEvent = await fixture.Client.SendAsync(crossEventRequest);

        string updateRoute =
            $"/api/events/{scenario.EventId}/locations/{scenario.EventLocationId}/disclosure";
        using HttpRequestMessage malformedRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Patch,
            updateRoute,
            scenario.UserId);
        malformedRequest.Content = JsonContent.Create(new UpdateEventLocationDisclosureDto
        {
            ExpectedPolicyVersion = scenario.PolicyVersion,
            ExpectedConcurrencyStamp = scenario.ConcurrencyStamp,
            Fields = new UpdateEventLocationDisclosureFieldsDto
            {
                ShowVenueName = true,
                ShowCity = true,
                ShowCountry = true,
                ShowRoomName = false,
                ShowStreetAddress = false,
                ShowPostcode = false,
                ShowCoordinates = false
            },
            Audience = new UpdateEventLocationDisclosureAudienceDto
            {
                FullDetailsAudienceId = int.MaxValue,
                RevealFullDetailsFromUtc = OptionalUpdate<DateTime?>.Set(DateTime.UtcNow)
            }
        });
        using HttpResponseMessage malformed = await fixture.Client.SendAsync(malformedRequest);

        await AssertProblemAsync(unknownPublic, HttpStatusCode.NotFound, "resource_not_found");
        await AssertProblemAsync(crossEvent, HttpStatusCode.NotFound, "resource_not_found");
        await AssertProblemAsync(
            malformed,
            HttpStatusCode.BadRequest,
            "event_location_policy_validation_failed");
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo(expectedCode);
    }

    private async Task<EventLocationRouteScenario> SeedScenarioAsync()
    {
        await fixture.ResetDatabaseAsync();
        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        TenantScenarioSeed.TenantScenarioResult tenant =
            await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        DateTime now = DateTime.UtcNow;

        var @event = new EventBuilder()
            .WithTitle("EventLocation route contract")
            .WithActorId(tenant.ActorId)
            .WithTenantId(tenant.TenantId)
            .WithStatus(EventStatusEnum.Published)
            .WithVisibility(VisibilityTypeEnum.Public)
            .Build();
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Tenant = null!,
            FullName = "Purpose scoped venue",
            Country = "BE",
            City = "Brussels",
            Timezone = "Europe/Brussels",
            CreatedAt = now.AddDays(-31),
            CreatedBy = tenant.UserId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        location.ClassifyAs(LocationKindEnum.CommercialVenue);
        location.SetProviderAddress(
            "405 Privacy Avenue",
            "ELP405",
            Explore.Domain.ValueObjects.GeoCoordinate.Create(50.85, 4.35));

        EventLocation placement = EventLocation.CreatePhysical(
            tenant.TenantId,
            @event.Id,
            location.Id,
            tenant.UserId,
            now.AddDays(-31));
        EventLocationDisclosureAudit initialAudit = placement.CreateInitialDisclosureAudit();
        EventLocationDisclosureAudit policyAudit = placement.ChangeDisclosurePolicy(
            EventLocationDisclosureFields.VenueName
                | EventLocationDisclosureFields.City
                | EventLocationDisclosureFields.Country
                | EventLocationDisclosureFields.StreetAddress
                | EventLocationDisclosureFields.Postcode
                | EventLocationDisclosureFields.Coordinates,
            LocationDisclosureAudienceEnum.ConfirmedParticipant,
            now.AddMinutes(-5),
            placement.PolicyVersion,
            tenant.UserId,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            now.AddMinutes(-10),
            needsPrivacyReview: false);
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Tenant = null!,
            EventId = @event.Id,
            Event = @event,
            Title = "Registered location session",
            RegistrationModeId = (int)RegistrationModeEnum.Open,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        session.AssignEventLocation(placement);
        @event.Sessions.Add(session);
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(
            tenant.TenantId,
            @event.Id,
            "USD",
            versionNumber: 1);
        RegistrationOrder order = CreateConfirmedOrder(
            tenant.TenantId,
            @event.Id,
            tenant.UserId,
            catalog.Id,
            now);
        RegistrationParticipant participant = RegistrationParticipant.Create(
            tenant.TenantId, order.Id, tenant.UserId, ParticipantTypeEnum.Adult, guardian: null);
        var registration = new EventRegistration
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Tenant = null!,
            EventId = @event.Id,
            Event = @event,
            LinkedUserId = tenant.UserId,
            RegistrationParticipantId = participant.Id,
            RegistrationParticipant = participant,
            EventSessionId = session.Id,
            EventSession = session,
            RegistrationOrderId = order.Id,
            RegistrationOrder = order,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            CoverageEstablishedAt = now.AddMinutes(-30),
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        context.Locations.Add(location);
        context.Events.Add(@event);
        context.EventTicketCatalogVersions.Add(catalog);
        context.RegistrationOrders.Add(order);
        context.RegistrationParticipants.Add(participant);
        context.EventLocations.Add(placement);
        context.EventLocationDisclosureAudits.AddRange(initialAudit, policyAudit);
        context.EventRegistrations.Add(registration);
        context.EventRoleAssignments.Add(EventRoleAssignment.Create(
            tenant.TenantId,
            @event.Id,
            tenant.UserId,
            (int)RoleEnum.EventOwner,
            EventRoleAssignmentStatus.Active,
            now.AddMinutes(-30),
            expiresAtUtc: null,
            tenant.UserId));
        await context.SaveChangesAsync();
        await context.EventLocations
            .Where(item => item.Id == placement.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                item => item.CreatedAt,
                now.AddDays(-31)));

        return new(
            tenant.UserId,
            @event.Id,
            placement.Id,
            location.Id,
            location.Address!,
            location.Postcode!,
            placement.PolicyVersion,
            placement.ConcurrencyStamp);
    }

    private static RegistrationOrder CreateConfirmedOrder(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        Guid catalogId,
        DateTime createdAt)
    {
        RegistrationOrder order = RegistrationOrder.Create(
            tenantId,
            eventId,
            userId,
            purchaserActorId: null,
            BookingPartyTypeEnum.Individual,
            catalogId,
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(),
                4,
                3,
                2,
                GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            registrationWorkflowVersionId: null,
            guestAccessTokenHash: null,
            "USD",
            createdAt,
            expiresAt: null);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, createdAt);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, createdAt);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, createdAt);
        order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, createdAt);
        return order;
    }

    private sealed record EventLocationRouteScenario(
        Guid UserId,
        Guid EventId,
        Guid EventLocationId,
        Guid LocationId,
        string Address,
        string Postcode,
        int PolicyVersion,
        Guid ConcurrencyStamp);
}

public sealed class EventLocationRouteRuntimeFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("event_location_routes")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private TestDatabaseReset _databaseReset = null!;

    public PostgreSqlApiWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var context = new ExploreDbContext(options))
        {
            await context.Database.MigrateAsync();
            await LookupTableSeeder.SeedAsync(context);
        }

        Factory = new PostgreSqlApiWebApplicationFactory(
            _container.GetConnectionString(),
            new Dictionary<string, string?>
            {
                ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
                ["RateLimiting:DisableInTesting"] = "true"
            },
            services =>
            {
                services.RemoveAll<IAuthorizationProvider>();
                services.AddScoped<IAuthorizationProvider, FallbackAuthorizationService>();
            });
        Factory.UseKestrel();
        Client = Factory.CreateClient();
        _databaseReset = await TestDatabaseReset.CreateAsync(_container.GetConnectionString());
    }

    public async Task ResetDatabaseAsync()
    {
        await _databaseReset.ResetAsync();
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await LookupTableSeeder.SeedAsync(context);
    }

    public HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string url,
        Guid userId)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            "X-Test-Auth",
            TestAuthHandler.CreateAuthHeaderValue(userId, "EventLocation route user"));
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
