using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ActorOwnershipReaderTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ProjectedRuntimeReadPreservesUserMembershipAndSoftDeletionBoundaries()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(rows.Tenant));
        context.BindTenantLease();
        var reader = new ActorOwnershipReader(context, fixture.NativeQueueEnrollment);

        var active = await reader.ReadForUserAsync(rows.Active.User, rows.Tenant);
        await Assert.That(active).IsEqualTo(new ActorOwnershipFacts(rows.Active.Actor, rows.Active.User, null, null));
        var suspended = await reader.ReadForUserAsync(rows.Suspended.User, rows.Tenant);
        await Assert.That(suspended).IsEqualTo(new ActorOwnershipFacts(rows.Suspended.Actor, rows.Suspended.User, null, null));
        await Assert.That(await reader.ReadForUserAsync(rows.DeletedMember.User, rows.Tenant)).IsNull();
        await Assert.That(await reader.ReadForUserAsync(rows.DeletedActor.User, rows.Tenant)).IsNull();
        await Assert.That(await reader.ReadForUserAsync(rows.Foreign.User, rows.Tenant)).IsNull();
        await Assert.That(await reader.ReadForUserAsync(Guid.CreateVersion7(), rows.Tenant)).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(rows.Tenant);
        await Assert.That(context.CurrentUserService).IsNull();
    }

    [Test]
    public async Task FailedInitialReadinessReleasesOwnedConnectionAndRetainsRecoveryOnOriginalContext()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(rows.Tenant));
        context.BindTenantLease();
        var reader = new ActorOwnershipReader(context, fixture.NativeQueueEnrollment);
        var connection = context.Database.GetDbConnection();
        var contextId = context.ContextId;
        var expected = new ActorOwnershipFacts(rows.Active.Actor, rows.Active.User, null, null);
        await Assert.That(await reader.ReadForUserAsync(rows.Active.User, rows.Tenant)).IsEqualTo(expected);
        await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Closed);

        await using var migration = OpenMigrator();
        await migration.Database.OpenConnectionAsync();
        string table = new HumanPrincipalSourceSql(migration).Table(typeof(TenantUser));
        try
        {
            await using (var change = new NpgsqlCommand($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY",
                (NpgsqlConnection)migration.Database.GetDbConnection()))
                await change.ExecuteNonQueryAsync();
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reader.ReadForUserAsync(rows.Active.User, rows.Tenant));
            await Assert.That(denied?.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
            await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Closed);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(context.ContextId).IsEqualTo(contextId);
            await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(await migration.Database.SqlQuery<bool>($"""
                SELECT relforcerowsecurity AS "Value" FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass({table})
                """).SingleAsync()).IsFalse();
        }
        finally
        {
            await using var restore = new NpgsqlCommand($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY",
                (NpgsqlConnection)migration.Database.GetDbConnection());
            await restore.ExecuteNonQueryAsync(CancellationToken.None);
        }
        await Assert.That(await reader.ReadForUserAsync(rows.Active.User, rows.Tenant)).IsEqualTo(expected);
        await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Closed);
    }

    [Test]
    public async Task FailedOriginalQueryBalancesOpeningBeforeTheSameContextRecovers()
    {
        var rows = await SeedAsync();
        var interruption = new TerminateOriginalQueryBackend(fixture.ConnectionString);
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(rows.Tenant), interruption);
        context.BindTenantLease();
        var reader = new ActorOwnershipReader(context, fixture.NativeQueueEnrollment);
        var connection = context.Database.GetDbConnection();
        var contextId = context.ContextId;
        var denied = await Assert.ThrowsAsync<Exception>(() => reader.ReadForUserAsync(rows.Active.User, rows.Tenant));
        await Assert.That(interruption.Terminated).IsTrue();
        await Assert.That(denied).IsNotNull();
        IEnumerable<Exception> causes = denied is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [denied!];
        await Assert.That(causes.Any(error => error is NpgsqlException
            || error is UnauthorizedAccessException { Message: "actor_ownership_original_connection_changed" })).IsTrue();
        await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Closed);
        await Assert.That(context.Database.CurrentTransaction).IsNull();

        await Assert.That(await reader.ReadForUserAsync(rows.Active.User, rows.Tenant))
            .IsEqualTo(new ActorOwnershipFacts(rows.Active.Actor, rows.Active.User, null, null));
        await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Closed);
        await Assert.That(context.ContextId).IsEqualTo(contextId);
        await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connection);
        await Assert.That(interruption.LatestBackend).IsNotEqualTo(interruption.TerminatedBackend);
    }

    [Test]
    public async Task HealthyNonemptyReaderRejectsEnabledActorRlsWithoutHidingTheActorOrRepairingPolicy()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(rows.Tenant));
        context.BindTenantLease();
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();
        int backend = ((NpgsqlConnection)connection).ProcessID;
        var contextId = context.ContextId;
        var reader = new ActorOwnershipReader(context, fixture.NativeQueueEnrollment);
        var expected = new ActorOwnershipFacts(rows.Active.Actor, rows.Active.User, null, null);
        await Assert.That(await reader.ReadForUserAsync(rows.Active.User, rows.Tenant)).IsEqualTo(expected);

        await using var migration = OpenMigrator();
        await migration.Database.OpenConnectionAsync();
        string table = new HumanPrincipalSourceSql(migration).Table(typeof(Actor));
        async Task<bool> RlsEnabledAsync() => await migration.Database.SqlQuery<bool>(
            $"SELECT relrowsecurity AS \"Value\" FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass({table})").SingleAsync();
        await Assert.That(await RlsEnabledAsync()).IsFalse();
        try
        {
            await using (var change = new NpgsqlCommand($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY",
                (NpgsqlConnection)migration.Database.GetDbConnection()))
                await change.ExecuteNonQueryAsync();
            await Assert.That(await RlsEnabledAsync()).IsTrue();
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                reader.ReadForUserAsync(rows.Active.User, rows.Tenant));
            await Assert.That(denied?.Message).IsEqualTo("actor_ownership_read_permissions_drift");
            await Assert.That(await RlsEnabledAsync()).IsTrue();
            await Assert.That(context.ContextId).IsEqualTo(contextId);
            await Assert.That(ReferenceEquals(context.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Open);
            await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
        }
        finally
        {
            await using var restore = new NpgsqlCommand($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY",
                (NpgsqlConnection)migration.Database.GetDbConnection());
            await restore.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await Assert.That(await reader.ReadForUserAsync(rows.Active.User, rows.Tenant)).IsEqualTo(expected);
        await Assert.That(await RlsEnabledAsync()).IsFalse();
    }

    [Test]
    [Arguments("foreign")]
    [Arguments("tenantless")]
    [Arguments("bypass")]
    public async Task ReaderCannotSubstituteTheOriginalTenant(string attack)
    {
        Guid selected = Guid.CreateVersion7(), requested = Guid.CreateVersion7();
        await using var context = attack == "tenantless"
            ? fixture.CreateEnrolledTenantlessDbContext()
            : fixture.CreateEnrolledTenantDbContext(new SelectedTenant(selected));
        context.BindTenantLease();
        if (attack == "bypass") context.EnableTenantFilterBypass("Actor ownership rejection invariant.");
        var reader = new ActorOwnershipReader(context, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            reader.ReadForUserAsync(Guid.CreateVersion7(), attack == "foreign" ? requested : selected));
    }

    [Test]
    [Arguments("description")]
    [Arguments("moderation")]
    [Arguments("profile")]
    public async Task RuntimeOwnershipReadDoesNotExposePrivateActorData(string field)
    {
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(Guid.CreateVersion7()));
        context.BindTenantLease();
        var actors = context.Actors.IgnoreAutoIncludes().AsNoTracking().Where(actor => actor.Id == Guid.Empty);
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            _ = field switch
            {
                "description" => await actors.Select(actor => actor.Description).ToListAsync(),
                "moderation" => await actors.Select(actor => actor.ModerationReasonCode).ToListAsync(),
                "profile" => await actors.Select(actor => (string?)actor.Pii.DisplayName).ToListAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
        });
        await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task MissingEnrollmentCannotLookLikeAnAbsentActor()
    {
        Guid tenant = Guid.CreateVersion7();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        context.BindTenantLease();
        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ActorOwnershipReader(context, null).ReadForUserAsync(Guid.CreateVersion7(), tenant));
        await Assert.That(denied?.Message).IsEqualTo("actor_ownership_enrollment_required");
    }

    [Test]
    public async Task MigratorCannotReplaceTheConfiguredRuntimeReader()
    {
        Guid tenant = Guid.CreateVersion7();
        await using var context = OpenMigrator(new SelectedTenant(tenant));
        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ActorOwnershipReader(context, fixture.NativeQueueEnrollment).ReadForUserAsync(Guid.CreateVersion7(), tenant));
        await Assert.That(denied?.Message).IsEqualTo("actor_ownership_read_wrong_identity");
    }

    [Test]
    public async Task ReadPreservesCallerTransactionAndOpenConnection()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(rows.Tenant));
        context.BindTenantLease();
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();
        int backend = ((NpgsqlConnection)connection).ProcessID;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var facts = await new ActorOwnershipReader(context, fixture.NativeQueueEnrollment)
                .ReadForUserAsync(rows.Active.User, rows.Tenant);
            await Assert.That(facts).IsEqualTo(new ActorOwnershipFacts(rows.Active.Actor, rows.Active.User, null, null));
            await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(ReferenceEquals(context.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Open);
            await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
            await transaction.RollbackAsync();
        });
        await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Open);
    }

    private async Task<Rows> SeedAsync()
    {
        await using var strategy = OpenMigrator();
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var humanGate = new HumanSecuritySourceGate(context);
            await using var human = await humanGate.AcquireAsync();
            await using var discoveryGate = new EventDiscoveryEnrollmentGate(context);
            await using var discovery = await discoveryGate.AcquireAsync();
            Guid tenant = Guid.CreateVersion7(), foreignTenant = Guid.CreateVersion7();
            context.Tenants.AddRange(Tenant(tenant), Tenant(foreignTenant));
            Person active = AddPerson(context, tenant);
            Person suspended = AddPerson(context, tenant, TenantUserStatusEnum.Suspended);
            Person deletedMember = AddPerson(context, tenant, deletedMember: true);
            Person deletedActor = AddPerson(context, tenant, deletedActor: true);
            Person foreign = AddPerson(context, foreignTenant);
            await context.SaveChangesAsync();
            return new Rows(tenant, active, suspended, deletedMember, deletedActor, foreign);
        });
    }

    private ExploreDbContext OpenMigrator(ITenantContext? tenant = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options) { TenantContext = tenant };
        context.BindTenantLease();
        return context;
    }

    private static Tenant Tenant(Guid id) => new()
    {
        Id = id,
        FullName = "Actor ownership fixture",
        Slug = $"actor-owner-{id:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static Person AddPerson(ExploreDbContext context, Guid tenant,
        TenantUserStatusEnum status = TenantUserStatusEnum.Active, bool deletedMember = false, bool deletedActor = false)
    {
        Guid user = Guid.CreateVersion7(), actor = Guid.CreateVersion7();
        context.Users.Add(new User { Id = user, Pii = null!, ConcurrencyStamp = Guid.CreateVersion7() });
        context.Actors.Add(new Actor
        {
            Id = actor,
            UserId = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = null!,
            IsDeleted = deletedActor,
            Description = "Private profile not needed for ownership",
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            Tenant = null!,
            UserId = user,
            User = null!,
            ActorId = actor,
            StatusId = (int)status,
            IsDeleted = deletedMember,
            JoinedAt = new DateTime(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc)
        });
        return new Person(user, actor);
    }

    private sealed record Person(Guid User, Guid Actor);
    private sealed record Rows(Guid Tenant, Person Active, Person Suspended, Person DeletedMember, Person DeletedActor, Person Foreign);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    private sealed class TerminateOriginalQueryBackend(string connectionString) : DbCommandInterceptor
    {
        internal bool Terminated { get; private set; }
        internal int TerminatedBackend { get; private set; }
        internal int LatestBackend { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            LatestBackend = ((NpgsqlConnection)command.Connection!).ProcessID;
            if (!Terminated)
            {
                TerminatedBackend = LatestBackend;
                await using var administrator = new NpgsqlConnection(connectionString);
                await administrator.OpenAsync(cancellationToken);
                await using var terminate = new NpgsqlCommand("SELECT pg_catalog.pg_terminate_backend(@backend)", administrator);
                terminate.Parameters.AddWithValue("backend", TerminatedBackend);
                Terminated = (bool)(await terminate.ExecuteScalarAsync(cancellationToken))!;
            }
            return result;
        }
    }
}
