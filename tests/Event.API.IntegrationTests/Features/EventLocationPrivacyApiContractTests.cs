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
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[Category("EventLocationPrivacy")]
[Category("EventLocationPrivacyApi")]
public sealed class EventLocationPrivacyApiMetadataTests
{
    [Test]
    public async Task GenericPhysicalLocationReads_AreAuthenticatedAndNotOutputCached()
    {
        MethodInfo[] actions =
        [
            GetAction<LocationController>(nameof(LocationController.GetAll)),
            GetAction<LocationController>(nameof(LocationController.GetById)),
            GetAction<LocationRoomController>(nameof(LocationRoomController.GetByLocation)),
            GetAction<LocationRoomController>(nameof(LocationRoomController.GetById))
        ];

        var violations = actions.SelectMany(action =>
        {
            var actionName = $"{action.DeclaringType!.Name}.{action.Name}";
            var actionViolations = new List<string>();

            if (!HasEffectiveAttribute<AuthorizeAttribute>(action))
                actionViolations.Add($"{actionName} is not authenticated");
            if (HasEffectiveAttribute<AllowAnonymousAttribute>(action))
                actionViolations.Add($"{actionName} still allows anonymous access");
            if (HasEffectiveAttribute<OutputCacheAttribute>(action))
                actionViolations.Add($"{actionName} still enables shared output caching");
            if (!HasEffectiveAttribute<PrivateNoStoreAttribute>(action))
                actionViolations.Add($"{actionName} does not declare private no-store response handling");

            var classification = GetEffectiveAttribute<EndpointClassificationAttribute>(action);
            if (classification?.Class != EndpointClass.Authenticated)
                actionViolations.Add($"{actionName} is not classified Authenticated");

            return actionViolations;
        }).ToArray();

        await Assert.That(violations).IsEmpty()
            .Because(string.Join("; ", violations));
    }

