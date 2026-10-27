using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadGrantSettlementCommitsOnlyItsNativeRecoveryAdvancement(bool revoke)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (caseId, grantId) = await GrantedReadAsync(database);
        await using var inspect = database.Context();
        var original = await inspect.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        var before = await inspect.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
        long? observedPosition = null;
        var checkpoint = new ReadGrantSavedCheckpoint(context =>
        {
            observedPosition = context.ChangeTracker.Entries<ModerationRecoveryStoreState>().Single().Entity.Position;
            return Task.CompletedTask;
        });
        var request = new ModerationCaseReadGrantRequest(Guid.CreateVersion7(), caseId, original.CaseRevision,
            original.SourceId, database.CallerId, database.Clock.Now.AddSeconds(30));
        await using var writer = database.Context(checkpoint);
        var repository = ReadGrantRepository(writer, database);
        var result = revoke ? await repository.RevokeAsync(grantId, request.OperationId, 1, default)
            : await repository.GrantAsync(request, default);
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(result).IsNotNull();
        await Assert.That(observedPosition).IsEqualTo(before.Position + 1);
        await using var verify = database.Context();
        var after = await verify.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
        await Assert.That(after.Position).IsEqualTo(observedPosition);
        await Assert.That(after.Generation).IsEqualTo(before.Generation);
        await Assert.That(await verify.ModerationCaseReadGrants.AnyAsync(row => row.Id == result!.Id)).IsTrue();
        await Assert.That(await verify.ModerationCaseReadGrantRevocationReceipts
            .AnyAsync(row => row.Id == request.OperationId)).IsEqualTo(revoke);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadGrantSettlementRejectsChangedOrReplacedRecoveryOwnership(bool replace)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (caseId, grantId) = await GrantedReadAsync(database);
        await using var inspect = database.Context();
        var original = await inspect.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        var before = await inspect.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
        var checkpoint = new ReadGrantSavedCheckpoint(context =>
        {
            var entry = context.ChangeTracker.Entries<ModerationRecoveryStoreState>().Single();
            if (replace)
            {
                var values = entry.CurrentValues.Clone();
                var replacement = ModerationRecoveryStoreState.Start(entry.Entity.Id);
                entry.State = EntityState.Detached;
                context.Attach(replacement).CurrentValues.SetValues(values);
            }
            else entry.Property(row => row.Position).CurrentValue++;
            return Task.CompletedTask;
        });
        var request = new ModerationCaseReadGrantRequest(Guid.CreateVersion7(), caseId, original.CaseRevision,
            original.SourceId, database.CallerId, database.Clock.Now.AddSeconds(30));
        await using var writer = database.Context(checkpoint);
        await Assert.That(await ReadGrantRepository(writer, database).GrantAsync(request, default)).IsNull();
        await Assert.That(checkpoint.Observed).IsTrue();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseReadGrants.AnyAsync(row => row.Id == request.OperationId)).IsFalse();
        await Assert.That((await verify.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync()).Position)
            .IsEqualTo(before.Position);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ReadGrantSettlementCannotLoseItsRecoveryAdvancementBeforeWriting(bool revoke, bool delete)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (caseId, grantId) = await GrantedReadAsync(database);
        await using var inspect = database.Context();
        var original = await inspect.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        var before = await inspect.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
        var operationId = Guid.CreateVersion7();
        var probe = new RemoveReadGrantRecoveryBeforeSave(delete);
        await using var writer = database.Context(probe);
        var repository = ReadGrantRepository(writer, database);
        var result = revoke ? await repository.RevokeAsync(grantId, operationId, 1, default)
            : await repository.GrantAsync(new(operationId, caseId, original.CaseRevision,
                original.SourceId, database.CallerId, database.Clock.Now.AddSeconds(30)), default);
        await Assert.That(result).IsNull();
        await Assert.That(probe.Observed).IsTrue();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseReadGrants.AnyAsync(row => row.Id == operationId)).IsFalse();
        await Assert.That((await verify.ModerationCaseReadGrants.SingleAsync(row => row.Id == grantId)).Revision)
            .IsEqualTo(original.Revision);
        await Assert.That(await verify.ModerationCaseReadGrantRevocationReceipts.AnyAsync(row => row.Id == operationId)).IsFalse();
        var after = await verify.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
        await Assert.That(after.Position).IsEqualTo(before.Position);
        await Assert.That(after.Generation).IsEqualTo(before.Generation);
    }

    private sealed class RemoveReadGrantRecoveryBeforeSave(bool delete) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationRecoveryStoreState>().SingleOrDefault() is { } entry)
            {
                Observed = true;
                entry.State = delete ? EntityState.Deleted : EntityState.Detached;
            }
            return ValueTask.FromResult(result);
        }
    }
}
