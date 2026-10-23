using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresHumanWebhookResourceSourceContractTests(PostgreSqlContainerFixture fixture)
{
    private const string Signature = "(uuid,integer,uuid,integer,uuid,integer,uuid,uuid,uuid)";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FixedOperationsReturnOnlyTheirPersistedLineage(bool acquire)
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        string before = await database.SourceRowsAsync();
        await database.InstallAsync();
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.Tenant);
        await WebhookDatabase.ExecuteAsync(caller,
            PostgresHumanWebhookResourceSourceContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment));
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        foreach (int operation in new[] { 2, 3, 13, 16, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15 })
        {
            Guid id = operation switch
            {
                2 or 3 or 13 or 16 => database.Selected.Consumer,
                >= 4 and <= 9 => database.Selected.Endpoint,
                10 => database.Selected.Attempt,
                11 => database.Selected.OperationKey,
                12 => database.Selected.Replay,
                _ => database.Selected.Publication
            };
            var facts = await database.ReadAsync(caller, database.Actor, 1, database.Tenant,
                operation, id, acquire: acquire);
            await AssertRowsAsync(facts, 0, database.Selected.Consumer);
            await AssertRowsAsync(facts, 1, operation is >= 4 and <= 12 ? [database.Selected.Endpoint] : []);
            await AssertRowsAsync(facts, 2, operation == 10 ? [database.Selected.Attempt] : []);
            await AssertRowsAsync(facts, 3, operation is 10 or 14 or 15 ? [database.Selected.Message] : []);
            await AssertRowsAsync(facts, 4, operation == 10 ? [database.Selected.LocalTarget] : []);
            await AssertRowsAsync(facts, 5, operation is 10 or 14 or 15 ? [database.Selected.Plan] : []);
            await AssertRowsAsync(facts, 6, operation is 14 or 15 ? [database.Selected.Publication] : []);
            await AssertRowsAsync(facts, 7, operation is 11 or 12 ? [database.Selected.Replay] : []);
            await AssertRowsAsync(facts, 8, database.Selected.Binding);
            for (int index = 9; index < 15; index++) await AssertRowsAsync(facts, index);
            await Assert.That(facts.EnumerateObject().Count()).IsEqualTo(15);
            await Assert.That(facts.GetProperty("q0")[0].GetProperty("ConfigurationVersion").GetInt32()).IsEqualTo(7);
            if (operation == 10)
            {
                var message = facts.GetProperty("q3")[0];
                await Assert.That(message.GetProperty("HasPayload").GetBoolean()).IsTrue();
                await Assert.That(message.TryGetProperty("_payloadBytes", out _)).IsFalse();
                await Assert.That(facts.GetProperty("q4")[0].GetProperty("DeliveryFence").GetInt64()).IsEqualTo(17);
            }
        }
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task OriginalRuntimeAndGlobalPurposesKeepTenantBoundsAndInstanceLineage()
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        foreach (var selection in new[]
        {
            (PrimaryDatabaseRole.Runtime, 1, (Guid?)database.Tenant),
            (PrimaryDatabaseRole.Global, 2, (Guid?)null),
            (PrimaryDatabaseRole.Global, 3, (Guid?)null)
        })
        {
            await using var caller = await database.OpenAsync(selection.Item1);
            await database.SetTenantAsync(caller, selection.Item3);
            foreach (bool acquire in new[] { false, true })
            {
                await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var foreign = await database.ReadAsync(caller, database.Actor, selection.Item2,
                    selection.Item3, 3, database.Foreign.Consumer, acquire: acquire);
                await AssertRowsAsync(foreign, 0,
                    selection.Item1 == PrimaryDatabaseRole.Global ? [database.Foreign.Consumer] : []);
                var instance = await database.ReadAsync(caller, database.Actor, selection.Item2,
                    selection.Item3, 4, database.InstanceEndpoint, acquire: acquire);
                await AssertRowsAsync(instance, 0, database.InstanceConsumer);
                await AssertRowsAsync(instance, 1, database.InstanceEndpoint);
                await Assert.That(instance.GetProperty("q0")[0].GetProperty("TenantId").ValueKind)
                    .IsEqualTo(JsonValueKind.Null);
                await Assert.That(await WebhookDatabase.ScalarAsync<string>(caller,
                    "SELECT current_setting('app.current_tenant_id')"))
                    .IsEqualTo(selection.Item3?.ToString("D") ?? "");
                await transaction.RollbackAsync();
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreationFactsAreBoundedByKindOwnerActorAndTenant(bool acquire)
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.Tenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        foreach (var selection in new[]
        {
            (Kind: 1, Owner: (Guid?)null, Index: -1, Id: Guid.Empty),
            (Kind: 2, Owner: (Guid?)database.Organization, Index: 10, Id: database.Organization),
            (Kind: 3, Owner: (Guid?)database.Group, Index: 12, Id: database.Group),
            (Kind: 4, Owner: (Guid?)database.OtherActor, Index: 14, Id: database.Membership),
            (Kind: 5, Owner: (Guid?)null, Index: 9, Id: database.Instance)
        })
        {
            var facts = await database.ReadAsync(caller, database.Actor, 1, database.Tenant, 1,
                null, selection.Kind, selection.Owner, acquire: acquire);
            for (int index = 0; index < 15; index++)
            {
                Guid[] expected = index == selection.Index ? [selection.Id]
                    : index == 11 && selection.Kind == 2 ? [database.OrganizationPlacement]
                    : index == 13 && selection.Kind == 3 ? [database.GroupPlacement] : [];
                await AssertRowsAsync(facts, index, expected);
            }
        }
        var otherActor = await database.ReadAsync(caller, database.OtherActor, 1, database.Tenant,
            1, null, 4, database.Actor, acquire: acquire);
        await AssertRowsAsync(otherActor, 14, database.OtherMembership);
        var missingOwner = await database.ReadAsync(caller, database.Actor, 1, database.Tenant,
            1, null, 2, Guid.CreateVersion7(), acquire: acquire);
        await AssertRowsAsync(missingOwner, 10);
        await AssertRowsAsync(missingOwner, 11);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task UnknownBoundedTargetsYieldExplicitEmptyFactsRatherThanCallerErrors()
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.Tenant);
        foreach (int operation in Enumerable.Range(2, 15))
        {
            var facts = await database.ReadAsync(caller, database.Actor, 1, database.Tenant,
                operation, Guid.CreateVersion7());
            await Assert.That(facts.EnumerateObject().Count()).IsEqualTo(15);
            for (int index = 0; index < 15; index++) await AssertRowsAsync(facts, index);
        }
        var filtered = await database.ReadAsync(caller, database.Actor, 1, database.Tenant, 11,
            Guid.CreateVersion7(), consumer: database.Selected.Consumer, endpoint: database.Selected.Endpoint);
        await AssertRowsAsync(filtered, 0, database.Selected.Consumer);
        await AssertRowsAsync(filtered, 1, database.Selected.Endpoint);
        await AssertRowsAsync(filtered, 7);
        var foreignFilter = await database.ReadAsync(caller, database.Actor, 1, database.Tenant, 11,
            Guid.CreateVersion7(), consumer: database.Foreign.Consumer, endpoint: database.Foreign.Endpoint);
        for (int index = 0; index < 15; index++) await AssertRowsAsync(foreignFilter, index);
    }

    [Test]
    public async Task WrongOriginalIdentityPurposeTenantAndIsolationAreRejected()
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        foreach (var scenario in new[]
        {
            (PrimaryDatabaseRole.Runtime, 2, (Guid?)database.Tenant, (Guid?)database.Tenant, IsolationLevel.Serializable, false),
            (PrimaryDatabaseRole.Runtime, 3, (Guid?)database.Tenant, (Guid?)database.Tenant, IsolationLevel.Serializable, true),
            (PrimaryDatabaseRole.Global, 1, (Guid?)null, (Guid?)null, IsolationLevel.Serializable, false),
            (PrimaryDatabaseRole.Global, 4, (Guid?)null, (Guid?)null, IsolationLevel.Serializable, true),
            (PrimaryDatabaseRole.Global, 2, (Guid?)database.Tenant, (Guid?)database.Tenant, IsolationLevel.Serializable, false),
            (PrimaryDatabaseRole.Runtime, 1, (Guid?)null, (Guid?)null, IsolationLevel.Serializable, false),
            (PrimaryDatabaseRole.Runtime, 1, (Guid?)Guid.Empty, (Guid?)Guid.Empty, IsolationLevel.Serializable, false),
            (PrimaryDatabaseRole.Runtime, 1, (Guid?)database.Tenant, (Guid?)database.OtherTenant, IsolationLevel.Serializable, false),
            (PrimaryDatabaseRole.Runtime, 1, (Guid?)database.Tenant, (Guid?)null, IsolationLevel.Serializable, true),
            (PrimaryDatabaseRole.Runtime, 1, (Guid?)database.Tenant, (Guid?)database.Tenant, IsolationLevel.ReadCommitted, true),
            (PrimaryDatabaseRole.Runtime, 1, (Guid?)database.Tenant, (Guid?)database.Tenant, IsolationLevel.RepeatableRead, true),
            (PrimaryDatabaseRole.Global, 2, (Guid?)null, (Guid?)null, IsolationLevel.ReadCommitted, true),
            (PrimaryDatabaseRole.EmailPolicyInstanceMutation, 1, (Guid?)database.Tenant, (Guid?)database.Tenant, IsolationLevel.Serializable, false)
        })
        {
            await using var caller = await database.OpenAsync(scenario.Item1);
            await database.SetTenantAsync(caller, scenario.Item4);
            await using var transaction = await caller.Database.BeginTransactionAsync(scenario.Item5);
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.ReadAsync(caller,
                database.Actor, scenario.Item2, scenario.Item3, 3, database.Selected.Consumer, acquire: scenario.Item6));
            await Assert.That(error!.SqlState).IsEqualTo("42501");
            if (scenario.Item1 != PrimaryDatabaseRole.EmailPolicyInstanceMutation)
                await Assert.That(error.MessageText).IsEqualTo("human_webhook_caller_mismatch");
            await transaction.RollbackAsync();
        }
    }

    [Test]
    public async Task MalformedSelectorsAreCallerErrorsNotEmptyObservations()
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.Tenant);
        foreach (var target in new[]
        {
            (Actor: (Guid?)null, Operation: (int?)3, Id: (Guid?)database.Selected.Consumer, Kind: (int?)null, Owner: (Guid?)null, Consumer: (Guid?)null, Endpoint: (Guid?)null),
            (Guid.Empty, (int?)3, (Guid?)database.Selected.Consumer, (int?)null, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)null, (Guid?)database.Selected.Consumer, (int?)null, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)0, (Guid?)database.Selected.Consumer, (int?)null, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)17, (Guid?)database.Selected.Consumer, (int?)null, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)3, (Guid?)null, (int?)null, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)3, (Guid?)Guid.Empty, (int?)null, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)1, (Guid?)database.Selected.Consumer, (int?)1, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)1, (Guid?)null, (int?)null, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)1, (Guid?)null, (int?)99, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)1, (Guid?)null, (int?)2, (Guid?)Guid.Empty, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)3, (Guid?)database.Selected.Consumer, (int?)1, (Guid?)null, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)3, (Guid?)database.Selected.Consumer, (int?)null, (Guid?)database.Organization, (Guid?)null, (Guid?)null),
            (database.Actor, (int?)3, (Guid?)database.Selected.Consumer, (int?)null, (Guid?)null, (Guid?)database.Selected.Consumer, (Guid?)null),
            (database.Actor, (int?)11, (Guid?)database.Selected.OperationKey, (int?)null, (Guid?)null, (Guid?)Guid.Empty, (Guid?)null),
            (database.Actor, (int?)11, (Guid?)database.Selected.OperationKey, (int?)null, (Guid?)null, (Guid?)null, (Guid?)Guid.Empty)
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.ReadAsync(caller,
                target.Actor, 1, database.Tenant, target.Operation, target.Id, target.Kind, target.Owner,
                target.Consumer, target.Endpoint));
            await Assert.That(error!.SqlState).IsEqualTo("42501");
            await Assert.That(error.MessageText).IsEqualTo("human_webhook_caller_mismatch");
        }
        await using var global = await database.OpenAsync(PrimaryDatabaseRole.Global);
        foreach (int operation in new[] { 11, 12, 14, 15 })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.ReadAsync(global,
                database.Actor, 2, null, operation, Guid.CreateVersion7()));
            await Assert.That(error!.SqlState).IsEqualTo("42501");
            await Assert.That(error!.MessageText).IsEqualTo("human_webhook_caller_mismatch");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcquisitionExcludesUnselectedContributorInsertOrUpdateUntilRollback(bool insert)
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await database.SetTenantAsync(caller, database.Tenant);
        await Assert.That(await WebhookDatabase.ScalarAsync<int>(caller, "SELECT pg_backend_pid()")
            == await WebhookDatabase.ScalarAsync<int>(writer, "SELECT pg_backend_pid()")).IsFalse();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await database.ReadAsync(caller, database.Actor, 1, database.Tenant, 3, database.Selected.Consumer, acquire: true);
        await WebhookDatabase.ExecuteAsync(writer, "SET lock_timeout = '250ms'");
        string before = await database.SourceRowsAsync();
        Guid newMembership = Guid.CreateVersion7();
        Task WriteAsync() => insert
            ? database.InsertAsync(writer, typeof(TenantUser), ("Id", newMembership),
                ("TenantId", database.OtherTenant), ("UserId", Guid.CreateVersion7()))
            : WebhookDatabase.ExecuteAsync(writer, $"""
                UPDATE {database.Sql.Table(typeof(WebhookConsumer))}
                SET {database.Sql.Column(typeof(WebhookConsumer), "ConfigurationVersion")} = 8
                WHERE {database.Sql.Column(typeof(WebhookConsumer), "Id")} = '{database.Foreign.Consumer:D}'
                """);

        // Acquisition has completed; neither write targets a selected row.
        var error = await Assert.ThrowsAsync<PostgresException>(WriteAsync);
        await Assert.That(error!.SqlState).IsEqualTo("55P03");
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
        await transaction.RollbackAsync();
        await WriteAsync();
        await Assert.That(await database.SourceRowsAsync() == before).IsFalse();
    }

    [Test]
    public async Task EveryOriginalContributorWriterOwnsTheSameEpochEvenForEmptyRanges()
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await using var contender = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        foreach (var source in HumanWebhookResourceSourceSql.Sources)
        {
            await using var owner = await writer.Database.BeginTransactionAsync();
            await WebhookDatabase.ExecuteAsync(writer, $"""
                UPDATE {database.Sql.Table(source.Type)}
                SET {database.Sql.Column(source.Type, "Id")} = {database.Sql.Column(source.Type, "Id")} WHERE false
                """);
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => WebhookDatabase.ExecuteAsync(contender,
                $"SELECT id FROM {database.Sql.Name("human_webhook_resource_source_epoch")} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(error!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
            await owner.RollbackAsync();
        }
    }

    [Test]
    public async Task RuntimeHasExecuteOnlyCapabilityWithoutOriginalSourceOrEpochAccess()
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        string before = await database.SourceRowsAsync();
        foreach (var source in HumanWebhookResourceSourceSql.Sources)
        {
            string table = database.Sql.Table(source.Type);
            string id = database.Sql.Column(source.Type, "Id");
            foreach (string query in new[] { $"SELECT {id} FROM {table}", $"UPDATE {table} SET {id} = {id}", $"DELETE FROM {table}" })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => WebhookDatabase.ExecuteAsync(runtime, query));
                await Assert.That(error!.SqlState).IsEqualTo("42501");
            }
        }
        var epoch = await Assert.ThrowsAsync<PostgresException>(() => WebhookDatabase.ExecuteAsync(runtime,
            $"UPDATE {database.Sql.Name("human_webhook_resource_source_epoch")} SET revision = NOT revision"));
        await Assert.That(epoch!.SqlState).IsEqualTo("42501");
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments("source", "human_webhook_source_drift")]
    [Arguments("private-column", "human_webhook_source_drift")]
    [Arguments("anchor", "human_webhook_authority_drift")]
    [Arguments("trigger", "human_webhook_writer_drift")]
    [Arguments("acl", "native_privilege_guard_not_ready")]
    [Arguments("role", "native_privilege_guard_not_ready")]
    [Arguments("routine", "human_webhook_routine_drift")]
    [Arguments("body", "human_webhook_routine_drift")]
    [Arguments("self-restamp", "human_webhook_body_drift")]
    [Arguments("rls-read", "human_webhook_rls_drift")]
    [Arguments("rls-update", "human_webhook_rls_drift")]
    [Arguments("rls-extra", "human_webhook_rls_drift")]
    [Arguments("tenant-rls", "human_webhook_tenant_policy_drift")]
    public async Task AdmissionAndReinstallationRejectExactAuthorityDriftWithoutRepair(string drift, string message)
    {
        await using var database = await WebhookDatabase.CreateAsync(fixture);
        string observe = PostgresHumanWebhookResourceSourceContract.ObserveFunctionSql(database.Migration);
        string table = database.Sql.Table(typeof(WebhookEndpoint));
        const string counterfeit = "BEGIN RETURN '{}'::jsonb; END;";
        string ddl = drift switch
        {
            "source" => $"ALTER TABLE {table} ALTER COLUMN {database.Sql.Column(typeof(WebhookEndpoint), "ConfigurationVersion")} DROP NOT NULL",
            "private-column" => $"ALTER TABLE {database.Sql.Table(typeof(WebhookMessage))} ALTER COLUMN {database.Sql.Column(typeof(WebhookMessage), "_payloadBytes")} TYPE text USING NULL::text",
            "anchor" => $"ALTER TABLE {database.Sql.Name("human_webhook_resource_source_epoch")} ALTER COLUMN revision DROP NOT NULL",
            "trigger" => $"ALTER TABLE {table} DISABLE TRIGGER zz_human_webhook_resource_source_write",
            "acl" => $"GRANT EXECUTE ON FUNCTION {observe}{Signature} TO PUBLIC",
            "role" => $"ALTER ROLE {PostgresHumanWebhookResourceSourceContract.RuntimeRole} BYPASSRLS",
            "routine" => $"ALTER FUNCTION {observe}{Signature} STABLE",
            "body" or "self-restamp" => $"""
                CREATE OR REPLACE FUNCTION {observe}
                    (p_actor uuid,p_purpose integer,p_tenant uuid,p_operation integer,p_id uuid,p_kind integer,p_owner uuid,p_consumer uuid,p_endpoint uuid)
                    RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${counterfeit}$body$;
                """ + (drift == "self-restamp" ? $"""
                COMMENT ON FUNCTION {observe}{Signature} IS
                    'human-webhook-resource-source:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(counterfeit)))}';
                """ : ""),
            "rls-read" => $"ALTER POLICY human_webhook_resource_source_read ON {table} USING (false)",
            "rls-update" => $"ALTER POLICY human_webhook_resource_source_update ON {table} WITH CHECK (false)",
            "rls-extra" => $"CREATE POLICY counterfeit ON {table} FOR SELECT TO {PostgresHumanWebhookResourceSourceContract.OwnerRole} USING (true)",
            "tenant-rls" => $"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await WebhookDatabase.ExecuteAsync(database.Migration, ddl);
        string before = await database.CatalogAsync();
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        var admission = await Assert.ThrowsAsync<PostgresException>(() => WebhookDatabase.ExecuteAsync(runtime,
            PostgresHumanWebhookResourceSourceContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment)));
        await Assert.That(admission!.SqlState).IsEqualTo("55000");
        await Assert.That(admission.MessageText).IsEqualTo(message);
        var reinstall = await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);
        await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
        await Assert.That(reinstall.MessageText).IsEqualTo(message);
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    private static async Task AssertRowsAsync(JsonElement facts, int index, params Guid[] ids)
    {
        var rows = facts.GetProperty("q" + index);
        await Assert.That(rows.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(rows.GetArrayLength()).IsEqualTo(ids.Length);
        foreach (Guid id in ids)
            await Assert.That(rows.EnumerateArray().Count(row => row.GetProperty("Id").GetGuid() == id)).IsEqualTo(1);
    }

    private sealed record Lineage(Guid Tenant)
    {
        internal Guid Consumer { get; } = Guid.CreateVersion7();
        internal Guid Endpoint { get; } = Guid.CreateVersion7();
        internal Guid Attempt { get; } = Guid.CreateVersion7();
        internal Guid Message { get; } = Guid.CreateVersion7();
        internal Guid LocalTarget { get; } = Guid.CreateVersion7();
        internal Guid Plan { get; } = Guid.CreateVersion7();
        internal Guid Publication { get; } = Guid.CreateVersion7();
        internal Guid Replay { get; } = Guid.CreateVersion7();
        internal Guid OperationKey { get; } = Guid.CreateVersion7();
        internal Guid Binding { get; } = Guid.CreateVersion7();
    }

    private sealed class WebhookDatabase(
        PostgreSqlContainerFixture fixture, PostgreSqlContainer container, ExploreDbContext migration) : IAsyncDisposable
    {
        internal ExploreDbContext Migration { get; } = migration;
        internal HumanPrincipalSourceSql Sql { get; } = new(migration);
        internal Guid Tenant { get; } = Guid.CreateVersion7();
        internal Guid OtherTenant { get; } = Guid.CreateVersion7();
        internal Guid Actor { get; } = Guid.CreateVersion7();
        internal Guid OtherActor { get; } = Guid.CreateVersion7();
        internal Guid Organization { get; } = Guid.CreateVersion7();
        internal Guid OrganizationPlacement { get; } = Guid.CreateVersion7();
        internal Guid Group { get; } = Guid.CreateVersion7();
        internal Guid GroupPlacement { get; } = Guid.CreateVersion7();
        internal Guid Membership { get; } = Guid.CreateVersion7();
        internal Guid OtherMembership { get; } = Guid.CreateVersion7();
        internal Guid Instance { get; } = Guid.CreateVersion7();
        internal Guid InstanceConsumer { get; } = Guid.CreateVersion7();
        internal Guid InstanceEndpoint { get; } = Guid.CreateVersion7();
        internal Lineage Selected { get; private set; } = null!;
        internal Lineage Foreign { get; private set; } = null!;

        internal static async Task<WebhookDatabase> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var original = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
            // Contract roles have cluster-wide custody; never alter the fixture cluster.
            var container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("webhook_source_" + Guid.CreateVersion7().ToString("N"))
                .WithUsername(original.Username!).WithPassword(original.Password!).Build();
            try
            {
                await container.StartAsync();
                var migration = CreateContext(fixture, container, PrimaryDatabaseRole.Migrator);
                try
                {
                    var database = new WebhookDatabase(fixture, container, migration);
                    database.Selected = new Lineage(database.Tenant);
                    database.Foreign = new Lineage(database.OtherTenant);
                    await migration.Database.OpenConnectionAsync();
                    await database.CreateSourcesAsync();
                    await database.InstallAsync();
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
            HumanWebhookResourceSourceSql.Sources.Single(source => source.Type == entity.ClrType).Columns.Contains(property.Name)
            || entity.GetKeys().Any(key => key.Properties.Contains(property))
            || entity.GetForeignKeys().Any(key => HumanWebhookResourceSourceSql.Sources.Any(
                source => source.Type == key.PrincipalEntityType.ClrType) && key.Properties.Contains(property)));

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
            foreach (var source in HumanWebhookResourceSourceSql.Sources)
            {
                var entity = Migration.Model.FindEntityType(source.Type)!;
                string columns = string.Join(",", Properties(entity).Select(property =>
                    $"{Sql.Column(source.Type, property.Name)} {property.GetRelationalTypeMapping().StoreType}"
                    + (property.IsNullable ? "" : " NOT NULL")));
                await ExecuteAsync(Migration, $"CREATE TABLE {Sql.Table(source.Type)} ({columns})");
                foreach (var key in entity.GetKeys())
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Sql.Table(source.Type)} ADD {(key.IsPrimaryKey() ? "PRIMARY KEY" : "UNIQUE")}
                            ({Columns(source.Type, key.Properties)})
                        """);
            }
            foreach (var source in HumanWebhookResourceSourceSql.Sources)
            {
                foreach (var key in Migration.Model.FindEntityType(source.Type)!.GetForeignKeys()
                    .Where(key => HumanWebhookResourceSourceSql.Sources.Any(parent => parent.Type == key.PrincipalEntityType.ClrType)))
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Sql.Table(source.Type)} ADD FOREIGN KEY ({Columns(source.Type, key.Properties)})
                            REFERENCES {Sql.Table(key.PrincipalEntityType.ClrType)}
                            ({Columns(key.PrincipalEntityType.ClrType, key.PrincipalKey.Properties)})
                        """);
            }
            foreach (var table in PostgresTenantRowLevelSecurityModel.GetTenantTables(Migration.Model)
                .Where(table => HumanWebhookResourceSourceSql.Sources.Any(source =>
                    Migration.Model.FindEntityType(source.Type)!.GetTableName() == table.TableName)))
                await ExecuteAsync(Migration, PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(table, Sql.Schema));
            await ExecuteAsync(Migration, $"""
                GRANT USAGE ON SCHEMA {Sql.Quote(Sql.Schema)} TO {Sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)},
                    {Sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)},
                    {Sql.Quote(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyInstanceMutation).Username!)};
                """);
        }

        internal Task InstallAsync() =>
            PostgresHumanWebhookResourceSourceContract.ApplyAsync(Migration, fixture.NativeQueueEnrollment);

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
            await using var command = Command(context, "SELECT set_config('app.current_tenant_id',@tenant,false)");
            command.Parameters.Add(new NpgsqlParameter("tenant", tenant?.ToString("D") ?? ""));
            await command.ExecuteScalarAsync();
        }

        internal async Task<JsonElement> ReadAsync(ExploreDbContext context, Guid? actor, int purpose,
            Guid? tenant, int? operation, Guid? id, int? kind = null, Guid? owner = null,
            Guid? consumer = null, Guid? endpoint = null, bool acquire = false)
        {
            string routine = acquire ? PostgresHumanWebhookResourceSourceContract.AcquireFunctionSql(Migration)
                : PostgresHumanWebhookResourceSourceContract.ObserveFunctionSql(Migration);
            await using var command = Command(context,
                $"SELECT {routine}(@actor,@purpose,@tenant,@operation,@id,@kind,@owner,@consumer,@endpoint)");
            foreach (var parameter in new (string Name, NpgsqlDbType Type, object? Value)[]
            {
                ("actor", NpgsqlDbType.Uuid, actor), ("purpose", NpgsqlDbType.Integer, purpose),
                ("tenant", NpgsqlDbType.Uuid, tenant), ("operation", NpgsqlDbType.Integer, operation),
                ("id", NpgsqlDbType.Uuid, id), ("kind", NpgsqlDbType.Integer, kind),
                ("owner", NpgsqlDbType.Uuid, owner), ("consumer", NpgsqlDbType.Uuid, consumer),
                ("endpoint", NpgsqlDbType.Uuid, endpoint)
            })
                command.Parameters.Add(new NpgsqlParameter(parameter.Name, parameter.Type) { Value = parameter.Value ?? DBNull.Value });
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            return document.RootElement.Clone();
        }

        internal async Task SeedAsync()
        {
            await InsertAsync(Migration, typeof(InstanceBootstrapState), ("Id", Instance));
            await InsertAsync(Migration, typeof(Organization), ("Id", Organization));
            await InsertAsync(Migration, typeof(Group), ("Id", Group));
            await InsertAsync(Migration, typeof(OrganizationTenant), ("Id", OrganizationPlacement),
                ("TenantId", Tenant), ("OrganizationId", Organization));
            await InsertAsync(Migration, typeof(OrganizationTenant), ("Id", Guid.CreateVersion7()),
                ("TenantId", OtherTenant), ("OrganizationId", Organization));
            await InsertAsync(Migration, typeof(GroupTenant), ("Id", GroupPlacement), ("TenantId", Tenant), ("GroupId", Group));
            await InsertAsync(Migration, typeof(GroupTenant), ("Id", Guid.CreateVersion7()), ("TenantId", OtherTenant), ("GroupId", Group));
            await InsertAsync(Migration, typeof(TenantUser), ("Id", Membership), ("TenantId", Tenant), ("UserId", Actor));
            await InsertAsync(Migration, typeof(TenantUser), ("Id", OtherMembership), ("TenantId", Tenant), ("UserId", OtherActor));
            await InsertAsync(Migration, typeof(TenantUser), ("Id", Guid.CreateVersion7()), ("TenantId", OtherTenant), ("UserId", Actor));
            await SeedLineageAsync(Selected);
            await SeedLineageAsync(new Lineage(Tenant));
            await SeedLineageAsync(Foreign);
            await InsertAsync(Migration, typeof(WebhookConsumer), ("Id", InstanceConsumer), ("InstanceId", Instance),
                ("ConfigurationScopeId", Instance), ("ConsumerKindId", 5));
            await InsertAsync(Migration, typeof(WebhookEndpoint), ("Id", InstanceEndpoint), ("InstanceId", Instance),
                ("ConfigurationScopeId", Instance), ("ConsumerId", InstanceConsumer));
        }

        private async Task SeedLineageAsync(Lineage row)
        {
            await InsertAsync(Migration, typeof(WebhookConsumer), ("Id", row.Consumer), ("TenantId", row.Tenant),
                ("ConfigurationScopeId", row.Tenant), ("ConsumerKindId", 1), ("ConfigurationVersion", 7));
            await InsertAsync(Migration, typeof(WebhookEndpoint), ("Id", row.Endpoint), ("TenantId", row.Tenant),
                ("ConfigurationScopeId", row.Tenant), ("ConsumerId", row.Consumer));
            await InsertAsync(Migration, typeof(WebhookConsumerProviderBinding), ("Id", row.Binding), ("TenantId", row.Tenant),
                ("InstanceId", Instance), ("ConfigurationScopeId", row.Tenant), ("WebhookConsumerId", row.Consumer));
            await InsertAsync(Migration, typeof(WebhookMessage), ("Id", row.Message), ("TenantId", row.Tenant),
                ("ConsumerId", row.Consumer), ("_payloadBytes", new byte[] { 1, 2, 3 }));
            await InsertAsync(Migration, typeof(WebhookDeliveryPlanSnapshot), ("Id", row.Plan), ("TenantId", row.Tenant),
                ("WebhookMessageId", row.Message), ("WebhookConsumerId", row.Consumer));
            await InsertAsync(Migration, typeof(WebhookLocalTargetSnapshot), ("Id", row.LocalTarget), ("TenantId", row.Tenant),
                ("WebhookMessageId", row.Message), ("DeliveryPlanSnapshotId", row.Plan), ("WebhookEndpointId", row.Endpoint),
                ("DeliveryFence", 17L));
            await InsertAsync(Migration, typeof(WebhookDeliveryAttempt), ("Id", row.Attempt), ("TenantId", row.Tenant),
                ("MessageId", row.Message), ("EndpointId", row.Endpoint));
            await InsertAsync(Migration, typeof(WebhookProviderPublication), ("Id", row.Publication), ("TenantId", row.Tenant),
                ("WebhookMessageId", row.Message), ("WebhookDeliveryPlanSnapshotId", row.Plan), ("ProviderBindingId", row.Binding));
            await InsertAsync(Migration, typeof(WebhookBulkReplayOperation), ("Id", row.Replay), ("TenantId", row.Tenant),
                ("OperationKey", row.OperationKey), ("WebhookConsumerId", row.Consumer), ("WebhookEndpointId", row.Endpoint));
        }

        internal async Task InsertAsync(ExploreDbContext context, Type type, params (string Name, object Value)[] supplied)
        {
            var values = supplied.ToDictionary(value => value.Name, value => value.Value);
            // Neutral values populate required storage columns only; expected facts above
            // are independent IDs and explicit domain versions, not generated expectations.
            foreach (var property in Properties(Migration.Model.FindEntityType(type)!).Where(property => !property.IsNullable))
            {
                if (values.ContainsKey(property.Name)) continue;
                string storeType = property.GetRelationalTypeMapping().StoreType;
                values[property.Name] = storeType switch
                {
                    "uuid" => Guid.CreateVersion7(),
                    "integer" => 1,
                    "bigint" => 1L,
                    "boolean" => false,
                    "timestamp with time zone" => DateTime.UnixEpoch,
                    "timestamp without time zone" => DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Unspecified),
                    "text" => "source",
                    _ when storeType.StartsWith("character varying", StringComparison.Ordinal) => "source",
                    _ => throw new InvalidOperationException($"Declare a seed value for {type.Name}.{property.Name} ({storeType}).")
                };
            }
            await using var command = Command(context, $"""
                INSERT INTO {Sql.Table(type)} ({string.Join(",", values.Keys.Select(name => Sql.Column(type, name)))})
                VALUES ({string.Join(",", values.Select((_, index) => "@p" + index))})
                """);
            int parameter = 0;
            foreach (object value in values.Values)
                command.Parameters.Add(new NpgsqlParameter("p" + parameter++, value));
            await command.ExecuteNonQueryAsync();
        }

        internal Task<string> SourceRowsAsync() => ScalarAsync<string>(Migration,
            "SELECT jsonb_build_array(" + string.Join(",", HumanWebhookResourceSourceSql.Sources.Select(source =>
                $"(SELECT jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text) FROM {Sql.Table(source.Type)} t)")) + ")::text");

        internal Task<string> CatalogAsync() => ScalarAsync<string>(Migration, $"""
            SELECT jsonb_build_object(
                'relations', (SELECT jsonb_agg(to_jsonb(c) ORDER BY c.oid) FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'attributes', (SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attrelid,a.attnum) FROM pg_attribute a
                    JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'routines', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_proc'))
                    ORDER BY p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'triggers', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.oid) FROM pg_trigger t
                    JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'policies', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_policy'))
                    ORDER BY p.oid) FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'roles', (SELECT jsonb_agg(to_jsonb(r) || jsonb_build_object('stamp',shobj_description(r.oid,'pg_authid'))
                    ORDER BY r.oid) FROM pg_roles r),
                'memberships', (SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member) FROM pg_auth_members m)
            )::text
            """);

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

        private static DbCommand Command(ExploreDbContext context, string sql)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = 15;
            command.CommandText = sql;
            return command;
        }

        private static ExploreDbContext CreateContext(
            PostgreSqlContainerFixture fixture, PostgreSqlContainer container, PrimaryDatabaseRole role)
        {
            var endpoint = new NpgsqlConnectionStringBuilder(container.GetConnectionString());
            var original = fixture.GetNativeQueueOptions(role) with
            {
                Host = endpoint.Host,
                Port = endpoint.Port,
                Database = endpoint.Database
            };
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, original);
            return new ExploreDbContext(builder.Options);
        }

        private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        public async ValueTask DisposeAsync()
        {
            try { await Migration.DisposeAsync(); }
            finally { await container.DisposeAsync(); }
        }
    }
}