    [Test]
    public async Task PublicEventFilter_DoesNotExposePhysicalLocationIds()
    {
        var locationIdsProperty = typeof(EventFilterRequest).GetProperty(
            "LocationIds",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        await Assert.That(locationIdsProperty).IsNull()
            .Because("anonymous discovery must not accept stable physical location identifiers");
    }

    [Test]
    public async Task PublicEventFilter_RejectsExplicitPhysicalLocationIds()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/event?locationIds={Guid.CreateVersion7()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StageAPublicPhysicalProjections_BypassSharedOutputCache()
    {
        MethodInfo[] actions =
        [
            GetAction<EventSessionController>(nameof(EventSessionController.GetAll)),
            GetAction<EventSessionController>(nameof(EventSessionController.GetById)),
            GetAction<EventSessionController>(nameof(EventSessionController.GetByEvent)),
            GetAction<EventSessionGroupController>(nameof(EventSessionGroupController.GetByEvent)),
            GetAction<EventSessionGroupController>(nameof(EventSessionGroupController.GetById)),
            GetAction<EventSessionGroupController>(nameof(EventSessionGroupController.GetSessions)),
            GetAction<EventAgendaItemController>(nameof(EventAgendaItemController.GetByEvent)),
            GetAction<EventAgendaItemController>(nameof(EventAgendaItemController.GetById)),
            GetAction<EventManagementReadController>(nameof(EventManagementReadController.GetProgramSummary)),
            GetAction<EventAgendaItemController>(nameof(EventAgendaItemController.GetAgendaProjection)),
            GetAction<EventCalendarController>(nameof(EventCalendarController.GetCalendar)),
            GetAction<EventSessionAgendaItemController>(nameof(EventSessionAgendaItemController.GetAll)),
            GetAction<EventSessionAgendaItemController>(nameof(EventSessionAgendaItemController.GetById)),
            GetAction<EventSessionAgendaItemController>(nameof(EventSessionAgendaItemController.GetBySession))
        ];

        string[] cachedActions = actions
            .Where(HasEffectiveAttribute<OutputCacheAttribute>)
            .Select(action => $"{action.DeclaringType!.Name}.{action.Name}")
            .ToArray();

        await Assert.That(cachedActions).IsEmpty()
            .Because("Stage A must bypass shared output caches that can retain pre-deployment physical venue data");
    }

    [Test]
    public async Task GeographicVenueBrowseRoutes_AreRemovedFromTheApiSurface()
    {
        // ELP-430 contraction: these enumerated exact venue addresses, including private homes, for any
        // caller holding tenant-wide location view and with no disclosure evaluation at all.
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        string[] removedRoutes =
        [
            "/api/location/by-city/Brussels",
            "/api/location/by-country/Belgium"
        ];

        var violations = new List<string>();
        foreach (string route in removedRoutes)
        {
            using var anonymous = new HttpRequestMessage(HttpMethod.Get, route);
            using var anonymousResponse = await client.SendAsync(anonymous);
            if (anonymousResponse.StatusCode != HttpStatusCode.NotFound)
            {
                violations.Add($"anonymous {route} -> {(int)anonymousResponse.StatusCode}");
            }

            using var authenticated = new HttpRequestMessage(HttpMethod.Get, route);
            authenticated.Headers.Add(
                TestAuthHandler.AuthHeaderName,
                TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
            using var authenticatedResponse = await client.SendAsync(authenticated);
            if (authenticatedResponse.StatusCode != HttpStatusCode.NotFound)
            {
                violations.Add($"authenticated {route} -> {(int)authenticatedResponse.StatusCode}");
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task ResourceAuthorizationDenial_FailsClosedForAuthenticatedPhysicalReads()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = factory.CreateClient();
        string[] routes =
        [
            "/api/location",
            $"/api/location/{Guid.CreateVersion7()}",
            $"/api/locationroom/by-location/{Guid.CreateVersion7()}",
            $"/api/locationroom/{Guid.CreateVersion7()}"
        ];

        var violations = new List<string>();
        foreach (string route in routes)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add(
                TestAuthHandler.AuthHeaderName,
                TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
            using var response = await client.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.Forbidden)
                violations.Add($"{route} returned {(int)response.StatusCode}, expected 403");
        }

        await Assert.That(violations).IsEmpty()
            .Because("an authenticated caller denied physical-location view authority must fail closed on every generic read");
    }

    [Test]
    public async Task AuthorizedPhysicalCollection_EmitsPrivateNoStore()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/location");
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task EventScopedManagedPhysicalReads_RequireEventManagementAuthorizationAndPrivateNoStore()
    {
        (MethodInfo Action, Type RequestType)[] contracts =
        [
            (GetAction<EventSessionController>(nameof(EventSessionController.GetManagedById)), typeof(GetManagedEventSessionDetailsRequest)),
            (GetAction<EventSessionController>(nameof(EventSessionController.GetManagedByEvent)), typeof(GetManagedSessionsByEventRequest)),
            (GetAction<EventSessionGroupController>(nameof(EventSessionGroupController.GetManagedByEvent)), typeof(GetManagedEventSessionGroupsByEventRequest)),
            (GetAction<EventSessionGroupController>(nameof(EventSessionGroupController.GetManagedById)), typeof(GetManagedEventSessionGroupDetailRequest)),
            (GetAction<EventAgendaItemController>(nameof(EventAgendaItemController.GetManagedByEvent)), typeof(GetManagedEventAgendaItemsByEventRequest)),
            (GetAction<EventAgendaItemController>(nameof(EventAgendaItemController.GetManagedById)), typeof(GetManagedEventAgendaItemDetailRequest)),
            (GetAction<EventSessionAgendaItemController>(nameof(EventSessionAgendaItemController.GetManagedBySession)), typeof(GetManagedAgendaItemsBySessionRequest)),
            (GetAction<EventManagementReadController>(nameof(EventManagementReadController.GetSessionCreateContext)), typeof(GetEventSessionCreateContextRequest)),
            (GetAction<EventManagementReadController>(nameof(EventManagementReadController.GetManagedProgramSummary)), typeof(GetManagedEventProgramSummaryRequest))
        ];

        var violations = new List<string>();
        foreach ((MethodInfo action, Type requestType) in contracts)
        {
            string actionName = $"{action.DeclaringType!.Name}.{action.Name}";
            var authorization = requestType.GetCustomAttribute<AuthorizeResourceAttribute>();

            if (!HasEffectiveAttribute<AuthorizeAttribute>(action))
                violations.Add($"{actionName} is not authenticated");
            if (HasEffectiveAttribute<AllowAnonymousAttribute>(action))
                violations.Add($"{actionName} allows anonymous access");
            if (!HasEffectiveAttribute<PrivateNoStoreAttribute>(action))
                violations.Add($"{actionName} lacks private no-store handling");
            if (HasEffectiveAttribute<OutputCacheAttribute>(action))
                violations.Add($"{actionName} enables shared output caching");
            if (GetEffectiveAttribute<EndpointClassificationAttribute>(action)?.Class != EndpointClass.Authenticated)
                violations.Add($"{actionName} is not classified Authenticated");
            if (authorization?.Resource != ResourceKinds.Event
                || authorization.Action != AuthorizationActions.Events.ViewManagement)
            {
                violations.Add($"{requestType.Name} is not authorized by Event/ViewManagement");
            }
            if (!typeof(ISecureRequest).IsAssignableFrom(requestType))
                violations.Add($"{requestType.Name} is not an ISecureRequest");
        }

        await Assert.That(violations).IsEmpty().Because(string.Join("; ", violations));
    }

    private static MethodInfo GetAction<TController>(string name)
        => typeof(TController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Missing controller action {typeof(TController).Name}.{name}.");

    private static bool HasEffectiveAttribute<TAttribute>(MethodInfo action)
        where TAttribute : Attribute
        => GetEffectiveAttribute<TAttribute>(action) is not null;

    private static TAttribute? GetEffectiveAttribute<TAttribute>(MethodInfo action)
        where TAttribute : Attribute
        => action.GetCustomAttribute<TAttribute>(inherit: true)
            ?? action.DeclaringType?.GetCustomAttribute<TAttribute>(inherit: true);
}

[Category("EventLocationPrivacy")]
[Category("EventLocationPrivacyApi")]
[ClassDataSource<EventLocationPrivacyRuntimeFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RealRuntimeDb")]
public sealed class EventLocationPrivacyApiRuntimeTests(EventLocationPrivacyRuntimeFixture fixture)
{
    private static readonly string[] SessionForbiddenProperties =
    [
        "locationId", "locationFullName", "locationAddress", "locationCity", "locationCountry",
        "roomId", "roomName"
    ];

    private static readonly string[] GroupForbiddenProperties = ["locationId", "locationName", "roomId", "roomName"];
    private static readonly string[] ProgramForbiddenProperties = ["locationName", "roomName"];
    private static readonly string[] AgendaForbiddenProperties = ["locationId", "roomId"];
    private static readonly string[] SessionAgendaForbiddenProperties = ["locationId", "locationFullName"];

    [Test]
    [Category("EventLocationPrivacy")]
    [Category("EventLocationPrivacyApi")]
    public async Task AnonymousPhysicalLocationAndRoomReads_FailClosedWithoutLeakingVenueData()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();

        var violations = new List<string>();
        foreach (string route in GetPhysicalRoutes(scenario))
        {
            using var response = await fixture.Client.GetAsync(route);
            string body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.Unauthorized)
                violations.Add($"{route} returned {(int)response.StatusCode}, expected 401");
            if (ContainsAny(body, scenario.PhysicalContentSecrets))
                violations.Add($"{route} disclosed seeded physical venue data");
        }

        await Assert.That(violations).IsEmpty()
            .Because(string.Join("; ", violations));
    }

    [Test]
    [Category("EventLocationPrivacy")]
    [Category("EventLocationPrivacyApi")]
    public async Task AuthorizedPhysicalReads_ArePrivateAndNoStore()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();

        var violations = new List<string>();
        foreach (string route in GetPhysicalRoutes(scenario))
        {
            using var request = fixture.CreateTenantAdminRequest(
                HttpMethod.Get,
                route,
                scenario.TenantId,
                scenario.TenantAdminUserId);
            using var response = await fixture.Client.SendAsync(request);
            var cacheControl = response.Headers.CacheControl;

            if (response.StatusCode != HttpStatusCode.OK)
                violations.Add($"{route} returned {(int)response.StatusCode}, expected 200 for the owning tenant administrator");
            if (cacheControl?.Private != true || cacheControl.NoStore != true)
                violations.Add($"{route} did not emit Cache-Control: private, no-store");
        }

        await Assert.That(violations).IsEmpty()
            .Because(string.Join("; ", violations));
    }

    [Test]
    [Category("EventLocationPrivacy")]
    [Category("EventLocationPrivacyApi")]
    public async Task PhysicalReads_DoNotDiscloseAnotherTenantsVenueData()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();

        var violations = new List<string>();
        foreach (string route in GetOtherTenantPhysicalRoutes(scenario))
        {
            using var request = fixture.CreateTenantAdminRequest(
                HttpMethod.Get,
                route,
                scenario.TenantId,
                scenario.TenantAdminUserId);
            using var response = await fixture.Client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            bool isDetailRoute = route == $"/api/location/{scenario.OtherLocationId}"
                || route == $"/api/locationroom/{scenario.OtherRoomId}";
            if (isDetailRoute
                && response.StatusCode != HttpStatusCode.Forbidden
                && response.StatusCode != HttpStatusCode.NotFound)
            {
                violations.Add($"{route} returned {(int)response.StatusCode}, expected 403 or non-disclosing 404 for a different tenant");
            }
            else if (!isDetailRoute && response.StatusCode != HttpStatusCode.OK)
            {
                violations.Add($"{route} returned {(int)response.StatusCode}, expected an own-tenant collection response");
            }
            if (ContainsAny(body, scenario.OtherPhysicalContentSecrets))
                violations.Add($"{route} disclosed seeded physical venue data across tenants");
        }

        await Assert.That(violations).IsEmpty()
            .Because("tenant administration authority must not cross tenant boundaries");
    }

    [Test]
    [Category("EventLocationPrivacy")]
    [Category("EventLocationPrivacyApi")]
    public async Task PublicSessionProgramAgendaAndCalendar_OmitPhysicalIdentifiersNamesAndLinks()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();
        (string Route, string[] ForbiddenProperties)[] routes =
        [
            ("/api/eventsession", SessionForbiddenProperties),
            ($"/api/eventsession/{scenario.SessionId}", SessionForbiddenProperties),
            ($"/api/eventsession/by-event/{scenario.EventId}", SessionForbiddenProperties),
            ($"/api/eventsessiongroup/by-event/{scenario.EventId}", GroupForbiddenProperties),
            ($"/api/eventsessiongroup/{scenario.SessionGroupId}", GroupForbiddenProperties),
            ($"/api/eventsessiongroup/{scenario.SessionGroupId}/sessions", SessionForbiddenProperties),
            ($"/api/eventagendaitem/by-event/{scenario.EventId}", AgendaForbiddenProperties),
            ($"/api/eventagendaitem/{scenario.AgendaItemId}", AgendaForbiddenProperties),
            ($"/api/event/{scenario.EventId}/program-summary", ProgramForbiddenProperties),
            ($"/api/eventagendaitem/agenda-projection/{scenario.EventId}", AgendaForbiddenProperties),
            ($"/api/event/{scenario.EventId}/calendar", []),
            ("/api/eventsessionagendaitem", SessionAgendaForbiddenProperties),
            ($"/api/eventsessionagendaitem/{scenario.SessionAgendaItemId}", SessionAgendaForbiddenProperties),
            ($"/api/eventsessionagendaitem/by-session/{scenario.SessionId}", SessionAgendaForbiddenProperties)
        ];

        var violations = new List<string>();
        foreach ((string route, string[] forbiddenProperties) in routes)
        {
            using var response = await fixture.Client.GetAsync(route);
            string body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.OK)
            {
                violations.Add($"{route} returned {(int)response.StatusCode}, expected 200");
                continue;
            }

            if (forbiddenProperties.Length > 0)
            {
                string[] exposedProperties = FindProperties(body, forbiddenProperties);
                if (exposedProperties.Length > 0)
                    violations.Add($"{route} exposed properties [{string.Join(", ", exposedProperties)}]");
            }
            if (ContainsAny(body, scenario.PhysicalSecrets))
                violations.Add($"{route} exposed seeded physical venue values");
            if (body.Contains("/api/location/", StringComparison.OrdinalIgnoreCase)
                || body.Contains("/api/locationroom/", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{route} exposed a physical-location HAL link");
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because(string.Join("; ", violations));
    }

    [Test]
    [Category("EventLocationPrivacy")]
    [Category("EventLocationPrivacyApi")]
    public async Task EveryPublicStageAProjection_IsByteEquivalentForAnonymousAndAuthenticatedCallers()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();
        string[] routes =
        [
            "/api/eventsession",
            $"/api/eventsession/{scenario.SessionId}",
            $"/api/eventsession/by-event/{scenario.EventId}",
            $"/api/eventsessiongroup/by-event/{scenario.EventId}",
            $"/api/eventsessiongroup/{scenario.SessionGroupId}",
            $"/api/eventsessiongroup/{scenario.SessionGroupId}/sessions",
            $"/api/eventagendaitem/by-event/{scenario.EventId}",
            $"/api/eventagendaitem/{scenario.AgendaItemId}",
            $"/api/event/{scenario.EventId}/program-summary",
            $"/api/eventagendaitem/agenda-projection/{scenario.EventId}",
            $"/api/event/{scenario.EventId}/calendar",
            "/api/eventsessionagendaitem",
            $"/api/eventsessionagendaitem/{scenario.SessionAgendaItemId}",
            $"/api/eventsessionagendaitem/by-session/{scenario.SessionId}"
        ];

        var violations = new List<string>();
        foreach (string route in routes)
        {
            using var anonymousResponse = await fixture.Client.GetAsync(route);
            byte[] anonymousBytes = await anonymousResponse.Content.ReadAsByteArrayAsync();

            await EvictPublicOutputCacheAsync();

            using var authenticatedRequest = fixture.CreateAuthenticatedRequest(
                HttpMethod.Get,
                route,
                scenario.UserId);
            using var authenticatedResponse = await fixture.Client.SendAsync(authenticatedRequest);
            byte[] authenticatedBytes = await authenticatedResponse.Content.ReadAsByteArrayAsync();

            if (anonymousResponse.StatusCode != HttpStatusCode.OK)
                violations.Add($"anonymous {route} returned {(int)anonymousResponse.StatusCode}, expected 200");
            if (authenticatedResponse.StatusCode != HttpStatusCode.OK)
                violations.Add($"authenticated {route} returned {(int)authenticatedResponse.StatusCode}, expected 200");
            if (!authenticatedBytes.SequenceEqual(anonymousBytes))
                violations.Add($"{route} varied its public response body for an authenticated principal");
        }

        await Assert.That(violations).IsEmpty()
            .Because("public projection bodies must not enrich or vary based on browser authentication state");
    }

    [Test]
    public async Task EventOwnerManagedReads_ReturnExactPhysicalFieldsWithPrivateNoStore()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();
        (string Route, string[] ExpectedValues)[] routes =
        [
            ($"/api/eventsession/management/by-event/{scenario.EventId}/{scenario.SessionId}",
                [scenario.LocationId.ToString(), scenario.RoomId.ToString(), scenario.LocationFullName, scenario.RoomName]),
            ($"/api/eventsession/management/by-event/{scenario.EventId}",
                [scenario.LocationId.ToString(), scenario.RoomId.ToString(), scenario.LocationFullName, scenario.RoomName]),
            ($"/api/eventsessiongroup/management/by-event/{scenario.EventId}",
                [scenario.LocationId.ToString(), scenario.RoomId.ToString(), scenario.LocationFullName, scenario.RoomName]),
            ($"/api/eventsessiongroup/management/by-event/{scenario.EventId}/{scenario.SessionGroupId}",
                [scenario.LocationId.ToString(), scenario.RoomId.ToString(), scenario.LocationFullName, scenario.RoomName]),
            ($"/api/eventagendaitem/management/by-event/{scenario.EventId}",
                [scenario.AgendaItemId.ToString()]),
            ($"/api/eventagendaitem/management/by-event/{scenario.EventId}/{scenario.AgendaItemId}",
                [scenario.LocationId.ToString(), scenario.RoomId.ToString()]),
            ($"/api/eventsessionagendaitem/management/by-event/{scenario.EventId}/by-session/{scenario.SessionId}",
                [scenario.LocationFullName]),
            ($"/api/event/{scenario.EventId}/session-create-context",
                [scenario.LocationId.ToString(), scenario.RoomId.ToString(), scenario.LocationFullName, scenario.RoomName])
        ];

        var violations = new List<string>();
        foreach ((string route, string[] expectedValues) in routes)
        {
            using var request = fixture.CreateAuthenticatedRequest(HttpMethod.Get, route, scenario.UserId);
            using var response = await fixture.Client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.OK)
                violations.Add($"{route} returned {(int)response.StatusCode}, expected 200 for the event owner");
            if (response.Headers.CacheControl?.Private != true || response.Headers.CacheControl.NoStore != true)
                violations.Add($"{route} did not emit Cache-Control: private, no-store");
            foreach (string expectedValue in expectedValues)
            {
                if (!body.Contains(expectedValue, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"{route} omitted expected managed value {expectedValue}");
            }

            if (route.EndsWith("session-create-context", StringComparison.OrdinalIgnoreCase)
                && (body.Contains(scenario.UnrelatedLocationId.ToString(), StringComparison.OrdinalIgnoreCase)
                    || body.Contains(scenario.UnrelatedRoomId.ToString(), StringComparison.OrdinalIgnoreCase)
                    || body.Contains(scenario.UnrelatedLocationFullName, StringComparison.OrdinalIgnoreCase)
                    || body.Contains(scenario.UnrelatedRoomName, StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add($"{route} enumerated a venue referenced only by another event");
            }
        }

        await Assert.That(violations).IsEmpty().Because(string.Join("; ", violations));
    }

    [Test]
    public async Task ManagedReads_RejectAnonymousAndUnrelatedTenantUsers()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();
        string[] routes =
        [
            $"/api/eventsession/management/by-event/{scenario.EventId}/{scenario.SessionId}",
            $"/api/eventsession/management/by-event/{scenario.EventId}",
            $"/api/eventsessiongroup/management/by-event/{scenario.EventId}",
            $"/api/eventsessiongroup/management/by-event/{scenario.EventId}/{scenario.SessionGroupId}",
            $"/api/eventagendaitem/management/by-event/{scenario.EventId}",
            $"/api/eventagendaitem/management/by-event/{scenario.EventId}/{scenario.AgendaItemId}",
            $"/api/eventsessionagendaitem/management/by-event/{scenario.EventId}/by-session/{scenario.SessionId}",
            $"/api/event/{scenario.EventId}/session-create-context",
            $"/api/event/{scenario.EventId}/management-program-summary"
        ];

        var violations = new List<string>();
        foreach (string route in routes)
        {
            using var anonymousResponse = await fixture.Client.GetAsync(route);
            if (anonymousResponse.StatusCode != HttpStatusCode.Unauthorized)
                violations.Add($"anonymous {route} returned {(int)anonymousResponse.StatusCode}, expected 401");

            using var unrelatedRequest = fixture.CreateAuthenticatedRequest(
                HttpMethod.Get,
                route,
                scenario.OtherTenantUserId);
            using var unrelatedResponse = await fixture.Client.SendAsync(unrelatedRequest);
            if (unrelatedResponse.StatusCode != HttpStatusCode.Forbidden)
                violations.Add($"unrelated user {route} returned {(int)unrelatedResponse.StatusCode}, expected 403");
        }

        await Assert.That(violations).IsEmpty().Because(string.Join("; ", violations));
    }

    [Test]
    public async Task ManagedDetailReads_RejectSameTenantChildrenFromAnotherEvent()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();
        string[] routes =
        [
            $"/api/eventsession/management/by-event/{scenario.EventId}/{scenario.UnrelatedSessionId}",
            $"/api/eventsessiongroup/management/by-event/{scenario.EventId}/{scenario.UnrelatedSessionGroupId}",
            $"/api/eventagendaitem/management/by-event/{scenario.EventId}/{scenario.UnrelatedAgendaItemId}",
            $"/api/eventsessionagendaitem/management/by-event/{scenario.EventId}/by-session/{scenario.UnrelatedSessionId}"
        ];

        var violations = new List<string>();
        foreach (string route in routes)
        {
            using var request = fixture.CreateAuthenticatedRequest(HttpMethod.Get, route, scenario.UserId);
            using var response = await fixture.Client.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.NotFound)
                violations.Add($"{route} returned {(int)response.StatusCode}, expected 404");
        }

        await Assert.That(violations).IsEmpty()
            .Because("managed child IDs must belong to the Event resource authorized by the route");
    }

