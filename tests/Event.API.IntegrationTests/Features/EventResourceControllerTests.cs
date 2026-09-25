using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EventResourceControllerTests
{
    private static CancellationToken Token => TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task DraftAuthoringIsPrivateVersionedAndCannotPublishOrReplayCreation(bool revokeDuringHal, bool denyOffPage)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid eventId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7(), userId;
        await using (var database = factory.CreateDatabase())
        {
            database.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"", ValueType = SettingValueType.String, Category = "Security"
            });
            var user = await database.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier, Token);
            userId = user.Id;
            var actor = await database.Actors.SingleAsync(row => row.UserId == user.Id, Token);
            database.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = user.Id, User = user, ActorId = actor.Id,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
            });
            database.Events.Add(new Explore.Domain.Event
            {
                Id = eventId, Title = "Resource authoring", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            });
            database.EventRoleAssignments.Add(EventRoleAssignment.Create(PlatformDefaults.DefaultTenantId, eventId, user.Id,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddMinutes(-1), null, user.Id));
            await database.SaveChangesAsync(Token);
        }
        bool revokedDuringAssembly = false;
        async Task RevokeAsync()
        {
            await using var revoke = factory.CreateDatabase();
            revoke.EnableTenantFilterBypass("Resource HTTP test revokes one exact tenant/event subject.");
            await revoke.EventRoleAssignments.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                && row.EventId == eventId && row.UserId == userId).ExecuteDeleteAsync(Token);
            await revoke.TenantUsers.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.StatusId, (int)TenantUserStatusEnum.Suspended), Token);
            revokedDuringAssembly = true;
        }
        using var intercepted = revokeDuringHal || denyOffPage ? factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventResourceAuthorizationProvider>();
            services.AddScoped<IEventResourceAuthorizationProvider>(provider => new ManagementProviderBoundary(
                ActivatorUtilities.CreateInstance<Explore.Infrastructure.Services.EventResourceAuthorizationProvider>(provider),
                revokeDuringHal ? RevokeAsync : null, denyOffPage ? resourceId : null));
        })) : null;
        using var client = (intercepted ?? factory).CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token))
        {
            login.EnsureSuccessStatusCode();
            using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        }
        var draft = new EventResourceDraftDto
        {
            Title = "Organizer-only draft title", SensitiveNotes = "Private authoring note",
            Kind = EventResourceKindEnum.GeneralDocument, DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly,
            DeliveryType = EventResourceDeliveryTypeEnum.StoredFile,
            AudienceRules = [new(EventResourceAudienceKindEnum.Public)]
        };
        string collection = $"/api/event/{eventId:D}/resources";
        using (var missingKey = await client.PostAsJsonAsync(collection, new CreateEventResourceRequestDto(resourceId, draft), Token))
            await Assert.That(missingKey.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        void IdempotencyKey(string key)
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        }
        IdempotencyKey($"resource-create-{resourceId:N}");
        using (var created = await client.PostAsJsonAsync(collection, new CreateEventResourceRequestDto(resourceId, draft), Token))
        {
            await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created).Because(await created.Content.ReadAsStringAsync(Token));
            await PrivateAsync(created);
        }
        if (denyOffPage)
        {
            using var empty = await client.GetAsync(collection + "/management?page=2&pageSize=100", Token);
            await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await empty.Content.ReadAsStringAsync(Token));
            await PrivateAsync(empty);
            using var body = await JsonDocument.ParseAsync(await empty.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(body.RootElement.TryGetProperty("totalCount", out _)).IsFalse();
            await Assert.That(body.RootElement.TryGetProperty("totalPages", out _)).IsFalse();
            await Assert.That(body.RootElement.GetProperty("_embedded").GetProperty("items").GetArrayLength()).IsEqualTo(0);
            await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("last", out _)).IsFalse();
            var allowedId = Guid.CreateVersion7();
            IdempotencyKey($"resource-create-{allowedId:N}");
            using var allowed = await client.PostAsJsonAsync(collection,
                new CreateEventResourceRequestDto(allowedId, draft with { SortOrder = 1 }), Token);
            await Assert.That(allowed.StatusCode).IsEqualTo(HttpStatusCode.Created).Because(await allowed.Content.ReadAsStringAsync(Token));
            using var partial = await client.GetAsync(collection + "/management?page=2&pageSize=1", Token);
            await Assert.That(partial.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await partial.Content.ReadAsStringAsync(Token));
            using var visible = await JsonDocument.ParseAsync(await partial.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(visible.RootElement.TryGetProperty("totalCount", out _)).IsFalse();
            var items = visible.RootElement.GetProperty("_embedded").GetProperty("items");
            await Assert.That(items.GetArrayLength()).IsEqualTo(1);
            await Assert.That(items[0].GetProperty("id").GetGuid()).IsEqualTo(allowedId);
            return;
        }
        Guid version;
        using (var detail = await client.GetAsync($"/api/eventresource/{resourceId:D}/management", Token))
        {
            if (revokeDuringHal)
            {
                await Assert.That(revokedDuringAssembly).IsTrue();
                await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.NotFound).Because(await detail.Content.ReadAsStringAsync(Token));
                await PrivateAsync(detail);
                using var denied = await JsonDocument.ParseAsync(await detail.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
                await Assert.That(denied.RootElement.TryGetProperty("draft", out _)).IsFalse();
                return;
            }
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await detail.Content.ReadAsStringAsync(Token));
            await PrivateAsync(detail);
            using var body = await JsonDocument.ParseAsync(await detail.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            version = body.RootElement.GetProperty("version").GetGuid();
            await Assert.That(body.RootElement.GetProperty("draft").GetProperty("title").GetString()).IsEqualTo(draft.Title);
            var links = body.RootElement.GetProperty("_links");
            await Assert.That(links.TryGetProperty("edit", out _)).IsTrue();
            await Assert.That(links.TryGetProperty("publish", out _) || links.TryGetProperty("download", out _)
                || links.TryGetProperty("access", out _)).IsFalse();
            await Assert.That(body.RootElement.TryGetProperty("storageObjectId", out _)
                || body.RootElement.TryGetProperty("externalDestinationCiphertext", out _)).IsFalse();
        }
        using (var replay = await client.PostAsJsonAsync(collection, new CreateEventResourceRequestDto(resourceId, draft), Token))
            await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Conflict).Because(await replay.Content.ReadAsStringAsync(Token));
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        using (var management = await client.GetAsync(collection + "/management", Token))
        {
            await Assert.That(management.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await management.Content.ReadAsStringAsync(Token));
            await PrivateAsync(management);
            using var body = await JsonDocument.ParseAsync(await management.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("create-resource", out _)).IsTrue();
        }
        IdempotencyKey($"resource-invalid-{resourceId:N}");
        using (var injected = await client.PostAsJsonAsync(collection, new
               {
                   resourceId = Guid.CreateVersion7(), draft, storageObjectId = Guid.CreateVersion7()
               }, Token))
            await Assert.That(injected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(await injected.Content.ReadAsStringAsync(Token));
        IdempotencyKey($"resource-publish-{resourceId:N}");
        using (var publish = await client.PostAsJsonAsync($"/api/eventresource/{resourceId:D}/publish",
                   new EventResourceVersionRequestDto(version), Token))
            await Assert.That(publish.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because(await publish.Content.ReadAsStringAsync(Token));
        var changed = draft with { Title = "Updated private draft" };
        IdempotencyKey($"resource-update-{resourceId:N}");
        using (var updated = await client.PutAsJsonAsync($"/api/eventresource/{resourceId:D}",
                   new UpdateEventResourceRequestDto(version, changed), Token))
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await updated.Content.ReadAsStringAsync(Token));
        IdempotencyKey($"resource-stale-{resourceId:N}");
        using (var stale = await client.PutAsJsonAsync($"/api/eventresource/{resourceId:D}",
                   new UpdateEventResourceRequestDto(version, draft), Token))
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict).Because(await stale.Content.ReadAsStringAsync(Token));
        using (var audit = await client.GetAsync($"/api/eventresource/{resourceId:D}/audit", Token))
        {
            await Assert.That(audit.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await audit.Content.ReadAsStringAsync(Token));
            await PrivateAsync(audit);
            using var body = await JsonDocument.ParseAsync(await audit.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            var entries = body.RootElement.GetProperty("items");
            await Assert.That(entries.GetArrayLength()).IsEqualTo(2);
            foreach (var entry in entries.EnumerateArray())
            {
                await Assert.That(entry.GetProperty("responsibleManagerUserId").GetGuid()).IsEqualTo(userId);
                await Assert.That(entry.EnumerateObject().Select(property => property.Name)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(
                        ["id", "action", "outcome", "reason", "timestamp", "responsibleManagerUserId"])).IsTrue();
            }
        }
        await RevokeAsync();
        client.DefaultRequestHeaders.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        IdempotencyKey($"resource-create-{resourceId:N}");
        using (var revokedReplay = await client.PostAsJsonAsync(collection, new CreateEventResourceRequestDto(resourceId, draft), Token))
            await Assert.That(revokedReplay.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because(await revokedReplay.Content.ReadAsStringAsync(Token));
        using (var revoked = await client.GetAsync($"/api/eventresource/{resourceId:D}/management", Token))
        {
            await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because(await revoked.Content.ReadAsStringAsync(Token));
            await PrivateAsync(revoked);
            using var body = await JsonDocument.ParseAsync(await revoked.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(body.RootElement.TryGetProperty("draft", out _)).IsFalse();
        }
        await using var verification = factory.CreateDatabase();
        verification.EnableTenantFilterBypass("Resource HTTP test verifies one exact tenant and resource.");
        var persisted = await verification.EventResources.AsNoTracking().SingleAsync(row => row.Id == resourceId, Token);
        await Assert.That(persisted.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
        await Assert.That(persisted.Title).IsEqualTo(changed.Title);
        await Assert.That(await verification.Set<EventResourceAuditEntry>().CountAsync(row => row.EventResourceId == resourceId, Token)).IsEqualTo(2);
    }

    private static async Task PrivateAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    private sealed class ManagementProviderBoundary(
        IEventResourceAuthorizationProvider inner, Func<Task>? revoke, Guid? deniedResourceId)
        : IEventResourceAuthorizationProvider
    {
        private bool _revoked;

        public async Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input, CancellationToken cancellationToken) =>
            (await CheckBatchAsync([input], cancellationToken))[0];

        public async Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
            IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken cancellationToken)
        {
            var decisions = await inner.CheckBatchAsync(inputs, cancellationToken);
            if (revoke is not null && !_revoked && inputs.Any(input => input.Action == "update"))
            {
                _revoked = true;
                await revoke();
            }
            return decisions.Select((decision, index) => inputs[index].Resource.Id == deniedResourceId
                ? EventResourceProviderDecision.Deny : decision).ToArray();
        }
    }
}
