using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Caching;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Commands;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventSeriesDisclosureHttpTests
{
    [Test]
    public async Task CreateDelete_RequirePersistedCurrentTenantAdmin_AndPreserveDefaultsAndValidation()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        using var owner = Client(factory, data.OwnerId);
        using var anonymous = factory.CreateClient();
        var input = new { title = "Summer Series", description = "Workshops", actorId = data.ActorId, isPublished = true };
        using (var denied = await anonymous.PostAsJsonAsync("/api/eventseries", input))
            await ProblemAsync(denied, HttpStatusCode.Unauthorized);
        using (var denied = await owner.PostAsJsonAsync("/api/eventseries", input))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var denied = await owner.DeleteAsync(Detail(data.PublicId)))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var invalid = await admin.PostAsJsonAsync("/api/eventseries", new { title = "", actorId = data.ActorId }))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        foreach (var slug in new string?[] { null, "custom" })
        {
            using var created = await admin.PostAsJsonAsync("/api/eventseries", new
            { input.title, input.description, input.actorId, input.isPublished, slug });
            await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await Assert.That(created.Headers.Location!.AbsolutePath).IsEqualTo(Detail(id));
            var detail = await DetailAsync(admin, id);
            await Assert.That(detail.GetProperty("slug").GetString()).IsEqualTo(slug ?? "summer-series");
            await Assert.That(detail.GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
            await Assert.That(detail.GetProperty("actorId").GetGuid()).IsEqualTo(data.ActorId);
            await Assert.That(detail.GetProperty("events").GetArrayLength()).IsEqualTo(0);
            using (var deleted = await admin.DeleteAsync(Detail(id)))
                await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using (var missing = await admin.GetAsync(Detail(id)))
                await ProblemAsync(missing, HttpStatusCode.NotFound);
            using var repeated = await admin.DeleteAsync(Detail(id));
            await ProblemAsync(repeated, HttpStatusCode.NotFound);
        }
        using (var foreign = await admin.DeleteAsync(Detail(data.ForeignId)))
            await ProblemAsync(foreign, HttpStatusCode.NotFound);
        using (var scope = Scope(factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var membership = await db.TenantUsers.SingleAsync(row => row.Id == data.AdminMembershipId);
            membership.StatusId = (int)TenantUserStatusEnum.Suspended;
            await db.SaveChangesAsync();
        }
        // A still-authenticated principal and its claims do not replace live membership.
        using (var revoked = await admin.PostAsJsonAsync("/api/eventseries", input))
            await ProblemAsync(revoked, HttpStatusCode.Forbidden);
        using (var revoked = await admin.DeleteAsync(Detail(data.PublicId)))
            await ProblemAsync(revoked, HttpStatusCode.Forbidden);
        await Assert.That((await DetailAsync(anonymous, data.PublicId)).GetProperty("title").GetString()).IsEqualTo("Public series");
    }

    [Test]
    public async Task RegisteredUpdate_EditsDraftAndPrivateSeriesWithoutMakingTheirReadsPublic()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        using var owner = Client(factory, data.OwnerId);
        foreach (var id in new[] { data.DraftId, data.PrivateId })
        {
            Guid stamp;
            using (var readScope = Scope(factory))
                stamp = (await readScope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Persistence.IEventSeriesRepository>()
                    .GetForUpdateAsync(id, PlatformDefaults.DefaultTenantId, default))!.ConcurrencyStamp;
            using (var denied = await PatchAsync(owner, id, new { title = new { value = "Denied" } }, stamp))
                await ProblemAsync(denied, HttpStatusCode.Forbidden);
            using (var changed = await PatchAsync(admin, id, new { title = new { value = "Edited before publication" } }, stamp))
                await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using (var hidden = await admin.GetAsync(Detail(id)))
                await ProblemAsync(hidden, HttpStatusCode.NotFound);
            using var scope = Scope(factory);
            var persisted = await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Persistence.IEventSeriesRepository>()
                .GetForUpdateAsync(id, PlatformDefaults.DefaultTenantId, default);
            await Assert.That(persisted!.Title).IsEqualTo("Edited before publication");
            await Assert.That(persisted.ConcurrencyStamp).IsNotEqualTo(stamp);
            await Assert.That(persisted.ActorId).IsEqualTo(data.ActorId);
        }
        foreach (var id in new[] { data.ForeignId, data.DeletedId, Guid.CreateVersion7() })
        {
            using var denied = await PatchAsync(admin, id, new { title = new { value = "Denied" } }, Guid.CreateVersion7());
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        }
        Guid draftStamp;
        using (var scope = Scope(factory))
            draftStamp = (await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Persistence.IEventSeriesRepository>()
                .GetForUpdateAsync(data.DraftId, PlatformDefaults.DefaultTenantId, default))!.ConcurrencyStamp;
        using (var published = await PatchAsync(admin, data.DraftId, new { publication = new { isPublished = true } }, draftStamp))
            await Assert.That(published.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await DetailAsync(admin, data.DraftId)).GetProperty("title").GetString()).IsEqualTo("Edited before publication");
    }

    [Test]
    public async Task ImagesAndPatches_ValidateMetadataPreserveOmissionClearReplacementAndCurrentRevision()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        var image = await ImageAsync(factory, data);
        foreach (var invalid in new[]
        {
            await ImageAsync(factory, data, visibility: StorageObjectVisibilities.PrivateOwner),
            await ImageAsync(factory, data, state: StorageObjectLifecycleStates.Quarantined),
            await ImageAsync(factory, data, contentType: "image/svg+xml", extension: "svg"),
            await ImageAsync(factory, data, foreign: true), Guid.CreateVersion7()
        })
        {
            using var rejected = await admin.PostAsJsonAsync("/api/eventseries", new
            { title = "Invalid image", actorId = data.ActorId, featuredImageId = invalid });
            await ProblemAsync(rejected, HttpStatusCode.BadRequest);
        }
        using var created = await admin.PostAsJsonAsync("/api/eventseries", new
        { title = "Image series", description = "Original", actorId = data.ActorId, featuredImageId = image, isPublished = true });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var before = await DetailAsync(admin, id);
        var stamp = before.GetProperty("concurrencyStamp").GetGuid();
        await Assert.That(before.GetProperty("featuredImageId").GetGuid()).IsEqualTo(image);
        using (var missingHeader = await admin.PatchAsJsonAsync(Detail(id), new { title = new { value = "New" } }))
            await ProblemAsync(missingHeader, HttpStatusCode.BadRequest);
        using (var stale = await PatchAsync(admin, id, new { title = new { value = "New" } }, Guid.CreateVersion7()))
            await ProblemAsync(stale, HttpStatusCode.Conflict);
        using (var empty = await PatchAsync(admin, id, new { }, stamp))
            await ProblemAsync(empty, HttpStatusCode.BadRequest);
        using (var unspecified = await PatchAsync(admin, id, new { description = new { } }, stamp))
            await ProblemAsync(unspecified, HttpStatusCode.BadRequest);
        using (var invalid = await PatchAsync(admin, id,
            new { featuredImage = new { value = new { hasValue = true, value = Guid.CreateVersion7() } } }, stamp))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        await Assert.That(JsonElement.DeepEquals(await DetailAsync(admin, id), before)).IsTrue();
        using (var omitted = await PatchAsync(admin, id, new { title = new { value = "New" } }, stamp))
            await Assert.That(omitted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var preserved = await DetailAsync(admin, id);
        await Assert.That(preserved.GetProperty("featuredImageId").GetGuid()).IsEqualTo(image);
        await Assert.That(preserved.GetProperty("description").GetString()).IsEqualTo("Original");
        await Assert.That(preserved.GetProperty("concurrencyStamp").GetGuid()).IsNotEqualTo(stamp);
        using (var clear = await PatchAsync(admin, id, new
        {
            description = new { value = new { hasValue = true, value = (string?)null } },
            featuredImage = new { value = new { hasValue = true, value = (Guid?)null } },
            slug = new { value = new { hasValue = true, value = (string?)null } }
        }, preserved.GetProperty("concurrencyStamp").GetGuid()))
            await Assert.That(clear.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var cleared = await DetailAsync(admin, id);
        await Assert.That(cleared.TryGetProperty("featuredImageId", out _)).IsFalse();
        await Assert.That(cleared.TryGetProperty("description", out _)).IsFalse();
        using (var replace = await PatchAsync(admin, id, new
        {
            description = new { value = new { hasValue = true, value = "Replacement" } },
            featuredImage = new { value = new { hasValue = true, value = image } }
        }, cleared.GetProperty("concurrencyStamp").GetGuid()))
            await Assert.That(replace.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var replaced = await DetailAsync(admin, id);
        await Assert.That(replaced.GetProperty("description").GetString()).IsEqualTo("Replacement");
        await Assert.That(replaced.GetProperty("featuredImageId").GetGuid()).IsEqualTo(image);
    }

    [Test]
    public async Task NativeUpdate_InvalidatesExistingEventCachesOnlyAfterSuccess()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await SeedAsync(factory);
        using var scope = Scope(factory, data.AdminId);
        var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
        var detailKey = $"event:detail:{data.PublicEventId}";
        var localKey = $"series-test-list:{data.PublicId}";
        var foreignKey = $"series-test-list:{data.ForeignId}";
        await cache.SetAsync(detailKey, 1);
        await cache.SetAsync(localKey, 1, tags: [CacheTags.EventListByTenant(PlatformDefaults.DefaultTenantId)]);
        await cache.SetAsync(foreignKey, 1, tags: [CacheTags.EventListByTenant(data.ForeignTenantId)]);
        var read = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSeriesDetailRequest, EventSeriesDto?>>();
        var original = (await read.QueryAsync(new(data.PublicId), default))!;
        var update = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventSeriesCommand, BaseCommandResponse<Guid>>>();
        var invalid = new UpdateEventSeriesCommand
        { EventSeriesId = data.PublicId, ExpectedConcurrencyStamp = original.ConcurrencyStamp, EventSeriesDto = new() };
        await Assert.That((await update.ExecuteAsync(invalid, default)).IsSuccess).IsFalse();
        await Assert.That(await cache.GetOrCreateAsync(detailKey, _ => ValueTask.FromResult(2))).IsEqualTo(1);
        await Assert.That(await cache.GetOrCreateAsync(localKey, _ => ValueTask.FromResult(2))).IsEqualTo(1);
        var valid = invalid with { EventSeriesDto = new() { Title = new() { Value = "Updated" } } };
        await Assert.That((await update.ExecuteAsync(valid, default)).IsSuccess).IsTrue();
        await Assert.That(await cache.GetOrCreateAsync(detailKey, _ => ValueTask.FromResult(2))).IsEqualTo(2);
        await Assert.That(await cache.GetOrCreateAsync(localKey, _ => ValueTask.FromResult(2))).IsEqualTo(2);
        await Assert.That(await cache.GetOrCreateAsync(foreignKey, _ => ValueTask.FromResult(2))).IsEqualTo(1);
        await Assert.That(original.Title).IsEqualTo("Public series");
        await Assert.That((await read.QueryAsync(new(data.PublicId), default))!.Title).IsEqualTo("Updated");
    }

    private static async Task<Guid> ImageAsync(NativeEventSeriesFactory factory, SeedData data,
        string visibility = StorageObjectVisibilities.PublicImage, string state = StorageObjectLifecycleStates.Active,
        string contentType = "image/png", string extension = "png", bool foreign = false)
    {
        using var scope = Scope(factory, tenantId: foreign ? data.ForeignTenantId : PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var image = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = foreign ? data.ForeignTenantId : PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Uri = "https://images.example.test/series.png",
            Provider = "legacy_external",
            FullName = "series.png",
            SafeDisplayName = "series.png",
            Extension = extension,
            ContentType = contentType,
            Visibility = visibility,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = state
        };
        db.StorageObjects.Add(image);
        await db.SaveChangesAsync();
        return image.Id;
    }
}