    [Test]
    public async Task DraftPrivateSessionLanguageWrites_SucceedForEventOwner()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();
        using var createRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Post,
            "/api/eventsessionlanguage",
            scenario.UserId);
        createRequest.Content = JsonContent.Create(new
        {
            eventSessionId = scenario.UnrelatedSessionId,
            languageId = 1
        });
        using var createResponse = await fixture.Client.SendAsync(createRequest);
        string createBody = await createResponse.Content.ReadAsStringAsync();

        if (createResponse.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Draft language create returned {(int)createResponse.StatusCode}: {createBody}");
        }

        using var createdDocument = JsonDocument.Parse(createBody);
        int assignmentId = createdDocument.RootElement.GetProperty("id").GetInt32();
        using var listRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/eventsessionlanguage/management/by-event/{scenario.UnrelatedEventId}/by-session/{scenario.UnrelatedSessionId}",
            scenario.UserId);
        using var listResponse = await fixture.Client.SendAsync(listRequest);
        using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var assignment = listDocument.RootElement
            .GetProperty("_embedded")
            .GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == assignmentId);
        string concurrencyStamp = assignment.GetProperty("concurrencyStamp").GetString()
            ?? throw new InvalidOperationException("Created language assignment omitted concurrencyStamp.");

        using var crossEventRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Patch,
            $"/api/eventsessionlanguage/{assignmentId}",
            scenario.UserId);
        crossEventRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{concurrencyStamp}\"");
        crossEventRequest.Content = JsonContent.Create(new
        {
            session = new { eventSessionId = scenario.SessionId }
        });
        using var crossEventResponse = await fixture.Client.SendAsync(crossEventRequest);

        using var updateRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Patch,
            $"/api/eventsessionlanguage/{assignmentId}",
            scenario.UserId);
        updateRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{concurrencyStamp}\"");
        updateRequest.Content = JsonContent.Create(new { language = new { languageId = 2 } });
        using var updateResponse = await fixture.Client.SendAsync(updateRequest);

        using var deleteRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"/api/eventsessionlanguage/{assignmentId}",
            scenario.UserId);
        using var deleteResponse = await fixture.Client.SendAsync(deleteRequest);

        await Assert.That(crossEventResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(updateResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(deleteResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task ManagedProgramSummary_RetainsDraftPrivateEventSessions()
    {
        PrivacyScenario scenario = await SeedPrivacyScenarioAsync();
        using var publicResponse = await fixture.Client.GetAsync(
            $"/api/event/{scenario.UnrelatedEventId}/program-summary");
        using var managedRequest = fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/event/{scenario.UnrelatedEventId}/management-program-summary",
            scenario.UserId);
        using var managedResponse = await fixture.Client.SendAsync(managedRequest);
        string body = await managedResponse.Content.ReadAsStringAsync();

        await Assert.That(publicResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(managedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(managedResponse.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(managedResponse.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(body).Contains($"Unrelated Private Session");
    }

    private async Task EvictPublicOutputCacheAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var outputCacheStore = scope.ServiceProvider.GetRequiredService<IOutputCacheStore>();
        await outputCacheStore.EvictByTagAsync("list-data", default);
        await outputCacheStore.EvictByTagAsync("detail-data", default);
    }

    private static string[] GetPhysicalRoutes(PrivacyScenario scenario) =>
    [
        "/api/location",
        $"/api/location/{scenario.LocationId}",
        $"/api/locationroom/by-location/{scenario.LocationId}",
        $"/api/locationroom/{scenario.RoomId}"
    ];

    private static string[] GetOtherTenantPhysicalRoutes(PrivacyScenario scenario) =>
    [
        "/api/location",
        $"/api/location/{scenario.OtherLocationId}",
        $"/api/locationroom/by-location/{scenario.OtherLocationId}",
        $"/api/locationroom/{scenario.OtherRoomId}"
    ];

    private async Task<PrivacyScenario> SeedPrivacyScenarioAsync()
    {
        await fixture.ResetDatabaseAsync();

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var otherTenant = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context);
        string marker = Guid.NewGuid().ToString("N");
        string otherMarker = Guid.NewGuid().ToString("N");
        var start = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var tenantAdmin = new UserBuilder().Build();
        context.Users.Add(tenantAdmin);
        var tenantUser = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Tenant = null!,
            UserId = tenantAdmin.Id,
            User = tenantAdmin,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        context.TenantUsers.Add(tenantUser);
        context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Tenant = null!,
            TenantUserId = tenantUser.Id,
            TenantUser = tenantUser,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Private Venue {marker}",
            Country = $"Private Country {marker}",
            City = $"Private City {marker}",
            TenantId = tenant.TenantId,
            Tenant = null!,
            Timezone = "UTC",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        location.SetProviderAddress(
            $"Secret Address {marker}",
            $"Secret Postcode {marker}",
            Explore.Domain.ValueObjects.GeoCoordinate.Create(50.8466, 4.3528));
        var room = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            LocationId = location.Id,
            Location = location,
            Name = $"Private Room {marker}",
            Description = $"Secret Room Description {marker}",
            SortOrder = 1,
            TenantId = tenant.TenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var otherLocation = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Other Private Venue {otherMarker}",
            Country = $"Other Private Country {otherMarker}",
            City = $"Other Private City {otherMarker}",
            TenantId = otherTenant.TenantId,
            Tenant = null!,
            Timezone = "UTC",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        otherLocation.SetProviderAddress(
            $"Other Secret Address {otherMarker}",
            $"Other Secret Postcode {otherMarker}",
            Explore.Domain.ValueObjects.GeoCoordinate.Create(51.2194, 4.4025));
        var otherRoom = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            LocationId = otherLocation.Id,
            Location = otherLocation,
            Name = $"Other Private Room {otherMarker}",
            Description = $"Other Secret Room Description {otherMarker}",
            SortOrder = 1,
            TenantId = otherTenant.TenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var unrelatedLocation = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Unrelated Private Home {marker}",
            Country = location.Country,
            City = location.City,
            TenantId = tenant.TenantId,
            Tenant = null!,
            Timezone = "UTC",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        unrelatedLocation.SetManualAddress(
            $"Unrelated Secret Address {marker}",
            $"Unrelated Secret Postcode {marker}");
        var unrelatedRoom = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            LocationId = unrelatedLocation.Id,
            Location = unrelatedLocation,
            Name = $"Unrelated Private Room {marker}",
            SortOrder = 1,
            TenantId = tenant.TenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var @event = new EventBuilder()
            .WithTitle($"Public Privacy Event {marker}")
            .WithActorId(tenant.ActorId)
            .WithTenantId(tenant.TenantId)
            .WithStatus(EventStatusEnum.Published)
            .WithVisibility(VisibilityTypeEnum.Public)
            .WithSessionDates(DateOnly.FromDateTime(start.UtcDateTime), DateOnly.FromDateTime(start.UtcDateTime))
            .Build();
        EventLocation eventLocation = EventLocation.CreatePhysical(
            tenant.TenantId,
            @event.Id,
            location.Id,
            tenant.UserId,
            DateTime.UtcNow);
        EventLocationDisclosureAudit eventLocationAudit = eventLocation.CreateInitialDisclosureAudit();
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            LocationId = location.Id,
            Location = location,
            RoomId = room.Id,
            Room = room,
            TenantId = tenant.TenantId,
            Tenant = null!,
            Title = $"Public Session {marker}",
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            RegistrationModeId = 1,
            SortOrder = 1,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        session.AssignEventLocation(eventLocation);
        session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());

        var agendaItem = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            Title = $"Public Agenda Item {marker}",
            LocationId = location.Id,
            Location = location,
            RoomId = room.Id,
            Room = room,
            TenantId = tenant.TenantId,
            Tenant = null!,
            SortOrder = 2,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        agendaItem.AssignEventLocation(eventLocation);
        agendaItem.Reschedule(UtcInstantRange.Create(start.AddHours(1), start.AddHours(2)), "UTC", new EventScheduleProjectionCalculator());

        var sessionGroup = new EventSessionGroup
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            Name = $"Private Track {marker}",
            LocationId = location.Id,
            Location = location,
            RoomId = room.Id,
            Room = room,
            IsPublished = true,
            TenantId = tenant.TenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        sessionGroup.AssignEventLocation(eventLocation);
        var groupAssignment = new EventSessionGroupSession
        {
            Id = Guid.CreateVersion7(),
            EventSessionGroupId = sessionGroup.Id,
            EventSessionGroup = sessionGroup,
            EventSessionId = session.Id,
            EventSession = session,
            EventId = @event.Id,
            Event = @event,
            IsPrimary = true,
            TenantId = tenant.TenantId,
            Tenant = null!
        };
        var sessionAgendaItem = new EventSessionAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = session.Id,
            EventSession = session,
            StartTime = start.AddMinutes(10),
            EndTime = start.AddMinutes(20),
            Title = $"Private Session Agenda {marker}",
            LocationId = location.Id,
            Location = location,
            TenantId = tenant.TenantId,
            Tenant = null!
        };
        sessionAgendaItem.AssignEventLocation(eventLocation);
        var unrelatedEvent = new EventBuilder()
            .WithTitle($"Unrelated Privacy Event {marker}")
            .WithActorId(tenant.ActorId)
            .WithTenantId(tenant.TenantId)
            .WithStatus(EventStatusEnum.Draft)
            .WithVisibility(VisibilityTypeEnum.Private)
            .WithSessionDates(DateOnly.FromDateTime(start.UtcDateTime), DateOnly.FromDateTime(start.UtcDateTime))
            .Build();
        EventLocation unrelatedEventLocation = EventLocation.CreatePhysical(
            tenant.TenantId,
            unrelatedEvent.Id,
            unrelatedLocation.Id,
            tenant.UserId,
            DateTime.UtcNow);
        EventLocationDisclosureAudit unrelatedEventLocationAudit = unrelatedEventLocation.CreateInitialDisclosureAudit();
        var unrelatedSession = new EventSession(EventSessionStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            EventId = unrelatedEvent.Id,
            Event = unrelatedEvent,
            LocationId = unrelatedLocation.Id,
            Location = unrelatedLocation,
            RoomId = unrelatedRoom.Id,
            Room = unrelatedRoom,
            TenantId = tenant.TenantId,
            Tenant = null!,
            Title = $"Unrelated Private Session {marker}",
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            RegistrationModeId = 1,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        unrelatedSession.AssignEventLocation(unrelatedEventLocation);
        unrelatedSession.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
        var unrelatedAgendaItem = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventId = unrelatedEvent.Id,
            Event = unrelatedEvent,
            Title = $"Unrelated Private Agenda Item {marker}",
            LocationId = unrelatedLocation.Id,
            Location = unrelatedLocation,
            RoomId = unrelatedRoom.Id,
            Room = unrelatedRoom,
            TenantId = tenant.TenantId,
            Tenant = null!,
            SortOrder = 1,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        unrelatedAgendaItem.AssignEventLocation(unrelatedEventLocation);
        unrelatedAgendaItem.Reschedule(UtcInstantRange.Create(start.AddHours(1), start.AddHours(2)), "UTC", new EventScheduleProjectionCalculator());
        var unrelatedSessionGroup = new EventSessionGroup
        {
            Id = Guid.CreateVersion7(),
            EventId = unrelatedEvent.Id,
            Event = unrelatedEvent,
            Name = $"Unrelated Private Track {marker}",
            LocationId = unrelatedLocation.Id,
            Location = unrelatedLocation,
            RoomId = unrelatedRoom.Id,
            Room = unrelatedRoom,
            IsPublished = true,
            TenantId = tenant.TenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        unrelatedSessionGroup.AssignEventLocation(unrelatedEventLocation);
        unrelatedEvent.Sessions.Add(unrelatedSession);
        unrelatedEvent.RecalculateScheduleSummaryFromSessions();

        @event.Sessions.Add(session);
        @event.RecalculateScheduleSummaryFromSessions();
        context.Locations.Add(location);
        context.Locations.Add(otherLocation);
        context.Locations.Add(unrelatedLocation);
        context.LocationRooms.Add(room);
        context.LocationRooms.Add(otherRoom);
        context.LocationRooms.Add(unrelatedRoom);
        context.Events.Add(@event);
        context.Events.Add(unrelatedEvent);
        context.EventLocations.AddRange(eventLocation, unrelatedEventLocation);
        context.EventLocationDisclosureAudits.AddRange(eventLocationAudit, unrelatedEventLocationAudit);
        context.EventAgendaItems.Add(agendaItem);
        context.EventAgendaItems.Add(unrelatedAgendaItem);
        context.EventSessionGroups.Add(sessionGroup);
        context.EventSessionGroups.Add(unrelatedSessionGroup);
        context.EventSessionGroupSessions.Add(groupAssignment);
        context.EventSessionAgendaItems.Add(sessionAgendaItem);
        context.EventRoleAssignments.Add(EventRoleAssignment.Create(
            tenant.TenantId,
            @event.Id,
            tenant.UserId,
            (int)RoleEnum.EventOwner,
            EventRoleAssignmentStatus.Active,
            DateTime.UtcNow.AddMinutes(-1),
            expiresAtUtc: null,
            tenant.UserId));
        context.EventRoleAssignments.Add(EventRoleAssignment.Create(
            tenant.TenantId,
            unrelatedEvent.Id,
            tenant.UserId,
            (int)RoleEnum.EventOwner,
            EventRoleAssignmentStatus.Active,
            DateTime.UtcNow.AddMinutes(-1),
            expiresAtUtc: null,
            tenant.UserId));
        await context.SaveChangesAsync();

        return new PrivacyScenario(
            tenant.TenantId,
            tenant.UserId,
            tenantAdmin.Id,
            otherTenant.TenantId,
            otherTenant.UserId,
            @event.Id,
            session.Id,
            sessionGroup.Id,
            agendaItem.Id,
            sessionAgendaItem.Id,
            unrelatedEvent.Id,
            unrelatedSession.Id,
            unrelatedSessionGroup.Id,
            unrelatedAgendaItem.Id,
            location.Id,
            room.Id,
            location.City,
            location.Country,
            location.FullName,
            room.Name,
            unrelatedLocation.Id,
            unrelatedRoom.Id,
            unrelatedLocation.FullName,
            unrelatedRoom.Name,
            otherLocation.Id,
            otherRoom.Id,
            otherLocation.City,
            otherLocation.Country,
            [
                location.Id.ToString(),
                room.Id.ToString(),
                location.FullName,
                location.Address,
                location.Postcode,
                location.City,
                location.Country,
                location.Pii?.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? throw new InvalidOperationException("The Location fixture requires latitude."),
                location.Pii?.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? throw new InvalidOperationException("The Location fixture requires longitude."),
                room.Name,
                room.Description!
            ],
            [
                location.FullName,
                location.Address,
                location.Postcode,
                location.Pii?.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? throw new InvalidOperationException("The Location fixture requires latitude."),
                location.Pii?.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? throw new InvalidOperationException("The Location fixture requires longitude."),
                room.Name,
                room.Description!
            ],
            [
                otherLocation.FullName,
                otherLocation.Address,
                otherLocation.Postcode,
                otherLocation.Pii?.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? throw new InvalidOperationException("The other Location fixture requires latitude."),
                otherLocation.Pii?.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? throw new InvalidOperationException("The other Location fixture requires longitude."),
                otherRoom.Name,
                otherRoom.Description!
            ]);
    }

    private static bool ContainsAny(string body, IEnumerable<string> values)
        => values.Any(value => body.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string[] FindProperties(string json, IEnumerable<string> forbiddenProperties)
    {
        var forbidden = forbiddenProperties.ToHashSet(StringComparer.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(json);
        return EnumerateProperties(document.RootElement)
            .Where(forbidden.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (string nested in EnumerateProperties(property.Value))
                    yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                foreach (string nested in EnumerateProperties(item))
                    yield return nested;
            }
        }
    }

    private sealed record PrivacyScenario(
        Guid TenantId,
        Guid UserId,
        Guid TenantAdminUserId,
        Guid OtherTenantId,
        Guid OtherTenantUserId,
        Guid EventId,
        Guid SessionId,
        Guid SessionGroupId,
        Guid AgendaItemId,
        Guid SessionAgendaItemId,
        Guid UnrelatedEventId,
        Guid UnrelatedSessionId,
        Guid UnrelatedSessionGroupId,
        Guid UnrelatedAgendaItemId,
        Guid LocationId,
        Guid RoomId,
        string LocationCity,
        string LocationCountry,
        string LocationFullName,
        string RoomName,
        Guid UnrelatedLocationId,
        Guid UnrelatedRoomId,
        string UnrelatedLocationFullName,
        string UnrelatedRoomName,
        Guid OtherLocationId,
        Guid OtherRoomId,
        string OtherLocationCity,
        string OtherLocationCountry,
        IReadOnlyList<string> PhysicalSecrets,
        IReadOnlyList<string> PhysicalContentSecrets,
        IReadOnlyList<string> OtherPhysicalContentSecrets);
}

public sealed class EventLocationPrivacyRuntimeFixture : PostgreSqlApiFixtureBase
{
    protected override Dictionary<string, string?> GetAdditionalConfiguration() => new()
    {
        ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
        ["RateLimiting:DisableInTesting"] = "true"
    };

    protected override void ConfigureAdditionalTestServices(IServiceCollection services)
    {
        services.RemoveAll<IAuthorizationProvider>();
        services.AddScoped<IAuthorizationProvider, FallbackAuthorizationService>();
    }
}
