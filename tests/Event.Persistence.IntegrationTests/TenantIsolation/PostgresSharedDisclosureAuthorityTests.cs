using System.Data;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresSharedDisclosureAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("dependencies")]
    [Arguments("finalize")]
    public async Task DiscoveryRejectsUnknownExecutorWithoutRemovingDeclaredNestedGrants(string operation)
    {
        await using var migrator = fixture.CreateDbContext();
        await using var outsider = await PostgresTenantApplicationTableIsolationTests.GlobalLookupAuthority.CreateAsync(fixture);
        string function = PostgresDiscoveryDisclosureContract.FunctionSql(migrator, operation);
        string signature = function + (operation == "dependencies"
            ? "(text, uuid[])" : "(uuid[], uuid[], boolean[], bigint[], bigint[])");
        string invocation = operation == "dependencies"
            ? $"SELECT * FROM {function}('record', ARRAY[]::uuid[])"
            : $"SELECT {function}(ARRAY[]::uuid[], ARRAY[]::uuid[], ARRAY[]::boolean[], ARRAY[]::bigint[], ARRAY[]::bigint[])";
        await migrator.Database.ExecuteSqlRawAsync($"GRANT EXECUTE ON FUNCTION {signature} TO \"{outsider.Role}\"");
        try
        {
            await using var rejected = await outsider.Factory.CreateDbContextAsync();
            PostgresException entry = await Assert.ThrowsAsync<PostgresException>(async () =>
                await rejected.Database.ExecuteSqlRawAsync(invocation));
            await Assert.That(entry.SqlState).IsEqualTo("55000");
            PostgresException reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresDiscoveryDisclosureContract.ApplyAsync(migrator));
            await Assert.That(reinstall.SqlState).IsEqualTo("55000");
            await Assert.That(await migrator.Database.SqlQueryRaw<bool>(
                $"SELECT pg_catalog.has_function_privilege('{outsider.Role}', '{signature}', 'EXECUTE') AS \"Value\"")
                .SingleAsync()).IsTrue();
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync($"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{outsider.Role}\"");
        }
        await PostgresDiscoveryDisclosureContract.ApplyAsync(migrator);
        string[] nestedRoles = operation == "dependencies"
            ? [PostgresAtprotoMaterializationContract.OwnerRole,
                Explore.Persistence.Privacy.ErasureAuthority.PostgresSubjectPrivacyErasureContract.OwnerRole]
            : [PostgresStorageReconciliationContract.OwnerRole,
                Explore.Persistence.Privacy.ErasureAuthority.PostgresSubjectPrivacyErasureContract.GlobalRole];
        foreach (string role in nestedRoles)
        {
            await Assert.That(await migrator.Database.SqlQueryRaw<bool>(
                $"SELECT pg_catalog.has_function_privilege('{role}', '{signature}', 'EXECUTE') AS \"Value\"")
                .SingleAsync()).IsTrue();
            await using var transaction = await migrator.Database.BeginTransactionAsync();
            await migrator.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {role}");
            await migrator.Database.ExecuteSqlRawAsync(invocation);
            await transaction.RollbackAsync();
        }
    }

    [Test]
    [Arguments("lookup", "login_owner")]
    [Arguments("lookup", "foreign_ownership")]
    [Arguments("lookup", "elevated_parent")]
    [Arguments("email", "login_owner")]
    [Arguments("email", "foreign_ownership")]
    [Arguments("email", "elevated_parent")]
    public async Task RelatedInstallerRejectsUnsafeManagedAuthority(string capability, string contamination)
    {
        string owner = capability == "lookup"
            ? PostgresTenantLookupContract.OwnerRole : PostgresEmailDispatchAdmissionContract.OwnerRole;
        string executor = capability == "lookup"
            ? PostgresTenantLookupContract.GlobalRole : PostgresEmailDispatchAdmissionContract.GlobalRole;
        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        string identity = $"related_adversarial_{Guid.CreateVersion7():N}";
        try
        {
            string install = capability == "lookup"
                ? PostgresTenantLookupContract.BuildSql(context)
                : PostgresEmailDispatchAdmissionContract.BuildSql(context);
            await context.Database.ExecuteSqlRawAsync(install);
            string contaminate = contamination switch
            {
                "login_owner" => $"ALTER ROLE {owner} LOGIN",
                "foreign_ownership" => $"CREATE SCHEMA \"{identity}\" AUTHORIZATION {owner}",
                "elevated_parent" => $"""
                    CREATE ROLE "{identity}" NOLOGIN BYPASSRLS;
                    GRANT "{identity}" TO {executor} WITH INHERIT TRUE, SET TRUE;
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(contamination))
            };
            await context.Database.ExecuteSqlRawAsync(contaminate);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(install));
            await Assert.That(rejected?.SqlState).IsEqualTo("42501");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Test]
    [Arguments("lookup")]
    [Arguments("email")]
    public async Task RelatedInstallerRejectsUnrelatedExecutionGroupTablePrivileges(string capability)
    {
        string executor = capability == "lookup"
            ? PostgresTenantLookupContract.GlobalRole : PostgresEmailDispatchAdmissionContract.GlobalRole;
        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        string identity = $"unrelated_privilege_{Guid.CreateVersion7():N}";
        try
        {
            string install = capability == "lookup"
                ? PostgresTenantLookupContract.BuildSql(context)
                : PostgresEmailDispatchAdmissionContract.BuildSql(context);
            await context.Database.ExecuteSqlRawAsync(install);
            await context.Database.ExecuteSqlRawAsync($"""
                CREATE SCHEMA "{identity}";
                CREATE TABLE "{identity}".sentinel (marker integer NOT NULL);
                INSERT INTO "{identity}".sentinel VALUES (42);
                GRANT USAGE ON SCHEMA "{identity}" TO {executor};
                GRANT SELECT ON "{identity}".sentinel TO {executor};
                SET LOCAL ROLE {executor};
                """);
            await using (var command = context.Database.GetDbConnection().CreateCommand())
            {
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = $"SELECT marker FROM \"{identity}\".sentinel";
                await using var reader = await command.ExecuteReaderAsync();
                await Assert.That(await reader.ReadAsync()).IsTrue();
                await Assert.That(reader.GetInt32(0)).IsEqualTo(42);
            }
            await context.Database.ExecuteSqlRawAsync("RESET ROLE");
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(install));
            await Assert.That(rejected?.SqlState).IsEqualTo("42501");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Test]
    [Arguments("login_owner")]
    [Arguments("foreign_ownership")]
    [Arguments("elevated_parent")]
    public async Task InstallerRejectsUnsafeManagedRoleAuthorityWithoutRepairingIt(string contamination)
    {
        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        string identity = $"disclosure_adversarial_{Guid.CreateVersion7():N}";
        try
        {
            string contaminate = contamination switch
            {
                "login_owner" => $"ALTER ROLE {PostgresDiscoveryDisclosureContract.OwnerRole} LOGIN",
                "foreign_ownership" =>
                    $"CREATE SCHEMA \"{identity}\" AUTHORIZATION {PostgresDiscoveryDisclosureContract.OwnerRole}",
                "elevated_parent" => $"""
                    CREATE ROLE "{identity}" NOLOGIN BYPASSRLS;
                    GRANT "{identity}" TO {PostgresDiscoveryDisclosureContract.RuntimeRole}
                        WITH INHERIT TRUE, SET TRUE;
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(contamination))
            };
            await context.Database.ExecuteSqlRawAsync(contaminate);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(PostgresDiscoveryDisclosureContract.BuildSql(context)));
            await Assert.That(rejected?.SqlState).IsEqualTo("42501");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Test]
    [Arguments(2, false, true)]
    [Arguments(2, true, true)]
    [Arguments(1024, false, true)]
    [Arguments(1025, false, true)]
    [Arguments(2, false, false)]
    public async Task SharedSourceWriteUsesBoundedAtomicDisclosureAuthority(
        int tenantCount, bool failBeforeCommit, bool capability)
    {
        Guid[] tenants = Enumerable.Range(0, tenantCount).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid recordId = Guid.CreateVersion7();
        string role = $"disclosure_global_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string source = $"https://example.test/{recordId:N}";
        string schema;
        string projectionTable;
        string recordTable;
        string revisionTable;
        string presentationTable;
        await using (var seed = fixture.CreateDbContext())
        {
            await PostgresDiscoveryDisclosureContract.ApplyAsync(seed);
            foreach (Guid[] batch in tenants.Chunk(128))
            {
                seed.Tenants.AddRange(batch.Select(Tenant));
                await seed.SaveChangesAsync();
            }
            seed.AtprotoRecords.Add(new AtprotoRecord
            {
                Id = recordId,
                Did = "did:plc:" + recordId.ToString("N")[..24],
                Collection = "community.lexicon.calendar.event",
                RecordKey = recordId.ToString("N"),
                Direction = AtprotoRecordDirection.Inbound,
                Provenance = AtprotoRecordProvenance.Jetstream,
                SourceVersion = 1,
                UpdatedAt = DateTime.UtcNow
            });
            seed.AtprotoEventProjections.Add(new AtprotoEventProjection
            {
                AtprotoRecordId = recordId,
                Name = $"shared-{recordId:N}",
                SourceVersion = 1,
                SourceUrl = source,
                CreatedAt = DateTimeOffset.UtcNow,
                MaterializedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
            // Seed through real disclosure transactions without exceeding the
            // production batch bound; the one source subsequently owns all rows.
            foreach (Guid[] batch in tenants.Chunk(128))
            {
                foreach (Guid tenant in batch)
                    seed.AtprotoRecordTenantPresentations.Add(new AtprotoRecordTenantPresentation
                    {
                        TenantId = tenant,
                        AtprotoRecordId = recordId,
                        SourceVersion = 1,
                        IsVisible = tenant == tenants[0],
                        EvaluatedAt = DateTime.UtcNow
                    });
                await seed.SaveChangesAsync();
            }
            var projection = seed.Model.FindEntityType(typeof(AtprotoEventProjection))!;
            var revision = seed.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
            schema = projection.GetSchema()!;
            projectionTable = $"\"{schema}\".\"{projection.GetTableName()}\"";
            var record = seed.Model.FindEntityType(typeof(AtprotoRecord))!;
            recordTable = $"\"{record.GetSchema()}\".\"{record.GetTableName()}\"";
            revisionTable = $"\"{revision.GetSchema()}\".\"{revision.GetTableName()}\"";
            var presentation = seed.Model.FindEntityType(typeof(AtprotoRecordTenantPresentation))!;
            presentationTable = $"\"{presentation.GetSchema()}\".\"{presentation.GetTableName()}\"";
            await Assert.That(await seed.AtprotoRecordTenantPresentations.IgnoreQueryFilters()
                .CountAsync(row => row.AtprotoRecordId == recordId)).IsEqualTo(tenantCount);
            await Assert.That(await seed.AtprotoRecordTenantPresentations.IgnoreQueryFilters()
                .CountAsync(row => row.AtprotoRecordId == recordId && !row.IsVisible)).IsEqualTo(tenantCount - 1);
        }
        Dictionary<Guid, long> before;
        await using (var baseline = fixture.CreateDbContext())
        {
            before = await baseline.Set<EventDiscoveryRevision>().AsNoTracking().IgnoreQueryFilters()
                .Where(row => tenants.Contains(row.TenantId))
                .ToDictionaryAsync(row => row.TenantId, row => row.DisclosureEpoch);
        }
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var grant = administration.CreateCommand())
        {
            grant.CommandText = $"""
                CREATE ROLE "{role}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}';
                GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
                GRANT SELECT ON {projectionTable}, {recordTable}, {presentationTable}, {revisionTable} TO "{role}";
                GRANT UPDATE ON {projectionTable} TO "{role}";
                GRANT UPDATE ON {recordTable} TO "{role}";
                """;
            await grant.ExecuteNonQueryAsync();
            if (capability)
            {
                grant.CommandText = $"GRANT {PostgresDiscoveryDisclosureContract.RuntimeRole} TO \"{role}\"";
                await grant.ExecuteNonQueryAsync();
            }
        }
        string connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Username = role,
            Password = password,
            MaxPoolSize = 1
        }.ConnectionString;
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var factory = new GlobalExploreDbContextFactory(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Global,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Username = connection.Username,
            Password = connection.Password,
            Schema = schema,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        }, poolSize: 1);
        string poolConnectionString = connectionString;
        try
        {
            await using var writer = await factory.CreateDbContextAsync();
            poolConnectionString = writer.Database.GetConnectionString()!;
            await writer.Database.OpenConnectionAsync();
            await using (var authority = writer.Database.GetDbConnection().CreateCommand())
            {
                authority.CommandText = """
                    SELECT current_user, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user;
                    """;
                await using var reader = await authority.ExecuteReaderAsync();
                await Assert.That(await reader.ReadAsync()).IsTrue();
                await Assert.That(reader.GetString(0)).IsEqualTo(role);
                await Assert.That(reader.GetBoolean(1)).IsFalse();
                await Assert.That(reader.GetBoolean(2)).IsFalse();
            }
            await using (var acl = writer.Database.GetDbConnection().CreateCommand())
            {
                acl.CommandText = """
                    SELECT has_table_privilege(current_user, @revisions, 'UPDATE'),
                           has_table_privilege(current_user, @presentations, 'UPDATE'),
                           pg_has_role(current_user, @owner, 'MEMBER'),
                           pg_has_role(current_user, @migrator, 'MEMBER');
                    """;
                foreach (var pair in new Dictionary<string, string>
                {
                    ["revisions"] = revisionTable,
                    ["presentations"] = presentationTable,
                    ["owner"] = PostgresDiscoveryDisclosureContract.OwnerRole,
                    ["migrator"] = PostgresDiscoveryDisclosureContract.MigratorRole
                })
                {
                    var parameter = acl.CreateParameter();
                    parameter.ParameterName = pair.Key;
                    parameter.Value = pair.Value;
                    acl.Parameters.Add(parameter);
                }
                await using var reader = await acl.ExecuteReaderAsync();
                await Assert.That(await reader.ReadAsync()).IsTrue();
                for (int index = 0; index < 4; index++)
                    await Assert.That(reader.GetBoolean(index)).IsFalse();
            }
            var shared = await writer.AtprotoEventProjections.SingleAsync(row => row.AtprotoRecordId == recordId);
            await Assert.That(shared.SourceUrl).IsEqualTo(source);
            await Assert.That(await writer.AtprotoRecordTenantPresentations.IgnoreQueryFilters()
                .CountAsync(row => row.AtprotoRecordId == recordId)).IsEqualTo(0);
            shared.SourceUrl = null;

            bool committed = capability && tenantCount <= 1024 && !failBeforeCommit;
            if (!capability || tenantCount > 1024)
            {
                PostgresException? rejected = null;
                try
                {
                    await writer.SaveChangesAsync();
                }
                catch (PostgresException exception)
                {
                    rejected = exception;
                }
                await Assert.That(rejected?.SqlState).IsEqualTo(capability ? "54000" : "42501");
            }
            else if (failBeforeCommit)
            {
                await writer.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                    var injected = new InvalidOperationException("injected_disclosure_precommit_failure");
                    try
                    {
                        await writer.SaveChangesAsync();
                        await writer.FlushDisclosureAsync(CancellationToken.None);
                        throw injected;
                    }
                    catch (InvalidOperationException exception) when (ReferenceEquals(exception, injected))
                    {
                        await transaction.RollbackAsync();
                    }
                });
            }
            else
            {
                await writer.SaveChangesAsync();
            }
            await using var verify = fixture.CreateDbContext();
            await Assert.That(await verify.AtprotoEventProjections
                .Where(row => row.AtprotoRecordId == recordId).Select(row => row.SourceUrl).SingleAsync())
                .IsEqualTo(committed ? null : source);
            var after = await verify.Set<EventDiscoveryRevision>().AsNoTracking().IgnoreQueryFilters()
                .Where(row => tenants.Contains(row.TenantId))
                .ToDictionaryAsync(row => row.TenantId, row => row.DisclosureEpoch);
            await Assert.That(after.Count).IsEqualTo(tenantCount);
            foreach (Guid tenant in tenants)
                await Assert.That(after[tenant]).IsEqualTo(before[tenant] + (committed ? 1 : 0));
        }
        finally
        {
            using var pool = new NpgsqlConnection(poolConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE SELECT ON {projectionTable}, {recordTable}, {presentationTable}, {revisionTable} FROM "{role}";
                REVOKE UPDATE ON {projectionTable} FROM "{role}";
                REVOKE UPDATE ON {recordTable} FROM "{role}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                """;
            await revoke.ExecuteNonQueryAsync();
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AtprotoRecordTenantPresentations
                .Where(row => row.AtprotoRecordId == recordId).ExecuteDeleteAsync();
            await cleanup.AtprotoEventProjections
                .Where(row => row.AtprotoRecordId == recordId).ExecuteDeleteAsync();
            await cleanup.AtprotoRecords.Where(row => row.Id == recordId).ExecuteDeleteAsync();
            await cleanup.Set<EventDiscoveryRevision>().Where(row => tenants.Contains(row.TenantId))
                .ExecuteDeleteAsync();
            await cleanup.Tenants.Where(row => tenants.Contains(row.Id)).ExecuteDeleteAsync();
        }
    }

    private static Tenant Tenant(Guid id) => new()
    {
        Id = id,
        FullName = $"disclosure-{id:N}",
        Slug = $"disclosure-{id:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };
}
