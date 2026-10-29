using Explore.Application.Configuration;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed partial class LocalIdentityWriterPermissionsNativeTests
{
    [Test]
    [Arguments("colocated")]
    [Arguments("external-application")]
    [Arguments("external-identity")]
    public async Task ProductionMigrationSelectsOnlyAnExplicitCompleteWriterEnrollment(string profile)
    {
        var topology = profile == "colocated" ? IdentityDatabaseTopology.Colocated : IdentityDatabaseTopology.External;
        var store = profile == "external-identity" ? LocalIdentityWriterInventory.Store.Identity : LocalIdentityWriterInventory.Store.Application;
        await using var fixture = new NativeFixture(topology);
        // Fixture admission is a prerequisite, not migration-activation evidence.
        await fixture.InitializeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var migratorOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
        var writerOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter);
        (migratorOptions, writerOptions) = await SelectMigrationDatabaseAsync(migratorOptions, writerOptions, store, timeout.Token);
        var configuration = MigrationWriterConfiguration(fixture, migratorOptions, topology, store);
        string writerKey = MigrationWriterUsernameKey(store);
        await Assert.That(configuration[writerKey]).IsNull();
        await Assert.That(configuration[MigrationWriterPasswordKey(store)]).IsNull();

        // Keycloak-only deployment has no Local purpose declaration or writer secret to bind.
        await RunProductionMigrationAsync(configuration, migratorOptions, store, timeout.Token);
        await using var observer = CreateContext(migratorOptions, store);
        await observer.Database.OpenConnectionAsync(timeout.Token);
        await RequireSessionAsync(observer, migratorOptions.Username!);
        await RequireNoWriterColumnAuthorityAsync(observer, writerOptions.Username!, topology, store, timeout.Token);
        string absentImage = await CatalogueImageAsync(observer, topology, store);

        configuration[writerKey] = fixture.NativeQueueEnrollment.RuntimeLogin;
        var alias = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProductionMigrationAsync(configuration, migratorOptions, store, timeout.Token));
        await Assert.That(alias.Message).IsEqualTo("local_identity_writer_enrollment_invalid");
        await Assert.That(await CatalogueImageAsync(observer, topology, store)).IsEqualTo(absentImage);

        // A declared LOGIN must already exist; migration may not create a privileged identity.
        string undeployedLogin = $"missing_local_writer_{Guid.CreateVersion7():N}";
        configuration[writerKey] = undeployedLogin;
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProductionMigrationAsync(configuration, migratorOptions, store, timeout.Token));
        await Assert.That(missing.Message).IsEqualTo("local_identity_writer_permissions_drift");
        await using (var role = Command(observer, "SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname=@login"))
        {
            role.Parameters.AddWithValue("login", undeployedLogin);
            await Assert.That((long)(await role.ExecuteScalarAsync(timeout.Token))!).IsEqualTo(0L);
        }
        await RequireNoWriterColumnAuthorityAsync(observer, writerOptions.Username!, topology, store, timeout.Token);
        await Assert.That(await CatalogueImageAsync(observer, topology, store)).IsEqualTo(absentImage);
    }

    [Test]
    [Arguments("colocated")]
    [Arguments("external-application")]
    [Arguments("external-identity")]
    public async Task ProductionMigrationInstallsDeclaredWriterWithoutItsSecretAndRejectsPartialDrift(string profile)
    {
        var topology = profile == "colocated" ? IdentityDatabaseTopology.Colocated : IdentityDatabaseTopology.External;
        var store = profile == "external-identity" ? LocalIdentityWriterInventory.Store.Identity : LocalIdentityWriterInventory.Store.Application;
        await using var fixture = new NativeFixture(topology);
        await fixture.InitializeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var migratorOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
        var writerOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter);
        (migratorOptions, writerOptions) = await SelectMigrationDatabaseAsync(migratorOptions, writerOptions, store, timeout.Token);
        var configuration = MigrationWriterConfiguration(fixture, migratorOptions, topology, store);
        // The purpose's credential is used only by the later real writer connection, never migration.
        configuration[MigrationWriterUsernameKey(store)] = writerOptions.Username;
        await Assert.That(configuration[MigrationWriterPasswordKey(store)]).IsNull();
        var enrollment = new PostgresLocalIdentityWriterEnrollment(migratorOptions.Username!,
            fixture.NativeQueueEnrollment.RuntimeLogin, writerOptions.Username!,
            store == LocalIdentityWriterInventory.Store.Application ? fixture.NativeQueueEnrollment.GlobalLogin : null);

        await RunProductionMigrationAsync(configuration, migratorOptions, store, timeout.Token);
        await using var observer = CreateContext(migratorOptions, store);
        await observer.Database.OpenConnectionAsync(timeout.Token);
        await RequireSessionAsync(observer, enrollment.MigratorLogin);
        string counts = await RowCountsAsync(observer, topology, store);
        string installedImage = await CatalogueImageAsync(observer, topology, store);
        await using var writer = CreateContext(writerOptions, store, enrollment, topology);
        await writer.Database.OpenConnectionAsync(timeout.Token);
        await RequireSessionAsync(writer, enrollment.WriterLogin);
        await PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store, timeout.Token);

        await RunProductionMigrationAsync(configuration, migratorOptions, store, timeout.Token);
        await Assert.That(await CatalogueImageAsync(observer, topology, store)).IsEqualTo(installedImage);
        await Assert.That(await RowCountsAsync(observer, topology, store)).IsEqualTo(counts);

        Type sourceType = store == LocalIdentityWriterInventory.Store.Identity ? typeof(LocalIdentityUser) : typeof(Explore.Domain.User);
        var entity = observer.Model.FindEntityType(sourceType)!;
        var names = observer.GetService<ISqlGenerationHelper>();
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string relation = names.DelimitIdentifier(table.Name, table.Schema);
        string column = names.DelimitIdentifier(entity.FindProperty("Id")!.GetColumnName(table)!);
        string login = Identifier(enrollment.WriterLogin);
        await using (var revoke = Command(observer, $"REVOKE SELECT ({column}) ON {relation} FROM {login}"))
            await revoke.ExecuteNonQueryAsync(timeout.Token);
        try
        {
            string revokedImage = await CatalogueImageAsync(observer, topology, store);
            var drift = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RunProductionMigrationAsync(configuration, migratorOptions, store, timeout.Token));
            await Assert.That(drift.Message).IsEqualTo("local_identity_writer_permissions_drift");
            await Assert.That(await CatalogueImageAsync(observer, topology, store)).IsEqualTo(revokedImage);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store, timeout.Token));
            await Assert.That(await RowCountsAsync(observer, topology, store)).IsEqualTo(counts);
        }
        finally
        {
            // Restore only this test's one revoked column, not the profile or unrelated grants.
            await using var restore = Command(observer, $"GRANT SELECT ({column}) ON {relation} TO {login}");
            await restore.ExecuteNonQueryAsync(CancellationToken.None);
        }
        await PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store, timeout.Token);
    }

    private static async Task RunProductionMigrationAsync(IConfiguration configuration,
        PrimaryDatabaseConnectionOptions migrator, LocalIdentityWriterInventory.Store store, CancellationToken token)
    {
        if (store == LocalIdentityWriterInventory.Store.Identity)
        {
            await Assert.That(await ExternalIdentityDatabaseMigrator.MigrateIfExternalAsync(
                configuration, NullLogger.Instance, token)).IsTrue();
            return;
        }
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        await ExploreDatabaseMigrator.MigrateAndSeedAsync(environment, configuration, migrator,
            NullLogger.Instance, seedDevelopmentData: false, cancellationToken: token);
    }

    private static IConfigurationRoot MigrationWriterConfiguration(NativeFixture fixture,
        PrimaryDatabaseConnectionOptions migrator, IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:Provider"] = AuthenticationProviderDeploymentOptions.KeycloakProvider,
            ["IdentityDatabase:Topology"] = topology == IdentityDatabaseTopology.Colocated ? "colocated" : "external",
            ["PrivacyErasure:Authority:Topology"] = "CoLocated"
        };
        foreach (PrimaryDatabaseRole role in Enum.GetValues<PrimaryDatabaseRole>().Where(role => role != PrimaryDatabaseRole.LocalIdentityWriter))
            values[$"Database:{role}:Username"] = fixture.GetNativeQueueOptions(role).Username;
        if (store == LocalIdentityWriterInventory.Store.Identity)
        {
            values["IdentityDatabase:Provider"] = "PostgreSql";
            values["IdentityDatabase:Host"] = migrator.Host;
            values["IdentityDatabase:Port"] = migrator.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            values["IdentityDatabase:Name"] = migrator.Database;
            values["IdentityDatabase:Schema"] = migrator.Schema;
            values["IdentityDatabase:TlsMode"] = migrator.TlsMode.ToString();
            values["IdentityDatabase:Migrator:Username"] = migrator.Username;
            values["IdentityDatabase:Migrator:Password"] = migrator.Password;
            values["IdentityDatabase:Runtime:Username"] = fixture.NativeQueueEnrollment.RuntimeLogin;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static string MigrationWriterUsernameKey(LocalIdentityWriterInventory.Store store) =>
        store == LocalIdentityWriterInventory.Store.Identity
            ? "IdentityDatabase:LocalIdentityWriter:Username" : "Database:LocalIdentityWriter:Username";

    private static string MigrationWriterPasswordKey(LocalIdentityWriterInventory.Store store) =>
        store == LocalIdentityWriterInventory.Store.Identity
            ? "IdentityDatabase:LocalIdentityWriter:Password" : "Database:LocalIdentityWriter:Password";

    private static async Task<(PrimaryDatabaseConnectionOptions Migrator, PrimaryDatabaseConnectionOptions Writer)>
        SelectMigrationDatabaseAsync(PrimaryDatabaseConnectionOptions migrator, PrimaryDatabaseConnectionOptions writer,
            LocalIdentityWriterInventory.Store store, CancellationToken token)
    {
        if (store == LocalIdentityWriterInventory.Store.Application) return (migrator, writer);
        await using var administrator = CreateContext(migrator, LocalIdentityWriterInventory.Store.Application);
        await administrator.Database.OpenConnectionAsync(token);
        string database = $"local_migration_{Guid.CreateVersion7():N}";
        await using (var create = Command(administrator, $"CREATE DATABASE {Identifier(database)}"))
            await create.ExecuteNonQueryAsync(token);
        return (migrator with { Database = database, Schema = IdentityDatabaseConfiguration.DefaultSchema },
            writer with { Database = database, Schema = IdentityDatabaseConfiguration.DefaultSchema });
    }

    private static async Task RequireNoWriterColumnAuthorityAsync(DbContext observer, string login,
        IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store, CancellationToken token)
    {
        foreach (var source in Sources(topology, store))
        {
            var entity = observer.Model.FindEntityType(source.EntityType)!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string relation = observer.GetService<ISqlGenerationHelper>().DelimitIdentifier(table.Name, table.Schema);
            await using var command = Command(observer, """
                SELECT NOT EXISTS (SELECT 1 FROM unnest(@columns::text[]) selected(column_name)
                    WHERE pg_catalog.has_column_privilege(@login,@relation,selected.column_name,'SELECT,INSERT,UPDATE'))
                """);
            command.Parameters.AddWithValue("login", login);
            command.Parameters.AddWithValue("relation", relation);
            command.Parameters.AddWithValue("columns", source.Select.Select(property => entity.FindProperty(property)!.GetColumnName(table)!).ToArray());
            await Assert.That(await command.ExecuteScalarAsync(token)).IsEqualTo(true);
        }
    }
}
