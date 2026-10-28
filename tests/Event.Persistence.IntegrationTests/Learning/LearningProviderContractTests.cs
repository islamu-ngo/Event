using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Learning;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

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
        winner.ClaimTour(winningTour, LearningTourFamily.Shell, 1, claim,
            Guid.CreateVersion7(), winner.Revision, LearningProviderDatabase.Now);
        loser.ClaimTour(losingTour, LearningTourFamily.Studio, 1, Guid.CreateVersion7(),
            Guid.CreateVersion7(), loser.Revision, LearningProviderDatabase.Now);
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
    public async Task ChildNaturalKeysRejectDuplicateTourVersionsAndPracticeContexts()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var state = await database.SeedLearningAsync(withChildren: true);
        await using var context = database.Open(database.TenantId);
        foreach (Type child in new[] { typeof(LearningTourProgress), typeof(LearningPracticeState) })
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
            string parent = sql.DelimitIdentifier(entity.FindProperty("UserLearningStateId")!.GetColumnName(table)!);
            var failure = await Assert.ThrowsAsync<System.Data.Common.DbException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    $"INSERT INTO {name} ({columns}) SELECT {values} FROM {name} WHERE {parent} = {{1}}",
                    Guid.CreateVersion7(), state.Id));
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
    }

    [Test]
    public async Task CompositeChildForeignKeysRejectAnotherTenantEvenWhenParentExists()
    {
        await using var database = await LearningProviderDatabase.CreateAsync();
        var state = await database.SeedLearningAsync(withChildren: true);
        await using var context = database.Open(database.TenantId);
        foreach (Type child in new[] { typeof(LearningTourProgress), typeof(LearningPracticeState) })
        {
            var entity = context.Model.FindEntityType(child)!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = context.GetService<ISqlGenerationHelper>();
            string tenantColumn = sql.DelimitIdentifier(entity.FindProperty(nameof(LearningPracticeState.TenantId))!.GetColumnName(table)!);
            string parentColumn = sql.DelimitIdentifier(entity.FindProperty(nameof(LearningPracticeState.UserLearningStateId))!.GetColumnName(table)!);
            string name = sql.DelimitIdentifier(table.Name, table.Schema);
            var failure = await Assert.ThrowsAsync<System.Data.Common.DbException>(() => context.Database.ExecuteSqlRawAsync(
                $"UPDATE {name} SET {tenantColumn} = {{0}} WHERE {parentColumn} = {{1}}",
                database.OtherTenantId, state.Id));
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
                Id = (int)TenantStatusEnum.Active, MasterCode = "Active", FullName = "Active", IsActiveState = true
            });
    }

    private async Task SeedOwnersAsync(ExploreDbContext context)
    {
        foreach (Guid tenant in new[] { TenantId, OtherTenantId })
            context.Add(new Tenant
            {
                Id = tenant, FullName = "Learning provider assurance", Slug = $"learning-{tenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
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
        state.SkipInvitation(Guid.CreateVersion7(), LearningTourFamily.Shell, 1,
            Guid.CreateVersion7(), state.Revision, Now);
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
