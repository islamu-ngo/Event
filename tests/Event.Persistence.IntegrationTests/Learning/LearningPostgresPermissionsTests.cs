using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Learning;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Learning;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class LearningPostgresPermissionsTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ActualRuntimeCanPersistLearningButCannotCrossTheTenantPolicy()
    {
        var seed = await SeedAsync();
        await using (var owner = await new TenantExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)).CreateDbContextAsync(seed.Tenant))
        {
            owner.BindTenantLease();
            await RequireOrdinaryLogin(owner, fixture.NativeQueueEnrollment.RuntimeLogin);
            var repository = new LearningRepository(owner);
            var state = UserLearningState.Create(Guid.CreateVersion7(), seed.Tenant, seed.User, Now);
            state.ApplyPractice(Guid.CreateVersion7(), "self", "event-details", 1,
                LearningPracticeAction.Start, LearningPracticeChoice.None, 0, 0,
                Guid.CreateVersion7(), 0, 0, Now);
            await repository.AddStateAsync(state, default);
            await repository.SaveChangesAsync(default);
            await Assert.That((await repository.GetStateAsync(seed.Tenant, seed.User, false, default))!.Revision)
                .IsEqualTo(1);
        }
        await using var foreign = await new TenantExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)).CreateDbContextAsync(seed.OtherTenant);
        foreign.BindTenantLease();
        await Assert.That(await foreign.UserLearningStates.IgnoreQueryFilters()
            .AnyAsync(row => row.TenantId == seed.Tenant && row.UserId == seed.User)).IsFalse();
    }

    [Test]
    public async Task ActualGlobalPurgeUsesRetainedFactAndDeletesOnlyTheSubjectAcrossTenants()
    {
        var seed = await SeedAsync();
        Guid[] roots;
        Guid otherRoot;
        await using (var arrangement = fixture.CreateDbContext())
        {
            var states = new[]
            {
                State(seed.Tenant, seed.User), State(seed.OtherTenant, seed.User),
                State(seed.Tenant, seed.OtherUser)
            };
            roots = states.Take(2).Select(row => row.Id).ToArray();
            otherRoot = states[2].Id;
            arrangement.AddRange(states);
            await arrangement.SaveChangesAsync();
            var names = arrangement.GetService<ISqlGenerationHelper>();
            await arrangement.Database.ExecuteSqlRawAsync(
                $"GRANT {names.DelimitIdentifier(PostgresSubjectPrivacyErasureContract.GlobalRole)} TO {names.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin)}");
        }
        PrivacyErasureIntent fact = await RecordRetainedFactAsync(seed.User);
        await using (var global = await new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)).CreateDbContextAsync())
        {
            global.BindTenantLease();
            await RequireOrdinaryLogin(global, fixture.NativeQueueEnrollment.GlobalLogin);
            await ((ISerializableAttemptOwner)new EfCoreUnitOfWork(global)).ExecuteAsync(async token =>
            {
                var store = new LearningPrivacyErasureStore(global);
                await store.PurgeAsync(fact, token);
                await store.RequireAbsentAsync(fact, token);
                return true;
            }, default);
        }
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.UserLearningStates.AnyAsync(row => roots.Contains(row.Id))).IsFalse();
        await Assert.That(await observer.Set<LearningPracticeState>()
            .AnyAsync(row => roots.Contains(row.UserLearningStateId))).IsFalse();
        await Assert.That(await observer.UserLearningStates.AnyAsync(row => row.Id == otherRoot)).IsTrue();
    }

    [Test]
    public async Task ProtectedGlobalPurposeDoesNotReceivePracticeDetailOrInsertAuthority()
    {
        var seed = await SeedAsync();
        await using (var arrangement = fixture.CreateDbContext())
        {
            var names = arrangement.GetService<ISqlGenerationHelper>();
            await arrangement.Database.ExecuteSqlRawAsync(
                $"GRANT {names.DelimitIdentifier(PostgresSubjectPrivacyErasureContract.GlobalRole)} TO {names.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin)}");
        }
        await using var global = await new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)).CreateDbContextAsync();
        global.BindTenantLease();
        var read = await Assert.ThrowsAsync<PostgresException>(() =>
            global.Set<LearningPracticeState>().IgnoreQueryFilters().AnyAsync());
        await Assert.That(read!.SqlState).IsEqualTo("42501");
        global.UserLearningStates.Add(UserLearningState.Create(Guid.CreateVersion7(), seed.Tenant, seed.User, Now));
        var insert = await Assert.ThrowsAsync<DbUpdateException>(() => global.SaveChangesAsync());
        await Assert.That(((PostgresException)insert!.InnerException!).SqlState).IsEqualTo("42501");
    }

    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private async Task<(Guid Tenant, Guid OtherTenant, Guid User, Guid OtherUser)> SeedAsync()
    {
        var ids = (Tenant: Guid.CreateVersion7(), OtherTenant: Guid.CreateVersion7(),
            User: Guid.CreateVersion7(), OtherUser: Guid.CreateVersion7());
        await using var context = fixture.CreateDbContext();
        context.BindTenantLease();
        context.AddRange(new[] { ids.Tenant, ids.OtherTenant }.Select(id => new Tenant
        {
            Id = id, Slug = $"learning-native-{id:N}", FullName = "Native learning assurance",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        }));
        context.AddRange(new User { Id = ids.User, Pii = null! }, new User { Id = ids.OtherUser, Pii = null! });
        await using var human = new HumanSecuritySourceGate(context);
        await using var heldHuman = await human.AcquireAsync();
        await using var discovery = new EventDiscoveryEnrollmentGate(context);
        await using var heldDiscovery = await discovery.AcquireAsync();
        await context.SaveChangesAsync();
        return ids;
    }

    private async Task<PrivacyErasureIntent> RecordRetainedFactAsync(Guid user)
    {
        var options = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var database = new CoLocatedPrivacyErasureAuthorityDbContext(options.Options);
        // Permission assurance uses the canonical current model; migration assurance is a separate gate.
        await database.Database.ExecuteSqlRawAsync(database.Database.GenerateCreateScript());
        var authority = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
            database, TimeProvider.System, Options.Create(new PrivacyErasureOptions()));
        return await authority.AppendAsync(new PrivacyErasureRequest(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, user, PrivacyErasureReasonCode.AccountDeletion, 1));
    }

    private static UserLearningState State(Guid tenant, Guid user)
    {
        var state = UserLearningState.Create(Guid.CreateVersion7(), tenant, user, Now);
        state.ApplyPractice(Guid.CreateVersion7(), "self", "event-details", 1,
            LearningPracticeAction.Start, LearningPracticeChoice.None, 0, 0,
            Guid.CreateVersion7(), 0, 0, Now);
        return state;
    }

    private static async Task RequireOrdinaryLogin(ExploreDbContext context, string expected)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT session_user = @expected AND current_user = session_user
                AND NOT rolsuper AND NOT rolbypassrls
            FROM pg_catalog.pg_roles WHERE rolname = session_user
            """;
        command.Parameters.Add(new NpgsqlParameter("expected", expected));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }
}
