using Explore.Persistence.Services;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Exceptions;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rows = Event.Persistence.IntegrationTests.TenantIsolation.PostgresAuthenticationLookupAuthorityTests.AuthenticationRows;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresInstanceApiKeyCapabilityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task MetadataExecutorReadsOnlyRedactedMetadataAndCannotWriteOrReadTables()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await InstallAndGrantAdministrator(rows.UserId);
        await using var role = await Executor.CreateAsync(fixture, rows.RuntimeOptions,
            PrimaryDatabaseRole.InstanceKeyRead, PostgresInstanceApiKeyManagementContract.ReadExecutor);
        await using var db = role.Context();
        await new InstanceApiKeyCapabilitySql(db).RequireReadyAsync(
            PostgresInstanceApiKeyManagementContract.ReadExecutor,
            PostgresInstanceApiKeyManagementContract.Functions(db).Where(
                function => function.Executor == PostgresInstanceApiKeyManagementContract.ReadExecutor).ToArray(), default,
            PlatformApiKeyNativeSourceTests.Enrollment(fixture));
        await Assert.ThrowsAsync<PostgresException>(() => db.ExternalApiKeys.IgnoreQueryFilters().CountAsync());
        await using var store = new InstanceApiKeyCapabilityStore(
            db, PrimaryDatabaseRole.InstanceKeyRead, rows.UserId, rows.PlatformKey.Id, null, new HttpContextAccessor());

        var metadata = await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(token => store.ReadAsync(token));

        await Assert.That(metadata.Single().Id).IsEqualTo(rows.PlatformKey.Id);
        await Assert.That(metadata.Single().SecretHash).IsEqualTo(string.Empty);
        await Assert.That(metadata.Single().LastUsedIp).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
            {
                await store.UpdatePolicyAsync("forbidden", "events:read", null, DateTime.UtcNow, token);
                return true;
            }));
    }

    [Test]
    public async Task FixedPolicyWritePreservesCredentialAndOwnershipAndTreatsNamesAsValues()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await InstallAndGrantAdministrator(rows.UserId);
        await using var role = await Executor.CreateAsync(fixture, rows.RuntimeOptions,
            PrimaryDatabaseRole.InstanceKeyWrite, PostgresInstanceApiKeyManagementContract.WriteExecutor);
        await using var db = role.Context();
        await using var store = new InstanceApiKeyCapabilityStore(
            db, PrimaryDatabaseRole.InstanceKeyWrite, rows.UserId, rows.PlatformKey.Id, null, new HttpContextAccessor());
        const string name = "O'Brien; DROP TABLE external_api_keys; --";

        await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
        {
            await store.UpdatePolicyAsync(name, "events:read", rows.PlatformKey.ExpiresAt, DateTime.UtcNow, token);
            return true;
        });

        await using var verify = fixture.CreateDbContext();
        var key = await verify.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == rows.PlatformKey.Id);
        await Assert.That(key.Name).IsEqualTo(name);
        await Assert.That(key.SecretHash).IsEqualTo(rows.PlatformKey.SecretHash);
        await Assert.That(key.OwnerId).IsEqualTo(rows.PlatformKey.OwnerId);
        await Assert.That(key.TenantId).IsNull();
        await Assert.That(key.KeyId).IsEqualTo(rows.PlatformKey.KeyId);
        await Assert.That(await verify.ExternalApiKeys.AnyAsync(row => row.Id == rows.KeyA.Id)).IsTrue();
    }

    [Test]
    public async Task IssuanceRecoveryBindsDigestAndRejectsRevokedCurrentAdministration()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        Guid grantId = await InstallAndGrantAdministrator(rows.UserId);
        await using var role = await Executor.CreateAsync(fixture, rows.RuntimeOptions,
            PrimaryDatabaseRole.InstanceKeyIssuance, PostgresInstanceApiKeyIssuanceContract.ExecutorRole);
        await using var db = role.Context();
        string operation = Guid.CreateVersion7().ToString("N");
        DateTime createdAt = DateTime.UtcNow;
        var key = new ExternalApiKey
        {
            Id = Guid.CreateVersion7(),
            Name = "Native issuance",
            KeyId = ApiKeyHashing.CreateKeyId(),
            SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
            Scopes = "events:read",
            OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
            OwnerId = rows.UserId,
            ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
            ExternalApiKeyCreditPeriod = null!,
            CreatedAt = createdAt,
            CreatedBy = rows.UserId
        };
        string fingerprint = ExternalApiKeyIssuanceReceipt.ComputeOperationFingerprint(
            rows.UserId, null, ExternalApiKeyOwnerType.InstanceAdmin, rows.UserId, operation);
        string digest = Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 2, 3 }));
        var receipt = ExternalApiKeyIssuanceReceipt.Create(Guid.CreateVersion7(), fingerprint, digest, null, key.Id, createdAt);
        await using var store = new InstanceApiKeyCapabilityStore(
            db, PrimaryDatabaseRole.InstanceKeyIssuance, rows.UserId, null, operation, new HttpContextAccessor());

        await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
        {
            await store.CreateAsync(receipt, key, token);
            return true;
        });
        await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
        {
            var found = await store.FindAsync(fingerprint, null, token);
            await Assert.That(found!.ExternalApiKeyId).IsEqualTo(key.Id);
            var recovered = await store.GetIssuedKeyAsync(key.Id, null, ExternalApiKeyOwnerType.InstanceAdmin, rows.UserId, token);
            await Assert.That(recovered!.SecretHash).IsEqualTo(string.Empty);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.FindAsync(new string('a', 64), null, token));
            return true;
        });

        await using (var revoke = fixture.CreateDbContext())
            await revoke.PlatformUserRoles.Where(row => row.Id == grantId).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            new EfCoreUnitOfWork(db).ExecuteSerializableAsync(token => store.IsAuthorizedForCommitAsync(token)));
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.ExternalApiKeyIssuanceReceipts.CountAsync(
            row => row.OperationFingerprint == fingerprint)).IsEqualTo(1);
    }

    [Test]
    public async Task HistoricalReportIncludesInactiveTenantAndInstanceAggregatesWithoutLedgerAccess()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await InstallAndGrantAdministrator(rows.UserId);
        DateOnly from = new(2026, 1, 1);
        DateOnly to = new(2026, 1, 31);
        Guid inactiveKeyId = Guid.CreateVersion7();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = inactiveKeyId,
                TenantId = rows.Inactive,
                OwnerType = ExternalApiKeyOwnerType.Tenant,
                OwnerId = rows.Inactive,
                Name = "Historical inactive key",
                KeyId = ApiKeyHashing.CreateKeyId(),
                SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
                Scopes = "events:read",
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                ExternalApiKeyCreditPeriod = null!
            });
            foreach (var (id, requests) in new[] { (rows.KeyA.Id, 2L), (rows.KeyB.Id, 3L), (rows.PlatformKey.Id, 5L), (inactiveKeyId, 7L) })
                seed.ExternalApiKeyQuotas.Add(new ExternalApiKeyQuota
                {
                    Id = Guid.CreateVersion7(),
                    ExternalApiKeyId = id,
                    ExternalApiKey = null!,
                    PeriodStart = from,
                    PeriodEnd = to,
                    RequestCount = requests,
                    CreditsUsed = 1
                });
            await seed.SaveChangesAsync();
        }
        await using var role = await Executor.CreateAsync(fixture, rows.RuntimeOptions,
            PrimaryDatabaseRole.PlatformKeyReport, PostgresPlatformApiKeyUsageReportContract.ExecutorRole);
        await using var db = role.Context();
        await using var store = new InstanceApiKeyCapabilityStore(
            db, PrimaryDatabaseRole.PlatformKeyReport, rows.UserId, null, null, new HttpContextAccessor());

        var report = await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(token => store.ReportAsync(from, to, token));

        await Assert.That(report.Count).IsEqualTo(4);
        await Assert.That(report.Sum(row => row.TotalRequestCount)).IsEqualTo(17);
        await Assert.That(report.Single(row => row.ApiKeyId == inactiveKeyId).TenantId).IsEqualTo(rows.Inactive);
        await Assert.That(report.Single(row => row.ApiKeyId == rows.PlatformKey.Id).TenantId).IsNull();
        await Assert.ThrowsAsync<PostgresException>(() => db.ExternalApiKeyQuotas.IgnoreQueryFilters().CountAsync());
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.InstanceKeyRead)]
    [Arguments(PrimaryDatabaseRole.InstanceKeyWrite)]
    [Arguments(PrimaryDatabaseRole.InstanceKeyIssuance)]
    [Arguments(PrimaryDatabaseRole.PlatformKeyReport)]
    public async Task EveryDedicatedExecutorRejectsCommittedAdministratorRevocation(PrimaryDatabaseRole role)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        Guid grantId = await InstallAndGrantAdministrator(rows.UserId);
        string executor = role switch
        {
            PrimaryDatabaseRole.InstanceKeyRead => PostgresInstanceApiKeyManagementContract.ReadExecutor,
            PrimaryDatabaseRole.InstanceKeyWrite => PostgresInstanceApiKeyManagementContract.WriteExecutor,
            PrimaryDatabaseRole.InstanceKeyIssuance => PostgresInstanceApiKeyIssuanceContract.ExecutorRole,
            PrimaryDatabaseRole.PlatformKeyReport => PostgresPlatformApiKeyUsageReportContract.ExecutorRole,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        await using var login = await Executor.CreateAsync(fixture, rows.RuntimeOptions, role, executor);
        await using var db = login.Context();
        await using var store = new InstanceApiKeyCapabilityStore(
            db, role, rows.UserId, rows.PlatformKey.Id, Guid.CreateVersion7().ToString("N"), new HttpContextAccessor());
        await using (var revoke = fixture.CreateDbContext())
            await revoke.PlatformUserRoles.Where(grant => grant.Id == grantId).ExecuteDeleteAsync();

        await Assert.ThrowsAsync<AuthorizationException>(() => new EfCoreUnitOfWork(db).ExecuteSerializableAsync(
            async token =>
            {
                switch (role)
                {
                    case PrimaryDatabaseRole.InstanceKeyRead:
                        await store.ReadAsync(token);
                        break;
                    case PrimaryDatabaseRole.InstanceKeyWrite:
                        await store.RevokeAsync(DateTime.UtcNow, token);
                        break;
                    case PrimaryDatabaseRole.InstanceKeyIssuance:
                        await store.IsAuthorizedForCommitAsync(token);
                        break;
                    case PrimaryDatabaseRole.PlatformKeyReport:
                        await store.ReportAsync(new(2026, 1, 1), new(2026, 1, 31), token);
                        break;
                }
                return true;
            }));
        await using var verify = fixture.CreateDbContext();
        await Assert.That((await verify.ExternalApiKeys.AsNoTracking().SingleAsync(key => key.Id == rows.PlatformKey.Id))
            .ExternalApiKeyStatusId).IsEqualTo(rows.PlatformKey.ExternalApiKeyStatusId);
    }

    [Test]
    public async Task ReadExecutorCannotBeAdmittedAsWriteExecutor()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await InstallAndGrantAdministrator(rows.UserId);
        await using var role = await Executor.CreateAsync(fixture, rows.RuntimeOptions,
            PrimaryDatabaseRole.InstanceKeyRead, PostgresInstanceApiKeyManagementContract.ReadExecutor);
        await using var db = role.Context();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InstanceApiKeyCapabilitySql(db).RequireReadyAsync(
                PostgresInstanceApiKeyManagementContract.WriteExecutor,
                PostgresInstanceApiKeyManagementContract.Functions(db)
                    .Where(function => function.Executor == PostgresInstanceApiKeyManagementContract.WriteExecutor).ToArray(),
                default, PlatformApiKeyNativeSourceTests.Enrollment(fixture)));
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, false)]
    [Arguments(false, true, true)]
    [Arguments(true, false, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public async Task DedicatedReadOrReportAdmissionRejectsFrozenAuthenticationContamination(
        bool report, bool scanner, bool directGrant)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await InstallAndGrantAdministrator(rows.UserId);
        string executor = report ? PostgresPlatformApiKeyUsageReportContract.ExecutorRole
            : PostgresInstanceApiKeyManagementContract.ReadExecutor;
        await using var login = await Executor.CreateAsync(fixture, rows.RuntimeOptions,
            report ? PrimaryDatabaseRole.PlatformKeyReport : PrimaryDatabaseRole.InstanceKeyRead, executor);
        await using var db = login.Context();
        InstanceApiKeySqlFunction[] functions = report
            ? [PostgresPlatformApiKeyUsageReportContract.Function(db)]
            : PostgresInstanceApiKeyManagementContract.Functions(db).Where(function => function.Executor == executor).ToArray();
        var admission = new InstanceApiKeyCapabilitySql(db);
        await admission.RequireReadyAsync(executor, functions, default, PlatformApiKeyNativeSourceTests.Enrollment(fixture));
        string contaminatedRole = scanner ? PostgresAdmissionScannerAuthenticationContract.GlobalRole
            : PostgresExternalApiKeyAuthenticationContract.GlobalRole;
        string signature = scanner
            ? PostgresAdmissionScannerAuthenticationContract.FunctionSql(db) + "(integer[], text[])"
            : PostgresExternalApiKeyAuthenticationContract.FunctionSql(db) + "(text)";

        await login.GrantContaminationAsync(contaminatedRole, signature, directGrant);

        await Assert.That(await db.Database.SqlQueryRaw<bool>(
            "SELECT pg_catalog.has_function_privilege(current_user, pg_catalog.to_regprocedure(@signature), 'EXECUTE') AS \"Value\"",
            new NpgsqlParameter("signature", signature)).SingleAsync()).IsTrue();
        await Assert.ThrowsAsync<InvalidOperationException>(() => admission.RequireReadyAsync(
            executor, functions, default, PlatformApiKeyNativeSourceTests.Enrollment(fixture)));
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.InstanceKeyRead, PrimaryDatabaseRole.InstanceKeyWrite)]
    [Arguments(PrimaryDatabaseRole.InstanceKeyRead, PrimaryDatabaseRole.InstanceKeyIssuance)]
    [Arguments(PrimaryDatabaseRole.InstanceKeyRead, PrimaryDatabaseRole.PlatformKeyReport)]
    [Arguments(PrimaryDatabaseRole.PlatformKeyReport, PrimaryDatabaseRole.InstanceKeyRead)]
    [Arguments(PrimaryDatabaseRole.PlatformKeyReport, PrimaryDatabaseRole.InstanceKeyWrite)]
    [Arguments(PrimaryDatabaseRole.PlatformKeyReport, PrimaryDatabaseRole.InstanceKeyIssuance)]
    public async Task DedicatedAdmissionRejectsDirectExecutionOfAnotherNewPurpose(
        PrimaryDatabaseRole selected, PrimaryDatabaseRole contaminated)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await InstallAndGrantAdministrator(rows.UserId);
        string executor = selected == PrimaryDatabaseRole.PlatformKeyReport
            ? PostgresPlatformApiKeyUsageReportContract.ExecutorRole : PostgresInstanceApiKeyManagementContract.ReadExecutor;
        await using var login = await Executor.CreateAsync(fixture, rows.RuntimeOptions, selected, executor);
        await using var db = login.Context();
        InstanceApiKeySqlFunction[] admitted = selected == PrimaryDatabaseRole.PlatformKeyReport
            ? [PostgresPlatformApiKeyUsageReportContract.Function(db)]
            : PostgresInstanceApiKeyManagementContract.Functions(db).Where(function => function.Executor == executor).ToArray();
        InstanceApiKeySqlFunction forbidden = contaminated switch
        {
            PrimaryDatabaseRole.InstanceKeyRead => PostgresInstanceApiKeyManagementContract.Functions(db)
                .Single(function => function.Name == PostgresInstanceApiKeyManagementContract.ReadFunction),
            PrimaryDatabaseRole.InstanceKeyWrite => PostgresInstanceApiKeyManagementContract.Functions(db)
                .Single(function => function.Name == PostgresInstanceApiKeyManagementContract.RevokeFunction),
            PrimaryDatabaseRole.InstanceKeyIssuance => PostgresInstanceApiKeyIssuanceContract.Functions(db)
                .Single(function => function.Name == PostgresInstanceApiKeyIssuanceContract.CreateFunction),
            PrimaryDatabaseRole.PlatformKeyReport => PostgresPlatformApiKeyUsageReportContract.Function(db),
            _ => throw new ArgumentOutOfRangeException(nameof(contaminated))
        };
        var admission = new InstanceApiKeyCapabilitySql(db);
        await admission.RequireReadyAsync(executor, admitted, default, PlatformApiKeyNativeSourceTests.Enrollment(fixture));
        string signature = admission.Function(forbidden.Name) + "(" + forbidden.ArgumentTypes + ")";

        await login.GrantContaminationAsync(forbidden.Executor, signature, directGrant: true);

        await Assert.That(await db.Database.SqlQueryRaw<bool>(
            "SELECT pg_catalog.has_function_privilege(current_user, pg_catalog.to_regprocedure(@signature), 'EXECUTE') AS \"Value\"",
            new NpgsqlParameter("signature", signature)).SingleAsync()).IsTrue();
        await Assert.ThrowsAsync<InvalidOperationException>(() => admission.RequireReadyAsync(
            executor, admitted, default, PlatformApiKeyNativeSourceTests.Enrollment(fixture)));
    }

    private async Task<Guid> InstallAndGrantAdministrator(Guid userId)
    {
        await using var db = fixture.CreateDbContext();
        await PostgresInstanceApiKeyManagementContract.ApplyAsync(db);
        await PostgresInstanceApiKeyIssuanceContract.ApplyAsync(db);
        await PostgresPlatformApiKeyUsageReportContract.ApplyAsync(db);
        await PostgresPlatformApiKeySourceContract.ApplyAsync(db, PlatformApiKeyNativeSourceTests.Enrollment(fixture));
        Guid id = Guid.CreateVersion7();
        db.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = id,
            UserId = userId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class Executor(
        PostgreSqlContainerFixture fixture, string login, string executor, PrimaryDatabaseConnectionOptions options)
        : IAsyncDisposable
    {
        private string? _contaminationRevocation;

        internal async Task GrantContaminationAsync(string roleName, string signature, bool directGrant)
        {
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var command = administration.CreateCommand();
            command.CommandText = directGrant
                ? $"GRANT EXECUTE ON FUNCTION {signature} TO \"{login}\";"
                : $"GRANT {roleName} TO \"{login}\" WITH INHERIT TRUE, SET FALSE;";
            await command.ExecuteNonQueryAsync();
            _contaminationRevocation = directGrant
                ? $"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{login}\";"
                : $"REVOKE {roleName} FROM \"{login}\";";
        }

        internal static async Task<Executor> CreateAsync(
            PostgreSqlContainerFixture fixture, PrimaryDatabaseConnectionOptions runtime,
            PrimaryDatabaseRole role, string executor)
        {
            string login = $"instance_key_{Guid.CreateVersion7():N}";
            string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var command = administration.CreateCommand();
            command.CommandText = $"""
                CREATE ROLE "{login}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
                GRANT {executor} TO "{login}" WITH INHERIT TRUE, SET FALSE;
                """;
            await command.ExecuteNonQueryAsync();
            return new(fixture, login, executor, runtime with { Role = role, Username = login, Password = password });
        }

        internal ExploreDbContext Context()
        {
            var builder = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
            builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            var context = new ExploreDbContext(builder.Options);
            context.BindTenantLease();
            return context;
        }

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var command = administration.CreateCommand();
            command.CommandText = $"{_contaminationRevocation} REVOKE {executor} FROM \"{login}\"; DROP ROLE \"{login}\";";
            await command.ExecuteNonQueryAsync();
        }
    }
}
