using System.Data;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Identity;

/// <summary>Real selected-purpose storage admission; no human lifecycle capability is fabricated.</summary>
[NotInParallel]
public sealed partial class LocalIdentityWriterPermissionsNativeTests
{
    [Test]
    [Arguments("colocated")]
    [Arguments("external-application")]
    [Arguments("external-identity")]
    public Task OriginalMigratorInstallsOnlyTheDeclaredWriterStore(string profile) =>
        ExerciseOriginalStoreAsync(profile, requireNonBypassOwner: false);

    [Test]
    public Task DeclaredNonBypassMigratorRetainsItsExistingSystemSettingSeedVisibility() =>
        ExerciseOriginalStoreAsync("colocated", requireNonBypassOwner: true);

    private static async Task ExerciseOriginalStoreAsync(string profile, bool requireNonBypassOwner)
    {
        var topology = profile == "colocated" ? IdentityDatabaseTopology.Colocated : IdentityDatabaseTopology.External;
        var store = profile == "external-identity" ? LocalIdentityWriterInventory.Store.Identity : LocalIdentityWriterInventory.Store.Application;
        await using var fixture = new NativeFixture(topology, distinctMigrator: requireNonBypassOwner);
        await fixture.InitializeAsync();
        var migratorOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
        var writerOptions = migratorOptions with
        {
            Role = PrimaryDatabaseRole.LocalIdentityWriter,
            Username = $"local_writer_{Guid.CreateVersion7():N}",
            Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        };
        await using var administrator = new NpgsqlConnection(
            PrimaryDatabaseConfiguration.BuildConnectionString(migratorOptions).ConnectionString);
        await administrator.OpenAsync();
        // A class-local dynamic secret-provider mock, never a source credential.
        await using (var provision = administrator.CreateCommand())
        {
            provision.CommandText = $"CREATE ROLE {Identifier(writerOptions.Username!)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD {Literal(writerOptions.Password!)}";
            await provision.ExecuteNonQueryAsync();
        }
        if (store == LocalIdentityWriterInventory.Store.Identity)
        {
            string database = $"identity_{Guid.CreateVersion7():N}";
            await using var create = administrator.CreateCommand();
            create.CommandText = $"CREATE DATABASE {Identifier(database)}";
            await create.ExecuteNonQueryAsync();
            migratorOptions = migratorOptions with { Database = database, Schema = IdentityDatabaseConfiguration.DefaultSchema };
            writerOptions = writerOptions with { Database = database, Schema = IdentityDatabaseConfiguration.DefaultSchema };
        }
        var enrollment = new PostgresLocalIdentityWriterEnrollment(migratorOptions.Username!,
            fixture.NativeQueueEnrollment.RuntimeLogin, writerOptions.Username!,
            store == LocalIdentityWriterInventory.Store.Application ? fixture.NativeQueueEnrollment.GlobalLogin : null);
        await using var migrator = CreateContext(migratorOptions, store);
        await migrator.Database.OpenConnectionAsync();
        await RequireSessionAsync(migrator, enrollment.MigratorLogin);
        if (store == LocalIdentityWriterInventory.Store.Identity)
            await migrator.Database.MigrateAsync();
        await EnsureDeclaredTablesAsync(migrator, topology, store);
        await using (var declared = administrator.CreateCommand())
        {
            declared.CommandText = "SELECT rolcanlogin AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls FROM pg_catalog.pg_roles WHERE rolname=@login";
            declared.Parameters.AddWithValue("login", enrollment.WriterLogin);
            await Assert.That(await declared.ExecuteScalarAsync()).IsEqualTo(true);
        }
        string counts = await RowCountsAsync(migrator, topology, store);
        var connector = (NpgsqlConnection)migrator.Database.GetDbConnection();
        int backend = connector.ProcessID;
        await using (var transaction = await migrator.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var physical = transaction.GetDbTransaction();
            foreach (string otherPurpose in new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin }
                         .Concat(enrollment.ApplicationGlobalLogin is { } global ? [global] : Array.Empty<string>()))
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator,
                        enrollment with { WriterLogin = otherPurpose }, topology, store));
            await PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment, topology, store);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(physical.Connection).IsSameReferenceAs(connector);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await transaction.CommitAsync();
        }
        await using var writer = CreateContext(writerOptions, store, enrollment, topology);
        await writer.Database.OpenConnectionAsync();
        await RequireSessionAsync(writer, enrollment.WriterLogin);
        var writerConnector = (NpgsqlConnection)writer.Database.GetDbConnection();
        int writerBackend = writerConnector.ProcessID;
        var contextId = writer.ContextId;
        await using (var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var physical = transaction.GetDbTransaction();
            await PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store);
            var wrongEnrollment = enrollment with { WriterLogin = $"unselected_{Guid.CreateVersion7():N}" };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, wrongEnrollment, topology, store));
            await PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store);
            await Assert.That(writer.ContextId).IsEqualTo(contextId);
            await Assert.That(writer.Database.GetDbConnection()).IsSameReferenceAs(writerConnector);
            await Assert.That(writerConnector.ProcessID).IsEqualTo(writerBackend);
            await Assert.That(writer.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(physical.Connection).IsSameReferenceAs(writerConnector);
            await Assert.That(writer.ChangeTracker.Entries().Any()).IsFalse();
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await RequireDriftRefusalAsync(migrator, writer, enrollment, topology, store);
        await RequireOrdinaryPurposeIsolationAsync(fixture, migratorOptions, migrator, topology, store);
        await Assert.That(await RowCountsAsync(migrator, topology, store)).IsEqualTo(counts);
        await RequireSessionAsync(writer, enrollment.WriterLogin);
        if (requireNonBypassOwner)
            await RequireNonBypassOwnerVisibilityAsync(migrator, enrollment);
    }

    private static async Task RequireNonBypassOwnerVisibilityAsync(DbContext migrator,
        PostgresLocalIdentityWriterEnrollment enrollment)
    {
        var entity = migrator.Model.FindEntityType(typeof(SystemSetting))!;
        string table = migrator.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        var connector = (NpgsqlConnection)migrator.Database.GetDbConnection();
        var contextId = migrator.ContextId;
        int backend = connector.ProcessID;
        await RequireSessionAsync(migrator, enrollment.MigratorLogin);
        await using var count = Command(migrator, $"SELECT count(*) FROM {table}");
        long expected = (long)(await count.ExecuteScalarAsync())!;
        await Assert.That(expected).IsGreaterThan(0);
        await using (var owner = Command(migrator, """
            SELECT relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@login)
                AND relrowsecurity AND relforcerowsecurity
            FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass(@table)
            """))
        {
            owner.Parameters.AddWithValue("login", enrollment.MigratorLogin);
            owner.Parameters.AddWithValue("table", table);
            await Assert.That(await owner.ExecuteScalarAsync()).IsEqualTo(true);
        }

        bool wasSuperuser, bypassedRowSecurity;
        await using (var attributes = Command(migrator,
            "SELECT rolsuper,rolbypassrls FROM pg_catalog.pg_roles WHERE rolname=@login"))
        {
            attributes.Parameters.AddWithValue("login", enrollment.MigratorLogin);
            await using var reader = await attributes.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("The fixture Migrator is absent.");
            wasSuperuser = reader.GetBoolean(0);
            bypassedRowSecurity = reader.GetBoolean(1);
        }
        // A fresh container-only secret-provider mock retains bootstrap administration;
        // the original Migrator stays on its exact connector throughout the demoted read.
        string bootstrapLogin = $"local_visibility_bootstrap_{Guid.CreateVersion7():N}";
        string bootstrapSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using (var provision = Command(migrator,
            $"CREATE ROLE {Identifier(bootstrapLogin)} LOGIN SUPERUSER NOINHERIT PASSWORD {Literal(bootstrapSecret)}"))
            await provision.ExecuteNonQueryAsync();
        var bootstrapOptions = new NpgsqlConnectionStringBuilder(connector.ConnectionString)
        {
            Username = bootstrapLogin,
            Password = bootstrapSecret,
            Pooling = false,
            IncludeErrorDetail = true
        };
        await using var bootstrap = new NpgsqlConnection(bootstrapOptions.ConnectionString);
        bool demotionAttempted = false;
        Exception? probeFailure = null;
        try
        {
            await bootstrap.OpenAsync();
            await using (var authority = new NpgsqlCommand("""
                SELECT current_user=session_user AND session_user=@login AND rolsuper
                FROM pg_catalog.pg_roles WHERE rolname=@login
                """, bootstrap))
            {
                authority.Parameters.AddWithValue("login", bootstrapLogin);
                await Assert.That(await authority.ExecuteScalarAsync()).IsEqualTo(true);
            }
            demotionAttempted = true;
            await using (var demote = new NpgsqlCommand(
                $"ALTER ROLE {Identifier(enrollment.MigratorLogin)} NOSUPERUSER NOBYPASSRLS", bootstrap))
            {
                try
                {
                    await demote.ExecuteNonQueryAsync();
                }
                catch (PostgresException exception)
                {
                    string detail = new((exception.Detail ?? "No server detail.")
                        .Replace(bootstrapSecret, "[redacted]", StringComparison.Ordinal)
                        .Where(character => !char.IsControl(character)).Take(512).ToArray());
                    throw new InvalidOperationException(
                        $"The verified isolated bootstrap superuser could not demote the original Migrator: {exception.SqlState}; {detail}",
                        exception);
                }
            }
            await using (var identity = Command(migrator, """
                SELECT current_user=session_user AND session_user=@login
                    AND NOT rolsuper AND NOT rolbypassrls
                FROM pg_catalog.pg_roles WHERE rolname=@login
                """))
            {
                identity.Parameters.AddWithValue("login", enrollment.MigratorLogin);
                await Assert.That(await identity.ExecuteScalarAsync()).IsEqualTo(true);
            }
            await Assert.That(migrator.ContextId).IsEqualTo(contextId);
            await Assert.That(migrator.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await Assert.That(migrator.Database.CurrentTransaction).IsNull();
            await Assert.That((long)(await count.ExecuteScalarAsync())!).IsEqualTo(expected);
        }
        catch (Exception exception)
        {
            probeFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                if (demotionAttempted)
                {
                    if (bootstrap.State != ConnectionState.Open)
                        throw new InvalidOperationException("The retained test bootstrap connector cannot restore the original Migrator.");
                    await using var restore = new NpgsqlCommand(
                        $"ALTER ROLE {Identifier(enrollment.MigratorLogin)} {(wasSuperuser ? "SUPERUSER" : "NOSUPERUSER")} {(bypassedRowSecurity ? "BYPASSRLS" : "NOBYPASSRLS")}", bootstrap);
                    await restore.ExecuteNonQueryAsync(CancellationToken.None);
                }
                await bootstrap.CloseAsync();
                await using var drop = Command(migrator, $"DROP ROLE {Identifier(bootstrapLogin)}");
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch (Exception cleanupFailure) when (probeFailure is not null)
            {
                throw new AggregateException("The original Migrator visibility probe and its isolated cleanup both failed.",
                    probeFailure, cleanupFailure);
            }
        }
    }

    private static DbContext CreateContext(PrimaryDatabaseConnectionOptions options, LocalIdentityWriterInventory.Store store,
        PostgresLocalIdentityWriterEnrollment? enrollment = null, IdentityDatabaseTopology topology = IdentityDatabaseTopology.Colocated)
    {
        if (store == LocalIdentityWriterInventory.Store.Application)
        {
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                new RelationalNamespaceOptionsExtension(options.Schema, options.Schema));
            if (enrollment is not null)
                builder.AddInterceptors(PostgresTenantSessionInterceptor.ForLocalIdentityWriter(enrollment, topology, store));
            var context = new ExploreDbContext(builder.Options);
            context.BindTenantLease();
            return context;
        }
        var identity = TestDbContextOptions.Create<ExternalIdentityDbContext>();
        IdentityDatabaseProviderComposition.Configure(identity, new ExternalIdentityDatabaseDescriptor(
            options.Provider, PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString,
            options.Schema, options.ServerVersion, options.ServerFlavor));
        if (enrollment is not null)
            identity.AddInterceptors(PostgresTenantSessionInterceptor.ForLocalIdentityWriter(enrollment, topology, store));
        return new ExternalIdentityDbContext(identity.Options);
    }

    private static async Task RequireDriftRefusalAsync(DbContext migrator, DbContext writer,
        PostgresLocalIdentityWriterEnrollment enrollment, IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store)
    {
        Type type = store == LocalIdentityWriterInventory.Store.Identity ? typeof(LocalIdentityUser) : typeof(User);
        var entity = migrator.Model.FindEntityType(type)!;
        var names = migrator.GetService<ISqlGenerationHelper>();
        var mapped = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string table = names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        string id = names.DelimitIdentifier(entity.FindProperty("Id")!.GetColumnName(mapped)!);
        string login = Identifier(enrollment.WriterLogin);
        var connector = writer.Database.GetDbConnection();
        int backend = ((NpgsqlConnection)connector).ProcessID;
        var contextId = writer.ContextId;

        async Task AttackAsync(string mutation, string restore)
        {
            await using (var command = Command(migrator, mutation)) await command.ExecuteNonQueryAsync();
            try
            {
                string attackedImage = await CatalogueImageAsync(migrator, topology, store);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store));
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment, topology, store));
                await Assert.That(await CatalogueImageAsync(migrator, topology, store)).IsEqualTo(attackedImage);
                await Assert.That(writer.ContextId).IsEqualTo(contextId);
                await Assert.That(writer.Database.GetDbConnection()).IsSameReferenceAs(connector);
                await Assert.That(((NpgsqlConnection)connector).ProcessID).IsEqualTo(backend);
                await Assert.That(writer.Database.CurrentTransaction).IsNull();
            }
            finally
            {
                await using var command = Command(migrator, restore);
                await command.ExecuteNonQueryAsync();
            }
            await PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store);
        }

        await AttackAsync($"GRANT SELECT ({id}) ON {table} TO {login} WITH GRANT OPTION",
            $"REVOKE GRANT OPTION FOR SELECT ({id}) ON {table} FROM {login}");
        await AttackAsync($"GRANT SELECT (ctid) ON {table} TO {login}",
            $"REVOKE SELECT (ctid) ON {table} FROM {login}");
        await AttackAsync($"GRANT SELECT ({id}) ON {table} TO PUBLIC",
            $"REVOKE SELECT ({id}) ON {table} FROM PUBLIC");

        if (store == LocalIdentityWriterInventory.Store.Application)
        {
            var settings = migrator.Model.FindEntityType(typeof(SystemSetting))!;
            var settingStore = StoreObjectIdentifier.Table(settings.GetTableName()!, settings.GetSchema());
            string settingsTable = names.DelimitIdentifier(settings.GetTableName()!, settings.GetSchema());
            string key = names.DelimitIdentifier(settings.FindProperty(nameof(SystemSetting.SettingKey))!.GetColumnName(settingStore)!);
            string allowed = string.Join(',', Explore.Application.Contracts.Persistence.HumanAuthorizationRouteFacts.Keys
                .Append(GovernanceSettingKeys.Email.DeliveryEnabled).Select(Literal));
            await using (var present = Command(migrator, $"SELECT count(*) FROM {settingsTable} WHERE {key} NOT IN ({allowed})"))
                await Assert.That((long)(await present.ExecuteScalarAsync())!).IsGreaterThan(0);
            await using (var hidden = Command(writer, $"SELECT count(*) FROM {settingsTable} WHERE {key} NOT IN ({allowed})"))
                await Assert.That((long)(await hidden.ExecuteScalarAsync())!).IsEqualTo(0);
            await using var original = Command(migrator, "SELECT pg_catalog.pg_get_expr(polqual,polrelid) FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass(@table) AND polname='local_identity_writer_read_boundary'");
            original.Parameters.AddWithValue("table", settingsTable);
            string expression = (string)(await original.ExecuteScalarAsync())!;
            await AttackAsync($"ALTER POLICY local_identity_writer_read_boundary ON {settingsTable} USING (true)",
                $"ALTER POLICY local_identity_writer_read_boundary ON {settingsTable} USING ({expression})");
            string probe = Identifier("local_writer_probe_" + Guid.CreateVersion7().ToString("N"));
            await AttackAsync($"CREATE POLICY {probe} ON {settingsTable} FOR SELECT TO PUBLIC USING (true)",
                $"DROP POLICY {probe} ON {settingsTable}");
        }
    }

    private static async Task<string> CatalogueImageAsync(DbContext context, IdentityDatabaseTopology topology,
        LocalIdentityWriterInventory.Store store)
    {
        var names = context.GetService<ISqlGenerationHelper>();
        var types = Sources(topology, store).Select(source => source.EntityType)
            .Concat(store == LocalIdentityWriterInventory.Store.Application ? new[] { typeof(SystemSetting) } : []);
        string relations = string.Join(',', types.Select(type =>
        {
            var entity = context.Model.FindEntityType(type)!;
            return Literal(names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema()));
        }));
        await using var command = Command(context, $"""
            SELECT md5(string_agg(image,'|' ORDER BY image)) FROM (
                SELECT c.oid::text || ':' || COALESCE(c.relacl::text,'') || ':' || c.relrowsecurity || ':' || c.relforcerowsecurity AS image
                FROM pg_catalog.pg_class c WHERE c.oid=ANY(ARRAY[{relations}]::regclass[])
                UNION ALL SELECT a.attrelid::text || ':' || a.attnum || ':' || COALESCE(a.attacl::text,'')
                FROM pg_catalog.pg_attribute a WHERE a.attrelid=ANY(ARRAY[{relations}]::regclass[]) AND a.attnum>0
                UNION ALL SELECT p.polrelid::text || ':' || p.polname || ':' || p.polcmd::text || ':' || p.polpermissive || ':' ||
                    p.polroles::text || ':' || COALESCE(p.polqual::text,'') || ':' || COALESCE(p.polwithcheck::text,'')
                FROM pg_catalog.pg_policy p WHERE p.polrelid=ANY(ARRAY[{relations}]::regclass[])
            ) original
            """);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task RequireOrdinaryPurposeIsolationAsync(NativeFixture fixture,
        PrimaryDatabaseConnectionOptions migratorOptions, DbContext context, IdentityDatabaseTopology topology,
        LocalIdentityWriterInventory.Store store)
    {
        bool hasIdentity = topology == IdentityDatabaseTopology.Colocated || store == LocalIdentityWriterInventory.Store.Identity;
        var entity = context.Model.FindEntityType(hasIdentity ? typeof(LocalIdentityUser) : typeof(UserPii))!;
        var names = context.GetService<ISqlGenerationHelper>();
        var mapped = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string table = names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        string column = names.DelimitIdentifier(entity.FindProperty(hasIdentity ? nameof(LocalIdentityUser.PasswordHash) : nameof(UserPii.Email))!.GetColumnName(mapped)!);
        foreach (var purpose in new[] { PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global })
        {
            var options = fixture.GetNativeQueueOptions(purpose) with
            {
                Database = migratorOptions.Database,
                Schema = migratorOptions.Schema
            };
            await using var connection = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"SELECT {column} FROM {table} WHERE false", connection);
            var error = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());
            await Assert.That(error.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    private static IEnumerable<LocalIdentityWriterInventory.Source> Sources(
        IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store) =>
        LocalIdentityWriterInventory.Sources.Where(source => topology == IdentityDatabaseTopology.Colocated || source.Database == store);

    private static async Task EnsureDeclaredTablesAsync(DbContext context, IdentityDatabaseTopology topology,
        LocalIdentityWriterInventory.Store store)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var names = context.GetService<ISqlGenerationHelper>();
        var missing = new HashSet<(string? Schema, string Table)>();
        foreach (var source in Sources(topology, store))
        {
            LocalIdentityWriterInventory.RequireModel(context, source);
            var entity = model.FindEntityType(source.EntityType)!;
            var key = (Schema: entity.GetSchema(), Table: entity.GetTableName()!);
            await using var existing = Command(context, "SELECT pg_catalog.to_regclass(@table) IS NOT NULL");
            existing.Parameters.AddWithValue("table", names.DelimitIdentifier(key.Table, key.Schema));
            if (await existing.ExecuteScalarAsync() is not true) missing.Add(key);
        }
        if (missing.Count == 0) return;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel())
            .Where(operation => operation is EnsureSchemaOperation schema && missing.Any(table => table.Schema == schema.Name)
                || operation is CreateTableOperation table && missing.Contains((table.Schema, table.Name))
                || operation is CreateIndexOperation index && missing.Contains((index.Schema, index.Table))).ToArray();
        await Assert.That(operations.OfType<CreateTableOperation>().Count()).IsEqualTo(missing.Count);
        foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
            await command.ExecuteNonQueryAsync(context.GetService<IRelationalConnection>());
    }

    private static async Task RequireSessionAsync(DbContext context, string login)
    {
        await using var command = Command(context, """
            SELECT current_user=session_user AND session_user=@login
                AND current_setting('role') IN ('none',session_user::text)
                AND NULLIF(current_setting('app.current_tenant_id',true),'') IS NULL
            """);
        command.Parameters.AddWithValue("login", login);
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    private static async Task<string> RowCountsAsync(DbContext context, IdentityDatabaseTopology topology,
        LocalIdentityWriterInventory.Store store)
    {
        var names = context.GetService<ISqlGenerationHelper>();
        var relations = Sources(topology, store).Select(source =>
        {
            var entity = context.Model.FindEntityType(source.EntityType)!;
            return names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        });
        await using var command = Command(context, "SELECT " + string.Join(" || ':' || ",
            relations.Select(table => $"(SELECT count(*)::text FROM {table})")));
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Local writer source counts are missing."));
    }

    private static NpgsqlCommand Command(DbContext context, string sql) => new(sql,
        (NpgsqlConnection)context.Database.GetDbConnection(),
        (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());
    private static string Identifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Literal(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private sealed class NativeFixture(IdentityDatabaseTopology topology, bool distinctMigrator = false)
        : PostgreSqlContainerFixture(topology, distinctMigrator)
    {
    }
}
