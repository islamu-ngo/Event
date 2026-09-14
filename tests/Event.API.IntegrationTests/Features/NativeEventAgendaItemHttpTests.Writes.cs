using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Application.DTOs.EventAgendaItem;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventAgendaItemHttpTests
{
    [Test]
    public async Task Writes_RejectAnonymousForeignForgedAndStaleRequestsWithoutMutation()
    {
        await using var factory = await AgendaFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        using var anonymous = factory.CreateClient();
        var before = await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId));
        var stamp = before.GetProperty("concurrencyStamp").GetGuid();
        using (var denied = await anonymous.PostAsJsonAsync("/api/eventagendaitem", Input(factory.PublicId)))
            await ProblemAsync(denied, HttpStatusCode.Unauthorized);
        using (var denied = await outsider.PostAsJsonAsync("/api/eventagendaitem", Input(factory.PublicId)))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        foreach (var parent in new[] { factory.ForeignId, factory.DeletedId, Guid.CreateVersion7() })
        {
            using var denied = await owner.PostAsJsonAsync("/api/eventagendaitem", Input(parent));
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        }
        foreach (var header in new string?[] { null, "invalid", "*", stamp.ToString(), $"W/\"{stamp}\"", $"\"{Guid.Empty}\"" })
        {
            using var invalid = await PatchAsync(owner, factory.ItemId, new { sortOrder = new { value = 8 } }, header);
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        }
        using (var stale = await PatchAsync(owner, factory.ItemId, new { sortOrder = new { value = 8 } }, $"\"{Guid.CreateVersion7()}\""))
            await ProblemAsync(stale, HttpStatusCode.Conflict);
        using (var denied = await PatchAsync(outsider, factory.ItemId, new { sortOrder = new { value = 8 } }, $"\"{stamp}\""))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var denied = await outsider.DeleteAsync(Detail(factory.ItemId)))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        foreach (var hidden in new[] { factory.ForeignItemId, factory.DeletedParentItemId, Guid.CreateVersion7() })
        {
            using var deniedUpdate = await PatchAsync(owner, hidden, new { sortOrder = new { value = 8 } }, $"\"{stamp}\"");
            await ProblemAsync(deniedUpdate, HttpStatusCode.Forbidden);
            using var deniedDelete = await owner.DeleteAsync(Detail(hidden));
            await ProblemAsync(deniedDelete, HttpStatusCode.Forbidden);
        }
        using (var invalid = await PatchAsync(owner, factory.ItemId,
            new { schedule = new { startTime = Start, endTime = Start } }, $"\"{stamp}\""))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        await Assert.That(JsonElement.DeepEquals(await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId)), before)).IsTrue();
        await Assert.That(Items(await GetAsync(owner, Managed(factory.PublicId))).Length).IsEqualTo(1);
    }

    [Test]
    public async Task Moves_RequireBothPersistedSourceAndDestinationAuthority()
    {
        await using var factory = await AgendaFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        var before = await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId));
        var stamp = before.GetProperty("concurrencyStamp").GetGuid();
        // The attacker really owns the destination; that authority cannot replace the source.
        using (var created = await outsider.PostAsJsonAsync("/api/eventagendaitem", Input(factory.UnownedId)))
            await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using (var forged = await PatchAsync(outsider, factory.ItemId,
            new { @event = new { eventId = factory.UnownedId } }, $"\"{stamp}\""))
            await ProblemAsync(forged, HttpStatusCode.Forbidden);
        using (var denied = await PatchAsync(owner, factory.ItemId,
            new { @event = new { eventId = factory.UnownedId } }, $"\"{stamp}\""))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        foreach (var parent in new[] { factory.ForeignId, factory.DeletedId, Guid.CreateVersion7() })
        {
            using var invalid = await PatchAsync(owner, factory.ItemId,
                new { @event = new { eventId = parent } }, $"\"{stamp}\"");
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        }
        await Assert.That(JsonElement.DeepEquals(await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId)), before)).IsTrue();
        await Assert.That(Items(await GetAsync(outsider, Managed(factory.UnownedId))).Length).IsEqualTo(1);
    }

    [Test]
    public async Task OwnerWrites_PreserveMoveReprojectionPlacementPatchPresenceAndDelete()
    {
        await using var factory = await AgendaFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        using var created = await owner.PostAsJsonAsync("/api/eventagendaitem", Input(factory.PublicId));
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        await Assert.That(created.Headers.Location!.AbsolutePath).IsEqualTo(Detail(id));
        var before = await GetAsync(owner, ManagedDetail(factory.PublicId, id));
        var stamp = before.GetProperty("concurrencyStamp").GetGuid();
        using (var moved = await PatchAsync(owner, id, new { @event = new { eventId = factory.PrivateId } }, $"\"{stamp}\""))
            await Assert.That(moved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var after = await GetAsync(owner, ManagedDetail(factory.PrivateId, id));
        await Assert.That(after.GetProperty("eventId").GetGuid()).IsEqualTo(factory.PrivateId);
        await Assert.That(after.GetProperty("eventDayId").GetGuid()).IsEqualTo(factory.DestinationDayId);
        await Assert.That(after.GetProperty("startTime").GetDateTimeOffset()).IsEqualTo(Start);
        await Assert.That(after.GetProperty("localStartDate").GetString()).IsEqualTo("2026-07-20");
        await Assert.That(after.GetProperty("localStartTime").GetString()).IsEqualTo("19:30:00");
        await Assert.That(after.GetProperty("description").GetString()).IsEqualTo("Keep until cleared");
        await Assert.That(after.GetProperty("concurrencyStamp").GetGuid()).IsNotEqualTo(stamp);
        await Assert.That(Items(await GetAsync(owner, Managed(factory.PublicId))).Any(item => item.GetProperty("id").GetGuid() == id)).IsFalse();
        using (var hidden = await owner.GetAsync(Detail(id)))
            await ProblemAsync(hidden, HttpStatusCode.NotFound);
        using (var changed = await PatchAsync(owner, id, new
        {
            title = new { value = "Rescheduled" },
            description = new { value = new { hasValue = true, value = (string?)null } },
            location = new { value = new { hasValue = true, value = factory.LocationId } },
            schedule = new { startTime = Start.AddDays(1), endTime = Start.AddDays(1).AddHours(2) }
        }, $"\"{after.GetProperty("concurrencyStamp").GetGuid()}\""))
            await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var rescheduled = await GetAsync(owner, ManagedDetail(factory.PrivateId, id));
        var dto = rescheduled.Deserialize<EventAgendaItemDto>(JsonOptions)!;
        await Assert.That(dto.EventDayId).IsNull();
        await Assert.That(dto.Description).IsNull();
        await Assert.That(rescheduled.GetProperty("locationId").GetGuid()).IsEqualTo(factory.LocationId);
        await Assert.That(rescheduled.GetProperty("localStartDate").GetString()).IsEqualTo("2026-07-21");
        using (var deleted = await owner.DeleteAsync(Detail(id)))
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using var missing = await owner.GetAsync(ManagedDetail(factory.PrivateId, id));
        await ProblemAsync(missing, HttpStatusCode.NotFound);
        await Assert.That(await PlacementIdsAsync(factory, factory.PrivateId)).IsEmpty();
        await Assert.That(await PlacementIdsAsync(factory, factory.PublicId)).IsEquivalentTo(new[] { factory.PlacementId });
    }

    private static object Input(Guid parent) => new
    {
        eventId = parent, title = "Opening", description = "Keep until cleared",
        startTime = Start, endTime = Start.AddHours(1), sortOrder = 4
    };
}
