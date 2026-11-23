using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Learning;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Learning;

[Category("LearningProvider")]
public sealed class LearningProviderContractTests
{
    [Test]
    public async Task DuplicateTenantSpaceAndInternalOwnerHaveOnePhysicalRow()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await using (var duplicate = database.Open(database.TenantId))
        {
            var repository = new LearningRepository(duplicate);
            await repository.AddSpaceAsync(TenantLearningSpace.Create(
                Guid.CreateVersion7(), database.TenantId, LearningProviderDatabase.Now), default);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.SaveChangesAsync(default));
        }
        await using (var duplicate = database.Open(database.TenantId))
        {
            var repository = new LearningRepository(duplicate);
            await repository.AddStateAsync(UserLearningState.Create(
                Guid.CreateVersion7(), database.TenantId, database.UserId, LearningProviderDatabase.Now), default);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.SaveChangesAsync(default));
        }
        await using var observer = database.Open(database.TenantId);
        await Assert.That(await observer.TenantLearningSpaces.CountAsync()).IsEqualTo(1);
        await Assert.That(await observer.UserLearningStates.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task OriginalLoadedRevisionCannotOverwriteCommittedPreferences()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await using var first = database.Open(database.TenantId);
        await using var second = database.Open(database.TenantId);
        var winnerRepository = new LearningRepository(first);
        var loserRepository = new LearningRepository(second);
        var winner = (await winnerRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        var loser = (await loserRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        long originalRevision = winner.Revision;
        await Assert.That(loser.Revision).IsEqualTo(originalRevision);
        winner.UpdatePreferences(true, false, true, originalRevision, LearningProviderDatabase.Now);
        loser.UpdatePreferences(false, true, false, originalRevision, LearningProviderDatabase.Now);
        await winnerRepository.SaveChangesAsync(default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => loserRepository.SaveChangesAsync(default));
        await using var observer = database.Open(database.TenantId);
        var stored = (await new LearningRepository(observer)
            .GetStateAsync(database.TenantId, database.UserId, false, default))!;
        await Assert.That(stored.Revision).IsEqualTo(originalRevision + 1);
        await Assert.That(stored.SuppressAutomaticTours).IsTrue();
        await Assert.That(stored.IsHidden).IsFalse();
        await Assert.That(stored.IsPinned).IsTrue();
    }

    [Test]
    public async Task TwoOriginalRowClaimsHaveOneDurableClaimAndNoLosingChild()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await using var first = database.Open(database.TenantId);
        await using var second = database.Open(database.TenantId);
        var winnerRepository = new LearningRepository(first);
        var loserRepository = new LearningRepository(second);
        var winner = (await winnerRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        var loser = (await loserRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        Guid claim = Guid.CreateVersion7();
        Guid winningTour = Guid.CreateVersion7();
        Guid losingTour = Guid.CreateVersion7();
        winner.ClaimSemanticTour(winningTour,
            new(LearningTourFamily.Shell, 2, [new("shell.navigation", 1)]), claim,
            Guid.CreateVersion7(), winner.Revision, LearningProviderDatabase.Now, new string('A', 64), new string('B', 64));
        loser.ClaimSemanticTour(losingTour,
            new(LearningTourFamily.Studio, 2, [new("studio.actor-context", 1)]), Guid.CreateVersion7(),
            Guid.CreateVersion7(), loser.Revision, LearningProviderDatabase.Now, new string('A', 64), new string('B', 64));
        await winnerRepository.SaveChangesAsync(default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => loserRepository.SaveChangesAsync(default));
        await using var observer = database.Open(database.TenantId);
        var stored = (await new LearningRepository(observer)
            .GetStateAsync(database.TenantId, database.UserId, false, default))!;
        await Assert.That(stored.ActiveClaimId).IsEqualTo(claim);
        await Assert.That(stored.Tours.Select(row => row.Id)).IsEquivalentTo([winningTour]);
        await Assert.That(await observer.Set<LearningTourProgress>().AnyAsync(row => row.Id == losingTour)).IsFalse();
    }

    [Test]
    public async Task PrivatePracticeLookupRequiresBothOriginalTenantAndInternalOwner()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var state = await database.SeedLearningAsync(withChildren: true);
        Guid practice = state.Practices.Single().Id;
        await using var owner = database.Open(database.TenantId);
        var repository = new LearningRepository(owner);
        await Assert.That(await repository.GetPracticeAsync(database.TenantId, database.UserId, practice, default)).IsNotNull();
        await Assert.That(await repository.GetPracticeAsync(database.TenantId, database.OtherUserId, practice, default)).IsNull();
        await using var foreign = database.Open(database.OtherTenantId);
        await Assert.That(await new LearningRepository(foreign)
            .GetStateAsync(database.TenantId, database.UserId, false, default)).IsNull();
        await Assert.That(await new LearningRepository(foreign)
            .GetPracticeAsync(database.TenantId, database.UserId, practice, default)).IsNull();
        await using var unbound = database.Open(null);
        await Assert.That(await new LearningRepository(unbound)
            .GetStateAsync(database.TenantId, database.UserId, false, default)).IsNull();
        await Assert.That(await new LearningRepository(unbound)
            .GetPracticeAsync(database.TenantId, database.UserId, practice, default)).IsNull();
    }

    [Test]
    public async Task SharedSpaceTombstoneRejectsStaleRecreationAndPreservesLearnerState()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var state = await database.SeedLearningAsync(withChildren: true);
        await using var first = database.Open(database.TenantId);
        await using var second = database.Open(database.TenantId);
        var repository = new LearningRepository(first);
        var staleRepository = new LearningRepository(second);
        var space = (await repository.GetSpaceAsync(database.TenantId, true, default))!;
        var stale = (await staleRepository.GetSpaceAsync(database.TenantId, true, default))!;
        space.Remove(space.Revision, LearningProviderDatabase.Now);
        stale.Remove(stale.Revision, LearningProviderDatabase.Now);
        await repository.SaveChangesAsync(default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => staleRepository.SaveChangesAsync(default));
        await using var refreshed = database.Open(database.TenantId);
        var refreshedRepository = new LearningRepository(refreshed);
        var removed = (await refreshedRepository.GetSpaceAsync(database.TenantId, true, default))!;
        await Assert.That(removed.Id).IsEqualTo(space.Id);
        await Assert.That(removed.IsRemoved).IsTrue();
        await Assert.That(removed.RemovedAtUtc).IsEqualTo(LearningProviderDatabase.Now);
        await Assert.That((await refreshedRepository.GetStateAsync(
            database.TenantId, database.UserId, false, default))!.Practices.Select(row => row.Id))
            .IsEquivalentTo(state.Practices.Select(row => row.Id));
        await using var staleRecreationContext = database.Open(database.TenantId);
        var staleRecreationRepository = new LearningRepository(staleRecreationContext);
        var staleRemoved = (await staleRecreationRepository.GetSpaceAsync(database.TenantId, true, default))!;
        removed.Recreate(removed.Revision, LearningProviderDatabase.Now.AddSeconds(1));
        staleRemoved.Recreate(staleRemoved.Revision, LearningProviderDatabase.Now.AddSeconds(1));
        await refreshedRepository.SaveChangesAsync(default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => staleRecreationRepository.SaveChangesAsync(default));
        await using var observer = database.Open(database.TenantId);
        var recreated = (await new LearningRepository(observer).GetSpaceAsync(database.TenantId, false, default))!;
        await Assert.That(recreated.Id).IsEqualTo(space.Id);
        await Assert.That(recreated.IsRemoved).IsFalse();
        await Assert.That(recreated.Revision).IsEqualTo(2);
    }

    [Test]
    public async Task ChildNaturalKeysRejectDuplicateTourVersionsPracticeContextsStepsAndAcknowledgements()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var state = await SeedConstraintChildrenAsync(database);
        await using var context = database.Open(database.TenantId);
        foreach (Type child in new[] { typeof(LearningTourProgress), typeof(LearningPracticeState),
            typeof(LearningTourStepProgress), typeof(LearningRequirementAcknowledgement) })
        {
            var entity = context.Model.FindEntityType(child)!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = context.GetService<ISqlGenerationHelper>();
            string name = sql.DelimitIdentifier(table.Name, table.Schema);
            var properties = entity.GetProperties().ToArray();
            string columns = string.Join(", ", properties.Select(property =>
                sql.DelimitIdentifier(property.GetColumnName(table)!)));
            string values = string.Join(", ", properties.Select(property => property.Name == "Id"
                ? "{0}" : sql.DelimitIdentifier(property.GetColumnName(table)!)));
            bool step = child == typeof(LearningTourStepProgress);
            string parent = sql.DelimitIdentifier(entity.FindProperty(
                step ? "TourProgressId" : "UserLearningStateId")!.GetColumnName(table)!);
            Guid parentId = step ? state.Tours.Single().Id : state.Id;
            var failure = await Assert.ThrowsAsync<System.Data.Common.DbException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    $"INSERT INTO {name} ({columns}) SELECT {values} FROM {name} WHERE {parent} = {{1}}",
                    Guid.CreateVersion7(), parentId));
            await Assert.That(failure switch
            {
                Npgsql.PostgresException postgres => postgres.SqlState == "23505",
                Microsoft.Data.Sqlite.SqliteException sqlite => sqlite.SqliteExtendedErrorCode == 2067,
                Microsoft.Data.SqlClient.SqlException sqlServer => sqlServer.Number is 2601 or 2627,
                MySqlConnector.MySqlException mysql => mysql.Number == 1062,
                _ => false
            }).IsTrue();
        }
        await Assert.That(await context.Set<LearningTourProgress>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.Set<LearningPracticeState>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.Set<LearningTourStepProgress>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.LearningRequirementAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task CompositeChildForeignKeysRejectAnotherTenantEvenWhenParentExists()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var state = await SeedConstraintChildrenAsync(database);
        await using var context = database.Open(database.TenantId);
        foreach (Type child in new[] { typeof(LearningTourProgress), typeof(LearningPracticeState),
            typeof(LearningTourStepProgress), typeof(LearningRequirementAcknowledgement) })
        {
            var entity = context.Model.FindEntityType(child)!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = context.GetService<ISqlGenerationHelper>();
            string tenantColumn = sql.DelimitIdentifier(entity.FindProperty(nameof(LearningPracticeState.TenantId))!.GetColumnName(table)!);
            bool step = child == typeof(LearningTourStepProgress);
            string parentColumn = sql.DelimitIdentifier(entity.FindProperty(
                step ? "TourProgressId" : "UserLearningStateId")!.GetColumnName(table)!);
            Guid parentId = step ? state.Tours.Single().Id : state.Id;
            string name = sql.DelimitIdentifier(table.Name, table.Schema);
            var failure = await Assert.ThrowsAsync<System.Data.Common.DbException>(() => context.Database.ExecuteSqlRawAsync(
                $"UPDATE {name} SET {tenantColumn} = {{0}} WHERE {parentColumn} = {{1}}",
                database.OtherTenantId, parentId));
            await Assert.That(failure switch
            {
                Npgsql.PostgresException postgres => postgres.SqlState == "23503",
                Microsoft.Data.Sqlite.SqliteException sqlite => sqlite.SqliteExtendedErrorCode == 787,
                Microsoft.Data.SqlClient.SqlException sqlServer => sqlServer.Number == 547,
                MySqlConnector.MySqlException mysql => mysql.Number == 1452,
                _ => false
            }).IsTrue();
        }
        await Assert.That(await context.Set<LearningTourProgress>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.Set<LearningPracticeState>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.Set<LearningTourStepProgress>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.LearningRequirementAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    private static async Task<UserLearningState> SeedConstraintChildrenAsync(LearningProviderDatabase database)
    {
        await database.SeedLearningAsync(withChildren: true);
        await using var context = database.Open(database.TenantId);
        var repository = new LearningRepository(context);
        var state = (await repository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        state.AcknowledgeRequirement(Guid.CreateVersion7(), LearningRequirementDefinition.Current,
            new string('A', 64), Guid.CreateVersion7(), state.Revision, LearningProviderDatabase.Now);
        await repository.SaveChangesAsync(default);
        return state;
    }

    [Test]
    public async Task OwnerCanClearOneOrphanContextWithoutClearingOtherOwnersOrRecipes()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var state = await database.SeedLearningAsync(withChildren: true);
        await database.SeedLearningAsync(userId: database.OtherUserId, withChildren: true);
        await using (var context = database.Open(database.TenantId))
        {
            var repository = new LearningRepository(context);
            var loaded = (await repository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
            loaded.ApplyPractice(Guid.CreateVersion7(), "self", "sessions-program", 1,
                LearningPracticeAction.Start, LearningPracticeChoice.None, 0, 0,
                Guid.CreateVersion7(), loaded.Revision, 0, LearningProviderDatabase.Now);
            var practice = loaded.Practices.Single(row => row.RecipeKey == "event-details");
            loaded.ApplyPractice(practice.Id, practice.ContextKey, practice.RecipeKey, 1,
                LearningPracticeAction.Clear, LearningPracticeChoice.None, 0, 0,
                Guid.CreateVersion7(), loaded.Revision, practice.Revision, LearningProviderDatabase.Now);
            await repository.SaveChangesAsync(default);
        }
        await using var observer = database.Open(database.TenantId);
        var reads = new LearningRepository(observer);
        var own = (await reads.GetStateAsync(database.TenantId, database.UserId, false, default))!;
        var other = (await reads.GetStateAsync(database.TenantId, database.OtherUserId, false, default))!;
        await Assert.That(own.Practices.Select(row => row.RecipeKey)).IsEquivalentTo(["sessions-program"]);
        await Assert.That(other.Practices.Select(row => row.RecipeKey)).IsEquivalentTo(["event-details"]);
        await Assert.That(await reads.GetPracticeAsync(
            database.TenantId, database.UserId, state.Practices.Single().Id, default)).IsNull();
        await Assert.That(own.Tours.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PhysicalTenantDeletionCascadesLearningOnlyWithinThatTenant()
    {
        await using var database = await LearningProviderDatabase.CreateTenantDeletionAsync();
        await database.SeedLearningAsync(withChildren: true);
        await database.SeedLearningAsync(database.OtherTenantId, withChildren: true);
        await using (var context = database.Open(database.TenantId))
        {
            Tenant tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
            await using var sourceOwner = new HumanSecuritySourceGate(context);
            await using var source = await sourceOwner.AcquireAsync();
            await new TenantRepository(context).Delete(tenant);
        }
        await using var observer = database.OpenSystem();
        await Assert.That(await observer.TenantLearningSpaces.Select(row => row.TenantId).ToArrayAsync())
            .IsEquivalentTo([database.OtherTenantId]);
        await Assert.That(await observer.UserLearningStates.Select(row => row.TenantId).ToArrayAsync())
            .IsEquivalentTo([database.OtherTenantId]);
        await Assert.That(await observer.Set<LearningTourProgress>().Select(row => row.TenantId).ToArrayAsync())
            .IsEquivalentTo([database.OtherTenantId]);
        await Assert.That(await observer.Set<LearningPracticeState>().Select(row => row.TenantId).ToArrayAsync())
            .IsEquivalentTo([database.OtherTenantId]);
        await Assert.That(await observer.Users.AnyAsync(row => row.Id == database.UserId)).IsTrue();
    }
}

/// <summary>
/// Source-only specifications for the closed retention port. Authentication, role
/// enrollment and service resolution are prerequisites, not evidence of rejection.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class LearningSourceRetentionPostgresTests(PostgreSqlContainerFixture fixture)
{
    private const string GuidanceKey = "tenant.learning_guidance";

    [Test]
    public async Task OriginalRuntimeHumanRetainsActualAbsenceAndInstanceTenantSettings()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var authentication = await AuthenticatedSettingsFixture.CreateAsync(
            CreateInstanceSeed, fixture);
        await using var rows = await SourceRows.CreateAsync(fixture, token);

        await authentication.RunAsync(rows.TenantId, async session =>
        {
            await RequireHumanRoleAsync(session, PrimaryDatabaseRole.Runtime, token);
            var source = session.Services.GetRequiredService<ILearningSourceRetention>();
            var observation = await source.ObserveAsync(token);
            await Assert.That(observation.Facts.TenantId).IsEqualTo(rows.TenantId);
            await Assert.That(observation.Facts.GuidanceDocument).IsNull();
            await Assert.That(observation.Facts.PersonalCreation).IsTrue();
            var tenant = observation.Facts.EffectiveSettings[GovernanceSettingKeys.Events.UserSubmissionEnabled];
            await Assert.That(tenant.Value).IsEqualTo("true");
            await Assert.That(tenant.Source).IsEqualTo(SettingSource.TenantOverride);
            var instance = observation.Facts.EffectiveSettings[GovernanceSettingKeys.AiAssistant.Enabled];
            await Assert.That(instance.Value).IsEqualTo("false");
            await Assert.That(instance.Source).IsEqualTo(SettingSource.SystemDefault);
            await Assert.That(string.IsNullOrWhiteSpace(observation.Facts.WorkspaceSourceStamp)).IsFalse();
            await Assert.That(string.IsNullOrWhiteSpace(observation.Facts.PolicySourceStamp)).IsFalse();
            await Assert.That(session.Context.ChangeTracker.Entries().Any()).IsFalse();
            await Assert.That(session.Context.Database.CurrentTransaction).IsNull();

            await using var owner = await session.Context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, token);
            var connection = session.Context.Database.GetDbConnection();
            var held = await source.AcquireAsync(observation, token);
            await held.ValidateAsync(token);
            held.RequireCurrentAt(DateTimeOffset.UtcNow);
            await Assert.That(held.Facts.TenantId).IsEqualTo(rows.TenantId);
            await Assert.That(held.Facts.GuidanceDocument).IsNull();
            await Assert.That(held.Facts.WorkspaceSourceStamp).IsEqualTo(observation.Facts.WorkspaceSourceStamp);
            await Assert.That(held.Facts.PolicySourceStamp).IsEqualTo(observation.Facts.PolicySourceStamp);
            await Assert.That(held.Facts.EffectiveSettings[GovernanceSettingKeys.Events.UserSubmissionEnabled].Value)
                .IsEqualTo("true");
            await Assert.That(session.Context.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await Assert.That(session.Context.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await owner.RollbackAsync(token);
            return true;
        }, cancellationToken: token);

        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.TenantSettingsDocuments.AsNoTracking()
            .AnyAsync(row => row.TenantId == rows.TenantId && row.DocumentKey == GuidanceKey, token)).IsFalse();
    }

    [Test]
    [Arguments("system-value")]
    [Arguments("system-lock")]
    [Arguments("tenant-value")]
    [Arguments("tenant-insert")]
    [Arguments("document-insert")]
    public async Task CommittedSourceChangeBetweenObservationAndAcquisitionRejectsOriginalImage(string change)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var authentication = await AuthenticatedSettingsFixture.CreateAsync(
            CreateInstanceSeed, fixture);
        await using var rows = await SourceRows.CreateAsync(fixture, token);

        await authentication.RunAsync(rows.TenantId, async session =>
        {
            await RequireHumanRoleAsync(session, PrimaryDatabaseRole.Runtime, token);
            var source = session.Services.GetRequiredService<ILearningSourceRetention>();
            var observation = await source.ObserveAsync(token);
            await Assert.That(observation.Facts.GuidanceDocument).IsNull();
            // A successful independent commit is the exact ordering barrier: it
            // follows observation and precedes acquisition, without scheduler waits.
            await rows.ChangeAsync(change, token);
            await using var owner = await session.Context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, token);
            var failure = await Assert.ThrowsAsync<Exception>(() => source.AcquireAsync(observation, token));
            await Assert.That(failure is ConcurrencyConflictException
                or PostgresException { SqlState: "40001" }).IsTrue();
            await Assert.That(session.Context.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await owner.RollbackAsync(token);
            return true;
        }, cancellationToken: token);
    }

    [Test]
    public async Task AnotherLiveRuntimeIssuerCannotAcquireOriginalObservation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var authentication = await AuthenticatedSettingsFixture.CreateAsync(
            CreateInstanceSeed, fixture);
        await using var rows = await SourceRows.CreateAsync(fixture, token);

        await authentication.RunAsync(rows.TenantId, async original =>
        {
            await RequireHumanRoleAsync(original, PrimaryDatabaseRole.Runtime, token);
            var source = original.Services.GetRequiredService<ILearningSourceRetention>();
            var observation = await source.ObserveAsync(token);
            return await authentication.RunAsync(rows.TenantId, async other =>
            {
                await RequireHumanRoleAsync(other, PrimaryDatabaseRole.Runtime, token);
                await Assert.That(other.Context).IsNotSameReferenceAs(original.Context);
                var foreign = other.Services.GetRequiredService<ILearningSourceRetention>();
                var ownObservation = await foreign.ObserveAsync(token);
                await Assert.That(ownObservation.Facts.TenantId).IsEqualTo(rows.TenantId);
                await using var owner = await other.Context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, token);
                var failure = await Assert.ThrowsAsync<Exception>(() => foreign.AcquireAsync(observation, token));
                await Assert.That(failure is InvalidOperationException or UnauthorizedAccessException).IsTrue();
                await owner.RollbackAsync(token);
                return true;
            }, cancellationToken: token);
        }, cancellationToken: token);
    }

    [Test]
    public async Task RetainedLeaseCannotMoveToReplacementSerializablePhysicalOwner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var authentication = await AuthenticatedSettingsFixture.CreateAsync(
            CreateInstanceSeed, fixture);
        await using var rows = await SourceRows.CreateAsync(fixture, token);

        await authentication.RunAsync(rows.TenantId, async session =>
        {
            await RequireHumanRoleAsync(session, PrimaryDatabaseRole.Runtime, token);
            var source = session.Services.GetRequiredService<ILearningSourceRetention>();
            var observation = await source.ObserveAsync(token);
            ILearningSourceLease held;
            await using (var original = await session.Context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, token))
            {
                held = await source.AcquireAsync(observation, token);
                await held.ValidateAsync(token);
                await original.RollbackAsync(token);
            }
            await using var replacement = await session.Context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, token);
            var failure = await Assert.ThrowsAsync<Exception>(() => held.ValidateAsync(token));
            await Assert.That(failure is InvalidOperationException or UnauthorizedAccessException).IsTrue();
            var clockFailure = Assert.Throws<Exception>(() => held.RequireCurrentAt(DateTimeOffset.UtcNow));
            await Assert.That(clockFailure is InvalidOperationException or UnauthorizedAccessException).IsTrue();
            var acquireFailure = await Assert.ThrowsAsync<Exception>(() => source.AcquireAsync(observation, token));
            await Assert.That(acquireFailure is InvalidOperationException or UnauthorizedAccessException).IsTrue();
            await replacement.RollbackAsync(token);
            return true;
        }, cancellationToken: token);
    }

    [Test]
    public async Task ActualGlobalHumanCannotBorrowSelectedRuntimeRetention()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var authentication = await AuthenticatedSettingsFixture.CreateAsync(
            CreateInstanceSeed, fixture);
        await using var rows = await SourceRows.CreateAsync(fixture, token);

        await authentication.RunAsync(rows.TenantId, async runtime =>
        {
            await RequireHumanRoleAsync(runtime, PrimaryDatabaseRole.Runtime, token);
            var source = runtime.Services.GetRequiredService<ILearningSourceRetention>();
            var observation = await source.ObserveAsync(token);
            return await authentication.RunAsync(null, async global =>
            {
                await RequireHumanRoleAsync(global, PrimaryDatabaseRole.Global, token);
                // Resolution is deliberately outside denial assertions. An absent
                // implementation or registration is setup failure, not a security pass.
                var forbidden = global.Services.GetRequiredService<ILearningSourceRetention>();
                var observeFailure = await Assert.ThrowsAsync<Exception>(() => forbidden.ObserveAsync(token));
                await Assert.That(observeFailure is InvalidOperationException or UnauthorizedAccessException
                    or PostgresException { SqlState: "42501" }).IsTrue();
                await using var owner = await global.Context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, token);
                var acquireFailure = await Assert.ThrowsAsync<Exception>(() => forbidden.AcquireAsync(observation, token));
                await Assert.That(acquireFailure is InvalidOperationException or UnauthorizedAccessException
                    or PostgresException { SqlState: "42501" }).IsTrue();
                await owner.RollbackAsync(token);
                return true;
            }, cancellationToken: token);
        }, cancellationToken: token);
    }

    private ExploreDbContext CreateInstanceSeed()
    {
        var context = fixture.CreateDbContext();
        context.ClearTenantFilterBypass();
        return context;
    }

    private async Task RequireHumanRoleAsync(
        AuthenticatedSettingsFixture.Session session, PrimaryDatabaseRole role, CancellationToken token)
        => await RequireHumanRoleAsync(fixture, session, role, token);

    internal static async Task RequireHumanRoleAsync(
        PostgreSqlContainerFixture fixture, AuthenticatedSettingsFixture.Session session,
        PrimaryDatabaseRole role, CancellationToken token)
    {
        var execution = session.Services.GetRequiredService<ExploreDbContextExecutionLease>();
        await Assert.That(execution.Context).IsSameReferenceAs(session.Context);
        await Assert.That(execution.HumanScope).IsNotNull();
        await Assert.That(execution.HumanScope!.Purpose).IsEqualTo(role == PrimaryDatabaseRole.Runtime
            ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance);
        await Assert.That(execution.HumanScope.Subject.PrincipalId).IsEqualTo(session.ActorId);
        await Assert.That(execution.HumanLifetime).IsNotNull();
        session.Services.GetRequiredService<IHumanOriginalCredentialCustody>()
            .RequireOriginalCredential(execution.HumanLifetime!);
        await Assert.That(session.Context.IsTenantFilterBypassed).IsFalse();
        await session.Context.Database.OpenConnectionAsync(token);
        await using var command = session.Context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT session_user = @expected AND current_user = session_user
                AND NOT rolsuper AND NOT rolbypassrls
            FROM pg_catalog.pg_roles WHERE rolname = session_user
            """;
        command.Parameters.Add(new NpgsqlParameter("expected", fixture.GetNativeQueueOptions(role).Username!));
        await Assert.That(await command.ExecuteScalarAsync(token)).IsEqualTo(true);
    }

    internal sealed class SourceRows(
        PostgreSqlContainerFixture fixture, ExploreDbContext arrangement,
        SystemSetting events, SystemSetting ai,
        (bool Inserted, string Value, bool Locked) originalEvents,
        (bool Inserted, string Value, bool Locked) originalAi) : IAsyncDisposable
    {
        internal Guid TenantId { get; } = Guid.CreateVersion7();

        internal static async Task<SourceRows> CreateAsync(PostgreSqlContainerFixture fixture, CancellationToken token)
        {
            var arrangement = fixture.CreateDbContext();
            arrangement.BindTenantLease();
            try
            {
                async Task<(SystemSetting Row, (bool Inserted, string Value, bool Locked) Original)> PrepareAsync(string key)
                {
                    var row = await arrangement.SystemSettings.SingleOrDefaultAsync(value => value.SettingKey == key, token);
                    var original = (Inserted: row is null, Value: row?.Value ?? string.Empty, Locked: row?.IsLocked ?? false);
                    if (row is null)
                    {
                        row = new SystemSetting
                        {
                            Id = Guid.CreateVersion7(),
                            SettingKey = key,
                            Value = "false",
                            ValueType = SettingValueType.Boolean,
                            CreatedAt = LearningProviderDatabase.Now
                        };
                        arrangement.SystemSettings.Add(row);
                    }
                    row.Value = "false";
                    row.IsLocked = false;
                    return (row, original);
                }
                var events = await PrepareAsync(GovernanceSettingKeys.Events.UserSubmissionEnabled);
                var ai = await PrepareAsync(GovernanceSettingKeys.AiAssistant.Enabled);
                var rows = new SourceRows(fixture, arrangement, events.Row, ai.Row, events.Original, ai.Original);
                arrangement.Tenants.Add(new Tenant
                {
                    Id = rows.TenantId,
                    FullName = "Learning retained sources",
                    Slug = $"learning-sources-{rows.TenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                });
                arrangement.Set<TenantSetting>().Add(new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = rows.TenantId,
                    Tenant = null!,
                    SettingKey = GovernanceSettingKeys.Events.UserSubmissionEnabled,
                    Value = "true",
                    CreatedAt = LearningProviderDatabase.Now
                });
                await using var human = new HumanSecuritySourceGate(arrangement);
                await using var heldHuman = await human.AcquireAsync(token);
                await using var discovery = new EventDiscoveryEnrollmentGate(arrangement);
                await using var heldDiscovery = await discovery.AcquireAsync(token);
                await arrangement.SaveChangesAsync(token);
                return rows;
            }
            catch
            {
                await arrangement.DisposeAsync();
                throw;
            }
        }

        internal async Task ChangeAsync(string change, CancellationToken token)
        {
            await using var writer = fixture.CreateDbContext();
            writer.BindTenantLease();
            switch (change)
            {
                case "system-value":
                case "system-lock":
                    var system = await writer.SystemSettings.SingleAsync(
                        row => row.SettingKey == GovernanceSettingKeys.Events.UserSubmissionEnabled, token);
                    if (change == "system-value") system.Value = "true";
                    else system.IsLocked = true;
                    break;
                case "tenant-value":
                    (await writer.Set<TenantSetting>().SingleAsync(row => row.TenantId == TenantId
                        && row.SettingKey == GovernanceSettingKeys.Events.UserSubmissionEnabled, token)).Value = "false";
                    break;
                case "tenant-insert":
                    writer.Set<TenantSetting>().Add(new TenantSetting
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = TenantId,
                        Tenant = null!,
                        SettingKey = GovernanceSettingKeys.AiAssistant.Enabled,
                        Value = "true",
                        CreatedAt = LearningProviderDatabase.Now
                    });
                    break;
                case "document-insert":
                    var document = TenantSettingsDocument.Create(TenantId, GuidanceKey, 1, "1", """{"enabled":false}""");
                    document.Id = Guid.CreateVersion7();
                    document.CreatedAt = LearningProviderDatabase.Now;
                    writer.TenantSettingsDocuments.Add(document);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
            await writer.SaveChangesAsync(token);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                Restore(events, originalEvents);
                Restore(ai, originalAi);
                await arrangement.SaveChangesAsync();
            }
            finally { await arrangement.DisposeAsync(); }

            void Restore(SystemSetting row, (bool Inserted, string Value, bool Locked) original)
            {
                if (original.Inserted) arrangement.SystemSettings.Remove(row);
                else
                {
                    row.Value = original.Value;
                    row.IsLocked = original.Locked;
                    arrangement.Entry(row).Property(value => value.Value).IsModified = true;
                    arrangement.Entry(row).Property(value => value.IsLocked).IsModified = true;
                }
            }
        }
    }
}

/// <summary>
/// Exercises original-human retention with independently selected external privacy
/// authority storage. The existing CoLocated cohort remains a separate profile.
/// </summary>
[ClassDataSource<LearningExternalAuthorityPostgresFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class LearningSourceRetentionExternalAuthorityPostgresTests(
    LearningExternalAuthorityPostgresFixture fixture)
{
    [Test]
    public async Task OriginalRuntimeHumanRetainsSourcesWithDedicatedExternalAuthority()
    {
        await using var authentication = await fixture.CreateAuthenticationAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var rows = await LearningSourceRetentionPostgresTests.SourceRows.CreateAsync(
            fixture.Application, token);

        await authentication.RunAsync(rows.TenantId, async session =>
        {
            await RequireOriginalRuntimeAsync(session, token);
            var source = session.Services.GetRequiredService<ILearningSourceRetention>();
            var observation = await source.ObserveAsync(token);
            await Assert.That(observation.Facts.TenantId).IsEqualTo(rows.TenantId);
            await Assert.That(observation.Facts.GuidanceDocument).IsNull();
            await Assert.That(observation.Facts.PersonalCreation).IsTrue();
            await Assert.That(observation.Facts.EffectiveSettings[
                GovernanceSettingKeys.Events.UserSubmissionEnabled].Value).IsEqualTo("true");
            await Assert.That(observation.Facts.EffectiveSettings[
                GovernanceSettingKeys.Events.UserSubmissionEnabled].Source).IsEqualTo(SettingSource.TenantOverride);
            await Assert.That(observation.Facts.EffectiveSettings[
                GovernanceSettingKeys.AiAssistant.Enabled].Value).IsEqualTo("false");
            await Assert.That(observation.Facts.EffectiveSettings[
                GovernanceSettingKeys.AiAssistant.Enabled].Source).IsEqualTo(SettingSource.SystemDefault);
            await Assert.That(session.Context.ChangeTracker.Entries()).IsEmpty();
            await Assert.That(session.Context.Database.CurrentTransaction).IsNull();

            var context = session.Context;
            var connection = context.Database.GetDbConnection();
            await using var owner = await context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, token);
            var held = await source.AcquireAsync(observation, token);
            await held.ValidateAsync(token);
            held.RequireCurrentAt(DateTimeOffset.UtcNow);
            await Assert.That(held.Facts.TenantId).IsEqualTo(rows.TenantId);
            await Assert.That(held.Facts.GuidanceDocument).IsNull();
            await Assert.That(held.Facts.PersonalCreation).IsTrue();
            await Assert.That(held.Facts.EffectiveSettings[
                GovernanceSettingKeys.Events.UserSubmissionEnabled].Value).IsEqualTo("true");
            await Assert.That(string.IsNullOrWhiteSpace(held.Facts.WorkspaceSourceStamp)).IsFalse();
            await Assert.That(string.IsNullOrWhiteSpace(held.Facts.PolicySourceStamp)).IsFalse();
            await Assert.That(held.Facts.WorkspaceSourceStamp).IsEqualTo(observation.Facts.WorkspaceSourceStamp);
            await Assert.That(held.Facts.PolicySourceStamp).IsEqualTo(observation.Facts.PolicySourceStamp);
            await Assert.That(session.Context).IsSameReferenceAs(context);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await owner.RollbackAsync(token);
            var settled = Assert.Throws<Exception>(() => held.RequireCurrentAt(DateTimeOffset.UtcNow));
            await Assert.That(settled is InvalidOperationException or UnauthorizedAccessException).IsTrue();
            return true;
        }, cancellationToken: token);
    }

    [Test]
    public async Task CommittedGuidanceInsertionRejectsOriginalExternalAuthoritySourceImage()
    {
        await using var authentication = await fixture.CreateAuthenticationAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var rows = await LearningSourceRetentionPostgresTests.SourceRows.CreateAsync(
            fixture.Application, token);

        await authentication.RunAsync(rows.TenantId, async session =>
        {
            await RequireOriginalRuntimeAsync(session, token);
            var source = session.Services.GetRequiredService<ILearningSourceRetention>();
            var observation = await source.ObserveAsync(token);
            await Assert.That(observation.Facts.GuidanceDocument).IsNull();
            // The committed independent writer orders the change before acquisition.
            await rows.ChangeAsync("document-insert", token);
            await using var owner = await session.Context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, token);
            var failure = await Assert.ThrowsAsync<Exception>(() => source.AcquireAsync(observation, token));
            await Assert.That(failure is ConcurrencyConflictException
                or PostgresException { SqlState: "40001" }).IsTrue();
            await Assert.That(session.Context.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await owner.RollbackAsync(token);
            return true;
        }, cancellationToken: token);
    }

    private async Task RequireOriginalRuntimeAsync(
        AuthenticatedSettingsFixture.Session session, CancellationToken token)
    {
        await LearningSourceRetentionPostgresTests.RequireHumanRoleAsync(
            fixture.Application, session, PrimaryDatabaseRole.Runtime, token);
        await Assert.That(session.Services.GetRequiredService<IOptions<PrivacyErasureDurabilityOptions>>()
            .Value.Topology).IsEqualTo(PrivacyErasureAuthorityTopology.ExternalDatabase);
        await Assert.That(session.Services.GetRequiredService<IConfiguration>()["IdentityDatabase:Topology"])
            .IsEqualTo("Colocated");
        var authority = session.Services.GetRequiredService<PrivacyErasureAuthorityDbContext>();
        await authority.Database.OpenConnectionAsync(token);
        await using var identity = authority.Database.GetDbConnection().CreateCommand();
        identity.CommandText = """
            SELECT session_user = @expected AND current_user = session_user
                AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole
                AND pg_catalog.pg_has_role(current_user, @runtime, 'USAGE')
                AND NOT pg_catalog.pg_has_role(current_user, @runtime, 'SET')
                AND NOT pg_catalog.pg_has_role(current_user, @migrator, 'MEMBER')
                AND NOT pg_catalog.pg_has_role(current_user, @owner, 'MEMBER')
            FROM pg_catalog.pg_roles WHERE rolname = session_user
            """;
        identity.Parameters.Add(new NpgsqlParameter("expected", fixture.AuthorityRuntimeUsername));
        identity.Parameters.Add(new NpgsqlParameter("runtime", PrivacyErasureAuthorityDatabaseContract.RuntimeRole));
        identity.Parameters.Add(new NpgsqlParameter("migrator", PrivacyErasureAuthorityDatabaseContract.MigratorRole));
        identity.Parameters.Add(new NpgsqlParameter("owner", PrivacyErasureAuthorityDatabaseContract.OwnerRole));
        await Assert.That(await identity.ExecuteScalarAsync(token)).IsEqualTo(true);
    }
}

/// <summary>
/// Probes fixed document storage rights using enrolled credentials, not human mutation authority.
/// Each invocation owns its database; rejected statements cannot poison another test's fixture.
/// </summary>
[Category("LearningProvider")]
[NotInParallel("PersistenceDb")]
public sealed class LearningGuidanceStoragePostgresTests
{
    [Test]
    public async Task ActualEnrolledRuntimeCanWriteOnlySelectedTenantGuidanceWithoutDelete()
    {
        await using var owned = new PostgreSqlContainerFixture();
        await owned.InitializeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        Guid tenantId = Guid.CreateVersion7();
        Guid foreignTenantId = Guid.CreateVersion7();
        Guid brandingId = Guid.CreateVersion7();
        Guid foreignGuidanceId = Guid.CreateVersion7();
        Guid guidanceId = Guid.CreateVersion7();
        Guid initialStamp = Guid.CreateVersion7();
        Guid updatedStamp = Guid.CreateVersion7();
        const string defaultsVersion = "learning-guidance:1";
        const string initialPayload = """
            {"enabled":true,"automaticInvitations":true,"invitationAudience":"ActiveMembers","manualAccessAudience":"ActiveMembers","requirementPolicy":"Optional","familyOverrides":[]}
            """;
        const string updatedPayload = """
            {"enabled":true,"automaticInvitations":false,"invitationAudience":"ActiveMembers","manualAccessAudience":"ActiveMembers","requirementPolicy":"Optional","familyOverrides":[]}
            """;

        var migratorOptions = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(migratorOptions,
            owned.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var migrator = new ExploreDbContext(migratorOptions.Options);
        migrator.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);
        migrator.BindTenantLease();
        await migrator.Database.OpenConnectionAsync(token);
        await using (var identity = migrator.Database.GetDbConnection().CreateCommand())
        {
            identity.CommandText = "SELECT session_user = @migrator AND current_user = session_user";
            identity.Parameters.Add(new NpgsqlParameter("migrator", owned.NativeQueueEnrollment.MigratorLogin));
            await Assert.That(await identity.ExecuteScalarAsync(token)).IsEqualTo(true);
        }
        var tenants = new[] { tenantId, foreignTenantId }.Select(id => new Tenant
        {
            Id = id,
            Slug = $"guidance-permissions-{id:N}",
            FullName = "Guidance storage permission witness",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        }).ToArray();
        migrator.Tenants.AddRange(tenants);
        var branding = TenantSettingsDocument.Create(
            tenantId, SettingsDocumentKeys.Tenant.Branding, 1, defaultsVersion, "{}");
        branding.Id = brandingId;
        branding.Tenant = tenants[0];
        var foreignGuidance = TenantSettingsDocument.Create(
            foreignTenantId, SettingsDocumentKeys.Tenant.LearningGuidance, 1, defaultsVersion, initialPayload);
        foreignGuidance.Id = foreignGuidanceId;
        foreignGuidance.Tenant = tenants[1];
        migrator.TenantSettingsDocuments.AddRange(branding, foreignGuidance);
        await using (var human = new HumanSecuritySourceGate(migrator))
        await using (var heldHuman = await human.AcquireAsync(token))
        await using (var discovery = new EventDiscoveryEnrollmentGate(migrator))
        await using (var heldDiscovery = await discovery.AcquireAsync(token))
            await migrator.SaveChangesAsync(token);

        await using var runtime = await new TenantExploreDbContextFactory(
            owned.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)).CreateDbContextAsync(tenantId);
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync(token);
        await using (var identity = runtime.Database.GetDbConnection().CreateCommand())
        {
            identity.CommandText = """
                SELECT session_user = @runtime AND current_user = session_user
                    AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole
                    AND NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '')::uuid = @tenant
                FROM pg_catalog.pg_roles WHERE rolname = session_user
                """;
            identity.Parameters.Add(new NpgsqlParameter("runtime", owned.NativeQueueEnrollment.RuntimeLogin));
            identity.Parameters.Add(new NpgsqlParameter("tenant", tenantId));
            await Assert.That(await identity.ExecuteScalarAsync(token)).IsEqualTo(true);
        }

        // Ignore only EF filters: the actual database must hide both seeded forbidden rows.
        // Missing SELECT permission is an intended Red here, never a passing denial assertion.
        await Assert.That(await runtime.TenantSettingsDocuments.IgnoreQueryFilters()
            .AsNoTracking().ToArrayAsync(token)).IsEmpty();

        var entity = runtime.Model.FindEntityType(typeof(TenantSettingsDocument))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = runtime.GetService<ISqlGenerationHelper>();
        string tableName = names.DelimitIdentifier(table.Name, table.Schema);
        string Column(string property) =>
            names.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
        string idColumn = Column(nameof(TenantSettingsDocument.Id));
        string tenantColumn = Column(nameof(TenantSettingsDocument.TenantId));
        string keyColumn = Column(nameof(TenantSettingsDocument.DocumentKey));
        string payloadColumn = Column(nameof(TenantSettingsDocument.PayloadJson));
        string stampColumn = Column(nameof(TenantSettingsDocument.ConcurrencyStamp));

        // Native parameterized DML reaches PostgreSQL privileges directly, without
        // impersonating a prepared human or bypassing an EF mutation-owner guard.
        async Task<int> InsertAsync(Guid targetTenant, string key, Guid documentId)
        {
            await using var command = runtime.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"""
                INSERT INTO {tableName}
                    ({idColumn}, {tenantColumn}, {keyColumn},
                     {Column(nameof(TenantSettingsDocument.SchemaVersion))},
                     {Column(nameof(TenantSettingsDocument.DefaultsVersion))},
                     {payloadColumn}, {stampColumn}, {Column(nameof(TenantSettingsDocument.CreatedAt))})
                VALUES (@id, @tenant, @key, @schema, @defaults, @payload, @stamp, @created)
                """;
            command.Parameters.Add(new NpgsqlParameter("id", documentId));
            command.Parameters.Add(new NpgsqlParameter("tenant", targetTenant));
            command.Parameters.Add(new NpgsqlParameter("key", key));
            command.Parameters.Add(new NpgsqlParameter("schema", 1));
            command.Parameters.Add(new NpgsqlParameter("defaults", defaultsVersion));
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlTypes.NpgsqlDbType.Jsonb)
            { Value = initialPayload });
            command.Parameters.Add(new NpgsqlParameter("stamp", initialStamp));
            command.Parameters.Add(new NpgsqlParameter("created", LearningProviderDatabase.Now));
            return await command.ExecuteNonQueryAsync(token);
        }

        async Task<int> UpdateAsync(Guid documentId)
        {
            await using var command = runtime.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"""
                UPDATE {tableName} SET {payloadColumn} = @payload, {stampColumn} = @stamp
                WHERE {idColumn} = @id
                """;
            command.Parameters.Add(new NpgsqlParameter("id", documentId));
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlTypes.NpgsqlDbType.Jsonb)
            { Value = updatedPayload });
            command.Parameters.Add(new NpgsqlParameter("stamp", updatedStamp));
            return await command.ExecuteNonQueryAsync(token);
        }

        await Assert.That(await InsertAsync(
            tenantId, SettingsDocumentKeys.Tenant.LearningGuidance, guidanceId)).IsEqualTo(1);
        var visible = await runtime.TenantSettingsDocuments.IgnoreQueryFilters().AsNoTracking().ToArrayAsync(token);
        await Assert.That(visible.Select(row => row.Id)).IsEquivalentTo([guidanceId]);
        await Assert.That(visible.Single().TenantId).IsEqualTo(tenantId);
        await Assert.That(visible.Single().DocumentKey).IsEqualTo(SettingsDocumentKeys.Tenant.LearningGuidance);
        await Assert.That(visible.Single().ConcurrencyStamp).IsEqualTo(initialStamp);
        await Assert.That(await UpdateAsync(guidanceId)).IsEqualTo(1);
        var accepted = await runtime.TenantSettingsDocuments.IgnoreQueryFilters().AsNoTracking().SingleAsync(token);
        await Assert.That(accepted.ConcurrencyStamp).IsEqualTo(updatedStamp);
        using (var payload = System.Text.Json.JsonDocument.Parse(accepted.PayloadJson))
            await Assert.That(payload.RootElement.GetProperty("automaticInvitations").GetBoolean()).IsFalse();

        // Creation's full catalog includes this document table even when its caller's
        // direct sources do not. Its optional guidance intersection must remain exact.
        await PostgresTenantCreationPermissions.ApplyAsync(
            migrator, owned.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
        string runtimeLogin = names.DelimitIdentifier(owned.NativeQueueEnrollment.RuntimeLogin);
        foreach (var (change, restore) in new[]
        {
            ($"GRANT UPDATE ({keyColumn}) ON {tableName} TO {runtimeLogin}",
                $"REVOKE UPDATE ({keyColumn}) ON {tableName} FROM {runtimeLogin}"),
            ($"GRANT SELECT ({payloadColumn}) ON {tableName} TO {runtimeLogin} WITH GRANT OPTION",
                $"REVOKE GRANT OPTION FOR SELECT ({payloadColumn}) ON {tableName} FROM {runtimeLogin}"),
            ($"REVOKE UPDATE ({stampColumn}) ON {tableName} FROM {runtimeLogin}",
                $"GRANT UPDATE ({stampColumn}) ON {tableName} TO {runtimeLogin}")
        })
        {
            await migrator.Database.ExecuteSqlRawAsync(change, token);
            try
            {
                var drift = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PostgresTenantCreationPermissions.ApplyAsync(
                        migrator, owned.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token));
                await Assert.That(drift!.Message).IsEqualTo("tenant_creation_permissions_drift");
            }
            finally
            {
                // Restore only this invocation's one privilege change, including when
                // an assertion fails. Do not rerun the guidance installer to hide drift.
                await migrator.Database.ExecuteSqlRawAsync(restore, CancellationToken.None);
            }
            await PostgresTenantCreationPermissions.ApplyAsync(
                migrator, owned.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
        }

        // Autocommit isolates each denied statement; no failed transaction is reused.
        var otherKey = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(tenantId, SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity, Guid.CreateVersion7()));
        await Assert.That(otherKey!.SqlState).IsEqualTo("42501");
        var foreignInsert = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(foreignTenantId, SettingsDocumentKeys.Tenant.LearningGuidance, Guid.CreateVersion7()));
        await Assert.That(foreignInsert!.SqlState).IsEqualTo("42501");
        await Assert.That(await UpdateAsync(brandingId)).IsEqualTo(0);
        await Assert.That(await UpdateAsync(foreignGuidanceId)).IsEqualTo(0);
        await using (var changeKey = runtime.Database.GetDbConnection().CreateCommand())
        {
            changeKey.CommandText = $"UPDATE {tableName} SET {keyColumn} = @key WHERE {idColumn} = @id";
            changeKey.Parameters.Add(new NpgsqlParameter("key", SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity));
            changeKey.Parameters.Add(new NpgsqlParameter("id", guidanceId));
            var denied = await Assert.ThrowsAsync<PostgresException>(() => changeKey.ExecuteNonQueryAsync(token));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
        await using (var changeTenant = runtime.Database.GetDbConnection().CreateCommand())
        {
            changeTenant.CommandText = $"UPDATE {tableName} SET {tenantColumn} = @tenant WHERE {idColumn} = @id";
            changeTenant.Parameters.Add(new NpgsqlParameter("tenant", foreignTenantId));
            changeTenant.Parameters.Add(new NpgsqlParameter("id", guidanceId));
            var denied = await Assert.ThrowsAsync<PostgresException>(() => changeTenant.ExecuteNonQueryAsync(token));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
        await using (var delete = runtime.Database.GetDbConnection().CreateCommand())
        {
            delete.CommandText = $"DELETE FROM {tableName} WHERE {idColumn} = @id";
            delete.Parameters.Add(new NpgsqlParameter("id", guidanceId));
            var denied = await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync(token));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
        await Assert.That(await runtime.TenantSettingsDocuments.IgnoreQueryFilters().AsNoTracking()
            .Select(row => row.Id).ToArrayAsync(token)).IsEquivalentTo([guidanceId]);
        var stored = await migrator.TenantSettingsDocuments.AsNoTracking()
            .Where(row => row.TenantId == tenantId || row.TenantId == foreignTenantId).ToArrayAsync(token);
        await Assert.That(stored.Select(row => row.Id))
            .IsEquivalentTo([brandingId, foreignGuidanceId, guidanceId]);
        using var foreignPayload = System.Text.Json.JsonDocument.Parse(
            stored.Single(row => row.Id == foreignGuidanceId).PayloadJson);
        await Assert.That(foreignPayload.RootElement.GetProperty("automaticInvitations").GetBoolean()).IsTrue();
        using var brandingPayload = System.Text.Json.JsonDocument.Parse(
            stored.Single(row => row.Id == brandingId).PayloadJson);
        await Assert.That(brandingPayload.RootElement.EnumerateObject().Count()).IsEqualTo(0);
    }
}

/// <summary>
/// Verifies fixed storage exclusions with the real Runtime login, independently
/// of human authentication. These primitives do not issue learning authority.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class LearningSourceStoragePostgresTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task WarmedGuidanceAttestationRejectsIndependentlyTamperedDocumentBody()
    {
        // Own the entire fixture: changing a routine must never poison the shared cohort.
        await using var owned = new PostgreSqlContainerFixture();
        await owned.InitializeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        Guid tenant = Guid.CreateVersion7();
        await using var context = owned.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        context.BindTenantLease();
        await context.Database.OpenConnectionAsync(token);
        int runtimeBackend;
        await using (var identity = context.Database.GetDbConnection().CreateCommand())
        {
            identity.CommandText = """
                SELECT pg_catalog.pg_backend_pid()
                FROM pg_catalog.pg_roles
                WHERE rolname = session_user AND current_user = session_user
                    AND session_user = @runtime AND NOT rolsuper AND NOT rolbypassrls
                """;
            identity.Parameters.Add(new NpgsqlParameter("runtime",
                owned.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime).Username!));
            runtimeBackend = (int)(await identity.ExecuteScalarAsync(token))!;
        }

        var cold = await PostgresMixedSettingsContract.ReadLearningGuidanceAsync(
            context, owned.NativeQueueEnrollment, tenant, token);
        var warm = await PostgresMixedSettingsContract.ReadLearningGuidanceAsync(
            context, owned.NativeQueueEnrollment, tenant, token);
        await Assert.That(cold).IsNull();
        await Assert.That(warm).IsNull();

        await using (var migrator = owned.CreateDbContext())
        {
            await migrator.Database.OpenConnectionAsync(token);
            await using var command = migrator.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT current_user = session_user AND session_user = @migrator
                    AND pg_catalog.pg_backend_pid() <> @runtime_backend
                """;
            command.Parameters.Add(new NpgsqlParameter("migrator",
                owned.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator).Username!));
            command.Parameters.Add(new NpgsqlParameter("runtime_backend", runtimeBackend));
            await Assert.That(await command.ExecuteScalarAsync(token)).IsEqualTo(true);
            command.Parameters.Clear();
            command.CommandText = "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure(@routine))";
            command.Parameters.Add(new NpgsqlParameter("routine",
                new HumanPrincipalSourceSql(migrator).Name("mixed_settings_learning_document") + "(uuid)"));
            string definition = (string)(await command.ExecuteScalarAsync(token))!;
            await Assert.That(definition.Contains("RETURN image;", StringComparison.Ordinal)).IsTrue();
            command.Parameters.Clear();
            command.CommandText = definition.Replace("RETURN image;", "RETURN NULL;", StringComparison.Ordinal);
            // Autocommit is the ordering barrier before the original Runtime reads again.
            await command.ExecuteNonQueryAsync(token);
        }

        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresMixedSettingsContract.ReadLearningGuidanceAsync(
                context, owned.NativeQueueEnrollment, tenant, token));
        await Assert.That(failure!.SqlState).IsEqualTo("55000");
        await Assert.That(failure.MessageText).IsEqualTo("mixed_settings_exact_body_drift");
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task ActualRuntimeCanObserveTheGuidanceDocumentsAbsence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        Guid tenant = Guid.CreateVersion7();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        context.BindTenantLease();
        await context.Database.OpenConnectionAsync(token);
        await using (var identity = context.Database.GetDbConnection().CreateCommand())
        {
            identity.CommandText = "SELECT current_user";
            await Assert.That((string)(await identity.ExecuteScalarAsync(token))!)
                .IsEqualTo(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime).Username);
        }

        var direct = await context.TenantSettingsDocuments.AsNoTracking().SingleOrDefaultAsync(
            row => row.TenantId == tenant && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance, token);
        await Assert.That(direct).IsNull();
        var document = await PostgresMixedSettingsContract.ReadLearningGuidanceAsync(
            context, fixture.NativeQueueEnrollment, tenant, token);

        await Assert.That(document).IsNull();
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
    }

    [Test]
    public async Task ActualRuntimeReadsOnlySelectedGuidanceDocumentEvidence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var rows = await LearningSourceRetentionPostgresTests.SourceRows.CreateAsync(fixture, token);
        await rows.ChangeAsync("document-insert", token);
        LearningGuidanceDocumentSource expected;
        await using (var observer = fixture.CreateDbContext())
        {
            var document = await observer.TenantSettingsDocuments.AsNoTracking().SingleAsync(
                row => row.TenantId == rows.TenantId
                    && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance, token);
            expected = new(document.Id, document.TenantId, document.SchemaVersion,
                document.DefaultsVersion, document.PayloadJson, document.ConcurrencyStamp);
        }
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(rows.TenantId));
        context.BindTenantLease();
        await context.Database.OpenConnectionAsync(token);

        var actual = await PostgresMixedSettingsContract.ReadLearningGuidanceAsync(
            context, fixture.NativeQueueEnrollment, rows.TenantId, token);

        await Assert.That(actual).IsEqualTo(expected);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresMixedSettingsContract.ReadLearningGuidanceAsync(
                context, fixture.NativeQueueEnrollment, Guid.CreateVersion7(), token));
        await Assert.That(foreign!.SqlState).IsEqualTo("42501");
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RetentionRejectsWrongIsolationOrTenant(bool mixed, bool changedTenant)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        Guid tenant = Guid.CreateVersion7();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        context.BindTenantLease();
        await using var owner = await context.Database.BeginTransactionAsync(changedTenant
            ? System.Data.IsolationLevel.Serializable : System.Data.IsolationLevel.ReadCommitted, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = owner.GetDbTransaction();
        command.CommandText = $"SELECT {new HumanPrincipalSourceSql(context).Name(
            mixed ? "mixed_settings_learning_retain" : "ordinary_settings_learning_retain")}(@tenant)";
        command.Parameters.Add(new NpgsqlParameter("tenant", changedTenant ? Guid.CreateVersion7() : tenant));

        var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));

        await Assert.That(failure!.SqlState).IsEqualTo("42501");
        await owner.RollbackAsync(token);
    }

    [Test]
    public async Task ActualRuntimeRetainsLearningSourcesOnItsOriginalSerializableOwner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        Guid tenant = Guid.CreateVersion7();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        context.BindTenantLease();
        await context.Database.OpenConnectionAsync(token);
        var connection = context.Database.GetDbConnection();
        await using (var identity = connection.CreateCommand())
        {
            identity.CommandText = """
                SELECT current_user = session_user AND current_user = @runtime
                    AND NOT rolsuper AND NOT rolbypassrls
                    AND pg_catalog.pg_has_role(current_user, @reader, 'USAGE')
                    AND NOT pg_catalog.pg_has_role(current_user, @reader, 'SET')
                    AND NOT pg_catalog.pg_has_role(current_user, @global, 'USAGE')
                    AND NOT pg_catalog.pg_has_role(current_user, @global, 'SET')
                FROM pg_catalog.pg_roles WHERE rolname = current_user
                """;
            identity.Parameters.Add(new NpgsqlParameter("runtime",
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime).Username!));
            identity.Parameters.Add(new NpgsqlParameter("reader", PostgresMixedSettingsContract.LearningReaderRole));
            identity.Parameters.Add(new NpgsqlParameter("global", PostgresMixedSettingsContract.GlobalRole));
            await Assert.That((bool)(await identity.ExecuteScalarAsync(token))!).IsTrue();
        }
        await using var owner = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, token);

        await new LearningSourceProviderLock(context, RelationalConnectionIdentity.Capture(context), tenant)
            .AcquireAsync(owner.GetDbTransaction(), token);

        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(owner);
        await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connection);
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
        await owner.RollbackAsync(token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetainedAnchorExcludesIndependentExclusiveOwnerUntilSettlement(bool mixed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        Guid tenant = Guid.CreateVersion7();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        context.BindTenantLease();
        await using var owner = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, token);
        await new LearningSourceProviderLock(context, RelationalConnectionIdentity.Capture(context), tenant)
            .AcquireAsync(owner.GetDbTransaction(), token);
        int ownerBackend;
        await using (var identity = context.Database.GetDbConnection().CreateCommand())
        {
            identity.Transaction = owner.GetDbTransaction();
            identity.CommandText = "SELECT pg_catalog.pg_backend_pid()";
            ownerBackend = (int)(await identity.ExecuteScalarAsync(token))!;
        }

        await using var writer = fixture.CreateDbContext();
        await writer.Database.OpenConnectionAsync(token);
        await using (var identity = writer.Database.GetDbConnection().CreateCommand())
        {
            identity.CommandText = """
                SELECT current_user = session_user AND current_user = @migrator
                    AND pg_catalog.pg_backend_pid() <> @owner
                """;
            identity.Parameters.Add(new NpgsqlParameter("migrator",
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator).Username!));
            identity.Parameters.Add(new NpgsqlParameter("owner", ownerBackend));
            await Assert.That((bool)(await identity.ExecuteScalarAsync(token))!).IsTrue();
        }
        await using var command = writer.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT revision FROM {new HumanPrincipalSourceSql(writer).Name(
            mixed ? PostgresMixedSettingsContract.SourceTable : PostgresOrdinarySettingsContract.SourceTable)}"
            + " WHERE id = 1 FOR UPDATE NOWAIT";
        await using (var excluded = await writer.Database.BeginTransactionAsync(token))
        {
            command.Transaction = excluded.GetDbTransaction();
            var conflict = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            await Assert.That(conflict!.SqlState).IsEqualTo("55P03");
            await excluded.RollbackAsync(token);
        }

        await owner.RollbackAsync(token);

        await using var admitted = await writer.Database.BeginTransactionAsync(token);
        command.Transaction = admitted.GetDbTransaction();
        await Assert.That((long)(await command.ExecuteScalarAsync(token))!).IsGreaterThanOrEqualTo(0);
        await admitted.RollbackAsync(token);
    }

    private sealed class SelectedTenant(Guid tenant) : ITenantContext
    {
        public Guid TenantId { get; set; } = tenant;
    }
}

