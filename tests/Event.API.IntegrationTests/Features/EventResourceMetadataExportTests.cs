using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
        Guid eventId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7(),
            downloadableId = Guid.CreateVersion7(), metadataOnlyId = Guid.CreateVersion7(), userId;
        string ciphertext = Guid.CreateVersion7().ToString("N");
        string providerKey = $"tenants/{PlatformDefaults.DefaultTenantId:N}/{Guid.CreateVersion7():N}.pdf";
        string checksum = Convert.ToHexString(SHA256.HashData("portable file"u8));
        await using (var database = factory.CreateDatabase())
        {
            database.SystemSettings.AddRange(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"", ValueType = SettingValueType.String, Category = "Security"
            }, new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                Value = "true", ValueType = SettingValueType.Boolean, Category = "EventResources"
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
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Portable semantic metadata", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            };
            parent.Publish(now);
            database.Events.Add(parent);
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
            var binding = StorageProviderBinding.Local(Path.GetFullPath("resource-export-test-storage"));
            database.Add(binding);
            foreach (var item in new[]
                     {
                         (Id: downloadableId, Future: false, Name: "downloadable.pdf"),
                         (Id: metadataOnlyId, Future: true, Name: "manager-only.pdf")
                     })
            {
                var storageId = Guid.CreateVersion7();
                var storage = new StorageObject
                {
                    Id = storageId, TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                    FileTypeId = (int)FileTypeEnum.Document, FileType = null!,
                    Uri = $"/api/eventresource/{item.Id:D}/content", ObjectKey = $"{providerKey}-{item.Id:N}",
                    Provider = StorageProviders.Local, StorageProviderBindingId = binding.Id,
                    FullName = item.Name, SafeDisplayName = item.Name,
                    Extension = ".pdf", ContentType = EventResourceGovernancePolicy.PdfMediaType,
                    Size = 13, Sha256Checksum = checksum, Purpose = StorageObjectPurposes.EventResource,
                    Visibility = StorageObjectVisibilities.PrivateOwner,
                    OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = item.Id,
                    LifecycleState = StorageObjectLifecycleStates.Active, CreatedBy = userId
                };
                storage.RecordEventResourceInspection(storageId, checksum);
                database.StorageObjects.Add(storage);
                var stored = EventResource.CreateDraft(item.Id, PlatformDefaults.DefaultTenantId, eventId, null,
                    new EventResourceMetadata
                    {
                        Title = item.Name, PublicTitle = item.Name, Kind = EventResourceKindEnum.GeneralDocument,
                        DisclosureMode = EventResourceDisclosureModeEnum.Public
                    }, EventResourceDeliveryTypeEnum.StoredFile,
                    EventResourceAvailability.Create(absoluteStartUtc: item.Future ? now.AddDays(1) : null),
                    [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, item.Id,
                        EventResourceAudienceKindEnum.Public)],
                    userId, now);
                stored.SetStoredFile(storageId, stored.ConcurrencyStamp, userId, now);
                stored.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                    false, true, null, false, new(null, null, null, null)), true,
                    stored.ConcurrencyStamp, userId, now);
                database.EventResources.Add(stored);
            }
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
            var items = body.RootElement.GetProperty("items").EnumerateArray()
                .ToDictionary(item => item.GetProperty("id").GetGuid());
            var external = items[resourceId];
            await Assert.That(external.GetProperty("title").GetString()).IsEqualTo("Organizer metadata");
            await Assert.That(external.GetProperty("sensitiveNotes").GetString()).IsEqualTo("Authorized private note");
            await Assert.That(external.GetProperty("availability").GetProperty("startAnchor").GetString()).IsEqualTo("EventEnd");
            await Assert.That(external.GetProperty("availability").GetProperty("startOffsetTicks").GetInt64()).IsEqualTo(TimeSpan.FromHours(2).Ticks);
            await Assert.That(external.TryGetProperty("file", out _)
                || external.TryGetProperty("download", out _)).IsFalse();
            var downloadable = items[downloadableId];
            await Assert.That(downloadable.GetProperty("file").GetProperty("fileName").GetString()).IsEqualTo("downloadable.pdf");
            await Assert.That(downloadable.GetProperty("file").GetProperty("contentType").GetString()).IsEqualTo("application/pdf");
            await Assert.That(downloadable.GetProperty("file").GetProperty("sizeBytes").GetInt64()).IsEqualTo(13L);
            await Assert.That(downloadable.GetProperty("file").GetProperty("safetyState").GetString()).IsEqualTo("unscanned");
            await Assert.That(downloadable.GetProperty("download").GetProperty("href").GetString())
                .IsEqualTo($"/api/eventresource/{downloadableId:D}/content");
            var metadataOnly = items[metadataOnlyId];
            await Assert.That(metadataOnly.GetProperty("file").GetProperty("fileName").GetString()).IsEqualTo("manager-only.pdf");
            await Assert.That(metadataOnly.TryGetProperty("download", out _)).IsFalse();
            await Assert.That(items.Values.Any(item => item.TryGetProperty("createdBy", out _)
                || item.TryGetProperty("version", out _) || item.TryGetProperty("audit", out _)
                || item.TryGetProperty("storageObjectId", out _))).IsFalse();
            await Assert.That(json.Contains(providerKey, StringComparison.Ordinal)
                || json.Contains(checksum, StringComparison.Ordinal)).IsFalse();
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
