using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class EventInitialCreationWriteReceiptTests
{
    [Test]
    public async Task Accepted_saves_retain_early_children_and_the_final_disclosure_flush_for_replay()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (actorId, imageId) = await SeedAsync(database);
        await using var context = database.Context();
        Guid eventId = Guid.CreateVersion7();
        using var receipt = new EventInitialCreationWriteReceipt(context, database.TenantId, eventId, [imageId]);
        Guid dayId = Guid.CreateVersion7();
        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async token =>
        {
            await receipt.InitializeAsync(token);
            await new StorageObjectReferenceRepository(context).FenceAsync([imageId], token);
            var root = NewEvent(database, eventId, actorId, imageId);
            context.Events.Add(root);
            await context.SaveChangesAsync(token);
            context.EventDays.Add(NewDay(database, eventId, dayId));
            await context.SaveChangesAsync(token);
            root.Description = "Final accepted source state";
            AddOwner(context, database, eventId);
            await context.SaveChangesAsync(token);
            return true;
        }, receipt.CaptureAsync, CancellationToken.None);

        await Assert.That(receipt.HasWrites).IsTrue();
        context.ChangeTracker.Clear();
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.After);
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.After);
        await context.EventDays.Where(row => row.Id == dayId).ExecuteDeleteAsync();
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.Uncertain);
    }

    [Test]
    public async Task Rollback_matches_the_pre_fence_storage_actor_stamp_and_first_canonical_recovery_values()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (actorId, imageId) = await SeedAsync(database);
        await using var context = database.Context();
        Guid eventId = Guid.CreateVersion7();
        using var receipt = new EventInitialCreationWriteReceipt(context, database.TenantId, eventId, [imageId]);
        var beforeImage = await context.StorageObjects.AsNoTracking().SingleAsync(row => row.Id == imageId);
        var beforeRecovery = await context.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await receipt.InitializeAsync(CancellationToken.None);
            await new StorageObjectReferenceRepository(context).FenceAsync([imageId], CancellationToken.None);
            context.Events.Add(NewEvent(database, eventId, actorId, imageId));
            AddOwner(context, database, eventId);
            var image = await context.StorageObjects.SingleAsync(row => row.Id == imageId);
            image.ActorId = actorId;
            var epoch = await context.ModerationQualificationEpochs.SingleAsync(row => row.Id == database.EpochId);
            epoch.Advance(epoch.Revision);
            await context.SaveChangesAsync();
            image.FullName = "Second accepted image state";
            epoch.Advance(epoch.Revision);
            await context.SaveChangesAsync();
            await context.FlushDisclosureAsync(CancellationToken.None);
            await receipt.CaptureAsync(CancellationToken.None);
            await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.After);
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.Before);
        var restoredImage = await context.StorageObjects.AsNoTracking().SingleAsync(row => row.Id == imageId);
        var restoredRecovery = await context.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
        await Assert.That(restoredImage.ActorId).IsEqualTo(beforeImage.ActorId);
        await Assert.That(restoredImage.ConcurrencyStamp).IsEqualTo(beforeImage.ConcurrencyStamp);
        await Assert.That(restoredRecovery.Revision).IsEqualTo(beforeRecovery.Revision);
        await Assert.That(restoredRecovery.Position).IsEqualTo(beforeRecovery.Position);
    }

    [Test]
    public async Task Native_bulk_stamp_without_a_source_save_is_not_mistaken_for_no_writes_or_a_complete_creation()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, imageId) = await SeedAsync(database);
        await using var context = database.Context();
        using var receipt = new EventInitialCreationWriteReceipt(context, database.TenantId, Guid.CreateVersion7(), [imageId]);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await receipt.InitializeAsync(CancellationToken.None);
        await new StorageObjectReferenceRepository(context).FenceAsync([imageId], CancellationToken.None);
        await receipt.CaptureAsync(CancellationToken.None);
        await Assert.That(receipt.HasWrites).IsTrue();
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.Uncertain);
        await transaction.RollbackAsync();
        context.ChangeTracker.Clear();
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.Before);
    }

    [Test]
    public async Task Database_after_images_do_not_alias_later_unsaved_tracker_edits_or_unrelated_reads()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (actorId, imageId) = await SeedAsync(database);
        await using var context = database.Context();
        Guid eventId = Guid.CreateVersion7();
        using var receipt = new EventInitialCreationWriteReceipt(context, database.TenantId, eventId, [imageId]);
        var unrelated = await context.Users.SingleAsync(row => row.Id == database.SubjectId);
        var trackedImage = await context.StorageObjects.SingleAsync(row => row.Id == imageId);
        Guid databaseStamp = Guid.CreateVersion7();
        var root = NewEvent(database, eventId, actorId, imageId);
        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async token =>
        {
            await receipt.InitializeAsync(token);
            context.Events.Add(root);
            AddOwner(context, database, eventId);
            await context.SaveChangesAsync(token);
            await context.StorageObjects.Where(row => row.Id == imageId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, databaseStamp), token);
            return true;
        }, receipt.CaptureAsync, CancellationToken.None);
        await Assert.That(trackedImage.ConcurrencyStamp).IsNotEqualTo(databaseStamp);
        root.Description = "Not a committed image";
        unrelated.IsDeleted = true;
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.After);
    }

    [Test]
    public async Task A_later_unobserved_child_cannot_borrow_the_retained_creation_receipt()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (actorId, imageId) = await SeedAsync(database);
        await using var context = database.Context();
        Guid eventId = Guid.CreateVersion7();
        using var receipt = new EventInitialCreationWriteReceipt(context, database.TenantId, eventId, [imageId]);
        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async token =>
        {
            await receipt.InitializeAsync(token);
            context.Events.Add(NewEvent(database, eventId, actorId, imageId));
            AddOwner(context, database, eventId);
            await context.SaveChangesAsync(token);
            return true;
        }, receipt.CaptureAsync, CancellationToken.None);
        long disclosure = await context.Set<EventDiscoveryRevision>().Where(row => row.TenantId == database.TenantId)
            .Select(row => row.DisclosureEpoch).SingleAsync();
        await using (var other = database.Context())
        {
            other.EventDays.Add(NewDay(database, eventId, Guid.CreateVersion7()));
            await other.SaveChangesAsync();
            // Keep the epoch unchanged so only the actual graph keyset can reject the extra child.
            await other.Set<EventDiscoveryRevision>().Where(row => row.TenantId == database.TenantId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.DisclosureEpoch, disclosure));
        }
        context.ChangeTracker.Clear();
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.Uncertain);
    }

    [Test]
    public async Task A_root_without_the_initial_owner_is_not_a_complete_after_image()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (actorId, imageId) = await SeedAsync(database);
        await using var context = database.Context();
        Guid eventId = Guid.CreateVersion7();
        using var receipt = new EventInitialCreationWriteReceipt(context, database.TenantId, eventId, [imageId]);
        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async token =>
        {
            await receipt.InitializeAsync(token);
            context.Events.Add(NewEvent(database, eventId, actorId, imageId));
            await context.SaveChangesAsync(token);
            return true;
        }, receipt.CaptureAsync, CancellationToken.None);
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.Uncertain);
    }

    [Test]
    public async Task An_unspecified_native_write_never_grants_replay_permission_even_after_rollback()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        Guid eventId = Guid.CreateVersion7();
        using var receipt = new EventInitialCreationWriteReceipt(context, database.TenantId, eventId, []);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await receipt.InitializeAsync(CancellationToken.None);
            context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.CreateVersion7(), TenantId = database.TenantId, Tenant = null!,
                EntityType = nameof(Explore.Domain.Event), EntityId = eventId.ToString("D"),
                Action = "unspecified-creation-source", Timestamp = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            await context.FlushDisclosureAsync(CancellationToken.None);
            await receipt.CaptureAsync(CancellationToken.None);
            await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.Uncertain);
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        await Assert.That(await ReadAsync(context, receipt)).IsEqualTo(EventInitialCreationWriteState.Uncertain);
    }

    [Test]
    public async Task Disposal_stops_observation_of_later_saves_on_the_supplied_context()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (actorId, imageId) = await SeedAsync(database);
        await using var context = database.Context();
        using var receipt = new EventInitialCreationWriteReceipt(context, database.TenantId, Guid.CreateVersion7(), [imageId]);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await receipt.InitializeAsync(CancellationToken.None);
        receipt.Dispose();
        context.Events.Add(NewEvent(database, Guid.CreateVersion7(), actorId, imageId));
        await context.SaveChangesAsync();
        await Assert.That(receipt.HasWrites).IsFalse();
        await transaction.RollbackAsync();
    }

    private static Task<EventInitialCreationWriteState> ReadAsync(ExploreDbContext context, EventInitialCreationWriteReceipt receipt) =>
        context.Database.CurrentTransaction is not null
            ? receipt.ReadStateAsync(CancellationToken.None)
            : new EfCoreUnitOfWork(context).ExecuteSerializableAsync(receipt.ReadStateAsync);

    private static Explore.Domain.Event NewEvent(SourceDatabase database, Guid eventId, Guid actorId, Guid imageId) => new(EventStatusEnum.Draft)
    {
        Id = eventId, TenantId = database.TenantId, Tenant = null!, ActorId = actorId, Actor = null!,
        OrganizerActorId = actorId, Title = "Invocation-owned Event", PublicCode = Guid.CreateVersion7().ToString("N")[..12],
        EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
        VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
        EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!, Timezone = "UTC",
        FeaturedImageId = imageId,
        ParticipationConfiguration = EventParticipationConfiguration.Create(eventId, database.TenantId,
            (int)ParticipationHandlingModeEnum.InformationOnly, (int)AdvanceRegistrationObligationEnum.NotApplicable,
            null, null, DateTime.UtcNow)
    };

    private static EventDay NewDay(SourceDatabase database, Guid eventId, Guid dayId) => new()
    {
        Id = dayId, EventId = eventId, Event = null!, TenantId = database.TenantId, Tenant = null!,
        LocalDate = new DateOnly(2026, 10, 19), Label = "Observed early day", IsPublished = true
    };

    private static void AddOwner(ExploreDbContext context, SourceDatabase database, Guid eventId) =>
        context.EventRoleAssignments.Add(EventRoleAssignment.Create(database.TenantId, eventId, database.AdministratorId,
            (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow, null, database.AdministratorId));

    private static async Task<(Guid ActorId, Guid ImageId)> SeedAsync(SourceDatabase database)
    {
        await using var context = database.Context();
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = database.AdministratorId, Pii = new ActorPii { DisplayName = "Creation receipt actor" }
        };
        var image = new StorageObject
        {
            Id = Guid.CreateVersion7(), TenantId = database.TenantId, Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Image, FileType = null!, Provider = "legacy_external",
            FullName = "Original image", SafeDisplayName = "image.png", Extension = ".png", ContentType = "image/png",
            Visibility = StorageObjectVisibilities.PublicImage, Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = StorageObjectLifecycleStates.Active
        };
        context.AddRange(actor, image);
        await context.SaveChangesAsync();
        return (actor.Id, image.Id);
    }
}
