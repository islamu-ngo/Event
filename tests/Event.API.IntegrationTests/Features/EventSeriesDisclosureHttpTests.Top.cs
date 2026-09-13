using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventSeriesDisclosureHttpTests
{
    [Test]
    public async Task Top_RanksOnlyEligibleUpcomingOrUndatedEvents_WithExactOffsetAwareBoundaries()
    {
        var now = new DateTimeOffset(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);
        await using var factory = new NativeEventSeriesFactory { Clock = new SeriesClock(now) };
        var data = await SeedAsync(factory);
        Guid competitorId;
        using (var scope = Scope(factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var originalEvent = await db.Events.SingleAsync(item => item.Id == data.PublicEventId);
            originalEvent.LastSessionEndUtc = now.AddTicks(1).ToOffset(TimeSpan.FromHours(5.5));
            var original = await db.EventSeries.Include(series => series.Actor).Include(series => series.Tenant)
                .SingleAsync(series => series.Id == data.PublicId);
            var competitor = Series("Popular series", true, VisibilityTypeEnum.Public, original.Tenant, original.Actor);
            competitor.TotalViews = 100;
            competitorId = competitor.Id;
            var undated = new EventBuilder().WithTitle("Undated eligible event").WithActorId(data.ActorId)
                .WithTenantId(original.TenantId).WithStatus(EventStatusEnum.Published).Build();
            undated.EventSeriesId = competitor.Id;
            db.AddRange(competitor, undated);
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        using (var first = await client.GetAsync("/api/eventseries/top"))
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var body = await first.Content.ReadFromJsonAsync<JsonElement>();
            // Hidden nested events cannot inflate the original Series' ranking.
            await Assert.That(body.GetProperty("id").GetGuid()).IsEqualTo(competitorId);
        }
        Guid secondId;
        using (var scope = Scope(factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var original = await db.EventSeries.SingleAsync(series => series.Id == data.PublicId);
            var second = new EventBuilder().WithTitle("Second eligible event").WithActorId(data.ActorId)
                .WithTenantId(original.TenantId).WithStatus(EventStatusEnum.Published).Build();
            second.EventSeriesId = data.PublicId;
            second.LastSessionEndUtc = now.AddTicks(1).ToOffset(TimeSpan.FromHours(-8));
            secondId = second.Id;
            var ended = new EventBuilder().WithTitle("Just ended event").WithActorId(data.ActorId)
                .WithTenantId(original.TenantId).WithStatus(EventStatusEnum.Published).Build();
            ended.EventSeriesId = data.PublicId;
            ended.LastSessionEndUtc = now.ToOffset(TimeSpan.FromHours(9));
            db.Events.AddRange(second, ended);
            await db.SaveChangesAsync();
        }
        using var top = await client.GetAsync("/api/eventseries/top");
        await Assert.That(top.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var winner = await top.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(winner.GetProperty("id").GetGuid()).IsEqualTo(data.PublicId);
        await Assert.That(winner.GetProperty("events").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()))
            .IsEquivalentTo(new[] { data.PublicEventId, secondId });
    }

    private sealed class SeriesClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
