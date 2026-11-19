using System.Data;
using Explore.Domain;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed partial class TenantCreationPermissionsNativeTests
{
    [Test]
    [Arguments("privilege")]
    [Arguments("body")]
    [Arguments("trigger")]
    public async Task PreparedCatalogueRechecksCommittedDriftOnItsOriginalOwner(string attack)
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(Tenant));
        string trigger = names.Quote("ordinary_settings_tenant_changed");
        string routine = names.Name("ordinary_settings_tenant_changed") + "()";
        string slugName = names.ColumnName(typeof(Tenant), nameof(Tenant.Slug));
        string slug = names.Quote(slugName);
        string globalRole = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string change;
        string restore;
        if (attack == "body")
        {
            var (definition, body) = await FunctionImageAsync(migrator, routine);
            int start = definition.IndexOf(body, StringComparison.Ordinal);
            if (body.Length == 0 || start < 0 || start != definition.LastIndexOf(body, StringComparison.Ordinal))
                throw new InvalidOperationException("The original writer body must be uniquely identifiable.");
            change = definition[..start] + "BEGIN RETURN NULL; END;" + definition[(start + body.Length)..];
            restore = definition;
        }
        else if (attack == "trigger")
        {
            await Assert.That(await TriggerStateAsync(migrator, table, "ordinary_settings_tenant_changed")).IsEqualTo("A");
            change = $"ALTER TABLE {table} DISABLE TRIGGER {trigger}";
            restore = $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER {trigger}";
        }
        else if (attack == "privilege")
        {
            change = $"GRANT UPDATE ({slug}) ON {table} TO {globalRole}";
            restore = $"REVOKE UPDATE ({slug}) ON {table} FROM {globalRole}";
        }
        else
            throw new ArgumentOutOfRangeException(nameof(attack));

        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var lease = global.ContextId;
        global.TenantCreationSources = PostgresTenantCreationPermissions.CompileSourceProgram(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        var assertCurrent = PostgresTenantCreationPermissions.PrepareCurrentAssertion(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await assertCurrent(CancellationToken.None);
        await Assert.ThrowsAsync<OperationCanceledException>(() => assertCurrent(new CancellationToken(canceled: true)));
        try
        {
            await ExecuteAsync(migrator, change);
            if (attack == "body")
                await Assert.That((await FunctionImageAsync(migrator, routine)).Body).IsEqualTo("BEGIN RETURN NULL; END;");
            else if (attack == "trigger")
                await Assert.That(await TriggerStateAsync(migrator, table, "ordinary_settings_tenant_changed")).IsEqualTo("D");
            else
                await Assert.That(await global.Database.SqlQueryRaw<bool>(
                    "SELECT pg_catalog.has_column_privilege({0},{1},{2},'UPDATE') AS \"Value\"",
                    fixture.NativeQueueEnrollment.GlobalLogin, table, slugName).SingleAsync()).IsTrue();

            await using var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var physical = transaction.GetDbTransaction();
            const string savepoint = "prepared_creator_drift";
            await transaction.CreateSavepointAsync(savepoint);
            try
            {
                if (attack == "privilege")
                {
                    var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => assertCurrent(CancellationToken.None))
                        ?? throw new InvalidOperationException("The catalogue refusal must be captured.");
                    await Assert.That(denied.Message).IsEqualTo("tenant_creation_permissions_not_ready");
                }
                else
                {
                    var denied = await Assert.ThrowsAsync<PostgresException>(() => assertCurrent(CancellationToken.None))
                        ?? throw new InvalidOperationException("The shared-writer refusal must be captured.");
                    await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
                    await Assert.That(denied.MessageText).IsEqualTo(attack == "body"
                        ? "human_provisioning_shared_body_drift" : "human_provisioning_shared_writer_drift");
                }
                await Assert.That(global.ContextId).IsEqualTo(lease);
                await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
                await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(connector.ProcessID).IsEqualTo(backend);
                await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
            }
            finally
            {
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
                await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
                await Assert.That(physical.Connection).IsSameReferenceAs(connector);
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }
        finally { await ExecuteAsync(migrator, restore); }

        await assertCurrent(CancellationToken.None);
        await RequireGlobalIdentityAsync(global);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    [Test]
    public async Task PreparedSourceProgramCannotCrossDeclaredEnrollment()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        var otherEnrollment = fixture.NativeQueueEnrollment with { GlobalLogin = "creator_source_other_global" };
        global.TenantCreationSources = PostgresTenantCreationPermissions.CompileSourceProgram(
            global, otherEnrollment, IdentityDatabaseTopology.Colocated);
        var wrongOwner = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresTenantCreationPermissions.RequireCurrentAsync(
                global, otherEnrollment, IdentityDatabaseTopology.Colocated))
            ?? throw new InvalidOperationException("The declared-owner refusal must be captured.");
        await Assert.That(wrongOwner.Message).IsEqualTo("tenant_creation_permissions_not_ready");
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }
}
