using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeStorageObjectHttpTests
{
    [Test]
    public async Task EligibleSourceAdvertisesRetirementInDetailAndCollection()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);
        Guid id = await FinalizeNewObjectAsync(owner, "eligible-links");

        await AssertRetirementLinksAsync(owner, id, deleteExpected: true);
    }

    [Test]
    [Arguments("hidden")]
    [Arguments("soft-deleted")]
    [Arguments("cross-tenant")]
    public async Task PhysicalOwnersHideRetirementWithoutHidingEdit(string ownerState)
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);
        Guid id = await FinalizeNewObjectAsync(owner, $"physical-owner-{ownerState}");
        string privateOwnerName = $"Private {ownerState} owner";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IStorageObjectReferenceRepository>()
                .FenceAsync([id], default);
            var group = new Group { Id = Guid.CreateVersion7(), FullName = privateOwnerName };
            db.GroupTenants.Add(new GroupTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = ownerState == "cross-tenant"
                    ? factory.OtherTenantId
                    : PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                GroupId = group.Id,
                Group = group,
                ApprovalStatusId = (int)ApprovalStatusEnum.Rejected,
                ApprovalStatus = null!,
                IsVisible = false,
                ProfilePictureId = id,
                IsDeleted = ownerState == "soft-deleted",
                DeletedAt = ownerState == "soft-deleted" ? DateTime.UtcNow : null,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        string detailBody = await AssertRetirementLinksAsync(owner, id, deleteExpected: false);
        await Assert.That(detailBody).DoesNotContain(privateOwnerName);
    }

    [Test]
    public async Task InvalidExactTargetHidesRetirementWithoutHidingEdit()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);
        Guid id = await FinalizeNewObjectAsync(owner, "invalid-target-links");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IStorageObjectReferenceRepository>()
                .FenceAsync([id], default);
            var storage = await db.StorageObjects.SingleAsync(row => row.Id == id);
            storage.Provider = StorageProviders.S3Compatible;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await AssertRetirementLinksAsync(owner, id, deleteExpected: false);
    }

    [Test]
    public async Task EarlierRetirementLinkDoesNotBypassTransactionalReferenceRescan()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);
        Guid id = await FinalizeNewObjectAsync(owner, "stale-retirement-link");
        await AssertRetirementLinksAsync(owner, id, deleteExpected: true);

        const string privateOwnerName = "Late private owner";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IStorageObjectReferenceRepository>()
                .FenceAsync([id], default);
            var group = new Group { Id = Guid.CreateVersion7(), FullName = privateOwnerName };
            db.GroupTenants.Add(new GroupTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                GroupId = group.Id,
                Group = group,
                ApprovalStatusId = (int)ApprovalStatusEnum.Rejected,
                ApprovalStatus = null!,
                IsVisible = false,
                ProfilePictureId = id,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        using var refused = await owner.DeleteAsync($"{Root}/{id}");
        await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var json = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("code").GetString())
            .IsEqualTo(FailureCodes.StorageObjectInUse);
        await Assert.That(json.RootElement.GetRawText()).DoesNotContain(privateOwnerName);
    }

    [Test]
    public async Task SourceLessResourceAndTombstoneStatesGrantNoGenericCapabilities()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);

        var reservation = await ReserveAsync(owner, Upload("source-less"));
        await AssertNoGenericCapabilitiesAsync(owner, reservation.Id);
        using (var canceled = await owner.DeleteAsync($"{Root}/upload-sessions/{reservation.Id}"))
            await Assert.That(canceled.StatusCode).IsEqualTo(HttpStatusCode.OK);

        Guid resourceId = await FinalizeNewObjectAsync(owner, "resource-only");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IStorageObjectReferenceRepository>()
                .FenceAsync([resourceId], default);
            var resource = await db.StorageObjects.SingleAsync(row => row.Id == resourceId);
            resource.Purpose = StorageObjectPurposes.EventResource;
            resource.OwningResourceKind = StorageOwningResourceKinds.EventResource;
            resource.OwningResourceId = Guid.CreateVersion7();
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await AssertNoGenericCapabilitiesAsync(owner, resourceId);

        Guid tombstoneId = await FinalizeNewObjectAsync(owner, "tombstone-only");
        using (var retired = await owner.DeleteAsync($"{Root}/{tombstoneId}"))
            await Assert.That(retired.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await AssertNoGenericCapabilitiesAsync(owner, tombstoneId);
    }

    private static async Task<Guid> FinalizeNewObjectAsync(HttpClient owner, string key)
    {
        var reservation = await ReserveAsync(owner, Upload(key));
        return (await FinalizeAsync(owner, reservation.Id)).StorageObjectId!.Value;
    }

    private static async Task<string> AssertRetirementLinksAsync(
        HttpClient owner, Guid id, bool deleteExpected)
    {
        using var detail = await owner.GetAsync($"{Root}/{id}");
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string detailBody = await detail.Content.ReadAsStringAsync();
        using var detailJson = JsonDocument.Parse(detailBody);
        await AssertMutationLinksAsync(detailJson.RootElement, deleteExpected);

        using var collection = await owner.GetAsync(Root);
        await Assert.That(collection.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var collectionJson = JsonDocument.Parse(await collection.Content.ReadAsStringAsync());
        JsonElement item = collectionJson.RootElement.GetProperty("_embedded").GetProperty("items")
            .EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == id);
        await AssertMutationLinksAsync(item, deleteExpected);
        return detailBody;
    }

    private static async Task AssertMutationLinksAsync(JsonElement resource, bool deleteExpected)
    {
        JsonElement links = resource.GetProperty("_links");
        await Assert.That(links.TryGetProperty("edit", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("delete", out _)).IsEqualTo(deleteExpected);
    }

    private static async Task AssertNoGenericCapabilitiesAsync(HttpClient owner, Guid id)
    {
        using (var detail = await owner.GetAsync($"{Root}/{id}"))
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var update = await owner.PatchAsJsonAsync($"{Root}/{id}", new UpdateStorageObjectDto
        {
            Metadata = new() { FullName = "forbidden.txt", SafeDisplayName = "forbidden.txt" }
        }))
            await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var delete = await owner.DeleteAsync($"{Root}/{id}"))
            await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}
