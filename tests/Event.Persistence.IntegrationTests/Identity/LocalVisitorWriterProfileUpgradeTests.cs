using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Identity;

// Reuse the native selected-role fixture and model-generated schema helpers.
public sealed partial class LocalIdentityWriterPermissionsNativeTests
{
    [Test]
    [Arguments("colocated")]
    [Arguments("external-application")]
    [Arguments("external-identity")]
    public async Task KnownInstalledVisitorWriterUpgradeIsTransactionalAndPreservesData(string profile)
    {
        var topology = profile == "colocated" ? IdentityDatabaseTopology.Colocated : IdentityDatabaseTopology.External;
        var store = profile == "external-identity" ? LocalIdentityWriterInventory.Store.Identity : LocalIdentityWriterInventory.Store.Application;
        await using var fixture = new NativeFixture(topology);
        await fixture.InitializeAsync();
        var (migratorOptions, writerOptions) = await SelectMigrationDatabaseAsync(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator),
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter), store, CancellationToken.None);
        var enrollment = new PostgresLocalIdentityWriterEnrollment(migratorOptions.Username!,
            fixture.NativeQueueEnrollment.RuntimeLogin, writerOptions.Username!,
            store == LocalIdentityWriterInventory.Store.Application ? fixture.NativeQueueEnrollment.GlobalLogin : null);
        await using var migrator = CreateContext(migratorOptions, store);
        await migrator.Database.OpenConnectionAsync();
        if (store == LocalIdentityWriterInventory.Store.Identity) await migrator.Database.MigrateAsync();
        await EnsureDeclaredTablesAsync(migrator, topology, store);
        if (store == LocalIdentityWriterInventory.Store.Application)
            await PostgresTenantRowLevelSecurityModel.ApplyAsync((ExploreDbContext)migrator);

        Guid retainedApplicationUser = Guid.CreateVersion7();
        Guid retainedApplicationStamp = Guid.CreateVersion7();
        if (store == LocalIdentityWriterInventory.Store.Application)
        {
            var user = VisitorUpgradeRelation(migrator, typeof(User));
            // A storage preservation witness, independent of enrollment workflow effects.
            await using var seed = Command(migrator, $"""
                INSERT INTO {user.Table} ({user.Columns["Id"]},{user.Columns["ConcurrencyStamp"]},
                    {user.Columns["CreatedAt"]},{user.Columns["IsDeleted"]})
                VALUES (@id,@stamp,@created,false)
                """);
            seed.Parameters.AddWithValue("id", retainedApplicationUser);
            seed.Parameters.AddWithValue("stamp", retainedApplicationStamp);
            seed.Parameters.AddWithValue("created", DateTime.UtcNow);
            await seed.ExecuteNonQueryAsync();
        }
        LocalIdentityUser? retained = null;
        if (topology == IdentityDatabaseTopology.Colocated || store == LocalIdentityWriterInventory.Store.Identity)
        {
            retained = new LocalIdentityUser
            {
                UserName = "retained-upgrade-user", NormalizedUserName = "RETAINED-UPGRADE-USER",
                CreatedAt = DateTime.UtcNow, FirstName = "Retained", LastName = "Profile"
            };
            migrator.Add(retained);
            await migrator.SaveChangesAsync();
        }
        await PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment, topology, store);
        await RestorePreVisitorProfileAsync(migrator, enrollment, topology, store);
        string original = await CatalogueImageAsync(migrator, topology, store);
        string counts = await RowCountsAsync(migrator, topology, store);
        await using var writer = CreateContext(writerOptions, store);
        await writer.Database.OpenConnectionAsync();
        await RequireSessionAsync(writer, enrollment.WriterLogin);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store));
        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresLocalIdentityWriterPermissions.ApplyAsync(writer, enrollment, topology, store));
        await Assert.That(denied.Message).IsEqualTo("local_identity_writer_installer_wrong_identity");
        await Assert.That(await CatalogueImageAsync(migrator, topology, store)).IsEqualTo(original);

        await using (var transaction = await migrator.Database.BeginTransactionAsync())
        {
            await PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment, topology, store);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(await CatalogueImageAsync(migrator, topology, store)).IsNotEqualTo(original);
            await transaction.RollbackAsync();
        }
        await Assert.That(await CatalogueImageAsync(migrator, topology, store)).IsEqualTo(original);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store));

        await PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment, topology, store);
        await PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store);
        await ProbeVisitorWriterOperationsAsync(writer, topology, store);
        await using var admitted = CreateContext(writerOptions, store, enrollment, topology);
        await admitted.Database.OpenConnectionAsync();
        await PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(admitted, enrollment, topology, store);
        await RequireOrdinaryPurposeIsolationAsync(fixture, migratorOptions, migrator, topology, store);
        await using var runtime = CreateContext(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime) with
        {
            Database = migratorOptions.Database, Schema = migratorOptions.Schema
        }, store);
        await runtime.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresLocalIdentityWriterPermissions.ApplyAsync(runtime, enrollment, topology, store));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(runtime, enrollment, topology, store));
        string installed = await CatalogueImageAsync(migrator, topology, store);
        await PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment, topology, store);
        await Assert.That(await CatalogueImageAsync(migrator, topology, store)).IsEqualTo(installed);
        await Assert.That(await RowCountsAsync(migrator, topology, store)).IsEqualTo(counts);
        if (store == LocalIdentityWriterInventory.Store.Application)
        {
            var user = VisitorUpgradeRelation(migrator, typeof(User));
            await using var preserved = Command(migrator,
                $"SELECT {user.Columns["ConcurrencyStamp"]} FROM {user.Table} WHERE {user.Columns["Id"]}=@id");
            preserved.Parameters.AddWithValue("id", retainedApplicationUser);
            await Assert.That((Guid)(await preserved.ExecuteScalarAsync())!).IsEqualTo(retainedApplicationStamp);
        }
        if (retained is not null)
        {
            var observed = await migrator.Set<LocalIdentityUser>().AsNoTracking().SingleAsync(user => user.Id == retained.Id);
            await Assert.That(observed.UserName).IsEqualTo(retained.UserName);
            await Assert.That(observed.SecurityStamp).IsEqualTo(retained.SecurityStamp);
            await Assert.That(observed.ConcurrencyStamp).IsEqualTo(retained.ConcurrencyStamp);
            await Assert.That(observed.FirstName).IsEqualTo("Retained");
            await Assert.That(observed.LastName).IsEqualTo("Profile");
        }
    }

    [Test]
    [Arguments("colocated", "missing")]
    [Arguments("colocated", "grant-option")]
    [Arguments("colocated", "partial-upgrade")]
    [Arguments("colocated", "introduced-policy")]
    [Arguments("external-application", "missing")]
    [Arguments("external-application", "grant-option")]
    [Arguments("external-application", "partial-upgrade")]
    [Arguments("external-application", "introduced-policy")]
    [Arguments("external-identity", "missing")]
    [Arguments("external-identity", "grant-option")]
    [Arguments("external-identity", "partial-upgrade")]
    public async Task UnknownInstalledVisitorWriterDriftCannotBeRepaired(string profile, string attack)
    {
        var topology = profile == "colocated" ? IdentityDatabaseTopology.Colocated : IdentityDatabaseTopology.External;
        var store = profile == "external-identity" ? LocalIdentityWriterInventory.Store.Identity : LocalIdentityWriterInventory.Store.Application;
        await using var fixture = new NativeFixture(topology);
        await fixture.InitializeAsync();
        var (migratorOptions, writerOptions) = await SelectMigrationDatabaseAsync(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator),
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter), store, CancellationToken.None);
        var enrollment = new PostgresLocalIdentityWriterEnrollment(migratorOptions.Username!,
            fixture.NativeQueueEnrollment.RuntimeLogin, writerOptions.Username!,
            store == LocalIdentityWriterInventory.Store.Application ? fixture.NativeQueueEnrollment.GlobalLogin : null);
        await using var migrator = CreateContext(migratorOptions, store);
        await migrator.Database.OpenConnectionAsync();
        if (store == LocalIdentityWriterInventory.Store.Identity) await migrator.Database.MigrateAsync();
        await EnsureDeclaredTablesAsync(migrator, topology, store);
        if (store == LocalIdentityWriterInventory.Store.Application)
            await PostgresTenantRowLevelSecurityModel.ApplyAsync((ExploreDbContext)migrator);
        await PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment, topology, store);
        await RestorePreVisitorProfileAsync(migrator, enrollment, topology, store);
        var target = VisitorUpgradeRelation(migrator,
            store == LocalIdentityWriterInventory.Store.Identity ? typeof(LocalIdentityUser) : typeof(User));
        string id = target.Columns["Id"];
        string login = Identifier(enrollment.WriterLogin);
        string mutation = attack switch
        {
            "missing" => $"REVOKE SELECT ({id}) ON {target.Table} FROM {login}",
            "grant-option" => $"GRANT SELECT ({id}) ON {target.Table} TO {login} WITH GRANT OPTION",
            "partial-upgrade" => store == LocalIdentityWriterInventory.Store.Application
                ? $"GRANT INSERT ({VisitorUpgradeRelation(migrator, typeof(TenantUser)).Columns["Id"]}) ON {VisitorUpgradeRelation(migrator, typeof(TenantUser)).Table} TO {login}"
                : $"GRANT DELETE ON {VisitorUpgradeRelation(migrator, typeof(LocalIdentityUser)).Table} TO {login}",
            "introduced-policy" => $"CREATE POLICY unexpected_visitor_writer ON {VisitorUpgradeRelation(migrator, typeof(VisitorEnrollmentOperation)).Table} FOR SELECT TO {login} USING (true)",
            _ => throw new ArgumentOutOfRangeException(nameof(attack))
        };
        await using (var mutate = Command(migrator, mutation)) await mutate.ExecuteNonQueryAsync();
        string attacked = await CatalogueImageAsync(migrator, topology, store);
        string counts = await RowCountsAsync(migrator, topology, store);
        await using var writer = CreateContext(writerOptions, store);
        await writer.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresLocalIdentityWriterPermissions.RequireCurrentAsync(writer, enrollment, topology, store));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment, topology, store));
        await Assert.That(failure.Message).IsEqualTo("local_identity_writer_permissions_drift");
        await Assert.That(await CatalogueImageAsync(migrator, topology, store)).IsEqualTo(attacked);
        await Assert.That(await RowCountsAsync(migrator, topology, store)).IsEqualTo(counts);
    }

    /// <summary>
    /// Reconstructs the installed 605c3b2 ACLs after model-generated schema expansion.
    /// The historical delta is explicit data, independent of the installer's predecessor matcher.
    /// </summary>
    private static async Task RestorePreVisitorProfileAsync(DbContext context,
        PostgresLocalIdentityWriterEnrollment enrollment, IdentityDatabaseTopology topology,
        LocalIdentityWriterInventory.Store store)
    {
        string login = Identifier(enrollment.WriterLogin);
        if (topology == IdentityDatabaseTopology.Colocated || store == LocalIdentityWriterInventory.Store.Identity)
        {
            foreach (Type type in new[] { typeof(LocalIdentityUser), typeof(LocalIdentityCredentialOperation),
                         typeof(IdentityUserToken<Guid>), typeof(LocalIdentityEligibility) })
            {
                await using var revoke = Command(context,
                    $"REVOKE DELETE ON {VisitorUpgradeRelation(context, type).Table} FROM {login}");
                await revoke.ExecuteNonQueryAsync();
            }
            var credential = VisitorUpgradeRelation(context, typeof(LocalIdentityCredentialOperation));
            foreach (string privilege in new[] { "SELECT", "INSERT" })
            {
                await using var revoke = Command(context,
                    $"REVOKE {privilege} ({credential.Columns["VisitorEnrollmentOperationId"]}) ON {credential.Table} FROM {login}");
                await revoke.ExecuteNonQueryAsync();
            }
            foreach (Type type in new[] { typeof(IdentityUserClaim<Guid>), typeof(IdentityUserLogin<Guid>) })
            {
                var relation = VisitorUpgradeRelation(context, type);
                await using var revoke = Command(context,
                    $"REVOKE SELECT ({relation.Columns["UserId"]}) ON {relation.Table} FROM {login}");
                await revoke.ExecuteNonQueryAsync();
            }
        }
        if (store != LocalIdentityWriterInventory.Store.Application) return;
        var receipt = VisitorUpgradeRelation(context, typeof(VisitorEnrollmentOperation));
        string[] receiptProperties = ["Id", "TenantId", "LocalSubjectId", "PersonalActorId", "ExternalLoginId",
            "CredentialOperationId", "CredentialGeneration", "TenantUserId", "IntentDigest", "Stage", "Generation",
            "CreatedAt", "ExpiresAt", "UpdatedAt", "CleanupCompletedAt"];
        foreach (var (privilege, properties) in new[]
        {
            ("SELECT", receiptProperties), ("INSERT", receiptProperties),
            ("UPDATE", new[] { "CredentialGeneration", "TenantUserId", "Stage", "Generation", "UpdatedAt", "CleanupCompletedAt" })
        })
        {
            await using var revoke = Command(context,
                $"REVOKE {privilege} ({string.Join(',', properties.Select(property => receipt.Columns[property]))}) ON {receipt.Table} FROM {login}");
            await revoke.ExecuteNonQueryAsync();
        }
        foreach (string policy in new[] { "local_identity_writer_read", "local_identity_writer_read_boundary" })
        {
            await using var drop = Command(context, $"DROP POLICY {Identifier(policy)} ON {receipt.Table}");
            await drop.ExecuteNonQueryAsync();
        }
        var membership = VisitorUpgradeRelation(context, typeof(TenantUser));
        string[] oldReads = ["Id", "TenantId", "UserId", "StatusId", "IsDeleted"];
        string[] membershipProperties = ["Id", "TenantId", "UserId", "ActorId", "StatusId", "JoinedAt", "SuspendedAt",
            "SuspendedBy", "BanExpiresAt", "RemovedAt", "RemovedBy", "ModerationNote", "CreatedAt", "CreatedBy",
            "UpdatedAt", "UpdatedBy", "IsDeleted", "DeletedAt", "DeletedBy"];
        foreach (var (privilege, properties) in new[]
        {
            ("INSERT", membershipProperties),
            ("SELECT", membershipProperties.Except(oldReads, StringComparer.Ordinal).ToArray())
        })
        {
            await using var revoke = Command(context,
                $"REVOKE {privilege} ({string.Join(',', properties.Select(property => membership.Columns[property]))}) ON {membership.Table} FROM {login}");
            await revoke.ExecuteNonQueryAsync();
        }
    }

    private static async Task ProbeVisitorWriterOperationsAsync(DbContext writer,
        IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store)
    {
        if (topology == IdentityDatabaseTopology.Colocated || store == LocalIdentityWriterInventory.Store.Identity)
        {
            var credential = VisitorUpgradeRelation(writer, typeof(LocalIdentityCredentialOperation));
            await using (var insert = Command(writer,
                $"INSERT INTO {credential.Table} ({credential.Columns["VisitorEnrollmentOperationId"]}) SELECT NULL::uuid WHERE false"))
                await insert.ExecuteNonQueryAsync();
            foreach (Type type in new[] { typeof(LocalIdentityUser), typeof(LocalIdentityCredentialOperation),
                         typeof(IdentityUserToken<Guid>), typeof(LocalIdentityEligibility) })
            {
                await using var delete = Command(writer, $"DELETE FROM {VisitorUpgradeRelation(writer, type).Table} WHERE false");
                await delete.ExecuteNonQueryAsync();
            }
            foreach (Type type in new[] { typeof(IdentityUserClaim<Guid>), typeof(IdentityUserLogin<Guid>) })
            {
                var relation = VisitorUpgradeRelation(writer, type);
                await using (var read = Command(writer, $"SELECT {relation.Columns["UserId"]} FROM {relation.Table} WHERE false"))
                    await read.ExecuteScalarAsync();
                await using var write = Command(writer,
                    $"UPDATE {relation.Table} SET {relation.Columns["UserId"]}=NULL WHERE false");
                var denied = await Assert.ThrowsAsync<PostgresException>(async () => await write.ExecuteNonQueryAsync());
                await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        }
        if (store != LocalIdentityWriterInventory.Store.Application) return;
        foreach (Type type in new[] { typeof(VisitorEnrollmentOperation), typeof(TenantUser) })
        {
            var relation = VisitorUpgradeRelation(writer, type);
            await using var insert = Command(writer,
                $"INSERT INTO {relation.Table} ({relation.Columns["Id"]}) SELECT NULL::uuid WHERE false");
            await insert.ExecuteNonQueryAsync();
        }
        var receipt = VisitorUpgradeRelation(writer, typeof(VisitorEnrollmentOperation));
        await using (var update = Command(writer,
            $"UPDATE {receipt.Table} SET {receipt.Columns["Stage"]}={receipt.Columns["Stage"]} WHERE false"))
            await update.ExecuteNonQueryAsync();
        var membership = VisitorUpgradeRelation(writer, typeof(TenantUser));
        await using var forbidden = Command(writer,
            $"UPDATE {membership.Table} SET {membership.Columns["StatusId"]}={membership.Columns["StatusId"]} WHERE false");
        var failure = await Assert.ThrowsAsync<PostgresException>(async () => await forbidden.ExecuteNonQueryAsync());
        await Assert.That(failure.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static (string Table, Dictionary<string, string> Columns) VisitorUpgradeRelation(DbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        var mapped = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = context.GetService<ISqlGenerationHelper>();
        return (names.DelimitIdentifier(mapped.Name, mapped.Schema), entity.GetProperties().ToDictionary(
            property => property.Name, property => names.DelimitIdentifier(property.GetColumnName(mapped)!),
            StringComparer.Ordinal));
    }
}
