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
    public Task ExecutableCheckOnDisposableControlLeavesCreatorReady() =>
        VerifyExecutableCheckAsync(attachToCreator: false, installation: false);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task UnexpectedExecutableDocumentCheckIsRejectedWithoutRepair(bool installation) =>
        VerifyExecutableCheckAsync(attachToCreator: true, installation);

    [Test]
    public async Task SameNameChangedDocumentCheckRejectsTheOriginalGlobalOwner()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(TenantSettingsDocument));
        const string constraintName = "ck_tenant_settings_documents_schema_version_positive";
        string constraint = names.Quote(constraintName);
        string definition;
        await using (var command = Command(migrator, """
            SELECT pg_catalog.pg_get_constraintdef(oid) FROM pg_catalog.pg_constraint
            WHERE conrelid=pg_catalog.to_regclass(@table) AND conname=@name AND contype='c'
            """))
        {
            command.Parameters.Add(new NpgsqlParameter("table", table));
            command.Parameters.Add(new NpgsqlParameter("name", constraintName));
            definition = (string)(await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("The declared document CHECK is missing."));
        }
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        try
        {
            await ExecuteAsync(migrator, $"ALTER TABLE {table} DROP CONSTRAINT {constraint}, ADD CONSTRAINT {constraint} CHECK (true)");
            await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.Colocated);
        }
        finally
        {
            await ExecuteAsync(migrator, $"ALTER TABLE {table} DROP CONSTRAINT {constraint}, ADD CONSTRAINT {constraint} {definition}");
        }
        // PostgreSQL assigns a new constraint OID on replacement; the original name
        // and complete captured definition, not that incidental OID, are restored.
        await using (var command = Command(migrator, """
            SELECT pg_catalog.pg_get_constraintdef(oid) FROM pg_catalog.pg_constraint
            WHERE conrelid=pg_catalog.to_regclass(@table) AND conname=@name AND contype='c'
            """))
        {
            command.Parameters.Add(new NpgsqlParameter("table", table));
            command.Parameters.Add(new NpgsqlParameter("name", constraintName));
            await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(definition);
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    [Test]
    public async Task NonEnforcedDeclaredDocumentCheckRejectsTheOriginalGlobalOwner()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(TenantSettingsDocument));
        const string constraintName = "ck_tenant_settings_documents_schema_version_positive";
        string constraint = names.Quote(constraintName);
        string originalDefinition = await CheckDefinitionAsync(migrator, table, constraintName);
        await Assert.That(await CheckEnforcedAsync(migrator, table, constraintName)).IsTrue();
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        try
        {
            await ExecuteAsync(migrator, $"ALTER TABLE {table} DROP CONSTRAINT {constraint}, ADD CONSTRAINT {constraint} {originalDefinition} NOT ENFORCED");
            await Assert.That(await CheckEnforcedAsync(migrator, table, constraintName)).IsFalse();
            string disabledImage = await CheckImageAsync(migrator, table, constraintName);

            await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.Colocated);

            await Assert.That(await CheckEnforcedAsync(migrator, table, constraintName)).IsFalse();
            await Assert.That(await CheckImageAsync(migrator, table, constraintName)).IsEqualTo(disabledImage);
            await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
        }
        finally
        {
            await ExecuteAsync(migrator, $"ALTER TABLE {table} DROP CONSTRAINT {constraint}, ADD CONSTRAINT {constraint} {originalDefinition}");
        }
        await Assert.That(await CheckEnforcedAsync(migrator, table, constraintName)).IsTrue();
        // Replacing a CHECK necessarily changes its incidental OID; restore its
        // captured expression and flags, not an invented substitute constraint.
        await Assert.That(await CheckDefinitionAsync(migrator, table, constraintName)).IsEqualTo(originalDefinition);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    [Test]
    public async Task ExtraDefaultBearingDocumentColumnRejectsTheOriginalGlobalOwner()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string suffix = Guid.CreateVersion7().ToString("N");
        string sentinel = names.Name("creator_default_effect_" + suffix);
        string control = names.Name("creator_default_control_" + suffix);
        string routine = names.Name("creator_default_fn_" + suffix);
        string column = names.Quote("creator_default_" + suffix);
        string document = names.Table(typeof(TenantSettingsDocument));
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        try
        {
            await ExecuteAsync(migrator, $"""
                CREATE TABLE {sentinel} (effect integer NOT NULL);
                CREATE FUNCTION {routine}() RETURNS boolean
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog AS $attack$
                BEGIN
                    INSERT INTO {sentinel} (effect) VALUES (1);
                    RETURN true;
                END;
                $attack$;
                CREATE TABLE {control} (id integer NOT NULL, extra boolean DEFAULT {routine}());
                INSERT INTO {control} (id) VALUES (1);
                """);
            await Assert.That(await SentinelCountAsync(migrator, sentinel)).IsEqualTo(1L);
            await ExecuteAsync(migrator, $"ALTER TABLE {document} ADD COLUMN {column} boolean DEFAULT {routine}()");
            long effectsBeforeAdmission = await SentinelCountAsync(migrator, sentinel);
            string installedGraph = await GraphDigestAsync(migrator);

            await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.Colocated);

            await Assert.That(await SentinelCountAsync(migrator, sentinel)).IsEqualTo(effectsBeforeAdmission);
            await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(installedGraph);
        }
        finally
        {
            await ExecuteAsync(migrator, $"""
                ALTER TABLE {document} DROP COLUMN IF EXISTS {column};
                DROP TABLE IF EXISTS {control};
                DROP FUNCTION IF EXISTS {routine}();
                DROP TABLE IF EXISTS {sentinel};
                """);
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    private async Task VerifyExecutableCheckAsync(bool attachToCreator, bool installation)
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string suffix = Guid.CreateVersion7().ToString("N");
        string sentinel = names.Name("creator_check_effect_" + suffix);
        string control = names.Name("creator_check_control_" + suffix);
        string routine = names.Name("creator_check_function_" + suffix);
        string constraintName = "creator_check_constraint_" + suffix;
        string constraint = names.Quote(constraintName);
        string document = names.Table(typeof(TenantSettingsDocument));

        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        var globalConnector = (NpgsqlConnection)global.Database.GetDbConnection();
        int globalBackend = globalConnector.ProcessID;
        var globalLease = global.ContextId;

        try
        {
            // Only this disposable control receives DML. The sentinel proves the CHECK
            // executes a database write; no human graph or creator authority is fabricated.
            await ExecuteAsync(migrator, $"""
                CREATE TABLE {sentinel} (effect integer NOT NULL);
                CREATE FUNCTION {routine}() RETURNS boolean
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog AS $attack$
                BEGIN
                    INSERT INTO {sentinel} (effect) VALUES (1);
                    RETURN true;
                END;
                $attack$;
                CREATE TABLE {control} (id integer NOT NULL, CHECK ({routine}()));
                INSERT INTO {control} (id) VALUES (1);
                """);
            await Assert.That(await SentinelCountAsync(migrator, sentinel)).IsEqualTo(1L);

            string? constraintImage = null;
            if (attachToCreator)
            {
                // A validated, always-true CHECK leaves existing constraints satisfied.
                // Validation may execute it on old documents; record that separately
                // before testing whether either admission path accepts this drift.
                await ExecuteAsync(migrator, $"ALTER TABLE {document} ADD CONSTRAINT {constraint} CHECK ({routine}())");
                constraintImage = await CheckImageAsync(migrator, document, constraintName);
            }
            long effectsBeforeAdmission = await SentinelCountAsync(migrator, sentinel);

            if (!attachToCreator)
            {
                await PostgresTenantCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
                await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
            }
            else if (installation)
            {
                var connector = (NpgsqlConnection)migrator.Database.GetDbConnection();
                int backend = connector.ProcessID;
                var lease = migrator.ContextId;
                await using var transaction = await migrator.Database.BeginTransactionAsync();
                try
                {
                    var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        PostgresTenantCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated));
                    await Assert.That(denied.Message).IsEqualTo("tenant_creation_permissions_drift");
                    await Assert.That(migrator.ContextId).IsEqualTo(lease);
                    await Assert.That(migrator.Database.GetDbConnection()).IsSameReferenceAs(connector);
                    await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                    await Assert.That(connector.ProcessID).IsEqualTo(backend);
                }
                finally { await transaction.RollbackAsync(CancellationToken.None); }
            }
            else
            {
                await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.Colocated);
            }

            if (constraintImage is not null)
                await Assert.That(await CheckImageAsync(migrator, document, constraintName)).IsEqualTo(constraintImage);
            await Assert.That(global.ContextId).IsEqualTo(globalLease);
            await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(globalConnector);
            await Assert.That(globalConnector.ProcessID).IsEqualTo(globalBackend);
            await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
            await Assert.That(await SentinelCountAsync(migrator, sentinel)).IsEqualTo(effectsBeforeAdmission);
            await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
        }
        finally
        {
            // No CASCADE or protocol reinstall: remove only the exact objects owned here.
            await ExecuteAsync(migrator, $"""
                ALTER TABLE {document} DROP CONSTRAINT IF EXISTS {constraint};
                DROP TABLE IF EXISTS {control};
                DROP FUNCTION IF EXISTS {routine}();
                DROP TABLE IF EXISTS {sentinel};
                """);
        }

        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(global.ContextId).IsEqualTo(globalLease);
        await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(globalConnector);
        await Assert.That(globalConnector.ProcessID).IsEqualTo(globalBackend);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    private static async Task<long> SentinelCountAsync(ExploreDbContext migrator, string sentinel)
    {
        await using var command = Command(migrator, $"SELECT count(*) FROM {sentinel}");
        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The owned CHECK sentinel observation is missing."));
    }

    private static async Task<string> CheckImageAsync(ExploreDbContext migrator, string document, string constraint)
    {
        await using var command = Command(migrator, """
            SELECT c.oid::text || ':' || pg_catalog.md5(c.conbin::text) || ':' || c.convalidated::text
            FROM pg_catalog.pg_constraint c
            WHERE c.conrelid=pg_catalog.to_regclass(@document) AND c.conname=@constraint AND c.contype='c'
            """);
        command.Parameters.Add(new NpgsqlParameter("document", document));
        command.Parameters.Add(new NpgsqlParameter("constraint", constraint));
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The test-owned executable CHECK was removed or replaced."));
    }

    private static async Task<bool> CheckEnforcedAsync(ExploreDbContext migrator, string document, string constraint)
    {
        await using var command = Command(migrator, """
            SELECT conenforced FROM pg_catalog.pg_constraint
            WHERE conrelid=pg_catalog.to_regclass(@document) AND conname=@constraint AND contype='c'
            """);
        command.Parameters.Add(new NpgsqlParameter("document", document));
        command.Parameters.Add(new NpgsqlParameter("constraint", constraint));
        return (bool)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The declared CHECK enforcement observation is missing."));
    }

    private static async Task<string> CheckDefinitionAsync(ExploreDbContext migrator, string document, string constraint)
    {
        await using var command = Command(migrator, """
            SELECT pg_catalog.pg_get_constraintdef(oid) FROM pg_catalog.pg_constraint
            WHERE conrelid=pg_catalog.to_regclass(@document) AND conname=@constraint AND contype='c'
            """);
        command.Parameters.Add(new NpgsqlParameter("document", document));
        command.Parameters.Add(new NpgsqlParameter("constraint", constraint));
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The declared CHECK definition is missing."));
    }
}
