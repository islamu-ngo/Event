using System.Diagnostics;
using System.Net;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Features.Calendar;

[Category("CalendarRuntime")]
[ClassDataSource<CalendarRouteRuntimeFixture>(Shared = SharedType.PerClass)]
[NotInParallel("RealRuntimeDb")]
public sealed class EventCalendarRuntimeTests(CalendarRouteRuntimeFixture fixture)
{
    private const string VenueCanary = "PRIVATE-HOME-CALENDAR-CANARY";
    private const string AddressCanary = "17 Confidential Crescent";
    private const string PostcodeCanary = "SECRET-1040";
    private const string RoomCanary = "FAMILY-ROOM-CANARY";
    private const string LatitudeCanary = "50.84673";
    private const string LongitudeCanary = "4.35247";

    [Test]
    public async Task CalendarRoutes_EnforcePurposeAuthorizationCacheAndRetentionContracts()
    {
        CalendarRouteScenario scenario = await SeedScenarioAsync();
        string publicRoute = $"/api/Event/{scenario.EventId}/calendar";
        string attendeeRoute = $"/api/Event/{scenario.EventId}/calendar/my-access";
        string publicCurl = Unfold(await InvokeCurlAsync(
            new Uri(fixture.Client.BaseAddress!, publicRoute),
            authenticationHeader: null));
        string attendeeCurl = Unfold(await InvokeCurlAsync(
            new Uri(fixture.Client.BaseAddress!, attendeeRoute),
            TestAuthHandler.CreateAuthHeaderValue(scenario.UserId, "Calendar route user")));

        await Assert.That(publicCurl).Contains("HTTP/1.1 200");
        await Assert.That(publicCurl).Contains("X-Calendar-Retention-Warning:");
        await Assert.That(publicCurl).Contains("LOCATION:Private venue");
        await Assert.That(publicCurl).DoesNotContain(AddressCanary);
        await Assert.That(attendeeCurl).Contains("HTTP/1.1 200");
        await Assert.That(attendeeCurl).Contains("Cache-Control: private, no-store");
        await Assert.That(attendeeCurl).Contains(AddressCanary);
        await Assert.That(attendeeCurl).Contains(RoomCanary);

        using HttpResponseMessage publicResponse = await fixture.Client.GetAsync(publicRoute);
        string publicCalendar = Unfold(await publicResponse.Content.ReadAsStringAsync());
        using HttpResponseMessage repeatedPublicResponse = await fixture.Client.GetAsync(publicRoute);
        string repeatedPublicCalendar = Unfold(await repeatedPublicResponse.Content.ReadAsStringAsync());

        await Assert.That(publicResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(publicResponse.Content.Headers.ContentType?.MediaType).IsEqualTo("text/calendar");
        await Assert.That(publicResponse.Content.Headers.ContentDisposition?.FileNameStar
                ?? publicResponse.Content.Headers.ContentDisposition?.FileName)
            .IsEqualTo("calendar-private-home.ics");
        await AssertRetentionWarningAsync(publicResponse);
        await Assert.That(publicCalendar).Contains("BEGIN:VCALENDAR");
        await Assert.That(publicCalendar).Contains("LOCATION:Private venue");
        await Assert.That(publicCalendar).DoesNotContain(VenueCanary);
        await Assert.That(publicCalendar).DoesNotContain(AddressCanary);
        await Assert.That(publicCalendar).DoesNotContain(PostcodeCanary);
        await Assert.That(publicCalendar).DoesNotContain(RoomCanary);
        await Assert.That(publicCalendar).DoesNotContain(LatitudeCanary);
        await Assert.That(publicCalendar).DoesNotContain(LongitudeCanary);
        await Assert.That(repeatedPublicCalendar).IsEqualTo(publicCalendar);

        using HttpResponseMessage anonymousAttendee = await fixture.Client.GetAsync(attendeeRoute);
        await Assert.That(anonymousAttendee.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        using HttpRequestMessage uncoveredRequest = fixture.CreateAuthenticatedRequest(
            attendeeRoute,
            Guid.CreateVersion7());
        using HttpResponseMessage uncovered = await fixture.Client.SendAsync(uncoveredRequest);
        string uncoveredBody = await uncovered.Content.ReadAsStringAsync();
        await Assert.That(uncovered.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(uncovered.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(uncovered.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(uncoveredBody).DoesNotContain(AddressCanary);

        using HttpRequestMessage attendeeRequest = fixture.CreateAuthenticatedRequest(
            attendeeRoute,
            scenario.UserId);
        using HttpResponseMessage attendee = await fixture.Client.SendAsync(attendeeRequest);
        string attendeeCalendar = Unfold(await attendee.Content.ReadAsStringAsync());
        await Assert.That(attendee.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(attendee.Content.Headers.ContentType?.MediaType).IsEqualTo("text/calendar");
        await Assert.That(attendee.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(attendee.Headers.CacheControl?.NoStore).IsTrue();
        await AssertRetentionWarningAsync(attendee);
        await Assert.That(attendeeCalendar).Contains(VenueCanary);
        await Assert.That(attendeeCalendar).Contains(RoomCanary);
        await Assert.That(attendeeCalendar).Contains(AddressCanary);
        await Assert.That(attendeeCalendar).Contains(PostcodeCanary);
    }

    private async Task<CalendarRouteScenario> SeedScenarioAsync()
    {
        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        TenantScenarioSeed.TenantScenarioResult tenant =
            await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        DateTime now = DateTime.UtcNow;

        Explore.Domain.Event @event = new EventBuilder()
            .WithTitle("Calendar Private Home")
            .WithDescription("Runtime calendar privacy contract")
            .WithActorId(tenant.ActorId)
            .WithTenantId(tenant.TenantId)
            .WithStatus(EventStatusEnum.Published)
            .WithVisibility(VisibilityTypeEnum.Public)
            .Build();
        @event.Slug = "calendar-private-home";

        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Tenant = null!,
            FullName = VenueCanary,
            Country = "BE",
            City = "Brussels",
            Timezone = "Europe/Brussels",
            CreatedAt = now.AddDays(-31),
            CreatedBy = tenant.UserId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        location.ClassifyAsPrivateHome(tenant.UserId);
        location.SetProviderAddress(
            AddressCanary,
            PostcodeCanary,
            Explore.Domain.ValueObjects.GeoCoordinate.Create(50.84673, 4.35247));
        var room = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Tenant = null!,
            LocationId = location.Id,
            Location = location,
            Name = RoomCanary,
            SortOrder = 1,
            CreatedAt = now.AddDays(-31),
            CreatedBy = tenant.UserId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

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
                | EventLocationDisclosureFields.RoomName
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
            Title = "Private home session",
            StartTime = new DateTimeOffset(2026, 8, 1, 16, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 8, 1, 17, 0, 0, TimeSpan.Zero),
            RegistrationModeId = (int)RegistrationModeEnum.Open,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        session.AssignEventLocation(placement);
        session.RoomId = room.Id;
        session.Room = room;
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
        context.LocationRooms.Add(room);
        context.Events.Add(@event);
        context.EventTicketCatalogVersions.Add(catalog);
        context.RegistrationOrders.Add(order);
        context.RegistrationParticipants.Add(participant);
        context.EventLocations.Add(placement);
        context.EventLocationDisclosureAudits.AddRange(initialAudit, policyAudit);
        context.EventRegistrations.Add(registration);
        await context.SaveChangesAsync();

        return new(tenant.UserId, @event.Id);
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

    private static async Task AssertRetentionWarningAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.TryGetValues(
                "X-Calendar-Retention-Warning",
                out IEnumerable<string>? values))
            .IsTrue();
        await Assert.That(values!.Single()).Contains("Third-party calendar providers may retain");
    }

    private static string Unfold(string calendar) =>
        calendar.Replace("\r\n ", string.Empty, StringComparison.Ordinal);

    private static async Task<string> InvokeCurlAsync(Uri uri, string? authenticationHeader)
    {
        var startInfo = new ProcessStartInfo("curl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--silent");
        startInfo.ArgumentList.Add("--show-error");
        startInfo.ArgumentList.Add("--include");
        startInfo.ArgumentList.Add("--http1.1");
        if (authenticationHeader is not null)
        {
            startInfo.ArgumentList.Add("--header");
            startInfo.ArgumentList.Add($"X-Test-Auth: {authenticationHeader}");
        }

        startInfo.ArgumentList.Add(uri.AbsoluteUri);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The curl process could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"curl exited with code {process.ExitCode}: {await error}");
        }

        return await output;
    }

    private sealed record CalendarRouteScenario(Guid UserId, Guid EventId);
}

public sealed class CalendarRouteRuntimeFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("calendar_routes")
        .WithUsername("postgres")
        .WithPassword(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
        .Build();

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
                services.RemoveAll<ILocationPrivacyGovernanceService>();
                services.AddSingleton<ILocationPrivacyGovernanceService, CalendarPrivacyGovernance>();
            });
        Factory.UseKestrel(0);
        Client = Factory.CreateClient();
    }

    public HttpRequestMessage CreateAuthenticatedRequest(string route, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add(
            "X-Test-Auth",
            TestAuthHandler.CreateAuthHeaderValue(userId, "Calendar route user"));
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

    private sealed class CalendarPrivacyGovernance : ILocationPrivacyGovernanceService
    {
        public Task<EffectiveLocationPrivacyGovernance> ResolveAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EffectiveLocationPrivacyGovernance(
                IsResolved: tenantId != Guid.Empty,
                LocationPrivacyGovernanceReasonCode.Resolved,
                AllowHomeLocations: true,
                AllowPublicExactAddress: false,
                AllowPublicCoordinates: false,
                MinimumHomeAudience: LocationDisclosureAudienceEnum.ConfirmedParticipant,
                DefaultRevealOffset: TimeSpan.Zero));
        }
    }
}
