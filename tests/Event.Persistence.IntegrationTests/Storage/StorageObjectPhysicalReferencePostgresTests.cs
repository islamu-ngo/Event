using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Storage;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class StorageObjectPhysicalReferencePostgresTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthoringCommitRetriesPendingGraphOrVerifiesLostAcknowledgement(bool loseAcknowledgement)
    {
        var scope = await SeedAsync();
        var fault = new AuthoringCommitFault(loseAcknowledgement);
        await using var context = fixture.CreateDbContext(fault);
        var source = await context.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
        source.FullName = "Changed with authoring";
        var form = RegistrationForm.Create(
            scope.TenantAId, scope.EventAId, "event", "retry", "Retry form", DateTime.UtcNow);

        await new RegistrationFormAuthoringRepository(context).CreateFormAsync(form, default);

        await Assert.That(fault.CommitAttempts).IsEqualTo(loseAcknowledgement ? 1 : 2);
        await Assert.That(fault.PendingFormAtEveryCommit).IsTrue();
        await Assert.That(context.Entry(form).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.RegistrationForms.CountAsync(row => row.Id == form.Id)).IsEqualTo(1);
        var persisted = await verify.StorageObjects.SingleAsync(row => row.Id == source.Id);
        await Assert.That(persisted.FullName).IsEqualTo(source.FullName);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(source.ConcurrencyStamp);
        await Assert.That(await context.SaveChangesAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, false)]
    [Arguments(false, true, true)]
    [Arguments(true, false, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public async Task OwnedSaveRetriesOrVerifiesCommitWithoutAcceptingPendingOwnersEarly(
        bool asynchronous, bool acceptChanges, bool loseAcknowledgement)
    {
        var scope = await SeedAsync();
        var fault = new CommitFault(loseAcknowledgement, transient: true);
        await using var context = fixture.CreateDbContext(fault);
        var source = await context.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
        source.FullName = "Changed in the owner transaction";
        var owner = NewSeries(scope);
        context.Add(owner);

        int saved = asynchronous
            ? await context.SaveChangesAsync(acceptChanges)
            : context.SaveChanges(acceptChanges);

        await Assert.That(saved).IsEqualTo(2);
        await Assert.That(fault.CommitAttempts).IsEqualTo(loseAcknowledgement ? 1 : 2);
        await Assert.That(fault.PendingOwnerAtEveryCommit).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.Entry(owner).State)
            .IsEqualTo(acceptChanges ? EntityState.Unchanged : EntityState.Added);
        await Assert.That(context.Entry(source).State)
            .IsEqualTo(acceptChanges ? EntityState.Unchanged : EntityState.Modified);
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.EventSeries.CountAsync(row => row.Id == owner.Id)).IsEqualTo(1);
        var persisted = await verify.StorageObjects.SingleAsync(row => row.Id == source.Id);
        await Assert.That(persisted.FullName).IsEqualTo(source.FullName);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(source.ConcurrencyStamp);
        context.ChangeTracker.AcceptAllChanges();
        await Assert.That(asynchronous ? await context.SaveChangesAsync() : context.SaveChanges()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedOwnedCommitPreservesPendingOwnerAndRestoresTrackedFenceStamp(bool asynchronous)
    {
        var scope = await SeedAsync();
        var fault = new CommitFault(loseAcknowledgement: false, transient: false);
        await using var context = fixture.CreateDbContext(fault);
        var source = await context.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
        Guid stamp = source.ConcurrencyStamp;
        var owner = NewSeries(scope);
        context.Add(owner);

        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        else
            await Assert.That(() => context.SaveChanges()).Throws<InvalidOperationException>();

        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.Entry(owner).State).IsEqualTo(EntityState.Added);
        await Assert.That(context.Entry(source).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(source.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(context.Entry(source).Property(row => row.ConcurrencyStamp).OriginalValue).IsEqualTo(stamp);
        await using (var verify = fixture.CreateDbContext())
        {
            await Assert.That(await verify.EventSeries.AnyAsync(row => row.Id == owner.Id)).IsFalse();
            await Assert.That((await verify.StorageObjects.SingleAsync(row => row.Id == source.Id)).ConcurrencyStamp)
                .IsEqualTo(stamp);
        }
        await Assert.That(asynchronous ? await context.SaveChangesAsync() : context.SaveChanges()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DetachedOwnerFencesPersistedOldTargetAndOptionalNewTarget(bool asynchronous, bool replace)
    {
        var scope = await SeedAsync();
        await using (var seed = fixture.CreateDbContext())
        {
            var pii = await seed.ActorPii.SingleAsync(row => row.ActorId == scope.ActorId);
            pii.SetProfilePicture(scope.StorageAId, null);
            await seed.SaveChangesAsync();
        }
        await using var context = fixture.CreateDbContext();
        var before = await context.StorageObjects.AsNoTracking()
            .Where(row => row.Id == scope.StorageAId || row.Id == scope.StorageBId)
            .ToDictionaryAsync(row => row.Id, row => row.ConcurrencyStamp);
        var detached = new ActorPii { ActorId = scope.ActorId, DisplayName = "Detached owner" };
        detached.SetProfilePicture(replace ? scope.StorageBId : null, null);
        context.Update(detached);
        if (asynchronous)
            await context.SaveChangesAsync();
        else
            context.SaveChanges();
        await using var verify = fixture.CreateDbContext();
        var after = await verify.StorageObjects.AsNoTracking()
            .Where(row => row.Id == scope.StorageAId || row.Id == scope.StorageBId)
            .ToDictionaryAsync(row => row.Id, row => row.ConcurrencyStamp);
        await Assert.That(after[scope.StorageAId]).IsNotEqualTo(before[scope.StorageAId]);
        await Assert.That(after[scope.StorageBId] != before[scope.StorageBId]).IsEqualTo(replace);
        await Assert.That((await verify.ActorPii.SingleAsync(row => row.ActorId == scope.ActorId))
            .ProfilePictureStorageObjectId).IsEqualTo(replace ? scope.StorageBId : (Guid?)null);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RejectedAttachmentPoisonsCallerEvenAfterNullingReference(bool asynchronous)
    {
        var scope = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async token =>
            {
                var pii = await context.ActorPii.SingleAsync(row => row.ActorId == scope.ActorId, token);
                pii.SetProfilePicture(Guid.CreateVersion7(), null);
                if (asynchronous)
                    await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.SaveChangesAsync(token));
                else
                    await Assert.That(() => context.SaveChanges()).Throws<ConcurrencyConflictException>();
                pii.SetProfilePicture(null, null);
                pii.DisplayName = "Must roll back";
                if (asynchronous)
                    await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.SaveChangesAsync(token));
                else
                    await Assert.That(() => context.SaveChanges()).Throws<ConcurrencyConflictException>();
            }));
        await using var verify = fixture.CreateDbContext();
        await Assert.That((await verify.ActorPii.SingleAsync(row => row.ActorId == scope.ActorId)).DisplayName)
            .IsNotEqualTo("Must roll back");
    }

    [Test]
    public async Task HiddenOtherTenantOwnerBlocksUntilPhysicalDetachmentAndRollbackRestoresIt()
    {
        await using var context = fixture.CreateDbContext();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(VerifyPhysicalOwnerAsync);
    }

    private async Task VerifyPhysicalOwnerAsync()
    {
        await fixture.ResetAsync();
        await using var seed = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => fixture.CreateDbContext());
        var scope = await seed.SeedScopeAsync();
        await using (var write = fixture.CreateDbContext())
        {
            var hidden = await write.Events.SingleAsync(row => row.Id == scope.EventBId);
            hidden.BackgroundImageId = scope.StorageAId;
            hidden.IsDeleted = true;
            await write.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            context.ClearTenantFilterBypass();
            context.TenantContext = new TenantScope(scope.TenantAId);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var repository = new StorageObjectReferenceRepository(context);
            await repository.FenceAsync([scope.StorageAId], default);
            await Assert.That(await context.Events.AnyAsync(row => row.Id == scope.EventBId)).IsFalse();
            await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsTrue();
            await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageBId, default)).IsFalse();
            var hidden = await context.Events.IgnoreQueryFilters().SingleAsync(row => row.Id == scope.EventBId);
            hidden.BackgroundImageId = null;
            await context.SaveChangesAsync();
            await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsFalse();
            await transaction.RollbackAsync();
        }

        await using var verify = fixture.CreateDbContext();
        await using var verificationTransaction = await verify.Database.BeginTransactionAsync();
        var verificationRepository = new StorageObjectReferenceRepository(verify);
        await verificationRepository.FenceAsync([scope.StorageAId], default);
        await Assert.That(await verificationRepository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsTrue();
    }

    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    private async Task<EventResourcePersistenceTests.ResourceScope> SeedAsync()
    {
        await fixture.ResetAsync();
        await using var seed = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => fixture.CreateDbContext());
        return await seed.SeedScopeAsync();
    }

    private static EventSeries NewSeries(EventResourcePersistenceTests.ResourceScope scope) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = scope.TenantAId,
        ActorId = scope.ActorId,
        Title = "Owned save reference",
        FeaturedImageId = scope.StorageAId,
        VisibilityTypeId = (int)VisibilityTypeEnum.Public,
        VisibilityType = null!
    };

    private sealed class AuthoringCommitFault(bool loseAcknowledgement) : DbTransactionInterceptor
    {
        private bool _injected;
        public int CommitAttempts { get; private set; }
        public bool PendingFormAtEveryCommit { get; private set; } = true;

        private void FailOnce()
        {
            if (_injected) return;
            _injected = true;
            throw new TimeoutException("Injected authoring commit fault.");
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            CommitAttempts++;
            PendingFormAtEveryCommit &= eventData.Context!.ChangeTracker.Entries<RegistrationForm>()
                .Single().State == EntityState.Added;
            if (!loseAcknowledgement) FailOnce();
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (loseAcknowledgement) FailOnce();
            return Task.CompletedTask;
        }
    }

    private sealed class CommitFault(bool loseAcknowledgement, bool transient) : DbTransactionInterceptor
    {
        public int CommitAttempts { get; private set; }
        public bool PendingOwnerAtEveryCommit { get; private set; } = true;
        private bool _injected;

        private void BeforeCommit(DbContext context)
        {
            CommitAttempts++;
            PendingOwnerAtEveryCommit &= context.ChangeTracker.Entries<EventSeries>()
                .Single().State == EntityState.Added;
            if (!loseAcknowledgement)
                FailOnce();
        }

        private void FailOnce()
        {
            if (_injected)
                return;
            _injected = true;
            if (transient)
                throw new TimeoutException("Injected transient commit fault.");
            throw new InvalidOperationException("Injected permanent commit fault.");
        }

        public override InterceptionResult TransactionCommitting(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            BeforeCommit(eventData.Context!);
            return result;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            BeforeCommit(eventData.Context!);
            return ValueTask.FromResult(result);
        }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (loseAcknowledgement)
                FailOnce();
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (loseAcknowledgement)
                FailOnce();
            return Task.CompletedTask;
        }
    }
}
