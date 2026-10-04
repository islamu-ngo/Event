using System.Net;
using System.Net.Http.Json;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeStorageObjectHttpTests
{
    [Test]
    public async Task GenericRetirementAcknowledgesCommittedCustodyWithoutDeletingProviderBytes()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);
        var reservation = await ReserveAsync(owner, Upload("retirement"));
        Guid id = (await FinalizeAsync(owner, reservation.Id)).StorageObjectId!.Value;
        string key = factory.Objects.Keys.Single();

        using var admitted = await owner.DeleteAsync($"{Root}/{id}");
        await Assert.That(admitted.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        var result = (await admitted.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(id);
        await Assert.That(factory.Objects[key]).IsEquivalentTo("hello"u8.ToArray());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That(await db.StorageObjects.IgnoreQueryFilters().AnyAsync(row => row.Id == id)).IsFalse();
            var work = await db.Set<StorageObjectDeletionTombstone>().SingleAsync(row => row.Id == id);
            await Assert.That(work.ObjectKey).IsEqualTo(key);
            await Assert.That(work.State).IsEqualTo(StorageObjectDeletionState.Ready);
            await Assert.That(await db.StorageUploadSessions.AnyAsync(row => row.Id == reservation.Id)).IsFalse();
        }

        // A fresh request cannot reconstruct generic authorization from retained tenant-only work.
        using var fresh = await owner.DeleteAsync($"{Root}/{id}");
        await Assert.That(fresh.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(factory.Objects[key]).IsEquivalentTo("hello"u8.ToArray());
    }
}
