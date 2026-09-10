
using System.Net;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Hateoas;
using Explore.API.Hateoas;
using Microsoft.AspNetCore.Http;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Event.Api.IntegrationTests.Features.AnonymousRegistrationChallengeHttpTests;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed partial class GuestRegistrationStatusHttpTests
{
    private const string CapabilityHeader = "X-Registration-Order-Capability";

    [Test]
    public async Task MissingCapability_ReturnsPrivateGenericNotFound()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        using HttpResponseMessage response = await host.Client.GetAsync(StatusPath(host.EventId, Guid.CreateVersion7()));
        await AssertPrivateNotFound(response);
    }

    [Test]
    public async Task ConfirmedGuest_StatusSurvivesHoldButDoesNotGrantCheckoutAuthority()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        host.Clock.Advance(TimeSpan.FromHours(1));
        using HttpResponseMessage status = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivate(status);
        using JsonDocument document = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        await Assert.That(document.RootElement.GetProperty("orderId").GetGuid()).IsEqualTo(orderId);
        await Assert.That(document.RootElement.GetProperty("_links").EnumerateObject().Select(link => link.Name).ToArray())
            .IsEquivalentTo(new[] { "self", "calendar", "cancel-registration" });
        await Assert.That(await status.Content.ReadAsStringAsync()).DoesNotContain(capability);

        foreach (string suffix in new[] { "", "/participants", "/requirement-progress", "/payment" })
        {
            using HttpResponseMessage denied = await SendAsync(host.Client, HttpMethod.Get,
                $"/api/events/{host.EventId}/registration-orders/guest/{orderId}{suffix}", capability);
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        using HttpResponseMessage edit = await SendAsync(host.Client, HttpMethod.Put,
            $"/api/events/{host.EventId}/registration-orders/guest/{orderId}/assignments", capability,
            "{\"assignments\":[]}");
        await Assert.That(edit.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UnconfirmedAndMissingEndLaunch_DoNotGrantStatus()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        SolvedProof proof = await host.IssueAsync(host.Key);
        using HttpResponseMessage start = await host.StartAsync(host.Key, proof);
        await Assert.That(start.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using JsonDocument allocation = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        Guid orderId = allocation.RootElement.GetProperty("id").GetGuid();
        string capability = start.Headers.GetValues(CapabilityHeader).Single();
        using HttpResponseMessage status = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await AssertPrivateNotFound(status);
        using HttpResponseMessage cancellation = await CancelAsync(host.Client,
            CancellationPath(host.EventId, orderId), capability);
        await AssertPrivateNotFound(cancellation);
        using HttpResponseMessage checkout = await SendAsync(host.Client, HttpMethod.Get,
            $"/api/events/{host.EventId}/registration-orders/guest/{orderId}", capability);
        await Assert.That(checkout.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument pending = JsonDocument.Parse(await checkout.Content.ReadAsStringAsync());
        await Assert.That(pending.RootElement.GetProperty("_links").TryGetProperty("guest-status", out _)).IsFalse();

        string key = Guid.CreateVersion7().ToString("N");
        SolvedProof missingEndProof = await host.IssueAsync(key);
        await UpdateEventAsync(host, target => target.LastSessionEndUtc = null);
        using HttpResponseMessage missingEnd = await host.StartAsync(key, missingEndProof);
        await Assert.That(missingEnd.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingEnd.Headers.Contains(CapabilityHeader)).IsFalse();
        using HttpResponseMessage challenge = await SendAsync(host.Client, HttpMethod.Post,
            $"/api/events/{host.EventId}/guest-registration-challenges", null, host.Body);
        await AssertPrivateNotFound(challenge);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StatusNeverProjectsParticipantPii_BeforeOrAfterRetention(bool expired)
    {
        const string canary = "STATUS-PARTICIPANT-PII-CANARY";
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            ExploreDbContext database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            RegistrationParticipant participant = RegistrationParticipant.Create(PlatformDefaults.DefaultTenantId,
                orderId, null, ParticipantTypeEnum.Adult, guardian: null);
            participant.SetPii(RegistrationParticipantPii.Create(participant.Id, PlatformDefaults.DefaultTenantId,
                canary, "pii-canary@example.test", "PII-PHONE-CANARY", (int)RegistrationRetentionPolicyEnum.SensitiveShort,
                host.Clock.GetUtcNow().AddDays(expired ? -365 : 0).UtcDateTime));
            database.RegistrationParticipants.Add(participant);
            await database.SaveChangesAsync();
        }
        await ReadDeadlineAsync(host, orderId, capability);
        using HttpResponseMessage response = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        foreach (string excluded in new[] { canary, "pii-canary@example.test", "PII-PHONE-CANARY", capability })
            await Assert.That(body).DoesNotContain(excluded);
    }

    [Test]
    public async Task AuthenticationShortCircuit_StillEmitsPrivateHeaders()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, StatusPath(host.EventId, Guid.CreateVersion7()));
        request.Headers.Add("Authorization", "Bearer " + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        request.Headers.Add("X-Api-Key", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        using HttpResponseMessage response = await host.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertPrivate(response);
    }

    [Test]
    public async Task ScopeAndHeaderOnlyFailures_AreIndistinguishableAndPrivate()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        string path = StatusPath(host.EventId, orderId);
        using HttpResponseMessage missing = await SendAsync(host.Client, HttpMethod.Get, path, null);
        await AssertPrivateNotFound(missing);
        using JsonDocument baseline = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        string title = baseline.RootElement.GetProperty("title").GetString()!;
        foreach ((string route, string? token) in new (string, string?)[]
        {
            (path, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))),
            (StatusPath(Guid.CreateVersion7(), orderId), capability),
            (StatusPath(host.EventId, Guid.CreateVersion7()), capability),
            (path + "?capability=" + Uri.EscapeDataString(capability), null)
        })
        {
            using HttpResponseMessage response = await SendAsync(host.Client, HttpMethod.Get, route, token);
            await AssertPrivateNotFound(response);
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(problem.RootElement.GetProperty("title").GetString()).IsEqualTo(title);
            await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(capability);
        }
        using var cookieRequest = new HttpRequestMessage(HttpMethod.Get, path);
        cookieRequest.Headers.Add("Cookie", $"{CapabilityHeader}={capability}");
        using HttpResponseMessage cookie = await host.Client.SendAsync(cookieRequest);
        await AssertPrivateNotFound(cookie);

        Guid otherTenant = await host.SeedTenantAsync();
        await using var foreignFactory = host.CreateReplica(otherTenant);
        using HttpClient foreignClient = foreignFactory.CreateClient();
        using HttpResponseMessage foreign = await SendAsync(foreignClient, HttpMethod.Get, path, capability);
        await AssertPrivateNotFound(foreign);
    }

    [Test]
    public async Task SchedulePromise_IsFiniteMonotonicAndCannotBeRevivedAfterDeadline()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        DateTimeOffset originalDeadline = await ReadDeadlineAsync(host, orderId, capability);
        await UpdateEventAsync(host, target => target.LastSessionEndUtc = host.Clock.GetUtcNow().AddDays(2));
        await Assert.That(await ReadDeadlineAsync(host, orderId, capability)).IsEqualTo(originalDeadline);
        await UpdateEventAsync(host, target => target.LastSessionEndUtc = null);
        await Assert.That(await ReadDeadlineAsync(host, orderId, capability)).IsEqualTo(originalDeadline);
        await UpdateEventAsync(host, target => target.LastSessionEndUtc = host.Clock.GetUtcNow().AddDays(45));
        DateTimeOffset extended = await ReadDeadlineAsync(host, orderId, capability);
        await Assert.That(extended).IsEqualTo(originalDeadline.AddDays(14));
        host.Clock.Advance(extended - host.Clock.GetUtcNow());
        using HttpResponseMessage expired = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await AssertPrivateNotFound(expired);
        await UpdateEventAsync(host, target => target.LastSessionEndUtc = host.Clock.GetUtcNow().AddDays(90));
        using HttpResponseMessage notRevived = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await AssertPrivateNotFound(notRevived);
    }

    [Test]
    [Arguments("cancelled")]
    [Arguments("nonpublic")]
    [Arguments("no-session")]
    public async Task PublicCalendarAbsence_DoesNotInvalidatePrivateStatus(string reason)
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        await UpdateEventAsync(host, target =>
        {
            if (reason == "cancelled") target.Cancel(host.Clock.GetUtcNow().UtcDateTime);
            if (reason == "nonpublic") target.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
        });
        if (reason == "no-session")
        {
            await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
            ExploreDbContext database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            EventSession session = await database.EventSessions.SingleAsync(row => row.EventId == host.EventId);
            session.StartTime = null;
            await database.SaveChangesAsync();
        }
        using HttpResponseMessage status = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        await Assert.That(document.RootElement.GetProperty("_links").EnumerateObject().Select(link => link.Name).ToArray())
            .IsEquivalentTo(new[] { "self", "cancel-registration" });
        if (reason == "cancelled")
        {
            await Assert.That(document.RootElement.GetProperty("eventStatusId").GetInt32()).IsEqualTo((int)EventStatusEnum.Cancelled);
            await Assert.That(document.RootElement.GetProperty("registrationOrderStatusId").GetInt32()).IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
            await Assert.That(document.RootElement.TryGetProperty("cancelledAt", out _)).IsFalse();
        }
        using HttpResponseMessage calendar = await host.Client.GetAsync($"/api/Event/{host.EventId}/calendar");
        await Assert.That(calendar.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PublicCalendar_ParsesStablePublicMetadataAndRedactsPrivateHome()
    {
        const string canary = "PRIVATE-STATUS-HOME-CANARY";
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            ExploreDbContext database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            Guid operatorId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
            var location = new Location
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                FullName = canary,
                City = "Brussels",
                Country = "BE",
                Timezone = "Europe/Brussels",
                CreatedAt = host.Clock.GetUtcNow().UtcDateTime,
                CreatedBy = operatorId,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            location.ClassifyAsPrivateHome(operatorId);
            location.SetProviderAddress(canary + " STREET", "PRIVATE-POSTCODE",
                Explore.Domain.ValueObjects.GeoCoordinate.Create(50.84673, 4.35247));
            EventLocation placement = EventLocation.CreatePhysical(PlatformDefaults.DefaultTenantId,
                host.EventId, location.Id, operatorId, host.Clock.GetUtcNow().UtcDateTime);
            EventSession session = await database.EventSessions.SingleAsync(row => row.EventId == host.EventId);
            session.AssignEventLocation(placement);
            database.AddRange(location, placement, placement.CreateInitialDisclosureAudit());
            await database.SaveChangesAsync();
        }
        using HttpResponseMessage status = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument snapshot = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        string href = snapshot.RootElement.GetProperty("_links").GetProperty("calendar").GetProperty("href").GetString()!;
        await Assert.That(new Uri(href).AbsolutePath).IsEqualTo($"/api/event/{host.EventId}/calendar");
        await Assert.That(new Uri(href).Query).IsEqualTo(string.Empty);
        using HttpResponseMessage response = await host.Client.GetAsync(href);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.RequestMessage!.Headers.Contains(CapabilityHeader)).IsFalse();
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/calendar");
        string text = await response.Content.ReadAsStringAsync();
        var calendar = Ical.Net.Calendar.Load(text)!;
        await Assert.That(calendar.Events.Count).IsEqualTo(1);
        var entry = calendar.Events.Single();
        await Assert.That(entry.Uid).IsEqualTo(host.EventId.ToString("D"));
        await Assert.That(entry.Url!.Query).IsEqualTo(string.Empty);
        await Assert.That(entry.Url.Fragment).IsEqualTo(string.Empty);
        foreach (string excluded in new[] { capability, canary, "PRIVATE-POSTCODE", "50.84673", "4.35247", orderId.ToString("D") })
            await Assert.That(text.Replace("\r\n ", string.Empty, StringComparison.Ordinal)).DoesNotContain(excluded);
        using HttpResponseMessage repeated = await host.Client.GetAsync(href);
        await Assert.That(await repeated.Content.ReadAsStringAsync()).IsEqualTo(text);
        using HttpResponseMessage privateCalendar = await SendAsync(host.Client, HttpMethod.Get,
            $"/api/Event/{host.EventId}/calendar/my-access", capability);
        await Assert.That(privateCalendar.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task NativeEventAssembler_RemovesImpossibleGuestLaunchFromCachedProjection()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var target = await scope.ServiceProvider.GetRequiredService<IEventRepository>()
            .GetEventWithDetails(host.EventId);
        EventDto cached = scope.ServiceProvider.GetRequiredService<AutoMapper.IMapper>().Map<EventDto>(target) with
        {
            IsPubliclyEligible = await scope.ServiceProvider.GetRequiredService<IEventRepository>()
                .IsPubliclyEligibleAsync(PlatformDefaults.DefaultTenantId, host.EventId, CancellationToken.None)
        };
        var assembler = scope.ServiceProvider.GetRequiredService<IResourceAssembler<EventDto, EventListDto>>();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = host.Client.BaseAddress!.Scheme;
        context.Request.Host = new HostString(host.Client.BaseAddress.Authority);
        HalResource<EventDto> available = await assembler.ToResource(cached, context);
        await Assert.That(available.Links.ContainsKey(LinkRelations.StartGuestRegistration)).IsTrue();
        foreach (DateTimeOffset? end in new DateTimeOffset?[]
        {
            null, host.Clock.GetUtcNow().AddDays(-31), DateTimeOffset.MaxValue
        })
        {
            await UpdateEventAsync(host, entity => entity.LastSessionEndUtc = end);
            HalResource<EventDto> unavailable = await assembler.ToResource(cached, context);
            await Assert.That(unavailable.Links.ContainsKey(LinkRelations.StartGuestRegistration)).IsFalse();
        }
    }

    [Test]
    public async Task CalendarAwaitCrossesStatusDeadline_ReturnsPrivateNotFoundInsteadOfAuthorizedSnapshot()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        DateTimeOffset deadline = await ReadDeadlineAsync(host, orderId, capability);
        await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
        string sessionTable = scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .Model.FindEntityType(typeof(EventSession))!.GetTableName()!;
        var boundary = new CalendarDeadlineBoundary(host.Clock, deadline, sessionTable);
        await using var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary))));
        using HttpClient client = factory.CreateClient();
        boundary.Armed = true;
        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await Assert.That(boundary.Triggered).IsTrue();
        await AssertPrivateNotFound(response);
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("statusAccessUntil");
        await Assert.That(body).DoesNotContain(capability);
    }

    private sealed class CalendarDeadlineBoundary(TestClock clock, DateTimeOffset deadline, string sessionTable) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && !Triggered && command.CommandText.Contains('"' + sessionTable + '"', StringComparison.Ordinal))
            {
                Triggered = true;
                clock.Advance(deadline - clock.GetUtcNow());
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<DateTimeOffset> ReadDeadlineAsync(NativeHost host, Guid orderId, string capability)
    {
        using HttpResponseMessage status = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivate(status);
        using JsonDocument document = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        string[] allowed = ["eventId", "orderId", "eventStatusId", "registrationOrderStatusId", "confirmedAt", "cancelledAt", "lastSessionEndUtc", "statusAccessUntil", "_links"];
        await Assert.That(document.RootElement.EnumerateObject().All(property => allowed.Contains(property.Name))).IsTrue();
        return document.RootElement.GetProperty("statusAccessUntil").GetDateTimeOffset();
    }

    private static async Task UpdateEventAsync(NativeHost host, Action<Explore.Domain.Event> update)
    {
        await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
        ExploreDbContext database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Explore.Domain.Event target = await database.Events.SingleAsync(row => row.Id == host.EventId);
        update(target);
        await database.SaveChangesAsync();
    }

    private static async Task<(Guid OrderId, string Capability)> ConfirmAsync(NativeHost host)
    {
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            ExploreDbContext database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            database.EventSessions.Add(new EventSession(EventSessionStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                EventId = host.EventId,
                Event = null!,
                Title = "Public session",
                StartTime = host.Clock.GetUtcNow().AddDays(30),
                EndTime = host.Clock.GetUtcNow().AddDays(31),
                RegistrationModeId = (int)RegistrationModeEnum.Open,
                EventSessionKindId = (int)EventSessionKindEnum.Talk,
                CreatedAt = host.Clock.GetUtcNow().UtcDateTime,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            await database.SaveChangesAsync();
        }
        SolvedProof proof = await host.IssueAsync(host.Key);
        using HttpResponseMessage start = await host.StartAsync(host.Key, proof);
        await Assert.That(start.StatusCode).IsEqualTo(HttpStatusCode.Created);
        string capability = start.Headers.GetValues(CapabilityHeader).Single();
        using JsonDocument allocation = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        Guid id = allocation.RootElement.GetProperty("id").GetGuid();
        foreach (string action in new[] { "continue", "finalize" })
        {
            if (action == "finalize")
            {
                // Native requirement completion normally performs this transition; this event has no forms.
                await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
                var ready = await scope.ServiceProvider.GetRequiredService<IRegistrationOrderLifecycleService>()
                    .ReadyForCheckoutAsync(id, PlatformDefaults.DefaultTenantId, CancellationToken.None);
                await Assert.That(ready.IsSuccess).IsTrue();
            }
            using HttpResponseMessage response = await SendAsync(host.Client, HttpMethod.Post,
                $"/api/events/{host.EventId}/registration-orders/guest/{id}/{action}", capability, "{}");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await response.Content.ReadAsStringAsync());
            using JsonDocument lifecycle = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (action == "finalize")
            {
                await Assert.That(lifecycle.RootElement.GetProperty("order").GetProperty("confirmedAt").ValueKind)
                    .IsEqualTo(JsonValueKind.String);
                string href = lifecycle.RootElement.GetProperty("_links").GetProperty("guest-status").GetProperty("href").GetString()!;
                await Assert.That(new Uri(href).AbsolutePath).IsEqualTo(StatusPath(host.EventId, id));
                await Assert.That(href).DoesNotContain(capability);
            }
        }
        return (id, capability);
    }

    private static string StatusPath(Guid eventId, Guid orderId) =>
        $"/api/events/{eventId}/guest-registration-orders/{orderId}/status";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method,
        string path, string? capability, string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (capability is not null) request.Headers.Add(CapabilityHeader, capability);
        if (method != HttpMethod.Get) request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task AssertPrivateNotFound(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await AssertPrivate(response);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("status").GetInt32()).IsEqualTo(404);
        await Assert.That(body.RootElement.TryGetProperty("_links", out _)).IsFalse();
    }

    private static async Task AssertPrivate(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
    }
}