/// <summary>
/// Uses only the explicitly selected engine and native migration composition.
/// Every destructive operation targets the unique database created by this instance.
/// Storage contracts use the selected Migrator; restricted Runtime/Global authority
/// is proved separately by LearningPostgresPermissionsTests.
/// </summary>
internal sealed class LearningProviderDatabase : IAsyncDisposable
{
    internal static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private readonly PrimaryDatabaseProviderBehaviorFixture _fixture;
    private readonly PrimaryDatabaseConnectionOptions _options;
    private readonly PostgreSqlContainerFixture? _enrolled;
    internal Guid TenantId { get; } = Guid.CreateVersion7();
    internal Guid OtherTenantId { get; } = Guid.CreateVersion7();
    internal Guid UserId { get; } = Guid.CreateVersion7();
    internal Guid OtherUserId { get; } = Guid.CreateVersion7();
    internal PrimaryDatabaseProvider Provider => _options.Provider;

    private LearningProviderDatabase(
        PrimaryDatabaseConnectionOptions options, PostgreSqlContainerFixture? enrolled = null)
    {
        _options = options;
        _fixture = PrimaryDatabaseProviderBehaviorFixture.Create(options);
        _enrolled = enrolled;
    }

    internal static async Task<LearningProviderDatabase> CreateTenantDeletionAsync()
    {
        if (Environment.GetEnvironmentVariable("Database__Provider") != "PostgreSql")
            return await CreateAsync();
        var enrolled = new PostgreSqlContainerFixture();
        try
        {
            await enrolled.InitializeAsync();
            var database = new LearningProviderDatabase(
                enrolled.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator), enrolled);
            await using var context = database.OpenSystem();
            await database.SeedOwnersAsync(context);
            return database;
        }
        catch
        {
            await enrolled.DisposeAsync();
            throw;
        }
    }

    internal static async Task<LearningProviderDatabase> CreateAsync(bool seed = true, bool migrations = false)
    {
        string selected = Environment.GetEnvironmentVariable("Database__Provider")
            ?? throw new InvalidOperationException("Learning assurance requires exact Database__Provider and approved structured Migrator credentials.");
        PrimaryDatabaseProvider provider = selected switch
        {
            "PostgreSql" => PrimaryDatabaseProvider.PostgreSql,
            "Sqlite" => PrimaryDatabaseProvider.Sqlite,
            "SqlServer" => PrimaryDatabaseProvider.SqlServer,
            "MySql" => PrimaryDatabaseProvider.MySql,
            "MariaDb" => PrimaryDatabaseProvider.MariaDb,
            _ => throw new InvalidOperationException("Database__Provider must be exactly PostgreSql, Sqlite, SqlServer, MySql or MariaDb.")
        };
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var source = PrimaryDatabaseConfiguration.BindMigrator(configuration);
        if (source.Provider != provider)
            throw new InvalidOperationException("The selected Learning provider differs from the native Migrator provider.");
        string name = $"learning_{Guid.CreateVersion7():N}";
        string ownedDatabase = provider == PrimaryDatabaseProvider.Sqlite
            ? Path.Combine(Path.GetTempPath(), name + ".db") : name;
        if (string.Equals(source.Database, ownedDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Learning assurance cannot own the configured shared database.");
        var database = new LearningProviderDatabase(source with { Database = ownedDatabase });
        try
        {
            await using var context = database.OpenSystem();
            if (migrations)
                await context.Database.MigrateAsync();
            else
                await context.Database.EnsureCreatedAsync();
            if (provider is PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb)
            {
                await context.Database.OpenConnectionAsync();
                await using var probe = context.Database.GetDbConnection().CreateCommand();
                probe.CommandText = "SELECT VERSION()";
                string version = (string)(await probe.ExecuteScalarAsync())!;
                bool mariaDb = version.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
                if (mariaDb != (provider == PrimaryDatabaseProvider.MariaDb))
                    throw new InvalidOperationException("The connected MySQL-family engine does not match exact Database__Provider.");
            }
            if (seed)
            {
                await LookupSeedAsync(context);
                await database.SeedOwnersAsync(context);
            }
            return database;
        }
        catch (Exception failure)
        {
            try
            {
                await database.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    $"Learning assurance could not provision or clean up its owned {provider} database.",
                    failure, cleanupFailure);
            }
            throw;
        }
    }

    internal ExploreDbContext Open(Guid? tenantId)
    {
        var context = _fixture.CreateTenantContext(tenantId);
        context.BindTenantLease();
        return context;
    }

    internal ExploreDbContext OpenSystem()
    {
        var context = _fixture.CreateSystemContext();
        context.BindTenantLease();
        return context;
    }

    private static async Task LookupSeedAsync(ExploreDbContext context)
    {
        if (!await context.Set<TenantStatus>().AnyAsync(row => row.Id == (int)TenantStatusEnum.Active))
            context.Add(new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active,
                MasterCode = "Active",
                FullName = "Active",
                IsActiveState = true
            });
    }

    private async Task SeedOwnersAsync(ExploreDbContext context)
    {
        foreach (Guid tenant in new[] { TenantId, OtherTenantId })
            context.Add(new Tenant
            {
                Id = tenant,
                FullName = "Learning provider assurance",
                Slug = $"learning-{tenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
        context.AddRange(new User { Id = UserId, Pii = null! }, new User { Id = OtherUserId, Pii = null! });
        await using var humanOwner = new HumanSecuritySourceGate(context);
        await using var human = await humanOwner.AcquireAsync();
        await using var discoveryOwner = new EventDiscoveryEnrollmentGate(context);
        await using var discovery = await discoveryOwner.AcquireAsync();
        await context.SaveChangesAsync();
    }

    internal async Task<UserLearningState> SeedLearningAsync(
        Guid? tenantId = null, Guid? userId = null, bool withChildren = false)
    {
        Guid tenant = tenantId ?? TenantId;
        await using var context = Open(tenant);
        var repository = new LearningRepository(context);
        if (await repository.GetSpaceAsync(tenant, false, default) is null)
            await repository.AddSpaceAsync(TenantLearningSpace.Create(Guid.CreateVersion7(), tenant, Now), default);
        var state = UserLearningState.Create(Guid.CreateVersion7(), tenant, userId ?? UserId, Now);
        if (withChildren) AddChildren(state);
        await repository.AddStateAsync(state, default);
        await repository.SaveChangesAsync(default);
        return state;
    }

    internal static void AddChildren(UserLearningState state)
    {
        var definition = new LearningTourDefinition(LearningTourFamily.Shell, 2, [new("shell.navigation", 1)]);
        Guid tour = Guid.CreateVersion7(), claim = Guid.CreateVersion7();
        string plan = new('A', 64), policy = new('B', 64);
        state.ClaimSemanticTour(tour, definition, claim, Guid.CreateVersion7(), state.Revision, Now, plan, policy);
        state.RecordSemanticTourAction(tour, definition, claim, Guid.CreateVersion7(),
            LearningTourAction.Progress, "shell.navigation", 1, null, plan, policy, state.Revision, Now);
        state.RecordSemanticTourAction(tour, definition, claim, Guid.CreateVersion7(),
            LearningTourAction.Skip, null, null, null, plan, policy, state.Revision, Now);
        state.ApplyPractice(Guid.CreateVersion7(), $"actor:{Guid.CreateVersion7():D}", "event-details", 1,
            LearningPracticeAction.Start, LearningPracticeChoice.None, 0, 0,
            Guid.CreateVersion7(), state.Revision, 0, Now);
    }

    internal static PrivacyErasureIntent Intent(Guid subjectId) => PrivacyErasureIntent.Record(
        Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User, subjectId,
        PrivacyErasureReasonCode.SubjectErasureRequest, 1, Now, Now);

    public async ValueTask DisposeAsync()
    {
        if (_enrolled is not null)
        {
            await _enrolled.DisposeAsync();
            return;
        }
        await using var context = OpenSystem();
        await context.Database.EnsureDeletedAsync();
    }

}
