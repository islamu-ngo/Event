using System.Net;
using System.Xml.Linq;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Seo;
using Explore.Application.Features.Seo.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using DomainEvent = Explore.Domain.Event;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeSeoHttpTests
{
    [Test]
    public async Task AnonymousSitemap_UsesNativeRepositoryQueryAndPreservesPublicTenantXml()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://sitemap.example.test"), AllowAutoRedirect = false
        });
        var foreignTenantId = Guid.CreateVersion7();
        var created = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var updated = new DateTime(2026, 9, 2, 15, 0, 0, DateTimeKind.Utc);
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), Pii = new() { DisplayName = "Sitemap organizer" },
            ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!, UserId = Guid.CreateVersion7()
        };
        var first = NewEvent(actor, PlatformDefaults.DefaultTenantId, created);
        var second = NewEvent(actor, PlatformDefaults.DefaultTenantId, created);
        second.UpdatedAt = updated;
        var draft = NewEvent(actor, PlatformDefaults.DefaultTenantId, created, EventStatusEnum.Draft);
        var privateEvent = NewEvent(actor, PlatformDefaults.DefaultTenantId, created);
        privateEvent.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
        var deleted = NewEvent(actor, PlatformDefaults.DefaultTenantId, created);
        deleted.IsDeleted = true;
        var foreign = NewEvent(actor, foreignTenantId, created);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EnableTenantFilterBypass("Seed both sitemap tenant boundaries.");
            db.Tenants.Add(new Tenant
            {
                Id = foreignTenantId, FullName = "Foreign sitemap tenant", Slug = "foreign-sitemap",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
            });
            db.Users.Add(new User
            {
                Id = actor.UserId!.Value,
                Pii = new() { Email = "sitemap@example.test", FirstName = "Sitemap", LastName = "Organizer" }
            });
            db.Actors.Add(actor);
            foreach (var tenantId in new[] { PlatformDefaults.DefaultTenantId, foreignTenantId })
                db.TenantUsers.Add(new TenantUser
                {
                    TenantId = tenantId, Tenant = null!, UserId = actor.UserId.Value, User = null!, ActorId = actor.Id,
                    StatusId = (int)TenantUserStatusEnum.Active
                });
            db.Events.AddRange(first, second, draft, privateEvent, deleted, foreign);
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/sitemap.xml");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/xml");
        await Assert.That(response.Content.Headers.ContentType.CharSet).IsEqualTo("utf-8");
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        await Assert.That(document.Root!.Name).IsEqualTo(ns + "urlset");
        var entries = document.Root.Elements(ns + "url").ToArray();
        await Assert.That(entries.Length).IsEqualTo(9);
        var locations = entries.Select(entry => entry.Element(ns + "loc")!.Value).ToArray();
        foreach (var path in new[] { "/", "/events", "/about", "/contact", "/privacy", "/terms", "/community-guidelines" })
            await Assert.That(locations).Contains("https://sitemap.example.test" + path);
        var firstEntry = entries.Single(entry => entry.Element(ns + "loc")!.Value == $"https://sitemap.example.test/events/{first.Id}");
        var secondEntry = entries.Single(entry => entry.Element(ns + "loc")!.Value == $"https://sitemap.example.test/events/{second.Id}");
        await Assert.That(firstEntry.Element(ns + "lastmod")!.Value).IsEqualTo("2026-08-01");
        await Assert.That(secondEntry.Element(ns + "lastmod")!.Value).IsEqualTo("2026-09-02");
        await Assert.That(secondEntry.Element(ns + "changefreq")!.Value).IsEqualTo("weekly");
        await Assert.That(secondEntry.Element(ns + "priority")!.Value).IsEqualTo("0.8");

        using var queryScope = factory.Services.CreateScope();
        var services = queryScope.ServiceProvider;
        var tenant = services.GetRequiredService<ITenantContextAccessor>();
        tenant.SetTenant(PlatformDefaults.DefaultTenantId);
        var query = services.GetRequiredService<IQueryHandler<GetSitemapEventsQuery, IReadOnlyList<SitemapEventEntryDto>>>();
        foreach (var limit in new[] { int.MinValue, 0, 1 })
        {
            var bounded = await query.QueryAsync(new(limit), default);
            await Assert.That(bounded.Count).IsEqualTo(1);
            await Assert.That(bounded[0]).IsEqualTo(new SitemapEventEntryDto(second.Id, updated));
        }
        var all = await query.QueryAsync(new(int.MaxValue), default);
        await Assert.That(all.Count).IsEqualTo(2);
        await Assert.That(all[1]).IsEqualTo(new SitemapEventEntryDto(first.Id, created));
        await Assert.That(services.GetRequiredService<ExploreDbContext>().ChangeTracker.Entries<DomainEvent>().Any()).IsFalse();
        tenant.SetTenant(foreignTenantId);
        var foreignEntries = await query.QueryAsync(new(), default);
        await Assert.That(foreignEntries.Count).IsEqualTo(1);
        await Assert.That(foreignEntries[0].EventId).IsEqualTo(foreign.Id);
        tenant.SetTenant(Guid.CreateVersion7());
        await Assert.That((await query.QueryAsync(new(), default)).Count).IsEqualTo(0);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.That(async () => { await query.QueryAsync(new(), cancellation.Token); }).Throws<OperationCanceledException>();
    }

    private static DomainEvent NewEvent(Actor actor, Guid tenantId, DateTime created, EventStatusEnum status = EventStatusEnum.Published) => new(status)
    {
        Id = Guid.CreateVersion7(), Title = "Sitemap event", PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
        ActorId = actor.Id, Actor = actor, TenantId = tenantId, Tenant = null!,
        VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!,
        EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
        EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
        CreatedAt = created, ConcurrencyStamp = Guid.CreateVersion7()
    };
}
