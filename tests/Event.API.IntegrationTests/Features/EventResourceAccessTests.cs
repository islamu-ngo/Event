using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Models;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EventResourceAccessTests
{
    [Test]
    [Arguments("allow")]
    [Arguments("tamper")]
    [Arguments("scope")]
    [Arguments("tenant")]
    [Arguments("version")]
    [Arguments("withdraw")]
    [Arguments("policy")]
    [Arguments("cancel-parent-before-final-read")]
    [Arguments("staff-expiry-before-final-read")]
    [Arguments("window-boundaries")]
    public async Task RedirectOnlyRevealsAuthorizedProtectedDestination(string change)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var clock = new AccessClock(DateTimeOffset.UtcNow);
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid eventId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7();
        string sentinel = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string destination = $"https://resource.example.org/visit/{sentinel}?ticket={sentinel}";
        Guid userId;
        await using (var db = factory.CreateDatabase())
        {
            var user = await db.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier);
            userId = user.Id;
            var actor = await db.Actors.SingleAsync(row => row.UserId == userId);
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = userId, User = user, ActorId = actor.Id,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
            });
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"", ValueType = SettingValueType.String, Category = "Security"
            });
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.ExternalOrigins,
                Value = "[\"https://resource.example.org\"]", ValueType = SettingValueType.Json,
                Category = "EventResources"
            });
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Resource destination", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            };
            parent.Publish(DateTime.UtcNow);
            db.Events.Add(parent);
            db.EventRoleAssignments.Add(EventRoleAssignment.Create(PlatformDefaults.DefaultTenantId, eventId, userId,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddMinutes(-1),
                change == "staff-expiry-before-final-read" ? clock.GetUtcNow().AddSeconds(5).UtcDateTime : null, userId));
            var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata { Title = "Public destination", PublicTitle = "Visit",
                    Kind = EventResourceKindEnum.GeneralDocument, DisclosureMode = EventResourceDisclosureModeEnum.Public },
                EventResourceDeliveryTypeEnum.ExternalLink,
                change == "window-boundaries"
                    ? EventResourceAvailability.Create(absoluteStartUtc: clock.GetUtcNow().AddMinutes(1),
                        absoluteEndUtc: clock.GetUtcNow().AddMinutes(2))
                    : EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                    change == "staff-expiry-before-final-read"
                        ? EventResourceAudienceKindEnum.EventStaff : EventResourceAudienceKindEnum.Public)],
                userId, DateTime.UtcNow);
            db.EventResources.Add(resource);
            await db.SaveChangesAsync();
        }
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<MvcOptions>(options =>
                options.Filters.Add(new BeforeRedirect(change, factory, eventId, resourceId, userId, clock)));
            if (change is "staff-expiry-before-final-read" or "window-boundaries")
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            }
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials))
        {
            login.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                json.RootElement.GetProperty("token").GetString());
        }
        Guid version;
        await using (var db = factory.CreateDatabase())
        {
            db.EnableTenantFilterBypass("Read exact draft version for management request.");
            version = (await db.EventResources.SingleAsync(row => row.Id == resourceId)).ConcurrencyStamp;
        }
        if (change == "allow")
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", $"invalid-destination-{resourceId:N}");
            using var rejected = await client.PutAsJsonAsync($"/api/eventresource/{resourceId}/destination",
                new { expectedVersion = version,
                    destination = $"https://reader:{sentinel}@resource.example.org/visit" });
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(await rejected.Content.ReadAsStringAsync()).DoesNotContain(sentinel);
            await Assert.That(rejected.Headers.Location).IsNull();
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
        }
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"destination-{resourceId:N}");
        using (var configured = await client.PutAsJsonAsync($"/api/eventresource/{resourceId}/destination",
                   new { expectedVersion = version, destination }))
        {
            await Assert.That(configured.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await configured.Content.ReadAsStringAsync());
            await Assert.That(await configured.Content.ReadAsStringAsync()).DoesNotContain(sentinel);
        }
        await using (var db = factory.CreateDatabase())
        {
            db.EnableTenantFilterBypass("Read exact updated version for publication.");
            version = (await db.EventResources.SingleAsync(row => row.Id == resourceId)).ConcurrencyStamp;
        }
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"publish-{resourceId:N}");
        using (var published = await client.PostAsJsonAsync($"/api/eventresource/{resourceId}/publish",
                   new { expectedVersion = version }))
            await Assert.That(published.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await published.Content.ReadAsStringAsync());
        long managementAuditRows;
        await using (var audit = factory.CreateDatabase())
        {
            audit.EnableTenantFilterBypass("Count management evidence before any attendee redirect read.");
            managementAuditRows = await audit.EventResourceAuditEntries.CountAsync(row => row.EventResourceId == resourceId);
            await Assert.That(managementAuditRows).IsGreaterThan(0);
        }
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        if (change != "staff-expiry-before-final-read")
            client.DefaultRequestHeaders.Authorization = null;
        if (change is "scope" or "tenant" or "tamper" or "version")
        {
            await using var db = factory.CreateDatabase();
            db.EnableTenantFilterBypass("Test exact protected payload scope and ciphertext corruption.");
            var resource = await db.EventResources.SingleAsync(row => row.Id == resourceId);
            using var scope = hosted.Services.CreateScope();
            var protector = scope.ServiceProvider.GetRequiredService<IEventResourceDestinationProtector>();
            string ciphertext = change == "tamper" ? "invalid-envelope" : protector.Protect(destination,
                change == "tenant" ? Guid.CreateVersion7() : PlatformDefaults.DefaultTenantId,
                change == "scope" ? Guid.CreateVersion7() : resourceId, protector.CurrentVersion);
            await db.EventResources.Where(row => row.Id == resourceId).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.ExternalDestinationCiphertext, ciphertext)
                .SetProperty(row => row.ExternalDestinationProtectionVersion,
                    protector.CurrentVersion + (change == "version" ? 1 : 0)));
        }
        using (var scope = hosted.Services.CreateScope())
        {
            var policy = await scope.ServiceProvider.GetRequiredService<IEventResourceGovernancePolicyReader>()
                .ReadAsync(PlatformDefaults.DefaultTenantId, CancellationToken.None);
            await Assert.That(policy).IsNotNull();
            await Assert.That(policy!.AllowsExternalOrigin("https://resource.example.org")).IsTrue();
            var reader = scope.ServiceProvider.GetRequiredService<IEventResourceAuthoritySnapshotReader>();
            var facts = await reader.ReadAsync(new(PlatformDefaults.DefaultTenantId, resourceId, null, false, "access"),
                DateTimeOffset.UtcNow, CancellationToken.None);
            await Assert.That(facts).IsNotNull();
            await Assert.That(facts!.Access.Parent.EventEligible).IsTrue();
            await Assert.That(facts.Access.PayloadSafetySatisfied).IsTrue();
        }
        using var metadata = await client.GetAsync($"/api/eventresource/{resourceId}");
        await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string metadataBody = await metadata.Content.ReadAsStringAsync();
        await Assert.That(metadataBody).DoesNotContain(sentinel);
        if (change == "staff-expiry-before-final-read")
        {
            using var document = JsonDocument.Parse(metadataBody);
            await Assert.That(document.RootElement.GetProperty("_links").TryGetProperty("access", out _)).IsTrue()
                .Because("the authenticated staff member must have a valid redirect action before expiry");
        }
        string? savedAction = null;
        if (change == "allow")
        {
            using var document = JsonDocument.Parse(metadataBody);
            await Assert.That(document.RootElement.GetProperty("externalDestinationSafeOrigin").GetString())
                .IsEqualTo("https://resource.example.org");
            savedAction = document.RootElement.GetProperty("_links").GetProperty("access")
                .GetProperty("href").GetString()!;
            await Assert.That(savedAction).DoesNotContain(sentinel);
            await Assert.That(savedAction).Contains($"/api/eventresource/{resourceId}/access");
        }
        using var response = await client.GetAsync($"/api/eventresource/{resourceId}/access");
        string body = await response.Content.ReadAsStringAsync();
        if (change == "allow")
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(response.Headers.Location!.AbsoluteUri).IsEqualTo(destination);
            await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
        }
        else
        {
            await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Redirect);
            await Assert.That(response.Headers.Location).IsNull();
        }
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(body).DoesNotContain(sentinel);
        if (change == "window-boundaries")
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            using (var start = await client.GetAsync($"/api/eventresource/{resourceId}/access"))
            {
                await Assert.That(start.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
                await Assert.That(start.Headers.Location!.AbsoluteUri).IsEqualTo(destination);
            }
            clock.Advance(TimeSpan.FromMinutes(1));
            using (var end = await client.GetAsync($"/api/eventresource/{resourceId}/access"))
            {
                await Assert.That(end.IsSuccessStatusCode).IsFalse();
                await Assert.That(end.Headers.Location).IsNull();
                await Assert.That((await end.Content.ReadAsStringAsync()).Contains(sentinel, StringComparison.Ordinal))
                    .IsFalse();
            }
        }
        if (change is not ("withdraw" or "policy" or "cancel-parent-before-final-read"))
        {
            await using var audit = factory.CreateDatabase();
            audit.EnableTenantFilterBypass("Attendee metadata and redirect reads never create browsing history.");
            long afterReads = await audit.EventResourceAuditEntries.CountAsync(row => row.EventResourceId == resourceId);
            await Assert.That(afterReads).IsEqualTo(managementAuditRows)
                .Because($"Management audit rows changed during an attendee read: before={managementAuditRows}, after={afterReads}.");
        }
        if (savedAction is not null)
        {
            await using (var db = factory.CreateDatabase())
            {
                db.EnableTenantFilterBypass("Withdraw the published destination after preserving its HAL action.");
                var resource = await db.EventResources.SingleAsync(row => row.Id == resourceId);
                resource.Withdraw(resource.ConcurrencyStamp, userId, DateTime.UtcNow);
                await db.SaveChangesAsync();
            }
            await AssertOldActionDeniedAsync();
            await using (var db = factory.CreateDatabase())
            {
                db.EnableTenantFilterBypass("Republish the same destination with current parent authority.");
                var resource = await db.EventResources.Include(row => row.AudienceRules)
                    .SingleAsync(row => row.Id == resourceId);
                resource.Republish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                    false, true, null, false, new(null, null, null, null)), true,
                    resource.ConcurrencyStamp, userId, DateTime.UtcNow);
                await db.SaveChangesAsync();
            }
            using (var restored = await client.GetAsync(savedAction))
            {
                await Assert.That(restored.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
                await Assert.That(restored.Headers.Location!.AbsoluteUri).IsEqualTo(destination);
            }
            await using (var db = factory.CreateDatabase())
            {
                db.EnableTenantFilterBypass("Archive the withdrawn destination after confirming republish.");
                var resource = await db.EventResources.SingleAsync(row => row.Id == resourceId);
                resource.Withdraw(resource.ConcurrencyStamp, userId, DateTime.UtcNow);
                resource.Archive(resource.ConcurrencyStamp, userId, DateTime.UtcNow);
                await db.SaveChangesAsync();
            }
            await AssertOldActionDeniedAsync();
            await using (var db = factory.CreateDatabase())
            {
                db.EnableTenantFilterBypass("Delete the archived resource and its protected destination.");
                var resource = await db.EventResources.SingleAsync(row => row.Id == resourceId);
                resource.Delete(resource.ConcurrencyStamp, userId, DateTime.UtcNow);
                await db.SaveChangesAsync();
            }
            await AssertOldActionDeniedAsync();

            async Task AssertOldActionDeniedAsync()
            {
                using var denied = await client.GetAsync(savedAction);
                await Assert.That(denied.IsSuccessStatusCode).IsFalse();
                await Assert.That(denied.Headers.Location).IsNull();
                await Assert.That((await denied.Content.ReadAsStringAsync()).Contains(sentinel, StringComparison.Ordinal))
                    .IsFalse();
            }
        }
    }

    private sealed class BeforeRedirect(string change, LocalAdmissionWebApplicationFactory factory, Guid eventId,
        Guid resourceId, Guid userId, AccessClock clock)
        : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is EventResourceRedirectResult && change is "withdraw" or "policy" or "cancel-parent-before-final-read")
            {
                await using var db = factory.CreateDatabase();
                db.EnableTenantFilterBypass("Change exact resource authority before redirect headers.");
                if (change == "withdraw")
                {
                    var resource = await db.EventResources.SingleAsync(row => row.Id == resourceId);
                    resource.Withdraw(resource.ConcurrencyStamp, userId, DateTime.UtcNow);
                }
                else if (change == "cancel-parent-before-final-read")
                {
                    var parent = await db.Events.SingleAsync(row => row.Id == eventId);
                    await Assert.That(parent.Cancel(DateTime.UtcNow)).IsTrue();
                }
                else
                {
                    var setting = await db.SystemSettings.SingleAsync(row =>
                        row.SettingKey == GovernanceSettingKeys.EventResources.ExternalOrigins);
                    setting.Value = "[]";
                }
                await db.SaveChangesAsync();
            }
            if (context.Result is EventResourceRedirectResult && change == "staff-expiry-before-final-read")
                clock.Advance(TimeSpan.FromSeconds(10));
            await next();
        }
    }

    private sealed class AccessClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
}
