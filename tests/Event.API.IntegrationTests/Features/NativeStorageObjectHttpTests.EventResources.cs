using System.Net;
using System.Net.Http.Json;
using System.Text;
using Event.Api.IntegrationTests.Builders;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeStorageObjectHttpTests
{
    [Test]
    [Arguments("detail")]
    [Arguments("content")]
    [Arguments("presign")]
    [Arguments("list")]
    [Arguments("update")]
    [Arguments("delete")]
    public async Task ResourceOwnershipClosesGenericStorageEvenForTheUploader(string surface)
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var client = Client(factory, factory.OwnerId);
        var session = await ReserveAsync(client, Upload("resource-owner"));
        var finalized = await FinalizeAsync(client, session.Id);
        Guid objectId = finalized.StorageObjectId!.Value;
        Guid resourceId = Guid.CreateVersion7();
        const string privateName = "private-session-handout.txt";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var stored = await db.StorageObjects.SingleAsync(row => row.Id == objectId);
            stored.OwningResourceKind = "event_resource";
            stored.OwningResourceId = resourceId;
            stored.FullName = privateName;
            stored.SafeDisplayName = privateName;
            // Deliberately persist a malformed tuple: the discriminator alone must close
            // generic access even when purpose metadata is corrupt. Restore enforcement
            // before any request; separate relational tests prove normal writes reject it.
            await db.Database.OpenConnectionAsync();
            try
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
                await db.SaveChangesAsync();
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF");
                await db.Database.CloseConnectionAsync();
            }
        }

        using var response = surface switch
        {
            "detail" => await client.GetAsync($"{Root}/{objectId}"),
            "content" => await client.GetAsync($"{Root}/{objectId}/content"),
            "presign" => await client.GetAsync($"{Root}/{objectId}/presigned-url"),
            "list" => await client.GetAsync(Root),
            "update" => await client.PatchAsJsonAsync($"{Root}/{objectId}", new UpdateStorageObjectDto
            {
                Metadata = new() { FullName = "replaced.txt", SafeDisplayName = "replaced.txt" },
                Ownership = new() { OwningResourceKind = null, OwningResourceId = null }
            }),
            "delete" => await client.DeleteAsync($"{Root}/{objectId}"),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        string body = await response.Content.ReadAsStringAsync();
        if (surface is "detail" or "content" or "presign")
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        if (surface == "list")
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).DoesNotContain(objectId.ToString());
        }
        if (surface == "update")
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        await Assert.That(body).DoesNotContain(privateName);
        await Assert.That(body).DoesNotContain(resourceId.ToString());
        await Assert.That(factory.Signings).IsEmpty();
        await Assert.That(factory.DisposedReads).IsEqualTo(0);
        await Assert.That(factory.Objects.Count).IsEqualTo(1);
        using var verification = factory.Services.CreateScope();
        var remaining = await verification.ServiceProvider.GetRequiredService<IStorageObjectRepository>()
            .GetForAuthorizationAsync(objectId, finalized.TenantId, default);
        await Assert.That(remaining).IsNotNull();
        await Assert.That(remaining!.FullName).IsEqualTo(privateName);
        await Assert.That(remaining.OwningResourceKind).IsEqualTo("event_resource");
        await Assert.That(remaining.OwningResourceId).IsEqualTo(resourceId);
        await Assert.That(remaining.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
    }

    [Test]
    [Arguments("purpose", "detail")]
    [Arguments("purpose", "content")]
    [Arguments("purpose", "presign")]
    [Arguments("purpose", "list")]
    [Arguments("purpose", "update")]
    [Arguments("purpose", "delete")]
    [Arguments("attachment", "detail")]
    [Arguments("attachment", "content")]
    [Arguments("attachment", "presign")]
    [Arguments("attachment", "list")]
    [Arguments("attachment", "update")]
    [Arguments("attachment", "delete")]
    [Arguments("soft-deleted-attachment", "detail")]
    [Arguments("soft-deleted-attachment", "content")]
    [Arguments("soft-deleted-attachment", "presign")]
    [Arguments("soft-deleted-attachment", "list")]
    [Arguments("soft-deleted-attachment", "update")]
    [Arguments("soft-deleted-attachment", "delete")]
    public async Task IndependentResourceSignalsCloseEveryGenericStorageSurface(string signal, string surface)
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var client = Client(factory, factory.OwnerId);
        var session = await ReserveAsync(client, Upload($"resource-{signal}-{surface}"));
        var finalized = await FinalizeAsync(client, session.Id);
        Guid objectId = finalized.StorageObjectId!.Value;
        Guid resourceId = await ApplyResourceSignalAsync(factory, objectId, signal);

        using var response = surface switch
        {
            "detail" => await client.GetAsync($"{Root}/{objectId}"),
            "content" => await client.GetAsync($"{Root}/{objectId}/content"),
            "presign" => await client.GetAsync($"{Root}/{objectId}/presigned-url"),
            "list" => await client.GetAsync(Root),
            "update" => await client.PatchAsJsonAsync($"{Root}/{objectId}", new UpdateStorageObjectDto
            {
                Metadata = new() { FullName = "replaced.txt", SafeDisplayName = "replaced.txt" },
                Ownership = new() { OwningResourceKind = null, OwningResourceId = null }
            }),
            "delete" => await client.DeleteAsync($"{Root}/{objectId}"),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        string body = await response.Content.ReadAsStringAsync();
        if (surface is "detail" or "content" or "presign")
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        if (surface == "list")
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).DoesNotContain(objectId.ToString());
        }
        if (surface == "update")
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        await Assert.That(body).DoesNotContain("file.txt");
        await Assert.That(body).DoesNotContain(resourceId.ToString());
        await Assert.That(factory.Signings).IsEmpty();
        await Assert.That(factory.DisposedReads).IsEqualTo(0);
        await Assert.That(factory.Objects.Count).IsEqualTo(1);
        using var verification = factory.Services.CreateScope();
        var remaining = await verification.ServiceProvider.GetRequiredService<IStorageObjectRepository>()
            .GetForAuthorizationAsync(objectId, finalized.TenantId, default);
        await Assert.That(remaining).IsNotNull();
        await Assert.That(remaining!.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
    }

    [Test]
    [Arguments("purpose")]
    [Arguments("owner")]
    [Arguments("attachment")]
    [Arguments("soft-deleted-attachment")]
    public async Task PublicImageAndOpenGraphReadersIgnoreEveryResourceSignal(string signal)
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);
        Guid objectId = (await FinalizeAsync(owner, (await ReserveAsync(owner, Upload($"public-{signal}"))).Id))
            .StorageObjectId!.Value;
        string publicCode = Guid.CreateVersion7().ToString("N");
        Guid eventId = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var stored = await db.StorageObjects.SingleAsync(row => row.Id == objectId);
            stored.Visibility = StorageObjectVisibilities.PublicImage;
            stored.Purpose = StorageObjectPurposes.EventImage;
            stored.ContentType = "image/png";
            stored.Extension = "png";
            var actorId = await db.TenantUsers.Where(row => row.UserId == factory.OwnerId)
                .Select(row => row.ActorId).SingleAsync() ?? throw new InvalidOperationException("Owner actor missing.");
            var target = new EventBuilder().WithId(eventId).WithTenantId(stored.TenantId).WithActorId(actorId)
                .WithPublicCode(publicCode).WithTitle("Resource image boundary")
                .WithStatus(EventStatusEnum.Published).WithVisibility(VisibilityTypeEnum.Public).Build();
            target.OrganizerActorId = actorId;
            target.FeaturedImageId = objectId;
            db.Events.Add(target);
            await db.SaveChangesAsync();
        }

        using (var ordinary = await factory.CreateClient().GetAsync($"{Root}/{objectId}/public"))
            await Assert.That(ordinary.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var ordinaryOg = await factory.CreateClient().GetAsync($"/api/event/public/resource-bound-{publicCode}/og-image"))
            await Assert.That(ordinaryOg.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(factory.DisposedReads).IsEqualTo(2);

        Guid resourceId = await ApplyResourceSignalAsync(factory, objectId, signal, eventId);
        using (var denied = await factory.CreateClient().GetAsync($"{Root}/{objectId}/public"))
        {
            string body = await denied.Content.ReadAsStringAsync();
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(body).DoesNotContain(resourceId.ToString());
        }
        using (var fallbackOg = await factory.CreateClient().GetAsync($"/api/event/public/resource-bound-{publicCode}/og-image"))
            await Assert.That(fallbackOg.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(factory.DisposedReads).IsEqualTo(2);
    }

    [Test]
    [Arguments("purpose")]
    [Arguments("owner")]
    [Arguments("attachment")]
    [Arguments("soft-deleted-attachment")]
    public async Task RegistrationImportCallerRetainsOrdinaryFilesAndRejectsEveryResourceSignal(string signal)
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);
        Guid objectId = (await FinalizeAsync(owner, (await ReserveAsync(owner, Upload($"import-{signal}"))).Id))
            .StorageObjectId!.Value;
        Guid eventId;
        Guid bindingId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var stored = await db.StorageObjects.SingleAsync(row => row.Id == objectId);
            byte[] csv = Encoding.UTF8.GetBytes(
                $"responseId,attemptId,attemptToken,timestamp\nresponse-1,{Guid.CreateVersion7():D},token,2026-09-23T00:00:00Z");
            stored.ContentType = "text/csv";
            stored.Extension = "csv";
            stored.FullName = "registration-import.csv";
            stored.SafeDisplayName = "registration-import.csv";
            stored.Size = csv.Length;
            factory.Objects[stored.ObjectKey!] = csv;
            var actorId = await db.TenantUsers.Where(row => row.UserId == factory.OwnerId)
                .Select(row => row.ActorId).SingleAsync() ?? throw new InvalidOperationException("Owner actor missing.");
            (eventId, bindingId) = await SeedManualImportBindingAsync(db, stored.TenantId, actorId);
            await db.SaveChangesAsync();
        }

        using var callerScope = factory.Services.CreateScope();
        var accessor = callerScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = FinalizationPrincipal(factory.OwnerId);
        try
        {
            var caller = ActivatorUtilities.CreateInstance<QueueManualRegistrationProviderImportCommandHandler>(callerScope.ServiceProvider);
            var ordinary = await caller.ExecuteAsync(new QueueManualRegistrationProviderImportCommand(
                PlatformDefaults.DefaultTenantId, eventId, bindingId, objectId.ToString("D"), $"ordinary-{signal}"));
            await Assert.That(ordinary.IsSuccess).IsTrue();
            await Assert.That(factory.DisposedReads).IsEqualTo(1);

            await ApplyResourceSignalAsync(factory, objectId, signal, eventId);
            var denied = await caller.ExecuteAsync(new QueueManualRegistrationProviderImportCommand(
                PlatformDefaults.DefaultTenantId, eventId, bindingId, objectId.ToString("D"), $"resource-{signal}"));
            await Assert.That(denied.FailureCode).IsEqualTo("registration_provider_manual_import_file_invalid");
            await Assert.That(factory.DisposedReads).IsEqualTo(1);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Test]
    public async Task GenericStorageCannotReserveAResourceOwnerFromCallerInput()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var client = Client(factory, factory.OwnerId);
        using var response = await client.PostAsJsonAsync(Root + "/upload-sessions", Upload("forged-owner") with
        {
            OwningResourceKind = StorageOwningResourceKinds.EventResource,
            OwningResourceId = Guid.CreateVersion7()
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var scope = factory.Services.CreateScope();
        var counters = await scope.ServiceProvider.GetRequiredService<IStorageUsageCounterRepository>()
            .GetByTenantAsync(PlatformDefaults.DefaultTenantId, default);
        await Assert.That(counters.Sum(counter => counter.ReservedBytes)).IsEqualTo(0);
        await Assert.That(factory.Objects).IsEmpty();
    }

    private static async Task<Guid> ApplyResourceSignalAsync(
        StorageFactory factory, Guid objectId, string signal, Guid? existingEventId = null)
    {
        Guid resourceId = Guid.CreateVersion7();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await db.StorageObjects.SingleAsync(row => row.Id == objectId);
        if (signal is "purpose" or "owner")
        {
            if (signal == "purpose")
                stored.Purpose = StorageObjectPurposes.EventResource;
            else
            {
                stored.OwningResourceKind = StorageOwningResourceKinds.EventResource;
                stored.OwningResourceId = resourceId;
            }
            await db.Database.OpenConnectionAsync();
            try
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
                await db.SaveChangesAsync();
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF");
                await db.Database.CloseConnectionAsync();
            }
            return resourceId;
        }

        if (signal is not ("attachment" or "soft-deleted-attachment"))
            throw new ArgumentOutOfRangeException(nameof(signal));
        var actorId = await db.TenantUsers.Where(row => row.UserId == factory.OwnerId)
            .Select(row => row.ActorId).SingleAsync() ?? throw new InvalidOperationException("Owner actor missing.");
        Guid eventId = existingEventId ?? Guid.CreateVersion7();
        if (existingEventId is null)
        {
            var target = new EventBuilder().WithId(eventId).WithTenantId(stored.TenantId).WithActorId(actorId).Build();
            target.OrganizerActorId = actorId;
            db.Events.Add(target);
        }
        var resource = EventResource.CreateDraft(resourceId, stored.TenantId, eventId, null,
            new EventResourceMetadata
            {
                Title = "Private attached resource",
                Kind = EventResourceKindEnum.GeneralDocument,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
            }, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(stored.TenantId, eventId, resourceId,
                EventResourceAudienceKindEnum.AuthenticatedTenantMember)], actorId, DateTime.UtcNow);
        resource.SetStoredFile(objectId, resource.ConcurrencyStamp, actorId, DateTime.UtcNow);
        if (signal == "soft-deleted-attachment")
        {
            ((ISoftDeletable)resource).IsDeleted = true;
            ((ISoftDeletable)resource).DeletedAt = DateTime.UtcNow;
            ((ISoftDeletable)resource).DeletedBy = actorId;
        }
        db.EventResources.Add(resource);
        if (signal == "soft-deleted-attachment")
        {
            // Normal deletion clears the payload. Corrupt retained references must
            // still close generic access, independently of the normal-write check.
            await db.Database.OpenConnectionAsync();
            try
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
                await db.SaveChangesAsync();
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF");
                await db.Database.CloseConnectionAsync();
            }
        }
        else
        {
            await db.SaveChangesAsync();
        }
        return resourceId;
    }

    [Test]
    public async Task GenericStorageCannotTurnAnOrdinaryObjectIntoAResourceOwner()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var client = Client(factory, factory.OwnerId);
        var reserved = await ReserveAsync(client, Upload("ordinary"));
        var finalized = await FinalizeAsync(client, reserved.Id);
        Guid objectId = finalized.StorageObjectId!.Value;
        using var response = await client.PatchAsJsonAsync($"{Root}/{objectId}", new UpdateStorageObjectDto
        {
            Ownership = new()
            {
                OwningResourceKind = StorageOwningResourceKinds.EventResource,
                OwningResourceId = Guid.CreateVersion7()
            }
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IStorageObjectRepository>()
            .GetForAuthorizationAsync(objectId, finalized.TenantId, default);
        await Assert.That(stored!.OwningResourceKind).IsNull();
        await Assert.That(stored.OwningResourceId).IsNull();
        await Assert.That(factory.Objects.Count).IsEqualTo(1);
    }
}
