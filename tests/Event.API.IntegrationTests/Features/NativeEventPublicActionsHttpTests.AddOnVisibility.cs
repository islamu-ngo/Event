using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments("private")]
    [Arguments("archived")]
    [Arguments("moderated")]
    [Arguments("deleted")]
    [Arguments("actor-suspended")]
    public async Task AnonymousAddOnCatalogRequiresCurrentPublicEventWhileRetainingOwnerManagement(string withdrawal)
    {
        await using var factory = new NativeEventSeriesFactory();
        Guid eventId;
        Guid privateEventId;
        Guid foreignEventId;
        Guid ownerId;
        Guid actorId;
        Guid catalogId;
        string canary = Guid.CreateVersion7().ToString("N");
        using (var scope = Scope(factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
            ownerId = owner.UserId;
            actorId = owner.ActorId;
            var publicEvent = new EventBuilder().WithTenantId(owner.TenantId).WithActorId(actorId)
                .WithStatus(EventStatusEnum.Published).Build();
            var privateEvent = new EventBuilder().WithTenantId(owner.TenantId).WithActorId(actorId)
                .WithStatus(EventStatusEnum.Published).WithVisibility(VisibilityTypeEnum.Private).Build();
            foreach (var parent in new[] { publicEvent, privateEvent })
            {
                parent.ParticipationConfiguration!.Reconfigure((int)ParticipationHandlingModeEnum.PlatformManaged,
                    (int)AdvanceRegistrationObligationEnum.Optional, (int)IdentityAccessModeEnum.AccountRequired, null);
                db.Events.Add(parent);
                var catalog = CreateVisibilityCatalog(parent, canary);
                db.EventAddOnCatalogVersions.Add(catalog);
            }
            await db.SaveChangesAsync();
            eventId = publicEvent.Id;
            privateEventId = privateEvent.Id;
            catalogId = await db.EventAddOnCatalogVersions.Where(item => item.EventId == eventId)
                .Select(item => item.Id).SingleAsync();
            using var foreignScope = Scope(factory, tenantId: foreign.TenantId);
            var foreignDb = foreignScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            foreignDb.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = foreign.TenantId,
                UserId = foreign.UserId,
                Tenant = await foreignDb.Tenants.SingleAsync(item => item.Id == foreign.TenantId),
                User = await foreignDb.Users.SingleAsync(item => item.Id == foreign.UserId),
                ActorId = foreign.ActorId,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            var foreignEvent = new EventBuilder().WithTenantId(foreign.TenantId).WithActorId(foreign.ActorId)
                .WithStatus(EventStatusEnum.Published).Build();
            foreignEvent.ParticipationConfiguration!.Reconfigure((int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Optional, (int)IdentityAccessModeEnum.AccountRequired, null);
            foreignDb.Events.Add(foreignEvent);
            foreignDb.EventAddOnCatalogVersions.Add(CreateVisibilityCatalog(foreignEvent, canary));
            await foreignDb.SaveChangesAsync();
            foreignEventId = foreignEvent.Id;
            await Assert.That(await foreignScope.ServiceProvider
                .GetRequiredService<Explore.Application.Contracts.Persistence.IEventRepository>()
                .IsPubliclyEligibleAsync(foreign.TenantId, foreignEventId, CancellationToken.None)).IsTrue();
        }
        using var anonymous = Client(factory);
        using var ownerClient = Client(factory, ownerId);
        string route = $"/api/events/{eventId}/add-ons";
        using (var visible = await anonymous.GetAsync(route))
        {
            await Assert.That(visible.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await visible.Content.ReadAsStringAsync());
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(catalogId);
            await Assert.That(body.RootElement.GetProperty("items")[0].GetProperty("name").GetString()).IsEqualTo(canary);
            await Assert.That(body.RootElement.GetProperty("items")[0].GetProperty("maximumSelectableQuantity").GetInt32())
                .IsEqualTo(7);
        }
        foreach (Guid hidden in new[] { privateEventId, foreignEventId })
        {
            using var response = await anonymous.GetAsync($"/api/events/{hidden}/add-ons");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(canary);
        }
        using (var deniedManagement = await anonymous.GetAsync($"/api/events/{privateEventId}/add-ons/management"))
            await Assert.That(deniedManagement.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var management = await ownerClient.GetAsync($"/api/events/{privateEventId}/add-ons/management"))
        {
            await Assert.That(management.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await management.Content.ReadAsStringAsync()).Contains(canary);
        }
        using (var scope = Scope(factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var parent = await db.Events.SingleAsync(item => item.Id == eventId);
            switch (withdrawal)
            {
                case "private": parent.VisibilityTypeId = (int)VisibilityTypeEnum.Private; break;
                case "archived": parent.Archive(DateTime.UtcNow); break;
                case "moderated": parent.ApplyLightModeration(DateTime.UtcNow); break;
                case "deleted": parent.IsDeleted = true; break;
                case "actor-suspended":
                    (await db.Actors.SingleAsync(item => item.Id == actorId)).IsSuspended = true;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(withdrawal));
            }
            await db.SaveChangesAsync();
        }
        using var unavailable = await anonymous.GetAsync(route);
        await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        string problemBody = await unavailable.Content.ReadAsStringAsync();
        await Assert.That(problemBody).DoesNotContain(canary);
        using var problem = JsonDocument.Parse(problemBody);
        await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo("event_add_on_catalog_unavailable");
        using (var scope = Scope(factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var retained = await db.EventAddOnCatalogVersions.AsNoTracking().Include(item => item.Items)
                .SingleAsync(item => item.Id == catalogId);
            await Assert.That(retained.IsPublished).IsTrue();
            await Assert.That(retained.Items.Single().InventoryCapacity).IsEqualTo(7);
        }
    }

    private static EventAddOnCatalogVersion CreateVisibilityCatalog(Explore.Domain.Event parent, string name)
    {
        var catalog = EventAddOnCatalogVersion.Create(parent.TenantId, parent.Id, "EUR", 1);
        catalog.AddItem(EventAddOnCatalogItem.Create(Guid.CreateVersion7(), parent.TenantId, catalog.Id,
            name, null, Money.Create(250, "EUR"), 7, "Collect at entry", "Refund on cancellation"));
        catalog.Publish(DateTime.UtcNow);
        return catalog;
    }
}
