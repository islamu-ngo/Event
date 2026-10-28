using Explore.Domain.Learning;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Learning;

[Category("LearningProvider")]
public sealed class LearningErasureTests
{
    [Test]
    public async Task PurgeAndAbsenceRequireAnExplicitBoundSubjectTransaction()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        await database.SeedLearningAsync(withChildren: true);
        await using var context = database.Open(null);
        var store = new LearningPrivacyErasureStore(context);
        var intent = LearningProviderDatabase.Intent(database.UserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurgeAsync(intent, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RequireAbsentAsync(intent, default));
        await using var observer = database.Open(database.TenantId);
        await Assert.That((await new LearningRepository(observer)
            .GetStateAsync(database.TenantId, database.UserId, false, default))!.Practices.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SubjectPurgeCascadesExactInternalOwnerAcrossTenantsAndLeavesSharedSpaces()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var first = await database.SeedLearningAsync(withChildren: true);
        var second = await database.SeedLearningAsync(database.OtherTenantId, withChildren: true);
        var other = await database.SeedLearningAsync(userId: database.OtherUserId, withChildren: true);
        await using (var context = database.Open(null))
        {
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var store = new LearningPrivacyErasureStore(context);
                var intent = LearningProviderDatabase.Intent(database.UserId);
                await store.PurgeAsync(intent, default);
                await store.RequireAbsentAsync(intent, default);
                await transaction.CommitAsync();
            });
        }
        await using var observer = database.OpenSystem();
        await Assert.That(await observer.UserLearningStates.Select(row => row.Id).ToArrayAsync())
            .IsEquivalentTo([other.Id]);
        await Assert.That(await observer.Set<LearningTourProgress>().Select(row => row.Id).ToArrayAsync())
            .IsEquivalentTo(other.Tours.Select(row => row.Id));
        await Assert.That(await observer.Set<LearningPracticeState>().Select(row => row.Id).ToArrayAsync())
            .IsEquivalentTo(other.Practices.Select(row => row.Id));
        await Assert.That(await observer.TenantLearningSpaces.Select(row => row.TenantId).ToArrayAsync())
            .IsEquivalentTo([database.TenantId, database.OtherTenantId]);
        await Assert.That(await observer.UserLearningStates.AnyAsync(row => row.Id == first.Id || row.Id == second.Id)).IsFalse();
        await Assert.That(await observer.Users.AnyAsync(row => row.Id == database.UserId)).IsTrue();
    }

    [Test]
    public async Task RollbackRestoresParentAndChildrenThenCommittedPurgeAndReplayStayAbsent()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var original = await database.SeedLearningAsync(withChildren: true);
        var intent = LearningProviderDatabase.Intent(database.UserId);
        await using (var context = database.Open(null))
        {
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var store = new LearningPrivacyErasureStore(context);
                await store.PurgeAsync(intent, default);
                await store.RequireAbsentAsync(intent, default);
                await transaction.RollbackAsync();
            });
        }
        await using (var restored = database.Open(database.TenantId))
        {
            var row = (await new LearningRepository(restored)
                .GetStateAsync(database.TenantId, database.UserId, false, default))!;
            await Assert.That(row.Id).IsEqualTo(original.Id);
            await Assert.That(row.Revision).IsEqualTo(original.Revision);
            await Assert.That(row.Tours.Select(value => value.Id)).IsEquivalentTo(original.Tours.Select(value => value.Id));
            await Assert.That(row.Practices.Select(value => value.Id)).IsEquivalentTo(original.Practices.Select(value => value.Id));
        }
        for (int pass = 0; pass < 2; pass++)
        {
            await using var context = database.Open(null);
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var store = new LearningPrivacyErasureStore(context);
                await store.PurgeAsync(intent, default);
                await store.RequireAbsentAsync(intent, default);
                await transaction.CommitAsync();
            });
        }
        await using var observer = database.OpenSystem();
        await Assert.That(await observer.UserLearningStates.CountAsync()).IsEqualTo(0);
        await Assert.That(await observer.Set<LearningTourProgress>().CountAsync()).IsEqualTo(0);
        await Assert.That(await observer.Set<LearningPracticeState>().CountAsync()).IsEqualTo(0);
        await Assert.That(await observer.TenantLearningSpaces.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task CrossTenantAbsenceFailureRollsBackEarlierWritesInTheSameTransaction()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        await database.SeedLearningAsync(database.OtherTenantId, withChildren: true);
        var unrelated = await database.SeedLearningAsync(userId: database.OtherUserId, withChildren: true);
        await using (var context = database.Open(database.TenantId))
        {
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var repository = new LearningRepository(context);
                var row = (await repository.GetStateAsync(database.TenantId, database.OtherUserId, true, default))!;
                row.UpdatePreferences(true, true, false, row.Revision, LearningProviderDatabase.Now);
                await repository.SaveChangesAsync(default);
                var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new LearningPrivacyErasureStore(context).RequireAbsentAsync(
                        LearningProviderDatabase.Intent(database.UserId), default));
                await Assert.That(failure!.Message).IsEqualTo("learning_erasure_incomplete");
                await transaction.RollbackAsync();
            });
        }
        await using var observer = database.OpenSystem();
        var retained = await observer.UserLearningStates.SingleAsync(row => row.Id == unrelated.Id);
        await Assert.That(retained.Revision).IsEqualTo(unrelated.Revision);
        await Assert.That(retained.SuppressAutomaticTours).IsFalse();
        await Assert.That(await observer.UserLearningStates.AnyAsync(row =>
            row.UserId == database.UserId && row.TenantId == database.OtherTenantId)).IsTrue();
        await Assert.That(await observer.Set<LearningPracticeState>().CountAsync()).IsEqualTo(2);
    }
}
