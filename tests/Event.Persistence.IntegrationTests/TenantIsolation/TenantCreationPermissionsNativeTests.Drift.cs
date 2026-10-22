using System.Data;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed partial class TenantCreationPermissionsNativeTests
{
    [Test]
    public async Task ExternalTopologyRejectsInstalledColocatedWriterMechanics()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);

        await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.External);

        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    [Test]
    public async Task ChangedRequiredWriterBodyRejectsTheOriginalGlobalOwner()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string routine = names.Name("ordinary_settings_tenant_changed") + "()";
        var (definition, body) = await FunctionImageAsync(migrator, routine);
        int bodyStart = definition.IndexOf(body, StringComparison.Ordinal);
        if (body.Length == 0 || bodyStart < 0 || bodyStart != definition.LastIndexOf(body, StringComparison.Ordinal))
            throw new InvalidOperationException("The exact native function body could not be isolated for this test.");
        string altered = definition[..bodyStart] + "BEGIN RETURN NULL; END;" + definition[(bodyStart + body.Length)..];
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);

        try
        {
            // Preserve the real function identity, owner, configuration and comment;
            // only its body stops maintaining the original tenant source witness.
            await ExecuteAsync(migrator, altered);
            await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.Colocated,
                "human_provisioning_shared_body_drift");
        }
        finally { await ExecuteAsync(migrator, definition); }

        var restored = await FunctionImageAsync(migrator, routine);
        await Assert.That(restored.Definition).IsEqualTo(definition);
        await Assert.That(restored.Body).IsEqualTo(body);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    [Test]
    public async Task DisabledRequiredWriterTriggerRejectsTheOriginalGlobalOwner()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(Tenant));
        const string trigger = "ordinary_settings_tenant_changed";
        string originalState = await TriggerStateAsync(migrator, table, trigger);
        await Assert.That(originalState).IsEqualTo("A");
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);

        try
        {
            await ExecuteAsync(migrator, $"ALTER TABLE {table} DISABLE TRIGGER {names.Quote(trigger)}");
            await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.Colocated,
                "human_provisioning_shared_writer_drift");
        }
        finally { await ExecuteAsync(migrator, $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER {names.Quote(trigger)}"); }

        await Assert.That(await TriggerStateAsync(migrator, table, trigger)).IsEqualTo(originalState);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    [Test]
    public async Task UnexpectedDocumentRewriteRuleRejectsTheOriginalGlobalOwner()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(TenantSettingsDocument));
        string rule = names.Quote("creator_rule_attack_" + Guid.CreateVersion7().ToString("N"));
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);

        try
        {
            await ExecuteAsync(migrator, $"CREATE RULE {rule} AS ON INSERT TO {table} DO INSTEAD NOTHING");
            await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.Colocated);
        }
        finally { await ExecuteAsync(migrator, $"DROP RULE IF EXISTS {rule} ON {table}"); }

        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    [Test]
    public async Task DisabledDocumentForeignKeyTriggerRejectsTheOriginalGlobalOwner()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(TenantSettingsDocument));
        string trigger;
        string originalState;
        await using (var command = Command(migrator, """
            SELECT t.tgname,t.tgenabled::text FROM pg_catalog.pg_trigger t
                JOIN pg_catalog.pg_constraint f ON f.oid=t.tgconstraint
            WHERE t.tgrelid=pg_catalog.to_regclass(@document) AND t.tgisinternal AND (t.tgtype & 4)=4
                AND f.contype='f' AND f.conrelid=t.tgrelid AND f.confrelid=pg_catalog.to_regclass(@tenant)
            """))
        {
            command.Parameters.Add(new NpgsqlParameter("document", table));
            command.Parameters.Add(new NpgsqlParameter("tenant", names.Table(typeof(Tenant))));
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            trigger = reader.GetString(0);
            originalState = reader.GetString(1);
            await Assert.That(await reader.ReadAsync()).IsFalse();
        }
        string restore = originalState switch
        {
            "O" => "ENABLE TRIGGER",
            "A" => "ENABLE ALWAYS TRIGGER",
            _ => throw new InvalidOperationException("The original document foreign key was not enabled.")
        };
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);

        try
        {
            await ExecuteAsync(migrator, $"ALTER TABLE {table} DISABLE TRIGGER {names.Quote(trigger)}");
            await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.Colocated);
        }
        finally { await ExecuteAsync(migrator, $"ALTER TABLE {table} {restore} {names.Quote(trigger)}"); }

        await Assert.That(await TriggerStateAsync(migrator, table, trigger)).IsEqualTo(originalState);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    private async Task AssertRejectedOnOriginalOwnerAsync(ExploreDbContext global,
        IdentityDatabaseTopology topology, string? nativeError = null)
    {
        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var lease = global.ContextId;
        await using var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (nativeError is null)
            {
                var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology));
                await Assert.That(denied.Message).IsEqualTo("tenant_creation_permissions_not_ready");
            }
            else
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                    PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology));
                await Assert.That(denied.SqlState).IsEqualTo("55000");
                await Assert.That(denied.MessageText).IsEqualTo(nativeError);
            }
            await Assert.That(global.ContextId).IsEqualTo(lease);
            await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await Assert.That(global.TenantFilterTenantId).IsNull();
            await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
        }
        finally { await transaction.RollbackAsync(CancellationToken.None); }
        await RequireGlobalIdentityAsync(global);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
    }

    private static async Task<(string Definition, string Body)> FunctionImageAsync(ExploreDbContext migrator, string routine)
    {
        await using var command = Command(migrator, """
            SELECT pg_catalog.pg_get_functiondef(f.oid),f.prosrc FROM pg_catalog.pg_proc f
            WHERE f.oid=pg_catalog.to_regprocedure(@routine)
            """);
        command.Parameters.Add(new NpgsqlParameter("routine", routine));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("The required native writer is missing.");
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<string> TriggerStateAsync(ExploreDbContext migrator, string table, string trigger)
    {
        await using var command = Command(migrator, """
            SELECT tgenabled::text FROM pg_catalog.pg_trigger
            WHERE tgrelid=pg_catalog.to_regclass(@table) AND tgname=@trigger
            """);
        command.Parameters.Add(new NpgsqlParameter("table", table));
        command.Parameters.Add(new NpgsqlParameter("trigger", trigger));
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The required trigger is missing."));
    }
}
