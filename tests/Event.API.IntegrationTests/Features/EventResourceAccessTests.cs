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
    public async Task RedirectOnlyRevealsAuthorizedProtectedDestination(string change)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
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
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddMinutes(-1), null, userId));
            var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata { Title = "Public destination", PublicTitle = "Visit",
                    Kind = EventResourceKindEnum.GeneralDocument, DisclosureMode = EventResourceDisclosureModeEnum.Public },
                EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                    EventResourceAudienceKindEnum.Public)], userId, DateTime.UtcNow);
            db.EventResources.Add(resource);
            await db.SaveChangesAsync();
        }
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<MvcOptions>(options => options.Filters.Add(new BeforeRedirect(change, factory, resourceId, userId)))));
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
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
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
        if (change == "allow")
        {
            using var document = JsonDocument.Parse(metadataBody);
            await Assert.That(document.RootElement.GetProperty("externalDestinationSafeOrigin").GetString())
                .IsEqualTo("https://resource.example.org");
            string href = document.RootElement.GetProperty("_links").GetProperty("access")
                .GetProperty("href").GetString()!;
            await Assert.That(href).DoesNotContain(sentinel);
            await Assert.That(href).Contains($"/api/eventresource/{resourceId}/access");
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
    }

    private sealed class BeforeRedirect(string change, LocalAdmissionWebApplicationFactory factory, Guid resourceId, Guid userId)
        : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is EventResourceRedirectResult && change is "withdraw" or "policy")
            {
                await using var db = factory.CreateDatabase();
                db.EnableTenantFilterBypass("Change exact resource authority before redirect headers.");
                if (change == "withdraw")
                {
                    var resource = await db.EventResources.SingleAsync(row => row.Id == resourceId);
                    resource.Withdraw(resource.ConcurrencyStamp, userId, DateTime.UtcNow);
                }
                else
                {
                    var setting = await db.SystemSettings.SingleAsync(row =>
                        row.SettingKey == GovernanceSettingKeys.EventResources.ExternalOrigins);
                    setting.Value = "[]";
                }
                await db.SaveChangesAsync();
            }
            await next();
        }
    }
}
