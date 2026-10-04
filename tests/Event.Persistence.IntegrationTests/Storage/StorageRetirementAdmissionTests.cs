using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Storage;

[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class StorageRetirementAdmissionTests(EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompetingAttachmentAndRetirementRequestsHaveOneFencedOutcome(bool retirementWins)
    {
        var source = await SeedAsync();
        Guid actorId = Guid.CreateVersion7();
        await using (var seed = database.CreateContext())
        {
            var group = new Group { Id = Guid.CreateVersion7(), FullName = "Competing owner" };
            seed.Add(new Actor
            {
                Id = actorId,
                Group = group,
                GroupId = group.Id,
                ActorTypeId = (int)ActorTypeEnum.Group,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Competing owner" }
            });
            await seed.SaveChangesAsync();
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mayAttach = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<StorageRetirementAdmission> retirement = RetireAsync();
        Task<bool> attachment = AttachAsync();
        await Task.WhenAll(retirement, attachment).WaitAsync(deadline.Token);

        await Assert.That(await attachment).IsEqualTo(!retirementWins);
        await Assert.That(await retirement).IsEqualTo(retirementWins
            ? StorageRetirementAdmission.Pending : StorageRetirementAdmission.InUse);
        await using var verify = database.CreateContext();
        await Assert.That((await verify.Actors.SingleAsync(row => row.Id == actorId))
            .Pii.ProfilePictureStorageObjectId).IsEqualTo(retirementWins ? null : (Guid?)source.Id);
        await Assert.That(await verify.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == source.Id))
            .IsEqualTo(retirementWins);
        await Assert.That(await verify.StorageObjects.AnyAsync(row => row.Id == source.Id
            && row.LifecycleState == StorageObjectLifecycleStates.Active)).IsEqualTo(!retirementWins);

        async Task<bool> AttachAsync()
        {
            await using var db = database.CreateContext();
            var actor = await db.Actors.SingleAsync(row => row.Id == actorId, deadline.Token);
            var eligible = await db.StorageObjects.AsNoTracking().SingleAsync(row => row.Id == source.Id, deadline.Token);
            await Assert.That(eligible.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
            prepared.SetResult();
            await mayAttach.Task.WaitAsync(deadline.Token);
            actor.Pii.SetProfilePicture(source.Id, null);
            try
            {
                await db.SaveChangesAsync(deadline.Token);
                return true;
            }
            catch (ConcurrencyConflictException) { return false; }
            finally { attached.TrySetResult(); }
        }

        async Task<StorageRetirementAdmission> RetireAsync()
        {
            await prepared.Task.WaitAsync(deadline.Token);
            if (!retirementWins)
            {
                mayAttach.SetResult();
                await attached.Task.WaitAsync(deadline.Token);
            }
            try
            {
                await using var db = database.CreateContext();
                await using var transaction = await db.Database.BeginTransactionAsync(deadline.Token);
                var result = await new EventResourceStorageLifecycleRepository(db)
                    .TryQueueRetirementAsync(source.TenantId, source.Id, Now, deadline.Token);
                await transaction.CommitAsync(deadline.Token);
                return result;
            }
            finally { mayAttach.TrySetResult(); }
        }
    }

    [Test]
    public async Task MissingAndForeignTargetsReturnOnlyNotFound()
    {
        var source = await SeedAsync();
        await using var db = database.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        IEventResourceStorageLifecycleRepository lifecycle = new EventResourceStorageLifecycleRepository(db);
        await Assert.That(await lifecycle.TryQueueRetirementAsync(Guid.CreateVersion7(), source.Id, Now, default))
            .IsEqualTo(StorageRetirementAdmission.NotFound);
        await Assert.That(await lifecycle.TryQueueRetirementAsync(source.TenantId, Guid.CreateVersion7(), Now, default))
            .IsEqualTo(StorageRetirementAdmission.NotFound);
    }

    [Test]
    public async Task AdmissionRequiresCallerTransactionAndServerUtc()
    {
        await using var db = database.CreateContext();
        var lifecycle = new EventResourceStorageLifecycleRepository(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.TryQueueRetirementAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), Now, default));
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            lifecycle.TryQueueRetirementAsync(Guid.CreateVersion7(), Guid.CreateVersion7(),
                DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), default));
    }

    [Test]
    public async Task ActiveMetadataTransfersOnceAndRebuildsSurvivingUsage()
    {
        var source = await SeedAsync();
        await using (var seed = database.CreateContext())
        {
            var survivor = NewSource(source.TenantId, source.StorageProviderBindingId!.Value);
            survivor.Size = 500;
            seed.Add(survivor);
            seed.Add(new StorageUsageCounter { TenantId = source.TenantId, Provider = source.Provider, UsedBytes = 9000 });
            await seed.SaveChangesAsync();
        }
        await using (var db = database.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var lifecycle = new EventResourceStorageLifecycleRepository(db);
            await Assert.That(await lifecycle.TryQueueRetirementAsync(source.TenantId, source.Id, Now, default))
                .IsEqualTo(StorageRetirementAdmission.Pending);
            await Assert.That(await lifecycle.TryQueueRetirementAsync(source.TenantId, source.Id, Now, default))
                .IsEqualTo(StorageRetirementAdmission.Pending);
            await transaction.CommitAsync();
        }
        await using var verify = database.CreateContext();
        await Assert.That(await verify.StorageObjects.AnyAsync(row => row.Id == source.Id)).IsFalse();
        var work = await verify.StorageObjectDeletionTombstones.SingleAsync(row => row.Id == source.Id);
        await Assert.That(work.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(work.ProviderBindingId).IsEqualTo(source.StorageProviderBindingId!.Value);
        await Assert.That(work.ObjectKey).IsEqualTo(source.ObjectKey);
        await Assert.That((await verify.StorageUsageCounters.SingleAsync(row => row.TenantId == source.TenantId))
            .UsedBytes).IsEqualTo(500);
    }

    [Test]
    public async Task PhysicalActorOwnerBlocksAdmissionAndRemovalEvenWithAuthority()
    {
        var source = await SeedAsync();
        await using (var seed = database.CreateContext())
        {
            var group = new Group { Id = Guid.CreateVersion7(), FullName = "Private owner" };
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(),
                Group = group,
                GroupId = group.Id,
                ActorTypeId = (int)ActorTypeEnum.Group,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Private owner" }
            };
            actor.Pii.SetProfilePicture(source.Id, null);
            seed.Add(actor);
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var lifecycle = new EventResourceStorageLifecycleRepository(db);
        await Assert.That(await lifecycle.TryQueueRetirementAsync(source.TenantId, source.Id, Now, default))
            .IsEqualTo(StorageRetirementAdmission.InUse);
        var tracked = await db.StorageObjects.SingleAsync(row => row.Id == source.Id);
        tracked.RequestDelete();
        db.Add(StorageRetirementTarget.Capture(source, [], null, Now));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.RemoveTransferredSourcesAsync(source.TenantId, [], [source.Id], default));
    }

    [Test]
    public async Task RegistrationSinkMissingPublishedRetentionAuthorityIsBlocked()
    {
        var source = await SeedAsync("registration_submission_sink");
        await using var db = database.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Assert.That(await new EventResourceStorageLifecycleRepository(db)
            .TryQueueRetirementAsync(source.TenantId, source.Id, Now, default))
            .IsEqualTo(StorageRetirementAdmission.RetentionBlocked);
        await Assert.That(await db.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == source.Id)).IsFalse();
    }

    [Test]
    public async Task RetirementFirstPreventsLaterActorAttachment()
    {
        var source = await SeedAsync();
        await using (var db = database.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await new EventResourceStorageLifecycleRepository(db)
                .TryQueueRetirementAsync(source.TenantId, source.Id, Now, default);
            await transaction.CommitAsync();
        }
        await using var attach = database.CreateContext();
        var group = new Group { Id = Guid.CreateVersion7(), FullName = "Later owner" };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            Group = group,
            GroupId = group.Id,
            ActorTypeId = (int)ActorTypeEnum.Group,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Later owner" }
        };
        actor.Pii.SetProfilePicture(source.Id, null);
        attach.Add(actor);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => attach.SaveChangesAsync());
    }

    [Test]
    public async Task TrackedDetachmentMustBeFlushedBeforePhysicalAdmission()
    {
        var source = await SeedAsync();
        Guid actorId = Guid.CreateVersion7();
        await using (var seed = database.CreateContext())
        {
            var group = new Group { Id = Guid.CreateVersion7(), FullName = "Detaching owner" };
            var actor = new Actor
            {
                Id = actorId,
                Group = group,
                GroupId = group.Id,
                ActorTypeId = (int)ActorTypeEnum.Group,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Detaching owner" }
            };
            actor.Pii.SetProfilePicture(source.Id, null);
            seed.Add(actor);
            await seed.SaveChangesAsync();
        }
        await using (var db = database.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await db.Actors.SingleAsync(row => row.Id == actorId)).Pii.SetProfilePicture(null, null);
            await Assert.That(await new EventResourceStorageLifecycleRepository(db)
                .TryQueueRetirementAsync(source.TenantId, source.Id, Now, default))
                .IsEqualTo(StorageRetirementAdmission.Pending);
            await transaction.CommitAsync();
        }
        await using var verify = database.CreateContext();
        await Assert.That((await verify.Actors.SingleAsync(row => row.Id == actorId)).Pii.ProfilePictureStorageObjectId).IsNull();
        await Assert.That(await verify.StorageObjects.AnyAsync(row => row.Id == source.Id)).IsFalse();
    }

    [Test]
    public async Task CompleteSetFenceSupportsReverseAdmissionAcrossMultipleSaves()
    {
        var first = await SeedAsync();
        StorageObject second;
        await using (var seed = database.CreateContext())
        {
            second = NewSource(first.TenantId, first.StorageProviderBindingId!.Value);
            seed.Add(second);
            await seed.SaveChangesAsync();
        }
        var ids = new[] { first.Id, second.Id }.OrderDescending().ToArray();
        await using var db = database.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await new StorageObjectReferenceRepository(db).FenceAsync(ids, default);
        var lifecycle = new EventResourceStorageLifecycleRepository(db);
        foreach (var id in ids)
            await Assert.That(await lifecycle.TryQueueRetirementAsync(first.TenantId, id, Now, default))
                .IsEqualTo(StorageRetirementAdmission.Pending);
        await transaction.CommitAsync();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.StorageObjectDeletionTombstones.CountAsync(row => ids.Contains(row.Id))).IsEqualTo(2);
        await Assert.That(await verify.StorageObjects.AnyAsync(row => ids.Contains(row.Id))).IsFalse();
    }

    [Test]
    public async Task UnsettledOperationTransfersAndOnlyCapturedAcknowledgementReleasesIt()
    {
        var source = await SeedAsync();
        await using (var seed = database.CreateContext())
        {
            var binding = await seed.StorageProviderBindings.SingleAsync(row => row.Id == source.StorageProviderBindingId);
            seed.Add(StorageProducerOperation.Create(source.Id, source.TenantId, binding, source.ObjectKey!, Now));
            await seed.SaveChangesAsync();
        }
        await using (var db = database.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await Assert.That(await new EventResourceStorageLifecycleRepository(db)
                .TryQueueRetirementAsync(source.TenantId, source.Id, Now, default))
                .IsEqualTo(StorageRetirementAdmission.Pending);
            await transaction.CommitAsync();
        }
        await using var verify = database.CreateContext();
        var repository = new StorageObjectDeletionTombstoneRepository(verify);
        var work = (await repository.GetByIdAsync(source.Id, default))!;
        await Assert.That(work.State).IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
        await Assert.That(await verify.Set<StorageProducerOperation>().AnyAsync(row => row.Id == source.Id)).IsFalse();
        await Assert.That(await repository.TryClaimAsync(source.Id, work.ConcurrencyStamp,
            Now.AddYears(50), Now.AddYears(50).AddMinutes(1), default)).IsNull();
        await Assert.That(await repository.TrySettleProducerAsync(source.Id, Guid.CreateVersion7(),
            source.ObjectKey!, "version", Now, default)).IsFalse();
        await Assert.That(await repository.TrySettleProducerAsync(source.Id, source.StorageProviderBindingId!.Value,
            source.ObjectKey!, "version", Now, default)).IsTrue();
        work = (await repository.GetByIdAsync(source.Id, default))!;
        await Assert.That(work.ProviderObjectVersion).IsEqualTo("version");
    }

    [Test]
    public async Task LateAcknowledgementCannotReplaceAKnownCapturedVersion()
    {
        var source = await SeedAsync();
        await using var db = database.CreateContext();
        db.Add(StorageObjectDeletionTombstone.Create(source.Id, source.TenantId, source.Provider,
            source.StorageProviderBindingId!.Value, source.ObjectKey!, "captured-version", false, Now));
        await db.SaveChangesAsync();
        var repository = new StorageObjectDeletionTombstoneRepository(db);
        await Assert.That(await repository.TrySettleProducerAsync(source.Id, source.StorageProviderBindingId.Value,
            source.ObjectKey!, "another-version", Now, default)).IsFalse();
        await Assert.That((await repository.GetByIdAsync(source.Id, default))!.ProviderObjectVersion)
            .IsEqualTo("captured-version");
        await Assert.That(await repository.TrySettleProducerAsync(source.Id, source.StorageProviderBindingId.Value,
            source.ObjectKey!, "captured-version", Now, default)).IsTrue();
    }

    [Test]
    public async Task SurvivingProducerOperationBlocksClaimAndAbsence()
    {
        var source = await SeedAsync();
        await using var db = database.CreateContext();
        var binding = await db.StorageProviderBindings.SingleAsync(row => row.Id == source.StorageProviderBindingId);
        var work = StorageRetirementTarget.Capture(source, [], null, Now);
        db.Add(work);
        await db.SaveChangesAsync();
        await db.StorageObjects.Where(row => row.Id == source.Id).ExecuteDeleteAsync();
        var repository = new StorageObjectDeletionTombstoneRepository(db);
        var claim = (await repository.TryClaimAsync(source.Id, work.ConcurrencyStamp, Now, Now.AddMinutes(1), default))!;
        db.Add(StorageProducerOperation.Create(source.Id, source.TenantId, binding, source.ObjectKey!, Now));
        await db.SaveChangesAsync();
        await Assert.That(await repository.TryRecordAbsenceAsync(source.Id, claim.ConcurrencyStamp, Now, default)).IsFalse();
        await Assert.That(await repository.TryClaimAsync(source.Id, claim.ConcurrencyStamp,
            Now.AddMinutes(1), Now.AddMinutes(2), default)).IsNull();
        await Assert.That((await repository.ListWithSourcesAsync(1000, default)).Any(row => row.Id == source.Id)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SessionCustodyClosesReservationAndPreservesSettlement(bool settled)
    {
        var source = await SeedAsync();
        await using (var seed = database.CreateContext())
        {
            var session = Session(source);
            if (settled) session.RecordProducerSettlement(source.Id, source.StorageProviderBindingId!.Value,
                source.ObjectKey!, "version-1");
            seed.Add(session);
            await seed.SaveChangesAsync();
        }
        await using (var db = database.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await Assert.That(await new EventResourceStorageLifecycleRepository(db)
                .TryQueueRetirementAsync(source.TenantId, source.Id, Now, default))
                .IsEqualTo(StorageRetirementAdmission.Pending);
            await transaction.CommitAsync();
        }
        await using var verify = database.CreateContext();
        await Assert.That(await verify.StorageUploadSessions.AnyAsync(row => row.Id == source.Id)).IsFalse();
        var work = await verify.StorageObjectDeletionTombstones.SingleAsync(row => row.Id == source.Id);
        await Assert.That(work.State).IsEqualTo(settled ? StorageObjectDeletionState.Ready : StorageObjectDeletionState.AwaitingProducer);
        await Assert.That(work.ProviderObjectVersion).IsEqualTo(settled ? "version-1" : null);
        var counter = await verify.StorageUsageCounters.SingleAsync(row => row.TenantId == source.TenantId);
        await Assert.That(counter.ReservedBytes).IsEqualTo(0);
        await Assert.That(counter.UsedBytes).IsEqualTo(0);
    }

    [Test]
    [Arguments("binding-provider")]
    [Arguments("custody-version")]
    public async Task InvalidCapturedTargetDoesNotTransferCustody(string mismatch)
    {
        var source = await SeedAsync();
        await using (var seed = database.CreateContext())
        {
            var tracked = await seed.StorageObjects.SingleAsync(row => row.Id == source.Id);
            if (mismatch == "binding-provider") tracked.Provider = StorageProviders.S3Compatible;
            else
            {
                tracked.ProviderVersionId = "source-version";
                var session = Session(source);
                session.ProviderVersionId = "session-version";
                seed.Add(session);
            }
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Assert.That(await new EventResourceStorageLifecycleRepository(db)
            .TryQueueRetirementAsync(source.TenantId, source.Id, Now, default))
            .IsEqualTo(StorageRetirementAdmission.InvalidTarget);
        await Assert.That(await db.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == source.Id)).IsFalse();
        await Assert.That((await db.StorageObjects.SingleAsync(row => row.Id == source.Id)).LifecycleState)
            .IsEqualTo(StorageObjectLifecycleStates.Active);
    }

    [Test]
    [Arguments("storage_objects")]
    [Arguments("storage_upload_sessions")]
    [Arguments("storage_producer_operations")]
    public async Task CustodyCasConflictRollsBackEveryPriorTransfer(string table)
    {
        var source = await SeedAsync();
        await using (var seed = database.CreateContext())
        {
            if (table == "storage_upload_sessions") seed.Add(Session(source));
            else if (table == "storage_producer_operations")
            {
                var binding = await seed.StorageProviderBindings.SingleAsync(row => row.Id == source.StorageProviderBindingId);
                seed.Add(StorageProducerOperation.Create(source.Id, source.TenantId, binding, source.ObjectKey!, Now));
            }
            await seed.SaveChangesAsync();
        }
        var custodyType = table switch
        {
            "storage_objects" => typeof(StorageObject),
            "storage_upload_sessions" => typeof(StorageUploadSession),
            _ => typeof(StorageProducerOperation)
        };
        var conflict = new CustodyConflict(custodyType, source.Id);
        await using (var db = database.CreateContext(conflict))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                new EventResourceStorageLifecycleRepository(db).TryQueueRetirementAsync(source.TenantId, source.Id, Now, default));
        }
        await Assert.That(conflict.Triggered).IsTrue();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == source.Id)).IsFalse();
        await Assert.That((await verify.StorageObjects.SingleAsync(row => row.Id == source.Id)).LifecycleState)
            .IsEqualTo(StorageObjectLifecycleStates.Active);
        if (table == "storage_upload_sessions")
            await Assert.That((await verify.StorageUploadSessions.SingleAsync(row => row.Id == source.Id)).Status)
                .IsEqualTo(StorageUploadSessionStates.Uploading);
        if (table == "storage_producer_operations")
            await Assert.That(await verify.Set<StorageProducerOperation>().AnyAsync(row => row.Id == source.Id)).IsTrue();
    }

    private sealed class CustodyConflict(Type custodyType, Guid id) : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            string table = eventData.Context!.Model.FindEntityType(custodyType)!.GetTableName()!;
            if (!Triggered && command.CommandText.StartsWith($"DELETE FROM \"{table}\"", StringComparison.Ordinal))
            {
                Triggered = true;
                await using var update = command.Connection!.CreateCommand();
                update.Transaction = command.Transaction;
                update.CommandText = $"UPDATE \"{table}\" SET \"concurrency_stamp\" = $stamp WHERE \"id\" = $id";
                var stamp = update.CreateParameter();
                stamp.ParameterName = "$stamp";
                stamp.Value = Guid.CreateVersion7();
                var key = update.CreateParameter();
                key.ParameterName = "$id";
                key.Value = id;
                update.Parameters.Add(stamp);
                update.Parameters.Add(key);
                await Assert.That(await update.ExecuteNonQueryAsync(cancellationToken)).IsEqualTo(1);
            }
            return result;
        }
    }

    private static StorageUploadSession Session(StorageObject source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        Provider = source.Provider,
        StorageProviderBindingId = source.StorageProviderBindingId,
        ObjectKey = source.ObjectKey,
        StorageObjectId = source.Id,
        Purpose = source.Purpose,
        Visibility = source.Visibility,
        ContentType = "image/png",
        SafeDisplayName = "image.png",
        Status = StorageUploadSessionStates.Uploading,
        UploadStartedAt = Now,
        ExpiresAt = Now.AddMinutes(5),
        ReservedBytes = 1000,
        ExpectedSizeBytes = 32
    };

    private async Task<StorageObject> SeedAsync(string? owningKind = null)
    {
        await using var db = database.CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Admission",
            Slug = $"admission-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var source = NewSource(tenant.Id, binding.Id);
        source.OwningResourceKind = owningKind;
        db.AddRange(tenant, binding, source);
        await db.SaveChangesAsync();
        return source;
    }

    private static StorageObject NewSource(Guid tenantId, Guid bindingId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        FileTypeId = (int)FileTypeEnum.Image,
        FileType = null!,
        Provider = StorageProviders.Local,
        StorageProviderBindingId = bindingId,
        ObjectKey = $"images/{Guid.CreateVersion7():N}.png",
        FullName = "image.png",
        SafeDisplayName = "image.png",
        Extension = "png",
        Size = 32,
        ContentType = "image/png",
        Purpose = StorageObjectPurposes.ProfileImage,
        Visibility = StorageObjectVisibilities.PublicImage,
        LifecycleState = StorageObjectLifecycleStates.Active
    };
}
