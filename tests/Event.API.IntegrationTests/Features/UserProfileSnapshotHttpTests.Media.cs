using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Handlers.Commands;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class UserProfileSnapshotHttpTests
{
    [Test]
    public async Task ProfileMediaPersistsManagedIdentityAndDisclosesExternalReplacementExplicitly()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        TenantScenarioSeed.TenantScenarioResult owner;
        Guid imageId = Guid.CreateVersion7();
        await using (var db = factory.CreateDatabase())
        {
            owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            ProfileMediaSeed.Add(db, imageId, owner.TenantId, owner.UserId);
            await db.SaveChangesAsync();
        }

        await Assert.That((await SetProfileImageAsync(factory, owner,
            new UpdateUserProfileImageDto { ProfilePictureId = imageId })).IsSuccess).IsTrue();
        await using (var db = factory.CreateDatabase())
        {
            var actor = await db.Actors.SingleAsync(value => value.Id == owner.ActorId);
            await Assert.That(actor.Pii.ProfilePictureStorageObjectId).IsEqualTo(imageId);
            await Assert.That(actor.Pii.ExternalProfilePictureUri).IsNull();
        }
        using var client = factory.CreateClient();
        using (var response = await client.GetAsync($"/api/actor/{owner.ActorId}"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            string json = await response.Content.ReadAsStringAsync();
            using var body = JsonDocument.Parse(json);
            await Assert.That(body.RootElement.GetProperty("profilePictureStorageObjectId").GetGuid()).IsEqualTo(imageId);
            await Assert.That(body.RootElement.TryGetProperty("externalProfilePictureUri", out _)).IsFalse();
            await Assert.That(body.RootElement.GetProperty("profilePictureUri").GetString())
                .IsEqualTo($"/api/storageobject/{imageId}/public");
            await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("edit", out _)).IsFalse();
            await Assert.That(json).DoesNotContain("private-bucket");
            await Assert.That(json).DoesNotContain("raw-key");
        }

        string external = $"https://foreign.example.test/api/storageobject/{imageId}/public";
        await Assert.That((await SetProfileImageAsync(factory, owner,
            new UpdateUserProfileImageDto { ExternalProfilePictureUri = external })).IsSuccess).IsTrue();
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(owner.TenantId);
            var dto = await ActivatorUtilities.CreateInstance<GetUserRequestHandler>(scope.ServiceProvider)
                .QueryAsync(new GetUserRequest(owner.UserId));
            await Assert.That(dto!.ProfilePictureStorageObjectId).IsNull();
            await Assert.That(dto.ExternalProfilePictureUri).IsEqualTo(external);
            await Assert.That(dto.ProfileImageUri).IsEqualTo(external);
        }
        await Assert.That((await SetProfileImageAsync(factory, owner, new UpdateUserProfileImageDto())).IsSuccess).IsTrue();
        await using var finalScope = factory.Services.CreateAsyncScope();
        finalScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(owner.TenantId);
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var cleared = await finalDb.Actors.SingleAsync(value => value.Id == owner.ActorId);
        await Assert.That(cleared.Pii.ProfilePictureStorageObjectId).IsNull();
        await Assert.That(cleared.Pii.ExternalProfilePictureUri).IsNull();
        var retained = await finalDb.StorageObjects.SingleAsync(value => value.Id == imageId);
        await Assert.That(retained.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
        await Assert.That(retained.SourceUri).IsEqualTo("https://provider.example.test/private-bucket/raw-key");
    }

    [Test]
    public async Task ProfileSelectionDeniesForeignPrivateUnsafeAndResourceOwnedImagesWithoutChangingTheProfile()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        TenantScenarioSeed.TenantScenarioResult owner;
        List<Guid> denied = [];
        await using (var db = factory.CreateDatabase())
        {
            owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            var foreign = new TenantBuilder().WithId(Guid.CreateVersion7()).WithSlug("foreign-profile").Build();
            db.Tenants.Add(foreign);
            var other = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            foreach (string state in new[] { "foreign", "private", "unsafe", "quarantined", "owned", "resource" })
            {
                var image = ProfileMediaSeed.Add(db, Guid.CreateVersion7(),
                    state == "foreign" ? foreign.Id : owner.TenantId, owner.UserId);
                if (state is "private" or "resource") image.Visibility = StorageObjectVisibilities.PrivateOwner;
                if (state == "unsafe")
                {
                    image.Purpose = StorageObjectPurposes.Attachment;
                    image.Visibility = StorageObjectVisibilities.AuthenticatedTenant;
                    image.ContentType = "image/svg+xml";
                    image.Extension = "svg";
                }
                if (state == "quarantined") image.LifecycleState = StorageObjectLifecycleStates.Quarantined;
                if (state == "owned") image.ActorId = other.ActorId;
                if (state == "resource")
                {
                    image.Purpose = StorageObjectPurposes.EventResource;
                    image.OwningResourceKind = StorageOwningResourceKinds.EventResource;
                    image.OwningResourceId = Guid.CreateVersion7();
                }
                denied.Add(image.Id);
            }
            (await db.Actors.SingleAsync(value => value.Id == owner.ActorId)).Pii.ExternalProfilePictureUri =
                "https://foreign.example.test/original.png";
            await db.SaveChangesAsync();
        }
        foreach (Guid id in denied)
        {
            var response = await SetProfileImageAsync(factory, owner,
                new UpdateUserProfileImageDto { ProfilePictureId = id });
            await Assert.That(response.IsSuccess).IsFalse();
        }
        await Assert.That((await SetProfileImageAsync(factory, owner, new UpdateUserProfileImageDto
        {
            ProfilePictureId = denied[0],
            ExternalProfilePictureUri = "https://foreign.example.test/replacement.png"
        })).IsSuccess).IsFalse();
        await using var verify = factory.CreateDatabase();
        var unchanged = await verify.Actors.SingleAsync(value => value.Id == owner.ActorId);
        await Assert.That(unchanged.Pii.ProfilePictureStorageObjectId).IsNull();
        await Assert.That(unchanged.Pii.ExternalProfilePictureUri).IsEqualTo("https://foreign.example.test/original.png");
    }

    [Test]
    public async Task CachedUserProfileCannotDiscloseRevokedOrForeignTenantManagedMedia()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        TenantScenarioSeed.TenantScenarioResult owner;
        Guid imageId = Guid.CreateVersion7();
        Guid foreignTenantId = Guid.CreateVersion7();
        await using (var db = factory.CreateDatabase())
        {
            owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            db.Tenants.Add(new TenantBuilder().WithId(foreignTenantId).WithSlug("other-profile").Build());
            ProfileMediaSeed.Add(db, imageId, owner.TenantId, owner.UserId);
            await db.SaveChangesAsync();
        }
        await SetProfileImageAsync(factory, owner, new UpdateUserProfileImageDto { ProfilePictureId = imageId });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(owner.TenantId);
            var dto = await ActivatorUtilities.CreateInstance<GetUserRequestHandler>(scope.ServiceProvider)
                .QueryAsync(new GetUserRequest(owner.UserId));
            await Assert.That(dto!.ProfilePictureStorageObjectId).IsEqualTo(imageId);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(foreignTenantId);
            var dto = await ActivatorUtilities.CreateInstance<GetUserRequestHandler>(scope.ServiceProvider)
                .QueryAsync(new GetUserRequest(owner.UserId));
            await Assert.That(dto!.ProfilePictureStorageObjectId).IsNull();
            await Assert.That(dto.ProfileImageUri).IsNull();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(owner.TenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            (await db.StorageObjects.SingleAsync(value => value.Id == imageId)).Visibility =
                StorageObjectVisibilities.PrivateOwner;
            await db.SaveChangesAsync();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(owner.TenantId);
            var dto = await ActivatorUtilities.CreateInstance<GetUserRequestHandler>(scope.ServiceProvider)
                .QueryAsync(new GetUserRequest(owner.UserId));
            await Assert.That(dto!.ProfilePictureStorageObjectId).IsNull();
            await Assert.That(dto.ProfileImageUri).IsNull();
        }
    }

    [Test]
    public async Task DatabaseRejectsAmbiguousProfileOwnershipOutsideTheDomainMutation()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var db = factory.CreateDatabase();
        var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var image = ProfileMediaSeed.Add(db, Guid.CreateVersion7(), owner.TenantId);
        await db.SaveChangesAsync();
        await Assert.That(async () => await db.ActorPii.Where(value => value.ActorId == owner.ActorId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ProfilePictureStorageObjectId, image.Id)
                .SetProperty(value => value.ExternalProfilePictureUri, "https://foreign.example.test/ambiguous.png")))
            .Throws<SqliteException>();
    }

    private static async Task<BaseCommandResponse<Guid>> SetProfileImageAsync(
        LocalAdmissionWebApplicationFactory factory,
        TenantScenarioSeed.TenantScenarioResult owner,
        UpdateUserProfileImageDto image)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(owner.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var user = await db.Users.SingleAsync(value => value.Id == owner.UserId);
        return await ActivatorUtilities.CreateInstance<UpdateUserCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new UpdateUserCommand
            {
                UserId = owner.UserId,
                ExpectedConcurrencyStamp = user.ConcurrencyStamp,
                UpdateUserDto = new UpdateUserDto { ProfileImage = image }
            });
    }
}
