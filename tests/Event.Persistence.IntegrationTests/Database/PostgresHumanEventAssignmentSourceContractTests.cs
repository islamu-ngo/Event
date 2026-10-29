using System.Data;
using System.Data.Common;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresHumanEventAssignmentSourceContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, false)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, true)]
    [Arguments(PrimaryDatabaseRole.Global, 2, false)]
    [Arguments(PrimaryDatabaseRole.Global, 2, true)]
    [Arguments(PrimaryDatabaseRole.Global, 3, false)]
    [Arguments(PrimaryDatabaseRole.Global, 3, true)]
    public async Task EnrolledCallsReturnBoundedActorFactsAndCompleteTeamCatalogs(
        PrimaryDatabaseRole role, int purpose, bool acquire)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await database.InstallAsync();
        await using var caller = await database.OpenAsync(role);
        await database.SetTenantAsync(caller, role == PrimaryDatabaseRole.Runtime ? database.Tenant : null);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var facts = await database.ReadAsync(caller, database.User, purpose, database.Tenant, [database.Event], acquire);

        await Assert.That(facts.EnumerateObject().Count()).IsEqualTo(13);
        var parent = facts.GetProperty("q0").EnumerateArray().Single();
        await Assert.That(parent.EnumerateObject().Count()).IsEqualTo(8);
        await Assert.That(parent.GetProperty("Id").GetGuid()).IsEqualTo(database.Event);
        await Assert.That(parent.GetProperty("TenantId").GetGuid()).IsEqualTo(database.Tenant);
        var assignment = facts.GetProperty("q1").EnumerateArray().Single();
        await Assert.That(assignment.EnumerateObject().Count()).IsEqualTo(12);
        await Assert.That(assignment.GetProperty("UserId").GetGuid()).IsEqualTo(database.User);
        await Assert.That(assignment.GetProperty("EventId").GetGuid()).IsEqualTo(database.Event);
        await Assert.That(assignment.GetProperty("Version").GetInt64()).IsEqualTo(7);
        await Assert.That(assignment.GetProperty("CreatedBy").GetGuid()).IsEqualTo(database.User);
        await Assert.That(facts.GetProperty("q2").GetArrayLength()).IsEqualTo(2);
        await Assert.That(facts.GetProperty("q3").GetArrayLength()).IsEqualTo(2);
        await Assert.That(facts.GetProperty("q4").EnumerateArray().Single().GetProperty("Id").GetGuid())
            .IsEqualTo(database.Actor);
        await Assert.That(facts.GetProperty("q5").EnumerateArray().Single().GetProperty("Id").GetGuid())
            .IsEqualTo(database.Membership);
        foreach (string key in new[] { "q6", "q7" })
        {
            var member = facts.GetProperty(key).EnumerateArray().Single();
            await Assert.That(member.GetProperty("IsDeleted").GetBoolean()).IsFalse();
            await Assert.That(member.GetProperty("PlacementIsDeleted").GetBoolean()).IsFalse();
            await Assert.That(member.GetProperty("IsOrganizerEligible").GetBoolean()).IsTrue();
        }
        await Assert.That(facts.GetProperty("q8").EnumerateArray().Single().GetProperty("MasterCode").GetString())
            .IsEqualTo("organizer.created");
        // Team catalogs are intentionally complete; only the team assignment rows are event/tenant bounded.
        await Assert.That(facts.GetProperty("event_team_roles").GetArrayLength()).IsEqualTo(3);
        await Assert.That(facts.GetProperty("event_team_permissions").GetArrayLength()).IsEqualTo(2);
        await Assert.That(facts.GetProperty("event_team_role_permissions").GetArrayLength()).IsEqualTo(2);
        var team = facts.GetProperty("event_team_assignments").EnumerateArray().ToArray();
        await Assert.That(team.Length).IsEqualTo(2);
        foreach (var row in team)
        {
            await Assert.That(row.GetProperty("EventId").GetGuid()).IsEqualTo(database.Event);
            await Assert.That(row.GetProperty("TenantId").GetGuid()).IsEqualTo(database.Tenant);
        }
        await Assert.That(team.Any(row => row.GetProperty("UserId").GetGuid() == database.OtherUser)).IsTrue();
        await transaction.RollbackAsync();
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            AssignmentDatabase.ExecuteAsync(caller, $"SELECT * FROM {database.Sql.Table(typeof(Explore.Domain.Event))}"));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 2, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Global, 1, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Global, 4, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, 1, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, IsolationLevel.ReadCommitted)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, IsolationLevel.RepeatableRead)]
    public async Task WrongLoginPurposeOrAcquisitionIsolationIsRefused(
        PrimaryDatabaseRole role, int purpose, IsolationLevel isolation)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(role);
        await database.SetTenantAsync(caller, role == PrimaryDatabaseRole.Global ? null : database.Tenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(isolation);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ReadAsync(caller, database.User, purpose, database.Tenant, [database.Event], acquire: true));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("session")]
    [Arguments("missing-session")]
    [Arguments("global-session")]
    [Arguments("actor")]
    [Arguments("tenant")]
    [Arguments("null-events")]
    [Arguments("null-element")]
    [Arguments("empty-element")]
    [Arguments("dimensions")]
    [Arguments("oversized")]
    public async Task SubstitutedContextAndMalformedEventArraysFailClosed(string scenario)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        bool global = scenario == "global-session";
        await using var caller = await database.OpenAsync(global ? PrimaryDatabaseRole.Global : PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, scenario == "missing-session" ? null
            : scenario == "session" ? database.OtherTenant : database.Tenant);
        string events = scenario switch
        {
            "null-events" => "NULL::uuid[]",
            "null-element" => "ARRAY[NULL]::uuid[]",
            "empty-element" => "ARRAY['00000000-0000-0000-0000-000000000000']::uuid[]",
            "dimensions" => "ARRAY[[@event]]::uuid[]",
            "oversized" => $"pg_catalog.array_fill(@event,ARRAY[{HumanEventAssignmentSourceFacts.MaximumEvents + 1}])",
            _ => "ARRAY[@event]::uuid[]"
        };
        await using var command = AssignmentDatabase.Command(caller,
            $"SELECT {PostgresHumanEventAssignmentSourceContract.ObserveFunctionSql(database.Migration)}(@actor,@purpose,@tenant,{events})");
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid)
        { Value = scenario == "actor" ? Guid.Empty : database.User });
        command.Parameters.Add(new NpgsqlParameter("purpose", global ? 2 : 1));
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
        { Value = scenario == "tenant" ? DBNull.Value : database.Tenant });
        command.Parameters.Add(new NpgsqlParameter("event", database.Event));

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync());

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await Assert.That(error.MessageText).IsEqualTo("human_assignment_caller_mismatch");
    }

    [Test]
    public async Task EmptySelectionIsLegalWithoutExpandingEventOrAssignmentRanges()
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.Tenant);

        var facts = await database.ReadAsync(caller, database.User, 1, database.Tenant, []);

        foreach (string key in new[] { "q0", "q1", "q4", "q8", "event_team_assignments" })
            await Assert.That(facts.GetProperty(key).GetArrayLength()).IsEqualTo(0);
        await Assert.That(facts.GetProperty("event_team_roles").GetArrayLength()).IsEqualTo(3);
    }

    [Test]
    public async Task AssignmentInsertedAfterSerializableSnapshotConflictsEvenWhenRangeWasAbsent()
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        await database.SeedAsync(assignments: false);
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.Tenant);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var before = await database.ReadAsync(caller, database.User, 1, database.Tenant, [database.Event]);
            await Assert.That(before.GetProperty("q1").GetArrayLength()).IsEqualTo(0);
            await database.InsertAssignmentAsync(database.Migration, database.Tenant, database.Event, database.User);

            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                database.ReadAsync(caller, database.User, 1, database.Tenant, [database.Event], acquire: true));

            await Assert.That(error!.SqlState).IsEqualTo("40001");
            await transaction.RollbackAsync();
        }
        await using var fresh = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var current = await database.ReadAsync(caller, database.User, 1, database.Tenant, [database.Event], acquire: true);
        await Assert.That(current.GetProperty("q1").GetArrayLength()).IsEqualTo(1);
        await fresh.RollbackAsync();
    }

    [Test]
    [Arguments("assignment")]
    [Arguments("event")]
    [Arguments("pii")]
    public async Task ZeroRowContributorWritesHoldEpochAndRollbackReleasesIt(string contributor)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        Type type = contributor switch
        {
            "assignment" => typeof(EventRoleAssignment),
            "event" => typeof(Explore.Domain.Event),
            "pii" => typeof(UserPii),
            _ => throw new ArgumentOutOfRangeException(nameof(contributor))
        };
        await using var contender = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await using (var writer = await database.Migration.Database.BeginTransactionAsync())
        {
            await AssignmentDatabase.ExecuteAsync(database.Migration, $"DELETE FROM {database.Sql.Table(type)} WHERE false");
            await database.AssertEpochBlockedAsync(contender);
            await writer.RollbackAsync();
        }
        await AssignmentDatabase.ExecuteAsync(contender, $"SELECT id FROM {database.Epoch} WHERE id = 1 FOR UPDATE NOWAIT");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcquiredAssignmentOrAiTargetHoldsSharedEpochAndActualRowsUntilRollback(bool ai)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, ai ? database.OtherTenant : database.Tenant);
        await using var contender = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            if (ai)
                await database.ReadTargetAsync(caller, database.User, database.OtherTenant, database.Event, acquire: true);
            else
                await database.ReadAsync(caller, database.User, 1, database.Tenant, [database.Event], acquire: true);
            await database.AssertEpochBlockedAsync(contender);
            foreach (var row in new[] { (typeof(Explore.Domain.Event), database.Event), (typeof(Actor), database.Actor) })
            {
                await using var attempt = await contender.Database.BeginTransactionAsync();
                await using var command = AssignmentDatabase.Command(contender,
                    $"SELECT {database.Sql.Column(row.Item1, "Id")} FROM {database.Sql.Table(row.Item1)} WHERE {database.Sql.Column(row.Item1, "Id")} = @id FOR UPDATE NOWAIT");
                command.Parameters.Add(new NpgsqlParameter("id", row.Item2));
                var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
                await Assert.That(error!.SqlState).IsEqualTo("55P03");
                await attempt.RollbackAsync();
            }
            await transaction.RollbackAsync();
        }
        await AssignmentDatabase.ExecuteAsync(contender, $"SELECT id FROM {database.Epoch} WHERE id = 1 FOR UPDATE NOWAIT");
    }

    [Test]
    [Arguments("trigger")]
    [Arguments("public")]
    [Arguments("column")]
    [Arguments("anchor")]
    [Arguments("routine")]
    [Arguments("assignment-restamp")]
    [Arguments("ai-restamp")]
    public async Task AdmissionAndActualInstallerRejectDriftWithoutRepair(string drift)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        bool ai = drift == "ai-restamp";
        string function = ai ? PostgresHumanAiModerationProposalTargetContract.AcquireFunctionSql(database.Migration)
            : PostgresHumanEventAssignmentSourceContract.AcquireFunctionSql(database.Migration);
        string signature = ai ? "(uuid,uuid,uuid)" : "(uuid,integer,uuid,uuid[])";
        string mutation = drift switch
        {
            "trigger" => $"ALTER TABLE {database.Sql.Table(typeof(EventRoleAssignment))} DISABLE TRIGGER zz_human_event_assignment_source_write",
            "public" => $"GRANT EXECUTE ON FUNCTION {function}{signature} TO PUBLIC",
            "column" => $"ALTER TABLE {database.Sql.Table(typeof(Actor))} ALTER COLUMN {database.Sql.Column(typeof(Actor), "IsDeleted")} DROP NOT NULL",
            "anchor" => $"ALTER TABLE {database.Epoch} ALTER COLUMN revision DROP NOT NULL",
            "routine" => $"ALTER FUNCTION {function}{signature} STABLE",
            "assignment-restamp" or "ai-restamp" => $"""
                CREATE OR REPLACE FUNCTION {function}({(ai ? "p_actor uuid,p_selected uuid,p_event uuid" : "p_actor uuid,p_purpose integer,p_tenant uuid,p_events uuid[]")})
                    RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body$BEGIN RETURN pg_catalog.jsonb_build_object(); END;$body$;
                DO $stamp$ BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L',
                        {AssignmentDatabase.Literal(function + signature)},
                        '{(ai ? "human-ai-moderation-proposal-target" : "human-event-assignment-source")}:v1:' ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex'))
                        FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure({AssignmentDatabase.Literal(function + signature)}));
                END $stamp$;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await AssignmentDatabase.ExecuteAsync(database.Migration, mutation);
        string before = await database.CatalogAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        string admission = ai
            ? PostgresHumanAiModerationProposalTargetContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment)
            : PostgresHumanEventAssignmentSourceContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment);

        var rejected = await Assert.ThrowsAsync<PostgresException>(() => AssignmentDatabase.ExecuteAsync(caller, admission));
        await Assert.That(rejected!.SqlState).IsEqualTo("55000");
        var reinstall = await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);
        await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    [Test]
    public async Task MandatoryAiInstallationFailureRollsBackAssignmentInstallationInOriginalTransaction()
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture, install: false);
        await AssignmentDatabase.ExecuteAsync(database.Migration,
            $"ALTER TABLE {database.Sql.Table(typeof(PlatformUserRole))} ALTER COLUMN {database.Sql.Column(typeof(PlatformUserRole), "UserId")} DROP NOT NULL");
        string before = await database.CatalogAsync();

        var error = await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);

        await Assert.That(error!.SqlState).IsEqualTo("55000");
        await Assert.That(error.MessageText).IsEqualTo("ai_moderation_target_source_drift");
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AiTargetReturnsOnlyRequestedCrossTenantEventForPersistedAdministrator(bool acquire)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.OtherTenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var target = await database.ReadTargetAsync(caller, database.User, database.OtherTenant, database.Event, acquire);

        await Assert.That(target!.Value.EnumerateObject().Count()).IsEqualTo(10);
        await Assert.That(target.Value.GetProperty("tenant").GetGuid()).IsEqualTo(database.Tenant);
        await Assert.That(target.Value.GetProperty("q0").EnumerateArray().Single().GetProperty("Id").GetGuid())
            .IsEqualTo(database.Event);
        await Assert.That(target.Value.GetProperty("q1").EnumerateArray().Single().GetProperty("UserId").GetGuid())
            .IsEqualTo(database.User);
        await Assert.That(await AssignmentDatabase.ScalarAsync<string>(caller,
            "SELECT current_setting('app.current_tenant_id')")).IsEqualTo(database.OtherTenant.ToString("D"));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("ordinary")]
    [Arguments("revoked")]
    [Arguments("deleted-user")]
    [Arguments("wrong-scope")]
    public async Task AiTargetRequiresCurrentPersistedPlatformAdministrator(string scenario)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        if (scenario == "revoked")
            await AssignmentDatabase.ExecuteAsync(database.Migration, $"DELETE FROM {database.Sql.Table(typeof(PlatformUserRole))}");
        else if (scenario == "deleted-user")
            await AssignmentDatabase.ExecuteAsync(database.Migration,
                $"UPDATE {database.Sql.Table(typeof(User))} SET {database.Sql.Column(typeof(User), "IsDeleted")} = true");
        else if (scenario == "wrong-scope")
            await AssignmentDatabase.ExecuteAsync(database.Migration,
                $"UPDATE {database.Sql.Table(typeof(Role))} SET {database.Sql.Column(typeof(Role), "RoleScopeId")} = {(int)RoleScopeEnum.Platform + 1}");
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.OtherTenant);

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ReadTargetAsync(caller,
            scenario == "ordinary" ? database.OtherUser : database.User, database.OtherTenant, database.Event));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await Assert.That(error.MessageText).IsEqualTo("ai_moderation_target_original_admin_required");
        // This direct call tests current authority only. Platform-role continuity belongs to the original principal lease.
    }

    [Test]
    [Arguments("selected")]
    [Arguments("deleted")]
    [Arguments("missing")]
    public async Task AiTargetDoesNotDiscoverSelectedTenantDeletedOrMissingEvent(string scenario)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        if (scenario == "deleted")
            await AssignmentDatabase.ExecuteAsync(database.Migration,
                $"UPDATE {database.Sql.Table(typeof(Explore.Domain.Event))} SET {database.Sql.Column(typeof(Explore.Domain.Event), "IsDeleted")} = true");
        Guid selected = scenario == "selected" ? database.Tenant : database.OtherTenant;
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, selected);

        await Assert.That(await database.ReadTargetAsync(caller, database.User, selected,
            scenario == "missing" ? Guid.CreateVersion7() : database.Event)).IsNull();
    }

    [Test]
    [Arguments("global")]
    [Arguments("session")]
    [Arguments("actor")]
    [Arguments("event")]
    [Arguments("isolation")]
    public async Task AiTargetRefusesInvalidCallerContextAndAcquisitionIsolation(string scenario)
    {
        await using var database = await AssignmentDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(scenario == "global"
            ? PrimaryDatabaseRole.Global : PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, scenario == "session" ? database.Tenant : database.OtherTenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ReadTargetAsync(caller,
            scenario == "actor" ? Guid.Empty : database.User, database.OtherTenant,
            scenario == "event" ? Guid.Empty : database.Event, acquire: scenario == "isolation"));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
    }

    private sealed class AssignmentDatabase(
        PostgreSqlContainerFixture fixture, PostgreSqlContainer container, ExploreDbContext migration) : IAsyncDisposable
    {
        private static readonly (Type Type, string[] Columns)[] Sources =
        [
            .. HumanEventAssignmentSourceSql.Sources,
            (typeof(PlatformUserRole), ["Id", "UserId", "RoleId"])
        ];

        internal ExploreDbContext Migration { get; } = migration;
        internal HumanPrincipalSourceSql Sql { get; } = new(migration);
        internal Guid Tenant { get; } = Guid.CreateVersion7();
        internal Guid OtherTenant { get; } = Guid.CreateVersion7();
        internal Guid User { get; } = Guid.CreateVersion7();
        internal Guid OtherUser { get; } = Guid.CreateVersion7();
        internal Guid Actor { get; } = Guid.CreateVersion7();
        internal Guid Event { get; } = Guid.CreateVersion7();
        internal Guid OtherEvent { get; } = Guid.CreateVersion7();
        internal Guid ForeignEvent { get; } = Guid.CreateVersion7();
        internal Guid Membership { get; } = Guid.CreateVersion7();
        internal string Epoch => Sql.Name("human_event_assignment_source_epoch");

        internal static async Task<AssignmentDatabase> CreateAsync(PostgreSqlContainerFixture fixture, bool install = true)
        {
            var original = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
            var container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("assignment_source_" + Guid.CreateVersion7().ToString("N"))
                .WithUsername(original.Username!).WithPassword(original.Password!).Build();
            try
            {
                await container.StartAsync();
                var migration = CreateContext(fixture, container, PrimaryDatabaseRole.Migrator);
                try
                {
                    var database = new AssignmentDatabase(fixture, container, migration);
                    await migration.Database.OpenConnectionAsync();
                    await database.CreateSourcesAsync();
                    if (install) await database.InstallAsync();
                    return database;
                }
                catch
                {
                    await migration.DisposeAsync();
                    throw;
                }
            }
            catch
            {
                await container.DisposeAsync();
                throw;
            }
        }

        private IEnumerable<IProperty> Properties(IEntityType entity) => entity.GetProperties().Where(property =>
            Sources.Single(source => source.Type == entity.ClrType).Columns.Contains(property.Name)
            || entity.GetKeys().Any(key => key.Properties.Contains(property))
            || entity.GetForeignKeys().Any(key => Sources.Any(source => source.Type == key.PrincipalEntityType.ClrType)
                && key.Properties.Contains(property)));

        private string Columns(Type type, IReadOnlyList<IProperty> properties) =>
            string.Join(",", properties.Select(property => Sql.Column(type, property.Name)));

        private async Task CreateSourcesAsync()
        {
            foreach (var role in new[]
            {
                PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global, PrimaryDatabaseRole.EmailPolicyInstanceMutation
            })
            {
                var original = fixture.GetNativeQueueOptions(role);
                await ExecuteAsync(Migration, $"""
                    CREATE ROLE {Sql.Quote(original.Username!)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                        NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD {Literal(original.Password!)};
                    """);
            }
            await ExecuteAsync(Migration, $"CREATE SCHEMA {Sql.Quote(Sql.Schema)}");
            foreach (var source in Sources)
            {
                var entity = Migration.Model.FindEntityType(source.Type)!;
                var declared = Migration.GetService<IDesignTimeModel>().Model.FindEntityType(source.Type)!;
                string columns = string.Join(",", Properties(entity).Select(property =>
                {
                    string identity = NpgsqlPropertyExtensions.GetValueGenerationStrategy(declared.FindProperty(property.Name)!) switch
                    {
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn => " GENERATED BY DEFAULT AS IDENTITY",
                        NpgsqlValueGenerationStrategy.IdentityAlwaysColumn => " GENERATED ALWAYS AS IDENTITY",
                        _ => ""
                    };
                    return $"{Sql.Column(source.Type, property.Name)} {property.GetRelationalTypeMapping().StoreType}"
                        + identity + (property.IsNullable ? "" : " NOT NULL");
                }));
                await ExecuteAsync(Migration, $"CREATE TABLE {Sql.Table(source.Type)} ({columns})");
                foreach (var key in entity.GetKeys())
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Sql.Table(source.Type)} ADD {(key.IsPrimaryKey() ? "PRIMARY KEY" : "UNIQUE")}
                            ({Columns(source.Type, key.Properties)})
                        """);
            }
            foreach (var source in Sources)
            {
                foreach (var key in Migration.Model.FindEntityType(source.Type)!.GetForeignKeys()
                    .Where(key => Sources.Any(parent => parent.Type == key.PrincipalEntityType.ClrType)))
                {
                    string delete = key.DeleteBehavior switch
                    {
                        DeleteBehavior.Cascade => "CASCADE",
                        DeleteBehavior.SetNull => "SET NULL",
                        DeleteBehavior.Restrict => "RESTRICT",
                        _ => "NO ACTION"
                    };
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Sql.Table(source.Type)} ADD FOREIGN KEY ({Columns(source.Type, key.Properties)})
                            REFERENCES {Sql.Table(key.PrincipalEntityType.ClrType)}
                            ({Columns(key.PrincipalEntityType.ClrType, key.PrincipalKey.Properties)}) ON DELETE {delete}
                        """);
                }
            }
            foreach (var table in PostgresTenantRowLevelSecurityModel.GetTenantTables(Migration.Model)
                .Where(table => Sources.Any(source => Migration.Model.FindEntityType(source.Type)!.GetTableName() == table.TableName)))
                await ExecuteAsync(Migration, PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(table, Sql.Schema));
            await ExecuteAsync(Migration, $"""
                GRANT USAGE ON SCHEMA {Sql.Quote(Sql.Schema)} TO {Sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)},
                    {Sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)},
                    {Sql.Quote(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyInstanceMutation).Username!)};
                """);
            // Provisioning has no footprint here. The real assignment guard still executes its exact optional leaf.
        }

        internal Task InstallAsync() =>
            PostgresHumanEventAssignmentSourceContract.ApplyAsync(Migration, fixture.NativeQueueEnrollment);

        internal async Task<ExploreDbContext> OpenAsync(PrimaryDatabaseRole role)
        {
            var context = CreateContext(fixture, container, role);
            try
            {
                await context.Database.OpenConnectionAsync();
                await Assert.That(await ScalarAsync<string>(context, "SELECT session_user::text"))
                    .IsEqualTo(fixture.GetNativeQueueOptions(role).Username!);
                await SetTenantAsync(context, null);
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        internal async Task SetTenantAsync(ExploreDbContext context, Guid? tenant)
        {
            await using var command = Command(context, "SELECT pg_catalog.set_config('app.current_tenant_id',@tenant,false)");
            command.Parameters.Add(new NpgsqlParameter("tenant", tenant?.ToString("D") ?? ""));
            await command.ExecuteScalarAsync();
        }

        internal async Task<JsonElement> ReadAsync(ExploreDbContext context, Guid actor, int purpose,
            Guid tenant, Guid[] events, bool acquire = false)
        {
            string routine = acquire ? PostgresHumanEventAssignmentSourceContract.AcquireFunctionSql(Migration)
                : PostgresHumanEventAssignmentSourceContract.ObserveFunctionSql(Migration);
            await using var command = Command(context, $"SELECT {routine}(@actor,@purpose,@tenant,@events)");
            command.Parameters.Add(new NpgsqlParameter("actor", actor));
            command.Parameters.Add(new NpgsqlParameter("purpose", purpose));
            command.Parameters.Add(new NpgsqlParameter("tenant", tenant));
            command.Parameters.Add(new NpgsqlParameter("events", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = events });
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            return document.RootElement.Clone();
        }

        internal async Task<JsonElement?> ReadTargetAsync(ExploreDbContext context, Guid actor,
            Guid selected, Guid target, bool acquire = false)
        {
            string routine = acquire ? PostgresHumanAiModerationProposalTargetContract.AcquireFunctionSql(Migration)
                : PostgresHumanAiModerationProposalTargetContract.ObserveFunctionSql(Migration);
            await using var command = Command(context, $"SELECT {routine}(@actor,@selected,@event)");
            command.Parameters.Add(new NpgsqlParameter("actor", actor));
            command.Parameters.Add(new NpgsqlParameter("selected", selected));
            command.Parameters.Add(new NpgsqlParameter("event", target));
            object? result = await command.ExecuteScalarAsync();
            if (result is null or DBNull) return null;
            using var document = JsonDocument.Parse((string)result);
            return document.RootElement.Clone();
        }

        internal async Task AssertEpochBlockedAsync(ExploreDbContext contender)
        {
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteAsync(contender, $"SELECT id FROM {Epoch} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(error!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
        }

        internal async Task SeedAsync(bool assignments = true)
        {
            foreach (Guid user in new[] { User, OtherUser })
            {
                await InsertAsync(Migration, typeof(User), ("Id", user), ("IsDeleted", false));
                await InsertAsync(Migration, typeof(UserPii), ("UserId", user));
            }
            await InsertAsync(Migration, typeof(Role), ("Id", 1), ("MasterCode", "event.manager"), ("RoleScopeId", 1));
            await InsertAsync(Migration, typeof(Role), ("Id", 2), ("MasterCode", "organization.manager"), ("RoleScopeId", 1));
            await InsertAsync(Migration, typeof(Role), ("Id", 3), ("MasterCode", "platform.admin"),
                ("RoleScopeId", (int)RoleScopeEnum.Platform));
            for (int permission = 1; permission <= 2; permission++)
            {
                await InsertAsync(Migration, typeof(Permission), ("Id", permission), ("MasterCode", "permission." + permission),
                    ("IsActive", true), ("IsFiltered", false));
                await InsertAsync(Migration, typeof(RolePermission), ("RoleId", permission), ("PermissionId", permission));
            }
            await InsertAsync(Migration, typeof(PlatformUserRole), ("UserId", User), ("RoleId", 3));
            await InsertAsync(Migration, typeof(EventProvenanceType), ("Id", 1), ("MasterCode", "organizer.created"));
            await InsertAsync(Migration, typeof(Actor), ("Id", Actor), ("UserId", User),
                ("IsDeleted", false), ("IsSuspended", false));
            foreach (var row in new[] { (Event, Tenant), (OtherEvent, Tenant), (ForeignEvent, OtherTenant) })
                await InsertAsync(Migration, typeof(Explore.Domain.Event), ("Id", row.Item1), ("TenantId", row.Item2),
                    ("ActorId", Actor), ("OrganizerActorId", Actor), ("EventProvenanceTypeId", 1),
                    ("SubmittedByUserId", User), ("IsDeleted", false));
            if (assignments)
            {
                await InsertAssignmentAsync(Migration, Tenant, Event, User);
                await InsertAssignmentAsync(Migration, Tenant, Event, OtherUser, role: 2);
            }
            await InsertAssignmentAsync(Migration, Tenant, OtherEvent, User, role: 2);
            await InsertAssignmentAsync(Migration, OtherTenant, ForeignEvent, User);
            foreach (var row in new[] { (Tenant, User), (Tenant, OtherUser), (OtherTenant, User) })
            {
                await InsertAsync(Migration, typeof(TenantUser), ("Id", row == (Tenant, User) ? Membership : Guid.CreateVersion7()),
                    ("TenantId", row.Item1), ("UserId", row.Item2), ("ActorId", Actor), ("StatusId", 1), ("IsDeleted", false));
                foreach (var pair in new[]
                {
                    (Placement: typeof(OrganizationTenant), Member: typeof(OrganizationMember), Key: "OrganizationTenantId"),
                    (Placement: typeof(GroupTenant), Member: typeof(GroupMember), Key: "GroupTenantId")
                })
                {
                    Guid placement = Guid.CreateVersion7();
                    await InsertAsync(Migration, pair.Placement, ("Id", placement), ("TenantId", row.Item1),
                        ("IsDeleted", false), ("IsSuspended", false), ("IsOrganizerEligible", true), ("ApprovalStatusId", 1));
                    await InsertAsync(Migration, pair.Member, ("TenantId", row.Item1), ("UserId", row.Item2),
                        (pair.Key, placement), ("RoleId", 2), ("IsDeleted", false));
                }
            }
        }

        internal Task InsertAssignmentAsync(ExploreDbContext context, Guid tenant, Guid parent, Guid user, int role = 1) =>
            InsertAsync(context, typeof(EventRoleAssignment), ("TenantId", tenant), ("EventId", parent), ("UserId", user),
                ("RoleId", role), ("Status", 1), ("StartsAtUtc", DateTime.UnixEpoch), ("Version", 7L), ("CreatedBy", user));

        private async Task InsertAsync(ExploreDbContext context, Type type, params (string Name, object Value)[] values)
        {
            var supplied = values.ToDictionary(value => value.Name, value => value.Value);
            var properties = Properties(Migration.Model.FindEntityType(type)!)
                .Where(property => !property.IsNullable || supplied.ContainsKey(property.Name)).ToArray();
            await using var command = Command(context, $"""
                INSERT INTO {Sql.Table(type)} ({string.Join(",", properties.Select(property => Sql.Column(type, property.Name)))})
                OVERRIDING SYSTEM VALUE VALUES ({string.Join(",", properties.Select((_, index) => "@p" + index))})
                """);
            for (int index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                if (!supplied.TryGetValue(property.Name, out object? value))
                {
                    Type storage = property.GetRelationalTypeMapping().Converter?.ProviderClrType ?? property.ClrType;
                    storage = Nullable.GetUnderlyingType(storage) ?? storage;
                    value = storage == typeof(Guid) ? Guid.CreateVersion7()
                        : storage == typeof(string) ? Guid.CreateVersion7().ToString("N")
                        : storage == typeof(bool) ? false
                        : storage == typeof(DateTime) ? DateTime.UnixEpoch
                        : storage == typeof(long) ? 1L : 1;
                }
                command.Parameters.Add(new NpgsqlParameter("p" + index, value));
            }
            await command.ExecuteNonQueryAsync();
        }

        internal Task<string> CatalogAsync() => ScalarAsync<string>(Migration, $"""
            SELECT jsonb_build_object(
                'relations', (SELECT jsonb_agg(to_jsonb(c) ORDER BY c.oid) FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'columns', (SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attrelid,a.attnum) FROM pg_attribute a
                    JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'functions', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_proc'))
                    ORDER BY p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'policies', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_policy'))
                    ORDER BY p.oid) FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'triggers', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.oid) FROM pg_trigger t
                    JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'roles', (SELECT jsonb_agg(to_jsonb(r) || jsonb_build_object('stamp',shobj_description(r.oid,'pg_authid'))
                    ORDER BY r.oid) FROM pg_roles r),
                'memberships', (SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member) FROM pg_auth_members m)
            )::text
            """);

        internal static DbCommand Command(ExploreDbContext context, string sql)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = 15;
            command.CommandText = sql;
            return command;
        }

        internal static async Task ExecuteAsync(ExploreDbContext context, string sql)
        {
            await using var command = Command(context, sql);
            await command.ExecuteNonQueryAsync();
        }

        internal static async Task<T> ScalarAsync<T>(ExploreDbContext context, string sql)
        {
            await using var command = Command(context, sql);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        private static ExploreDbContext CreateContext(
            PostgreSqlContainerFixture fixture, PostgreSqlContainer container, PrimaryDatabaseRole role)
        {
            var endpoint = new NpgsqlConnectionStringBuilder(container.GetConnectionString());
            var options = fixture.GetNativeQueueOptions(role) with
            {
                Host = endpoint.Host,
                Port = endpoint.Port,
                Database = endpoint.Database
            };
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
            builder.UseNpgsql(provider => provider.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
            return new ExploreDbContext(builder.Options);
        }

        internal static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        public async ValueTask DisposeAsync()
        {
            try { await Migration.DisposeAsync(); }
            finally { await container.DisposeAsync(); }
        }
    }
}
