using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class EventSeriesDisclosureHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnonymousDetail_DoesNotDiscloseUnpublishedOrPrivateSeries(bool published)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await SeedAsync(factory);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Detail(published ? data.PrivateId : data.DraftId));
        await ProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnonymousDetailAndTop_OnlyDiscloseEligibleNestedEvents(bool top)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await SeedAsync(factory);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(top ? "/api/eventseries/top" : Detail(data.PublicId));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("events").EnumerateArray().Select(row => row.GetProperty("title").GetString()))
            .IsEquivalentTo(new string?[] { "Public nested event" });
        await Assert.That(body.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
    }

    [Test]
    public async Task AnonymousList_OnlyCountsEligibleNestedEvents()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await SeedAsync(factory);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/eventseries");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.GetProperty("_embedded").EnumerateObject().Single().Value.EnumerateArray().ToArray();
        var series = rows.Single(row => row.GetProperty("id").GetGuid() == data.PublicId);
        await Assert.That(series.GetProperty("eventCount").GetInt32()).IsEqualTo(1);
        await Assert.That(rows.Select(row => row.GetProperty("id").GetGuid())).IsEquivalentTo(new[] { data.PublicId });
    }

    [Test]
    public async Task ForeignDeletedAndMissingSeries_RemainPrivate_AndTrackedChildrenCannotPolluteNativeReads()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await SeedAsync(factory);
        using var client = factory.CreateClient();
        foreach (var id in new[] { data.ForeignId, data.DeletedId, Guid.CreateVersion7() })
        {
            using var response = await client.GetAsync(Detail(id));
            await ProblemAsync(response, HttpStatusCode.NotFound);
        }
        using var scope = Scope(factory);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tracked = await db.EventSeries.Include(series => series.Events).SingleAsync(series => series.Id == data.PublicId);
        await Assert.That(tracked.Events.Count).IsGreaterThan(1);
        var port = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSeriesDetailRequest, EventSeriesDto?>>();
        var first = await port.QueryAsync(new(data.PublicId), default);
        await Assert.That(first!.Events.Select(item => item.Id)).IsEquivalentTo(new[] { data.PublicEventId });
        using (var writeScope = Scope(factory))
        {
            var current = await writeScope.ServiceProvider.GetRequiredService<ExploreDbContext>().Events.SingleAsync(item => item.Id == data.PublicEventId);
            current.IsDeleted = true;
            await writeScope.ServiceProvider.GetRequiredService<ExploreDbContext>().SaveChangesAsync();
        }
        await Assert.That((await port.QueryAsync(new(data.PublicId), default))!.Events).IsEmpty();
        await Assert.That(first.Events.Count).IsEqualTo(1);
        using var top = await client.GetAsync("/api/eventseries/top");
        await Assert.That(top.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }
}
