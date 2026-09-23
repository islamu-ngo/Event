using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed partial class EventResourceDiscoveryTests
{
    private static CancellationToken Token => TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AudienceReadsConcealPrivateMetadataAndReauthorizeEveryContinuation(bool revokeDuringHal)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid eventId = Guid.CreateVersion7(), hiddenId = Guid.CreateVersion7(), teaserId = Guid.CreateVersion7(),
            publicId = Guid.CreateVersion7(), userId;
        const string privateTitle = "Restricted study material";
        const string privateNotes = "Management-only attribution note";
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
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Audience resource discovery", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            };
            parent.Publish(now);
            database.Events.Add(parent);
            void AddResource(Guid id, EventResourceDisclosureModeEnum disclosure, EventResourceAudienceKindEnum audience, int sort)
            {
                var resource = EventResource.CreateDraft(id, PlatformDefaults.DefaultTenantId, eventId, null,
                    new EventResourceMetadata
                    {
                        Title = audience == EventResourceAudienceKindEnum.Public ? "Open material" : privateTitle,
                        PublicTitle = "Public material notice", SensitiveNotes = privateNotes,
                        Kind = EventResourceKindEnum.GeneralDocument, DisclosureMode = disclosure, SortOrder = sort,
                        AccessibleAlternativeEventResourceId = id == publicId ? hiddenId : null
                    }, EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                    [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, id, audience)], userId, now);
                resource.SetExternalDestination(Guid.CreateVersion7().ToString("N"), 1, "https://materials.example.test",
                    resource.ConcurrencyStamp, userId, now);
                resource.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                    false, true, null, false, new(null, null, null, null)), true, resource.ConcurrencyStamp, userId, now);
                database.EventResources.Add(resource);
            }
            AddResource(hiddenId, EventResourceDisclosureModeEnum.EligibleOnly, EventResourceAudienceKindEnum.AuthenticatedTenantMember, 0);
            AddResource(teaserId, EventResourceDisclosureModeEnum.Teaser, EventResourceAudienceKindEnum.AuthenticatedTenantMember, 1);
            AddResource(publicId, EventResourceDisclosureModeEnum.Public, EventResourceAudienceKindEnum.Public, 2);
            await database.SaveChangesAsync(Token);
        }
        bool revokedDuringAssembly = false;
        async Task RevokeAsync()
        {
            await using var revoke = factory.CreateDatabase();
            revoke.EnableTenantFilterBypass("Audience test revokes one exact tenant subject.");
            await revoke.TenantUsers.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.StatusId, (int)TenantUserStatusEnum.Suspended), Token);
            revokedDuringAssembly = true;
        }
        using var intercepted = revokeDuringHal ? factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventResourceAuthorizationProvider>();
            services.AddScoped<IEventResourceAuthorizationProvider>(provider => new RevokingAudienceProvider(
                ActivatorUtilities.CreateInstance<Explore.Infrastructure.Services.EventResourceAuthorizationProvider>(provider),
                hiddenId, RevokeAsync));
        })) : null;
        using var client = (intercepted ?? factory).CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using (var publicDetail = await client.GetAsync($"/api/eventresource/{publicId:D}", Token))
        {
            await Assert.That(publicDetail.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await publicDetail.Content.ReadAsStringAsync(Token));
            await PrivateAsync(publicDetail);
            using var body = await JsonDocument.ParseAsync(await publicDetail.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(body.RootElement.GetProperty("title").GetString()).IsEqualTo("Open material");
            await Assert.That(body.RootElement.TryGetProperty("accessibleAlternativeEventResourceId", out _)).IsFalse();
            await Assert.That(body.RootElement.TryGetProperty("sensitiveNotes", out _)).IsFalse();
            var links = body.RootElement.GetProperty("_links");
            await Assert.That(links.TryGetProperty("access", out _) || links.TryGetProperty("download", out _)).IsFalse();
        }
        using (var hidden = await client.GetAsync($"/api/eventresource/{hiddenId:D}", Token))
        using (var absent = await client.GetAsync($"/api/eventresource/{Guid.CreateVersion7():D}", Token))
        {
            await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(absent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using var hiddenBody = await JsonDocument.ParseAsync(await hidden.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            using var absentBody = await JsonDocument.ParseAsync(await absent.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(hiddenBody.RootElement.GetProperty("code").GetString())
                .IsEqualTo(absentBody.RootElement.GetProperty("code").GetString());
        }
        string continuation;
        using (var page = await client.GetAsync($"/api/event/{eventId:D}/resources?pageSize=1", Token))
        {
            await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await page.Content.ReadAsStringAsync(Token));
            await PrivateAsync(page);
            var json = await page.Content.ReadAsStringAsync(Token);
            await Assert.That(json.Contains(privateTitle, StringComparison.Ordinal) || json.Contains(privateNotes, StringComparison.Ordinal)).IsFalse();
            using var body = JsonDocument.Parse(json);
            await Assert.That(body.RootElement.TryGetProperty("totalCount", out _) || body.RootElement.TryGetProperty("totalPages", out _)).IsFalse();
            var item = body.RootElement.GetProperty("_embedded").GetProperty("items")[0];
            await Assert.That(item.GetProperty("id").GetGuid()).IsEqualTo(teaserId);
            await Assert.That(item.GetProperty("isTeaser").GetBoolean()).IsTrue();
            continuation = body.RootElement.GetProperty("_links").GetProperty("next").GetProperty("href").GetString()
                ?? throw new InvalidOperationException("Missing authorized continuation.");
        }
        using (var next = await client.GetAsync(continuation, Token))
        {
            await Assert.That(next.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = await JsonDocument.ParseAsync(await next.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(body.RootElement.GetProperty("_embedded").GetProperty("items")[0].GetProperty("id").GetGuid()).IsEqualTo(publicId);
            await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("next", out _)).IsFalse();
        }
        using (var malformed = await client.GetAsync($"/api/event/{eventId:D}/resources?cursor=invalid", Token))
            await Assert.That(malformed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token))
        {
            login.EnsureSuccessStatusCode();
            using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        }
        using (var wrongSubjectCursor = await client.GetAsync(continuation, Token))
            await Assert.That(wrongSubjectCursor.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var entitled = await client.GetAsync($"/api/eventresource/{hiddenId:D}", Token))
        {
            if (revokeDuringHal)
            {
                await Assert.That(revokedDuringAssembly).IsTrue();
                await Assert.That(entitled.StatusCode).IsEqualTo(HttpStatusCode.NotFound).Because(await entitled.Content.ReadAsStringAsync(Token));
                await PrivateAsync(entitled);
                var denied = await entitled.Content.ReadAsStringAsync(Token);
                await Assert.That(denied.Contains(privateTitle, StringComparison.Ordinal)
                    || denied.Contains(privateNotes, StringComparison.Ordinal)).IsFalse();
                return;
            }
            await Assert.That(entitled.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = await JsonDocument.ParseAsync(await entitled.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(body.RootElement.GetProperty("title").GetString()).IsEqualTo(privateTitle);
            await Assert.That(body.RootElement.TryGetProperty("sensitiveNotes", out _)).IsFalse();
        }
        await RevokeAsync();
        client.DefaultRequestHeaders.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        using (var revoked = await client.GetAsync($"/api/eventresource/{hiddenId:D}", Token))
        {
            await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await PrivateAsync(revoked);
        }
        await using var verification = factory.CreateDatabase();
        verification.EnableTenantFilterBypass("Audience test checks exact event resources for unintended audit collection.");
        await Assert.That(await verification.EventResourceAuditEntries
            .AnyAsync(row => row.EventResourceId == hiddenId || row.EventResourceId == teaserId || row.EventResourceId == publicId, Token)).IsFalse();
    }

    private static async Task PrivateAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    private sealed class RevokingAudienceProvider(
        IEventResourceAuthorizationProvider inner, Guid resourceId, Func<Task> revoke) : IEventResourceAuthorizationProvider
    {
        private bool _revoked;

        public async Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input, CancellationToken cancellationToken) =>
            (await CheckBatchAsync([input], cancellationToken))[0];

        public async Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
            IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken cancellationToken)
        {
            var decisions = await inner.CheckBatchAsync(inputs, cancellationToken);
            if (!_revoked && inputs.Any(input => input.Resource.Id == resourceId && input.Action == "view-management"))
            {
                _revoked = true;
                await revoke();
            }
            return decisions;
        }
    }
}
