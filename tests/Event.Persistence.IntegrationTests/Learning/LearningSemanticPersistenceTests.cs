using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Learning;
using Explore.Persistence;
using Explore.Persistence.Privacy;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Event.Persistence.IntegrationTests.Learning.LearningProviderDatabase;

namespace Event.Persistence.IntegrationTests.Learning;

[Category("LearningProvider")]
public sealed class LearningSemanticPersistenceTests
{
    private static readonly string PlanFingerprint = new('A', 64);
    private static readonly string PolicySourceStamp = new('B', 64);
    private static readonly LearningTourDefinition Definition = new(
        LearningTourFamily.Shell, 2, [new("shell.navigation", 1), new("shell.help", 2)]);

    [Test]
    public async Task ReloadIncludesStableStepEvidenceAndAcceptedClaimSources()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        UserLearningState original = await SeedSemanticAsync(database);
        await using var context = database.Open(database.TenantId);
        var stored = (await new LearningRepository(context)
            .GetStateAsync(database.TenantId, database.UserId, false, default))!;
        await Assert.That(stored.ActivePlanFingerprint).IsEqualTo(PlanFingerprint);
        await Assert.That(stored.ActivePolicySourceStamp).IsEqualTo(PolicySourceStamp);
        var tour = stored.Tours.Single();
        await Assert.That(tour.LastStepKey).IsEqualTo("shell.navigation");
        var step = tour.Steps.Single();
        await Assert.That(step.Id).IsEqualTo(original.Tours.Single().Steps.Single().Id);
        await Assert.That(step.TenantId).IsEqualTo(database.TenantId);
        await Assert.That(step.TourProgressId).IsEqualTo(tour.Id);
        await Assert.That(step.StepKey).IsEqualTo("shell.navigation");
        await Assert.That(step.StepVersion).IsEqualTo(1);
        await Assert.That(step.CompletedAtUtc).IsEqualTo(Now.AddSeconds(1));
        await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task StepReadsRemainTenantQualifiedAndUnboundReadsFailClosed()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        await SeedSemanticAsync(database);
        await using var other = database.Open(database.OtherTenantId);
        await Assert.That(await other.Set<LearningTourStepProgress>().AnyAsync()).IsFalse();
        await using var unbound = database.Open(null);
        await Assert.That(await unbound.Set<LearningTourStepProgress>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task PhysicalCompositeForeignKeyRejectsAnotherTenantAndMissingParent()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        await SeedSemanticAsync(database);
        await using var context = database.Open(database.TenantId);
        var foreignTenant = await Assert.ThrowsAsync<System.Data.Common.DbException>(() =>
            context.Set<LearningTourStepProgress>()
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.TenantId, database.OtherTenantId)));
        await Assert.That(IsForeignKeyViolation(foreignTenant!)).IsTrue();
        Guid missingParent = Guid.CreateVersion7();
        var orphan = await Assert.ThrowsAsync<System.Data.Common.DbException>(() =>
            context.Set<LearningTourStepProgress>()
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.TourProgressId, missingParent)));
        await Assert.That(IsForeignKeyViolation(orphan!)).IsTrue();
    }

    [Test]
    public async Task PhysicalUniquenessBindsTenantParentKeyAndSemanticVersion()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var original = await SeedSemanticAsync(database);
        Guid parent = original.Tours.Single().Id;
        await using var context = database.Open(database.TenantId);
        var repository = new LearningRepository(context);
        var state = (await repository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        RecordHelp(state);
        await repository.SaveChangesAsync(default);
        var failure = await Assert.ThrowsAsync<System.Data.Common.DbException>(() =>
            context.Set<LearningTourStepProgress>()
                .Where(row => row.TourProgressId == parent && row.StepKey == "shell.help")
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.StepKey, "shell.navigation")
                    .SetProperty(row => row.StepVersion, 1)));
        await Assert.That(failure switch
        {
            Npgsql.PostgresException postgres => postgres.SqlState == "23505",
            Microsoft.Data.Sqlite.SqliteException sqlite => sqlite.SqliteExtendedErrorCode == 2067,
            Microsoft.Data.SqlClient.SqlException sqlServer => sqlServer.Number is 2601 or 2627,
            MySqlConnector.MySqlException mysql => mysql.Number == 1062,
            _ => false
        }).IsTrue();
        await context.Set<LearningTourStepProgress>()
            .Where(row => row.TourProgressId == parent && row.StepKey == "shell.help")
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.StepKey, "shell.navigation")
                .SetProperty(row => row.StepVersion, 2));
        context.ChangeTracker.Clear();
        var stored = (await new LearningRepository(context)
            .GetStateAsync(database.TenantId, database.UserId, false, default))!;
        await Assert.That(stored.Tours.Single().Steps.Select(row => row.StepVersion))
            .IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task CompetingLoadedProgressKeepsOnlyTheWinningChild()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        await SeedSemanticAsync(database);
        await using var first = database.Open(database.TenantId);
        await using var second = database.Open(database.TenantId);
        var firstRepository = new LearningRepository(first);
        var secondRepository = new LearningRepository(second);
        var winner = (await firstRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        var loser = (await secondRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        RecordHelp(winner);
        RecordHelp(loser);
        Guid winningStep = winner.Tours.Single().Steps.Single(row => row.StepKey == "shell.help").Id;
        await firstRepository.SaveChangesAsync(default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondRepository.SaveChangesAsync(default));
        await using var observer = database.Open(database.TenantId);
        var stored = (await new LearningRepository(observer)
            .GetStateAsync(database.TenantId, database.UserId, false, default))!;
        await Assert.That(stored.Tours.Single().Steps.Count).IsEqualTo(2);
        await Assert.That(stored.Tours.Single().Steps.Single(row => row.StepKey == "shell.help").Id)
            .IsEqualTo(winningStep);
    }

    [Test]
    public async Task PruningOldMaterialDeletesItsStepsAfterCompatibleCarryForward()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var original = await SeedSemanticAsync(database);
        Guid retiredTour = original.Tours.Single().Id;
        await using (var context = database.Open(database.TenantId))
        {
            var repository = new LearningRepository(context);
            var state = (await repository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
            state.RecordSemanticTourAction(retiredTour, Definition, state.ActiveClaimId,
                Guid.CreateVersion7(), LearningTourAction.Interrupt, null, null, null,
                PlanFingerprint, PolicySourceStamp, state.Revision, Now.AddSeconds(2));
            for (int version = 3; version <= 4; version++)
            {
                var definition = new LearningTourDefinition(Definition.Family, version, Definition.Steps);
                Guid tourId = Guid.CreateVersion7();
                state.ClaimSemanticTour(tourId, definition, Guid.CreateVersion7(), Guid.CreateVersion7(),
                    state.Revision, Now.AddSeconds(version), PlanFingerprint, PolicySourceStamp);
                state.RecordSemanticTourAction(tourId, definition, state.ActiveClaimId,
                    Guid.CreateVersion7(), LearningTourAction.Interrupt, null, null, null,
                    PlanFingerprint, PolicySourceStamp, state.Revision, Now.AddSeconds(version));
            }
            await repository.SaveChangesAsync(default);
        }
        await using var observer = database.Open(database.TenantId);
        var stored = (await new LearningRepository(observer)
            .GetStateAsync(database.TenantId, database.UserId, false, default))!;
        await Assert.That(stored.Tours.Select(row => row.MaterialVersion)).IsEquivalentTo([3, 4]);
        await Assert.That(stored.Tours.All(row => row.Steps.Single().StepKey == "shell.navigation")).IsTrue();
        await Assert.That(await observer.Set<LearningTourStepProgress>()
            .AnyAsync(row => row.TourProgressId == retiredTour)).IsFalse();
    }

    [Test]
    public async Task ProtectedSubjectPurgeRemovesStepsAcrossTenantsAndPreservesOtherOwner()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        await SeedSemanticAsync(database);
        await SeedSemanticAsync(database, database.OtherTenantId);
        var retained = await SeedSemanticAsync(database, userId: database.OtherUserId);
        await using (var context = database.Open(null))
        {
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var store = new LearningPrivacyErasureStore(context);
                var intent = Intent(database.UserId);
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.RequireAbsentAsync(intent, default));
                await store.PurgeAsync(intent, default);
                await store.RequireAbsentAsync(intent, default);
                await store.PurgeAsync(intent, default);
                await transaction.CommitAsync();
            });
        }
        await using var first = database.Open(database.TenantId);
        await using var second = database.Open(database.OtherTenantId);
        await Assert.That(await first.Set<LearningTourStepProgress>().Select(row => row.Id).ToArrayAsync())
            .IsEquivalentTo(retained.Tours.Single().Steps.Select(row => row.Id));
        await Assert.That(await second.Set<LearningTourStepProgress>().AnyAsync()).IsFalse();
        await Assert.That(await new LearningRepository(first)
            .GetStateAsync(database.TenantId, database.UserId, false, default)).IsNull();
        await Assert.That(await new LearningRepository(second)
            .GetStateAsync(database.OtherTenantId, database.UserId, false, default)).IsNull();
    }

    private static async Task<UserLearningState> SeedSemanticAsync(
        LearningProviderDatabase database, Guid? tenantId = null, Guid? userId = null)
    {
        Guid tenant = tenantId ?? database.TenantId;
        var state = UserLearningState.Create(Guid.CreateVersion7(), tenant, userId ?? database.UserId, Now);
        Guid tourId = Guid.CreateVersion7();
        Guid claimId = Guid.CreateVersion7();
        state.ClaimSemanticTour(tourId, Definition, claimId, Guid.CreateVersion7(), state.Revision,
            Now, PlanFingerprint, PolicySourceStamp);
        state.RecordSemanticTourAction(tourId, Definition, claimId, Guid.CreateVersion7(),
            LearningTourAction.Progress, "shell.navigation", 1, null, PlanFingerprint, PolicySourceStamp,
            state.Revision, Now.AddSeconds(1));
        await using var context = database.Open(tenant);
        var repository = new LearningRepository(context);
        await repository.AddStateAsync(state, default);
        await repository.SaveChangesAsync(default);
        return state;
    }

    private static void RecordHelp(UserLearningState state) =>
        state.RecordSemanticTourAction(state.Tours.Single().Id, Definition, state.ActiveClaimId,
            Guid.CreateVersion7(), LearningTourAction.Progress, "shell.help", 2, null,
            PlanFingerprint, PolicySourceStamp, state.Revision, Now.AddSeconds(2));

    private static bool IsForeignKeyViolation(System.Data.Common.DbException failure) => failure switch
    {
        Npgsql.PostgresException postgres => postgres.SqlState == "23503",
        Microsoft.Data.Sqlite.SqliteException sqlite => sqlite.SqliteExtendedErrorCode == 787,
        Microsoft.Data.SqlClient.SqlException sqlServer => sqlServer.Number == 547,
        MySqlConnector.MySqlException mysql => mysql.Number == 1452,
        _ => false
    };
}
