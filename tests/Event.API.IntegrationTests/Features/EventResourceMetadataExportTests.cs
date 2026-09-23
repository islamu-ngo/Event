using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EventResourceMetadataExportTests
{
    private static CancellationToken Token => TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;

    [Test]
    public async Task ExportIsPrivateBoundedSemanticDataAndCannotSurviveRevocation()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid eventId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7(), userId;
        string ciphertext = Guid.CreateVersion7().ToString("N");
        await using (var database = factory.CreateDatabase())
        {
            database.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"", ValueType = SettingValueType.String, Category = "Security"
            });
            var user = await database.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier, Token);
            userId = user.Id;
            var actor = await database.Actors.SingleAsync(row => row.UserId == userId, Token);
            var now = DateTime.UtcNow;
            database.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = userId, User = user, ActorId = actor.Id,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = now
            });
            database.Events.Add(new Explore.Domain.Event
            {
                Id = eventId, Title = "Portable semantic metadata", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            });
            var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata
                {
                    Title = "Organizer metadata", SensitiveNotes = "Authorized private note",
                    Kind = EventResourceKindEnum.GeneralDocument, DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                }, EventResourceDeliveryTypeEnum.ExternalLink,
                EventResourceAvailability.Create(startAnchor: EventResourceAvailabilityAnchorEnum.EventEnd, startOffset: TimeSpan.FromHours(2)),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId, EventResourceAudienceKindEnum.Public)], userId, now);
            resource.SetExternalDestination(ciphertext, 1, "https://not-exported.example.test", resource.ConcurrencyStamp, userId, now);
            database.EventResources.Add(resource);
            await database.SaveChangesAsync(Token);
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token))
        {
            login.EnsureSuccessStatusCode();
            using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        }
        string path = $"/api/event/{eventId:D}/resources/export";
        using (var exported = await client.GetAsync(path, Token))
        {
            await Assert.That(exported.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await exported.Content.ReadAsStringAsync(Token));
            await Assert.That(exported.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(exported.Headers.CacheControl?.NoStore).IsTrue();
            var json = await exported.Content.ReadAsStringAsync(Token);
            await Assert.That(json.Contains(ciphertext, StringComparison.Ordinal)
                || json.Contains("not-exported.example.test", StringComparison.Ordinal)).IsFalse();
            using var body = JsonDocument.Parse(json);
            await Assert.That(body.RootElement.TryGetProperty("totalCount", out _)).IsFalse();
            var item = body.RootElement.GetProperty("items")[0];
            await Assert.That(item.GetProperty("title").GetString()).IsEqualTo("Organizer metadata");
            await Assert.That(item.GetProperty("sensitiveNotes").GetString()).IsEqualTo("Authorized private note");
            await Assert.That(item.GetProperty("availability").GetProperty("startAnchor").GetString()).IsEqualTo("EventEnd");
            await Assert.That(item.GetProperty("availability").GetProperty("startOffsetTicks").GetInt64()).IsEqualTo(TimeSpan.FromHours(2).Ticks);
            await Assert.That(item.TryGetProperty("createdBy", out _) || item.TryGetProperty("version", out _)
                || item.TryGetProperty("audit", out _) || item.TryGetProperty("storageObjectId", out _)).IsFalse();
        }
        using (var invalid = await client.GetAsync(path + "?pageSize=101", Token))
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var empty = await client.GetAsync(path + "?page=2", Token))
        {
            await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = await JsonDocument.ParseAsync(await empty.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(body.RootElement.GetProperty("items").GetArrayLength()).IsEqualTo(0);
            await Assert.That(body.RootElement.TryGetProperty("totalCount", out _)).IsFalse();
        }
        await using (var revoke = factory.CreateDatabase())
        {
            revoke.EnableTenantFilterBypass("Export test revokes one exact tenant subject.");
            await revoke.TenantUsers.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.StatusId, (int)TenantUserStatusEnum.Suspended), Token);
        }
        client.DefaultRequestHeaders.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        using var denied = await client.GetAsync(path, Token);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound).Because(await denied.Content.ReadAsStringAsync(Token));
        await Assert.That(denied.Headers.CacheControl?.NoStore).IsTrue();
    }
}
