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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EventResourceQuarantineDisclosureTests
{
    private static CancellationToken Token => TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task QuarantineDisclosesOnlyNamedVersionStatusWithoutExpandingAudience(bool storedFile)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = Client(factory);
        var seed = await SeedAsync(factory, client, storedFile, quarantined: true);
        var authorization = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = null;
        using (var visible = await client.GetAsync($"/api/eventresource/{seed.PublicId:D}", Token))
        {
            visible.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await visible.Content.ReadAsStringAsync(Token));
            await SafeItemAsync(body.RootElement, seed.PublicMaterialId, "audience");
        }
        using (var concealed = await client.GetAsync($"/api/eventresource/{seed.PrivateId:D}", Token))
        using (var absent = await client.GetAsync($"/api/eventresource/{Guid.CreateVersion7():D}", Token))
        {
            await Assert.That(concealed.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(absent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using var concealedBody = JsonDocument.Parse(await concealed.Content.ReadAsStringAsync(Token));
            using var absentBody = JsonDocument.Parse(await absent.Content.ReadAsStringAsync(Token));
            await Assert.That(concealedBody.RootElement.GetProperty("code").GetString())
                .IsEqualTo(absentBody.RootElement.GetProperty("code").GetString());
        }
        using (var page = await client.GetAsync($"/api/event/{seed.EventId:D}/resources", Token))
        {
            page.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await page.Content.ReadAsStringAsync(Token));
            var items = body.RootElement.GetProperty("_embedded").GetProperty("items");
            await Assert.That(items.GetArrayLength()).IsEqualTo(1);
            await SafeItemAsync(items[0], seed.PublicMaterialId, "audience");
        }

        client.DefaultRequestHeaders.Authorization = authorization;
        foreach (string surface in new[] { "audience", "audience-page", "management", "management-page", "export", "audit" })
        {
            using var response = await client.GetAsync(Path(seed, surface), Token);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await response.Content.ReadAsStringAsync(Token));
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
            if (surface == "audit")
            {
                await Assert.That(body.RootElement.GetProperty("items").GetArrayLength()).IsEqualTo(0);
                await SafeItemAsync(body.RootElement, seed.PrivateMaterialId, surface);
                continue;
            }
            var items = surface.EndsWith("-page", StringComparison.Ordinal)
                ? body.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray()
                : surface == "export" ? body.RootElement.GetProperty("items").EnumerateArray().ToArray()
                : [body.RootElement];
            await Assert.That(items.Length).IsEqualTo(surface is "audience" or "management" ? 1 : 2);
            foreach (var item in items)
                await SafeItemAsync(item, item.GetProperty("id").GetGuid() == seed.PublicId
                    ? seed.PublicMaterialId : seed.PrivateMaterialId, surface);
        }
        foreach (var id in new[] { seed.PublicId, seed.PrivateId })
            foreach (string suffix in new[] { "content", "access" })
            {
                using var denied = await client.GetAsync($"/api/eventresource/{id:D}/{suffix}", Token);
                await Assert.That(denied.IsSuccessStatusCode).IsFalse();
                await Assert.That(denied.Headers.Location).IsNull();
                await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
            }
        if (seed.StorageId is { } storageId)
        {
            foreach (string suffix in new[] { "", "/content", "/presigned-url", "/public" })
            {
                using var denied = await client.GetAsync($"/api/storageobject/{storageId:D}{suffix}", Token);
                await Assert.That(denied.IsSuccessStatusCode).IsFalse();
                await Assert.That(denied.Headers.Location).IsNull();
                await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
            }
        }
    }

    [Test]
    [Arguments(false, "audience")]
    [Arguments(true, "audience")]
    [Arguments(false, "audience-page")]
    [Arguments(true, "audience-page")]
    [Arguments(false, "management")]
    [Arguments(true, "management")]
    [Arguments(false, "management-page")]
    [Arguments(true, "management-page")]
    [Arguments(false, "export")]
    [Arguments(true, "export")]
    [Arguments(false, "audit")]
    [Arguments(true, "audit")]
    public async Task QuarantineBeforeTerminalExecutionDiscardsPreparedPrivateRepresentation(bool storedFile, string surface)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var ordinary = Client(factory);
        var seed = await SeedAsync(factory, ordinary, storedFile, quarantined: false);
        string path = Path(seed, surface);
        bool applied = false;
        using var intercepted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<MvcOptions>(options => options.Filters.Add(new BeforeTerminal(path, async () =>
            {
                await using var database = factory.CreateDatabase();
                var resource = await database.EventResources.Include(row => row.ModerationQuarantines)
                    .SingleAsync(row => row.Id == seed.PrivateId, Token);
                resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
                    seed.PrivateMaterialId, resource.ConcurrencyStamp, DateTimeOffset.UtcNow);
                await database.SaveChangesAsync(Token);
                applied = true;
            })))));
        using var racing = Client(intercepted);
        racing.DefaultRequestHeaders.Authorization = ordinary.DefaultRequestHeaders.Authorization;
        using (var response = await racing.GetAsync(path, Token))
        {
            await Assert.That(applied).IsTrue();
            await Assert.That(response.IsSuccessStatusCode).IsFalse();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
            await Assert.That(body.RootElement.TryGetProperty("title", out _)).IsFalse();
            await Assert.That(body.RootElement.TryGetProperty("_embedded", out _)).IsFalse();
            await Assert.That(body.RootElement.TryGetProperty("items", out _)).IsFalse();
            await Assert.That(body.RootElement.TryGetProperty("draft", out _)).IsFalse();
        }
        using var refreshed = await ordinary.GetAsync(path, Token);
        refreshed.EnsureSuccessStatusCode();
        using var current = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync(Token));
        var safe = surface.EndsWith("-page", StringComparison.Ordinal)
            ? current.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == seed.PrivateId)
            : surface == "export" ? current.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == seed.PrivateId)
            : current.RootElement;
        await SafeItemAsync(safe, seed.PrivateMaterialId, surface);
    }

    [Test]
    [Arguments("audience")]
    [Arguments("audience-page")]
    public async Task SafeStatusCannotOutliveItsCurrentPrivateAudience(string surface)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var ordinary = Client(factory);
        var seed = await SeedAsync(factory, ordinary, storedFile: false, quarantined: true);
        string path = Path(seed, surface);
        bool revoked = false;
        using var intercepted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<MvcOptions>(options => options.Filters.Add(new BeforeTerminal(path, async () =>
            {
                await using var database = factory.CreateDatabase();
                var membership = await database.TenantUsers.SingleAsync(row =>
                    row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == seed.UserId, Token);
                membership.StatusId = (int)TenantUserStatusEnum.Suspended;
                await database.SaveChangesAsync(Token);
                revoked = true;
            })))));
        using var client = Client(intercepted);
        client.DefaultRequestHeaders.Authorization = ordinary.DefaultRequestHeaders.Authorization;
        using var response = await client.GetAsync(path, Token);
        await Assert.That(revoked).IsTrue();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        await Assert.That(body.RootElement.TryGetProperty("materialVersionId", out _)).IsFalse();
        await Assert.That(body.RootElement.TryGetProperty("availability", out _)).IsFalse();
        await Assert.That(body.RootElement.TryGetProperty("_embedded", out _)).IsFalse();
    }

    private static async Task SafeItemAsync(JsonElement item, Guid materialVersion, string surface)
    {
        await Assert.That(item.GetProperty("materialVersionId").GetGuid()).IsEqualTo(materialVersion);
        await Assert.That(item.GetProperty(surface.StartsWith("audience", StringComparison.Ordinal)
            ? "availability" : "status").GetString()).IsEqualTo("quarantined");
        string[] permitted = surface.StartsWith("audience", StringComparison.Ordinal)
            ? ["id", "eventId", "isTeaser", "availability", "materialVersionId", "_links"]
            : surface == "export" ? ["id", "publicationState", "status", "materialVersionId"]
            : surface == "audit" ? ["items", "status", "materialVersionId"]
            : ["id", "eventId", "version", "publicationState", "status", "materialVersionId", "_links"];
        foreach (var property in item.EnumerateObject())
            await Assert.That(permitted.Contains(property.Name, StringComparer.Ordinal)).IsTrue()
                .Because($"Quarantined {surface} must not disclose {property.Name}.");
        if (item.TryGetProperty("_links", out var links))
            foreach (var link in links.EnumerateObject())
                await Assert.That(link.Name is "self" or "collection").IsTrue()
                    .Because("Ordinary status navigation must not grant a material or management bypass.");
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static string Path(Scenario seed, string surface) => surface switch
    {
        "audience" => $"/api/eventresource/{seed.PrivateId:D}",
        "audience-page" => $"/api/event/{seed.EventId:D}/resources",
        "management" => $"/api/eventresource/{seed.PrivateId:D}/management",
        "management-page" => $"/api/event/{seed.EventId:D}/resources/management",
        "export" => $"/api/event/{seed.EventId:D}/resources/export",
        "audit" => $"/api/eventresource/{seed.PrivateId:D}/audit",
        _ => throw new ArgumentOutOfRangeException(nameof(surface))
    };

    private static async Task<Scenario> SeedAsync(LocalAdmissionWebApplicationFactory factory, HttpClient client,
        bool storedFile, bool quarantined)
    {
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid eventId = Guid.CreateVersion7(), publicId = Guid.CreateVersion7(), privateId = Guid.CreateVersion7();
        Guid publicMaterial, privateMaterial, userId;
        Guid? privateStorage = null;
        await using (var database = factory.CreateDatabase())
        {
            database.SystemSettings.AddRange(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"",
                ValueType = SettingValueType.String,
                Category = "Security"
            }, new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                Value = "true",
                ValueType = SettingValueType.Boolean,
                Category = "EventResources"
            }, new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.ExternalOrigins,
                Value = "[\"https://quarantined.example.test\"]",
                ValueType = SettingValueType.Json,
                Category = "EventResources"
            });
            var user = await database.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier, Token);
            userId = user.Id;
            var actor = await database.Actors.SingleAsync(row => row.UserId == user.Id, Token);
            var now = DateTime.UtcNow;
            database.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = user.Id,
                User = user,
                ActorId = actor.Id,
                StatusId = (int)TenantUserStatusEnum.Active,
                CreatedAt = now
            });
            var parent = new Explore.Domain.Event
            {
                Id = eventId,
                Title = "Native quarantine disclosure",
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                ActorId = actor.Id,
                Actor = actor,
                OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventStatus = null!
            };
            parent.Publish(now);
            database.Events.Add(parent);
            var binding = StorageProviderBinding.Local(System.IO.Path.GetFullPath("quarantine-disclosure-test-storage"));
            if (storedFile) database.Add(binding);
            Guid Add(Guid id, bool isPublic)
            {
                var resource = EventResource.CreateDraft(id, PlatformDefaults.DefaultTenantId, eventId, null,
                    new EventResourceMetadata
                    {
                        Title = "Restricted embedded title",
                        PublicTitle = isPublic ? "Public embedded title" : null,
                        Description = "Private description",
                        SensitiveNotes = "Private management note",
                        LanguageCode = "en",
                        AccessibilityNote = "Private accessibility note",
                        Kind = EventResourceKindEnum.GeneralDocument,
                        DisclosureMode = isPublic ? EventResourceDisclosureModeEnum.Public : EventResourceDisclosureModeEnum.EligibleOnly,
                        AccessibleAlternativeEventResourceId = isPublic ? privateId : null
                    }, storedFile ? EventResourceDeliveryTypeEnum.StoredFile : EventResourceDeliveryTypeEnum.ExternalLink,
                    EventResourceAvailability.Create(),
                    [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, id,
                        isPublic ? EventResourceAudienceKindEnum.Public : EventResourceAudienceKindEnum.AuthenticatedTenantMember)],
                    user.Id, now);
                if (storedFile)
                {
                    Guid storageId = Guid.CreateVersion7();
                    string checksum = Convert.ToHexString(SHA256.HashData("private bytes"u8));
                    var storage = new StorageObject
                    {
                        Id = storageId,
                        TenantId = PlatformDefaults.DefaultTenantId,
                        Tenant = null!,
                        FileTypeId = (int)FileTypeEnum.Document,
                        FileType = null!,
                        Provider = StorageProviders.Local,
                        ObjectKey = $"tenants/{PlatformDefaults.DefaultTenantId:N}/{storageId:N}.pdf",
                        StorageProviderBindingId = binding.Id,
                        FullName = "private-name.pdf",
                        SafeDisplayName = "private-name.pdf",
                        Extension = ".pdf",
                        ContentType = EventResourceGovernancePolicy.PdfMediaType,
                        Size = 13,
                        Sha256Checksum = checksum,
                        Purpose = StorageObjectPurposes.EventResource,
                        Visibility = StorageObjectVisibilities.PrivateOwner,
                        OwningResourceKind = StorageOwningResourceKinds.EventResource,
                        OwningResourceId = id,
                        LifecycleState = StorageObjectLifecycleStates.Active,
                        CreatedBy = user.Id
                    };
                    storage.RecordEventResourceInspection(storageId, checksum);
                    database.StorageObjects.Add(storage);
                    resource.SetStoredFile(storageId, resource.ConcurrencyStamp, user.Id, now);
                    if (!isPublic) privateStorage = storageId;
                }
                else resource.SetExternalDestination(Guid.CreateVersion7().ToString("N"), 1,
                    "https://quarantined.example.test", resource.ConcurrencyStamp, user.Id, now);
                resource.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                    false, true, null, false, new(null, null, null, null)), true, resource.ConcurrencyStamp, user.Id, now);
                if (quarantined)
                {
                    resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
                        resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, new DateTimeOffset(now));
                    resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
                        resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, new DateTimeOffset(now));
                }
                database.EventResources.Add(resource);
                database.EventResourceAuditEntries.Add(EventResourceAuditEntry.Create(
                    resource.TenantId, id, user.Id, EventResourceAuditAction.ConfigureDelivery,
                    EventResourceAuditOutcome.Succeeded, EventResourceAuditReason.OrganizerMutation, now));
                return resource.MaterialVersionId!.Value;
            }
            publicMaterial = Add(publicId, true);
            privateMaterial = Add(privateId, false);
            await database.SaveChangesAsync(Token);
        }
        using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        login.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync(Token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            body.RootElement.GetProperty("token").GetString());
        return new(eventId, publicId, privateId, publicMaterial, privateMaterial, privateStorage, userId);
    }

    private sealed record Scenario(Guid EventId, Guid PublicId, Guid PrivateId,
        Guid PublicMaterialId, Guid PrivateMaterialId, Guid? StorageId, Guid UserId);

    /// <summary>The awaited native mutation is the exact barrier before ActionResult execution; no timing race.</summary>
    private sealed class BeforeTerminal(string path, Func<Task> apply) : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.HttpContext.Request.Path.Value == path) await apply();
            await next();
        }
    }
}
