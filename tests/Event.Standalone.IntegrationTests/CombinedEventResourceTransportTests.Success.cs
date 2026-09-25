using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Standalone.IntegrationTests.Fixtures;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Standalone.IntegrationTests;

public sealed partial class CombinedEventResourceTransportTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task NativeCombinedCookieUploadAndPrivateDeliveryUseRealSqliteAndLocalBytes(
        bool heavyModeration, bool retainedEvidence)
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        await using var host = deployment.CreateHost();
        using var client = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        string password = NativeEmailOptionalStandaloneFixture.NewPassword();
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password = deployment.InitialPassword }))
        {
            login.EnsureSuccessStatusCode();
            using var challenge = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                challenge.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString());
        }
        using (var replace = await client.PostAsJsonAsync("/api/auth/local/credential-replacement", new { newPassword = password }))
            replace.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;
        string bearer;
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password }))
        {
            login.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            bearer = body.RootElement.GetProperty("token").GetString()!;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        await ActivateResourceDirectoryAsync(client);

        Guid eventId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7(), version;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EnableTenantFilterBypass("Combined resource transport seeds one exact default-tenant resource.");
            var user = await db.Users.SingleAsync(row => row.Id == deployment.Subject);
            var actor = await db.Actors.SingleAsync(row => row.UserId == user.Id);
            var membership = await db.TenantUsers.SingleOrDefaultAsync(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == user.Id);
            if (membership is null)
                db.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                    UserId = user.Id, User = user, ActorId = actor.Id,
                    StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
                });
            else
            {
                membership.ActorId = actor.Id;
                membership.StatusId = (int)TenantUserStatusEnum.Active;
            }
            var unscanned = await db.SystemSettings.SingleOrDefaultAsync(row =>
                row.SettingKey == GovernanceSettingKeys.EventResources.AllowUnscannedDocuments);
            if (unscanned is null)
                db.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                    Value = "true", ValueType = SettingValueType.Boolean, Category = "EventResources"
                });
            else unscanned.Value = "true";
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Combined private file", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            };
            parent.Publish(DateTime.UtcNow);
            db.Events.Add(parent);
            var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata
                {
                    Title = "Member handout", Kind = EventResourceKindEnum.GeneralDocument,
                    DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                }, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                    EventResourceAudienceKindEnum.AuthenticatedTenantMember)], user.Id, DateTime.UtcNow);
            db.EventResources.Add(resource);
            await db.SaveChangesAsync();
            version = resource.ConcurrencyStamp;
        }

        client.DefaultRequestHeaders.Authorization = null;
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        void AcceptCookies(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (string value in values)
                {
                    string[] pair = value.Split(';', 2)[0].Split('=', 2);
                    cookies[pair[0]] = pair[1];
                }
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(cookies["XSRF-TOKEN"]));
        }
        using (var csrf = await client.GetAsync("/bff/me")) AcceptCookies(csrf);
        using (var browserLogin = await client.PostAsJsonAsync("/bff/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password, returnUrl = "/" }))
        {
            browserLogin.EnsureSuccessStatusCode();
            AcceptCookies(browserLogin);
        }
        using (var csrf = await client.GetAsync("/bff/me"))
        {
            csrf.EnsureSuccessStatusCode();
            AcceptCookies(csrf);
        }
        // The actual bridge must use its protected session token, not this browser header.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Guid.CreateVersion7().ToString("N"));
        byte[] bytes = "%PDF-1.7\ncombined native storage\n%%EOF"u8.ToArray();
        string opaque;
        using (var reserve = await client.PostAsJsonAsync($"/bff/event-resources/{resourceId}/upload-session",
            new { expectedVersion = version, fileName = "handout.pdf", contentType = "application/pdf", expectedSizeBytes = bytes.Length }))
        {
            reserve.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await reserve.Content.ReadAsStringAsync());
            opaque = body.RootElement.GetProperty("uploadSessionId").GetString()!;
        }
        using (var form = new MultipartFormDataContent())
        {
            using var sessionField = new StringContent(opaque);
            using var typeField = new StringContent("application/pdf");
            using var file = new ByteArrayContent(bytes);
            form.Add(sessionField, "uploadSessionId");
            form.Add(typeField, "contentType");
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "handout.pdf");
            using var upload = await client.PostAsync("/bff/storage/upload-proxy", form);
            upload.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
            await Assert.That(body.RootElement.GetProperty("resourceId").GetGuid()).IsEqualTo(resourceId);
        }

        using (var management = await client.GetAsync($"/api/eventresource/{resourceId}/management"))
        {
            management.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await management.Content.ReadAsStringAsync());
            string publishHref = body.RootElement.GetProperty("_links").GetProperty("publish").GetProperty("href").GetString()!;
            using var publish = new HttpRequestMessage(HttpMethod.Post, publishHref)
            {
                Content = JsonContent.Create(new { expectedVersion = body.RootElement.GetProperty("version").GetGuid() })
            };
            publish.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
            using var response = await client.SendAsync(publish);
            response.EnsureSuccessStatusCode();
        }
        using (var response = await client.GetAsync($"/api/eventresource/{resourceId}/content"))
        {
            response.EnsureSuccessStatusCode();
            await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo(bytes);
            await Assert.That(response.Headers.CacheControl!.Private).IsTrue();
            await Assert.That(response.Headers.CacheControl.NoStore).IsTrue();
            await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
            await Assert.That(response.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
        }
        if (heavyModeration)
        {
            Guid storageId;
            long usedBefore;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                db.EnableTenantFilterBypass("Inspect one resource before native heavy moderation.");
                storageId = (await db.EventResources.SingleAsync(row => row.Id == resourceId)).StorageObjectId!.Value;
                usedBefore = (await db.StorageUsageCounters.SingleAsync(row =>
                    row.TenantId == PlatformDefaults.DefaultTenantId && row.Provider == StorageProviders.Local)).UsedBytes;
                if (retainedEvidence)
                {
                    var organizationId = Guid.CreateVersion7();
                    var participation = new OrganizationTenant
                    {
                        Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId,
                        Tenant = null!, OrganizationId = organizationId,
                        Organization = new Organization
                        {
                            Id = organizationId,
                            Pii = new OrganizationPii
                            {
                                OrganizationId = organizationId, FullName = "Independent evidence holder"
                            }
                        },
                        ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                        ApprovalStatus = null!
                    };
                    db.Add(OrganizationTenantEvidence.CreatePending(participation,
                        await db.StorageObjects.SingleAsync(row => row.Id == storageId)));
                    await db.SaveChangesAsync();
                }
                // Subject erasure may already have removed finalized uploader attribution.
                await db.StorageUploadSessions.Where(row => row.OwningResourceId == resourceId
                    && row.Status == StorageUploadSessionStates.Finalized).ExecuteDeleteAsync();
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/Event/{eventId}/moderation/heavy")
            {
                Content = JsonContent.Create(new { reasonCode = "heavy_redaction" })
            };
            request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            await using var verification = host.Services.CreateAsyncScope();
            var database = verification.ServiceProvider.GetRequiredService<ExploreDbContext>();
            database.EnableTenantFilterBypass("Verify atomic resource retirement after native parent moderation.");
            var retired = await database.EventResources.IgnoreQueryFilters().SingleAsync(row => row.Id == resourceId);
            await Assert.That(retired.IsDeleted).IsTrue();
            await Assert.That(retired.StorageObjectId).IsNull();
            await Assert.That(await database.StorageObjects.IgnoreQueryFilters().AnyAsync(row => row.Id == storageId))
                .IsEqualTo(retainedEvidence);
            await Assert.That(await database.OrganizationTenantEvidence.IgnoreQueryFilters().AnyAsync(row =>
                row.DocumentStorageObjectId == storageId)).IsEqualTo(retainedEvidence);
            await Assert.That(await database.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == storageId))
                .IsEqualTo(!retainedEvidence);
            if (retainedEvidence)
            {
                var source = await database.StorageObjects.IgnoreQueryFilters().SingleAsync(row => row.Id == storageId);
                var binding = await database.StorageProviderBindings.SingleAsync(row =>
                    row.Id == source.StorageProviderBindingId);
                await Assert.That(File.Exists(Path.Combine(binding.LocalRootPath!,
                    source.ObjectKey!.Replace('/', Path.DirectorySeparatorChar)))).IsTrue();
            }
            await Assert.That((await database.StorageUsageCounters.SingleAsync(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.Provider == StorageProviders.Local)).UsedBytes)
                .IsEqualTo(usedBefore - (retainedEvidence ? 0 : bytes.Length));
        }
        else
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EnableTenantFilterBypass("Combined resource transport revokes one exact tenant membership.");
            await db.TenantUsers.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == deployment.Subject)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.StatusId, (int)TenantUserStatusEnum.Suspended));
        }
        using var denied = await client.GetAsync($"/api/eventresource/{resourceId}/content");
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
    }

    private static async Task ActivateResourceDirectoryAsync(HttpClient client)
    {
        const string path = "/api/tenant/settings/documents/directory-operator-identity";
        using var current = await client.GetAsync(path);
        current.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        using var saved = await client.PatchAsJsonAsync(path, new PatchTenantDirectoryOperatorIdentityDocumentDto
        {
            ExpectedConcurrencyStamp = body.RootElement.GetProperty("concurrencyStamp").GetGuid(),
            LegalEntity = new PatchTenantDirectoryOperatorLegalEntityDto
            {
                PublicName = OptionalUpdate<string?>.Set("Resource directory"),
                LegalName = OptionalUpdate<string?>.Set("Resource Directory ASBL"),
                OperatorKindCode = OptionalUpdate<string?>.Set("registered_organization"),
                JurisdictionCountryCode = OptionalUpdate<string?>.Set("BE")
            },
            Contacts = new PatchTenantDirectoryOperatorContactsDto
            {
                PublicContactEmail = OptionalUpdate<string?>.Set("contact@standalone.example.test")
            },
            LegalLinks = new PatchTenantDirectoryOperatorLegalLinksDto
            {
                LegalNoticeUrl = OptionalUpdate<string?>.Set("https://standalone.example.test/legal"),
                PrivacyUrl = OptionalUpdate<string?>.Set("https://standalone.example.test/privacy")
            }
        });
        saved.EnsureSuccessStatusCode();
        using var activated = await client.PostAsJsonAsync(
            $"/api/admin/control-plane/tenants/{PlatformDefaults.DefaultTenantId}/activate", new { });
        activated.EnsureSuccessStatusCode();
    }
}
