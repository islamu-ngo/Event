using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Standalone.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Standalone.IntegrationTests;

public sealed partial class CombinedEventResourceTransportTests
{
    [Test]
    public async Task OfflineSnapshotRestoresSqliteLocalBytesAndDestinationKeysForFreshCombinedHost()
    {
        string temporaryDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "agent-tmp");
        Directory.CreateDirectory(temporaryDirectory);
        using var deployment = new NativeEmailOptionalStandaloneFixture(temporaryDirectory);
        string root = Path.GetDirectoryName(deployment.DatabasePath)!;
        string storageRoot = Path.Combine(root, "storage");
        Guid fileId = Guid.CreateVersion7(), linkId = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        byte[] bytes = "%PDF-1.7\ncoordinated restore\n%%EOF"u8.ToArray();
        string destination = $"https://files.example.org/visit?ticket={Guid.CreateVersion7():N}";
        string ciphertext;
        string password = NativeEmailOptionalStandaloneFixture.NewPassword();

        await using (var first = deployment.CreateHost())
        {
            using var client = first.OpenClient();
            using (var login = await client.PostAsJsonAsync("/api/auth/local/login",
                new { identifier = deployment.Subject.ToString("D"), password = deployment.InitialPassword }))
            {
                RequireSuccess(login, "initial local login");
                using var challenge = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                    challenge.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString());
            }
            using (var replace = await client.PostAsJsonAsync("/api/auth/local/credential-replacement",
                new { newPassword = password })) RequireSuccess(replace, "local credential replacement");
            client.DefaultRequestHeaders.Authorization = null;
            using (var login = await client.PostAsJsonAsync("/api/auth/local/login",
                new { identifier = deployment.Subject.ToString("D"), password }))
            {
                RequireSuccess(login, "post-replacement local login");
                using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                    body.RootElement.GetProperty("token").GetString());
            }
            await ActivateResourceDirectoryAsync(client);
            await using var scope = first.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EnableTenantFilterBypass("Seed two durable resources for an offline Standalone restore.");
            var user = await db.Users.SingleAsync(row => row.Id == deployment.Subject);
            var actor = await db.Actors.SingleAsync(row => row.UserId == user.Id);
            if (!await db.TenantUsers.AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == user.Id))
                db.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                    UserId = user.Id, User = user, ActorId = actor.Id,
                    StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
                });
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                Value = "true", ValueType = SettingValueType.Boolean, Category = "EventResources"
            });
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.ExternalOrigins,
                Value = "[\"https://files.example.org\"]", ValueType = SettingValueType.Json,
                Category = "EventResources"
            });
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Restored materials", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            };
            parent.Publish(DateTime.UtcNow);
            db.Events.Add(parent);
            var file = EventResource.CreateDraft(fileId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata { Title = "Restored file", PublicTitle = "Handout",
                    Kind = EventResourceKindEnum.GeneralDocument, DisclosureMode = EventResourceDisclosureModeEnum.Public },
                EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, fileId,
                    EventResourceAudienceKindEnum.Public)], user.Id, DateTime.UtcNow);
            var provider = scope.ServiceProvider.GetRequiredService<IFileStorageProviderResolver>()
                .GetRequired(StorageProviders.Local);
            await using var content = new MemoryStream(bytes);
            var stored = await provider.WriteAsync(new FileStorageWriteInput(PlatformDefaults.DefaultTenantId,
                content, "application/pdf", "handout.pdf", ".pdf", bytes.Length, bytes.Length), CancellationToken.None);
            var binding = StorageProviderBinding.Local(storageRoot);
            var storage = new StorageObject
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                FileTypeId = (int)FileTypeEnum.Document, FileType = null!,
                Uri = $"/api/eventresource/{fileId}/content", ObjectKey = stored.ObjectKey,
                Provider = StorageProviders.Local, StorageProviderBindingId = binding.Id,
                FullName = "handout.pdf", SafeDisplayName = "handout.pdf", Extension = "pdf",
                ContentType = "application/pdf", Size = bytes.Length,
                Sha256Checksum = Convert.ToHexString(SHA256.HashData(bytes)),
                Purpose = StorageObjectPurposes.EventResource, Visibility = StorageObjectVisibilities.PrivateOwner,
                OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = fileId,
                LifecycleState = StorageObjectLifecycleStates.Active, CreatedBy = user.Id
            };
            storage.RecordEventResourceInspection(storage.Id, storage.Sha256Checksum);
            db.AddRange(binding, storage);
            file.SetStoredFile(storage.Id, file.ConcurrencyStamp, user.Id, DateTime.UtcNow);
            file.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                false, true, null, false, new(null, null, null, null)), true,
                file.ConcurrencyStamp, user.Id, DateTime.UtcNow);
            db.EventResources.Add(file);
            var link = EventResource.CreateDraft(linkId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata { Title = "Restored link", PublicTitle = "Visit",
                    Kind = EventResourceKindEnum.GeneralDocument, DisclosureMode = EventResourceDisclosureModeEnum.Public },
                EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, linkId,
                    EventResourceAudienceKindEnum.Public)], user.Id, DateTime.UtcNow);
            var protector = scope.ServiceProvider.GetRequiredService<IEventResourceDestinationProtector>();
            ciphertext = protector.Protect(destination, PlatformDefaults.DefaultTenantId, linkId, protector.CurrentVersion);
            link.SetExternalDestination(ciphertext, protector.CurrentVersion, "https://files.example.org",
                link.ConcurrencyStamp, user.Id, DateTime.UtcNow);
            link.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                false, true, null, false, new(null, null, null, null)), true,
                link.ConcurrencyStamp, user.Id, DateTime.UtcNow);
            db.EventResources.Add(link);
            await db.SaveChangesAsync();
            await Assert.That(await scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>()
                .DataProtectionKeys.CountAsync()).IsGreaterThan(0);
        }

        // No writers remain: the SQLite database (including its DP key table) and local files
        // are one consistent offline snapshot. Move away the originals before restoring copies
        // into their exact former paths (the persisted local provider binding is absolute).
        string snapshot = Path.Combine(root, "snapshot"), original = Path.Combine(root, "original");
        Directory.CreateDirectory(snapshot);
        Directory.CreateDirectory(original);
        File.Copy(deployment.DatabasePath, Path.Combine(snapshot, "event.db"));
        CopyDirectory(storageRoot, Path.Combine(snapshot, "storage"));
        File.Move(deployment.DatabasePath, Path.Combine(original, "event.db"));
        Directory.Move(storageRoot, Path.Combine(original, "storage"));
        File.Copy(Path.Combine(snapshot, "event.db"), deployment.DatabasePath);
        CopyDirectory(Path.Combine(snapshot, "storage"), storageRoot);
        deployment.SetEnvironment("INSTANCE_BOOTSTRAP_LOCAL_PASSWORD", null);

        await using var restored = deployment.CreateHost();
        using var browser = restored.OpenClient();
        await using (var scope = restored.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EnableTenantFilterBypass("Verify both restored resource records and their durable key authority.");
            await Assert.That(await db.EventResources.CountAsync(row => row.Id == fileId || row.Id == linkId)).IsEqualTo(2);
            await Assert.That(await scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>()
                .DataProtectionKeys.CountAsync()).IsGreaterThan(0);
            var protector = scope.ServiceProvider.GetRequiredService<IEventResourceDestinationProtector>();
            await Assert.That(protector.Unprotect(ciphertext, PlatformDefaults.DefaultTenantId, linkId,
                protector.CurrentVersion)).IsEqualTo(destination);
        }
        using (var direct = restored.OpenClient())
        using (var login = await direct.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password }))
            RequireSuccess(login, "restored direct local login");
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        void AcceptCookies(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (string value in values)
                {
                    string[] pair = value.Split(';', 2)[0].Split('=', 2);
                    cookies[pair[0]] = pair[1];
                }
            browser.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(cookies["XSRF-TOKEN"]));
            browser.DefaultRequestHeaders.Remove("Cookie");
            browser.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",
                string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }
        using (var csrf = await browser.GetAsync("/bff/me")) AcceptCookies(csrf);
        using (var login = await browser.PostAsJsonAsync("/bff/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password, returnUrl = "/" }))
        {
            RequireSuccess(login, "restored BFF login");
            AcceptCookies(login);
        }
        using (var csrf = await browser.GetAsync("/bff/me"))
        {
            RequireSuccess(csrf, "restored BFF identity");
            AcceptCookies(csrf);
        }
        using (var response = await browser.GetAsync($"/api/eventresource/{fileId}/content"))
        {
            RequireSuccess(response, "restored file delivery");
            await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo(bytes);
        }
        using (var response = await browser.GetAsync($"/api/eventresource/{linkId}/access"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect)
                .Because(await response.Content.ReadAsStringAsync());
            await Assert.That(response.Headers.Location!.AbsoluteUri).IsEqualTo(destination);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void RequireSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{operation} returned HTTP {(int)response.StatusCode}.");
    }
}
