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
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.API.IntegrationTests.Features;

public sealed partial class EventResourceDiscoveryTests
{
    [Test]
    public async Task CompleteFiveHundredCandidateDiscoveryFindsSparseProviderAllowAndRejectsResourceFiveHundredOne()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid eventId = Guid.CreateVersion7(), visibleId = Guid.CreateVersion7();
        var allIds = new HashSet<Guid>();
        await using (var database = factory.CreateDatabase())
        {
            database.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"", ValueType = SettingValueType.String, Category = "Security"
            });
            var user = await database.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier, Token);
            var actor = await database.Actors.SingleAsync(row => row.UserId == user.Id, Token);
            var now = DateTime.UtcNow;
            database.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = user.Id, User = user, ActorId = actor.Id,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = now
            });
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Sparse governed material", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            };
            parent.Publish(now);
            database.Events.Add(parent);
            for (int index = 0; index < 500; index++)
            {
                var id = index == 499 ? visibleId : Guid.CreateVersion7();
                allIds.Add(id);
                var resource = EventResource.CreateDraft(id, PlatformDefaults.DefaultTenantId, eventId, null,
                    new EventResourceMetadata
                    {
                        Title = index == 499 ? "Visible last material" : "Provider-denied title",
                        SensitiveNotes = "Never disclose manager note", SortOrder = index,
                        Kind = EventResourceKindEnum.GeneralDocument, DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                    }, EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                    [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, id,
                        EventResourceAudienceKindEnum.AuthenticatedTenantMember)], user.Id, now);
                resource.SetExternalDestination(Guid.CreateVersion7().ToString("N"), 1, "https://materials.example.test",
                    resource.ConcurrencyStamp, user.Id, now);
                resource.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                    false, true, null, false, new(null, null, null, null)), true, resource.ConcurrencyStamp, user.Id, now);
                database.EventResources.Add(resource);
            }
            await database.SaveChangesAsync(Token);
        }
        var evaluatedIds = new HashSet<Guid>();
        var evaluationBatches = new List<int>();
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventResourceAuthorizationProvider>();
            services.AddScoped<IEventResourceAuthorizationProvider>(provider => new SparseResourceProvider(
                ActivatorUtilities.CreateInstance<Explore.Infrastructure.Services.EventResourceAuthorizationProvider>(provider),
                eventId, visibleId, evaluatedIds, evaluationBatches));
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token))
        {
            login.EnsureSuccessStatusCode();
            using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        }
        using (var page = await client.GetAsync($"/api/event/{eventId:D}/resources", Token))
        {
            await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await page.Content.ReadAsStringAsync(Token));
            await PrivateAsync(page);
            var json = await page.Content.ReadAsStringAsync(Token);
            await Assert.That(json.Contains("Provider-denied title", StringComparison.Ordinal)
                || json.Contains("Never disclose manager note", StringComparison.Ordinal)).IsFalse();
            using var body = JsonDocument.Parse(json);
            var items = body.RootElement.GetProperty("_embedded").GetProperty("items");
            await Assert.That(items.GetArrayLength()).IsEqualTo(1);
            await Assert.That(items[0].GetProperty("id").GetGuid()).IsEqualTo(visibleId);
            await Assert.That(body.RootElement.TryGetProperty("totalCount", out _)).IsFalse();
            await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("next", out _)).IsFalse();
        }
        await Assert.That(evaluatedIds.SetEquals(allIds)).IsTrue();
        await Assert.That(evaluationBatches.Count <= 12).IsTrue().Because("provider work must be bounded by batches, not one request per resource");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"capacity-{Guid.CreateVersion7():N}");
        using var rejected = await client.PostAsJsonAsync($"/api/event/{eventId:D}/resources",
            new CreateEventResourceRequestDto(Guid.CreateVersion7(), new EventResourceDraftDto
            {
                Title = "Overflow draft", Kind = EventResourceKindEnum.GeneralDocument,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly, DeliveryType = EventResourceDeliveryTypeEnum.ExternalLink,
                AudienceRules = [new(EventResourceAudienceKindEnum.AuthenticatedTenantMember)]
            }), Token);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict).Because(await rejected.Content.ReadAsStringAsync(Token));
        await using var verify = factory.CreateDatabase();
        verify.EnableTenantFilterBypass("Capacity test counts one exact event after rejected creation.");
        await Assert.That(await verify.EventResources.CountAsync(row => row.EventId == eventId, Token)).IsEqualTo(500);
    }

    private sealed class SparseResourceProvider(
        IEventResourceAuthorizationProvider inner, Guid eventId, Guid visibleId, HashSet<Guid> evaluated, List<int> batches)
        : IEventResourceAuthorizationProvider
    {
        public async Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input, CancellationToken cancellationToken) =>
            (await CheckBatchAsync([input], cancellationToken))[0];

        public async Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
            IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken cancellationToken)
        {
            var decisions = await inner.CheckBatchAsync(inputs, cancellationToken);
            var views = inputs.Where(input => input.Action == "view" && input.Resource.Id != eventId).ToArray();
            if (views.Length > 0)
            {
                batches.Add(views.Length);
                foreach (var input in views) evaluated.Add(input.Resource.Id);
            }
            return inputs.Select((input, index) => input.Action == "view" && input.Resource.Id != eventId && input.Resource.Id != visibleId
                ? EventResourceProviderDecision.Deny : decisions[index]).ToArray();
        }
    }
}
