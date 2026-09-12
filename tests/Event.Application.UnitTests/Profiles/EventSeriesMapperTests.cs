using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Handlers.Commands;
using Explore.Application.Features.EventSeries.Requests.Commands;
using Explore.Application.Mappings;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventSeriesMapperTests
{
    [Test]
    [Arguments(null, "summer-series")]
    [Arguments("custom", "custom")]
    public async Task Creation_PreservesContentAndTrustedTenantDefaults(string? slug, string expectedSlug)
    {
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var actorId = Guid.Parse("01900000-0000-7000-8000-000000000002");
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000003");
        var seriesId = Guid.Parse("01900000-0000-7000-8000-000000000004");
        var tenants = Substitute.For<ITenantContext>();
        tenants.TenantId.Returns(tenantId);
        var admin = Substitute.For<IAdminContext>();
        admin.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns((Guid?)userId);
        admin.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([tenantId]));
        var repository = Substitute.For<IEventSeriesRepository>();
        EventSeries? stored = null;
        repository.Create(Arg.Any<EventSeries>()).Returns(call =>
        {
            var entity = call.Arg<EventSeries>();
            stored = entity;
            entity.Id = seriesId;
            return entity;
        });
        var handler = new CreateEventSeriesCommandHandler(
            repository, tenants, admin, Substitute.For<IStorageObjectRepository>());

        var result = await handler.Handle(new CreateEventSeriesCommand
        {
            EventSeriesDto = new CreateEventSeriesDto
            {
                Title = "Summer Series", Description = "Weekly workshops", Slug = slug,
                ActorId = actorId, IsPublished = true
            }
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(seriesId);
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Title).IsEqualTo("Summer Series");
        await Assert.That(stored.Description).IsEqualTo("Weekly workshops");
        await Assert.That(stored.Slug).IsEqualTo(expectedSlug);
        await Assert.That(stored.ActorId).IsEqualTo(actorId);
        await Assert.That(stored.IsPublished).IsTrue();
        await Assert.That(stored.TenantId).IsEqualTo(tenantId);
        await Assert.That(stored.VisibilityTypeId).IsEqualTo(1);
        await Assert.That(stored.TotalViews).IsEqualTo(0);
        await Assert.That(stored.CreatedAt).IsEqualTo(default(DateTime));
        await Assert.That(stored.StartDateUtc).IsNull();
        await Assert.That(stored.EndDateUtc).IsNull();
        await Assert.That(stored.Events).IsEmpty();
        await Assert.That(stored.Actor).IsNull();
        await Assert.That(stored.FeaturedImage).IsNull();
        var detail = EventMapper.ToDetail(stored);
        var list = EventMapper.ToListItem(stored);
        stored.Description = "Changed";
        await Assert.That(detail.Description).IsEqualTo("Weekly workshops");
        await Assert.That(detail.ActorDisplayName).IsNull();
        await Assert.That(list.EventCount).IsEqualTo(0);
        await Assert.That(list.ActorDisplayName).IsNull();
    }
}
