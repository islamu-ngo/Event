using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresAuthenticationLookupAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthenticationKeyLookupDoesNotInheritAbsentOrForeignRequestTenant(bool foreignTenant)
    {
        await using var rows = await AuthenticationRows.CreateAsync(fixture);
        await using var own = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(own);
        await Assert.That(await own.ExternalApiKeys.AnyAsync(key => key.Id == rows.KeyA.Id)).IsTrue();
        await using var request = rows.Context(foreignTenant ? rows.B : null);
        await rows.AssertOrdinaryRoleAsync(request);
        await Assert.That(await request.ExternalApiKeys.IgnoreQueryFilters()
            .AnyAsync(key => key.Id == rows.KeyA.Id)).IsFalse();

        await using var global = await AuthenticationGlobalAuthority.CreateAsync(fixture);
        var repository = new ExternalApiKeyAuthenticationRepository(global.Factory);
        ExternalApiKey? found = await repository.GetByKeyIdForAuthentication(rows.KeyA.KeyId);

        await Assert.That(found?.Id).IsEqualTo(rows.KeyA.Id);
        await Assert.That(found!.TenantId).IsEqualTo(rows.A);
        await Assert.That(found.SecretHash).IsEqualTo(rows.KeyA.SecretHash);
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(foreignTenant ? rows.B : (Guid?)null);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthenticationUsageTouchOwnsOnlyItsExactKeyAndTwoMetadataFields(bool platformKey)
    {
        await using var rows = await AuthenticationRows.CreateAsync(fixture);
        await using var request = rows.Context(rows.B);
        await rows.AssertOrdinaryRoleAsync(request);
        await using var global = await AuthenticationGlobalAuthority.CreateAsync(fixture);
        var repository = new ExternalApiKeyAuthenticationRepository(global.Factory);
        ExternalApiKey key = platformKey ? rows.PlatformKey : rows.KeyA;
        DateTime first = AuthenticationRows.Now;
        TimeSpan interval = TimeSpan.FromMinutes(5);

        bool touched = await repository.TouchUsageMetadata(key.Id, first, "192.0.2.1", interval);

        await Assert.That(touched).IsTrue();
        await Assert.That(await repository.TouchUsageMetadata(
            key.Id, first.AddMinutes(5), "192.0.2.2", interval)).IsFalse();
        await Assert.That(await repository.TouchUsageMetadata(
            key.Id, first.AddMinutes(5).AddSeconds(1), "192.0.2.3", interval)).IsTrue();
        await using var verify = fixture.CreateDbContext();
        ExternalApiKey actual = await verify.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == key.Id);
        await Assert.That(actual.LastUsedAt).IsEqualTo(first.AddMinutes(5).AddSeconds(1));
        await Assert.That(actual.LastUsedIp).IsEqualTo("192.0.2.3");
        await Assert.That(actual.SecretHash).IsEqualTo(key.SecretHash);
        await Assert.That(actual.ExternalApiKeyStatusId).IsEqualTo(key.ExternalApiKeyStatusId);
        await Assert.That(actual.Scopes).IsEqualTo(key.Scopes);
        await Assert.That(actual.OwnerId).IsEqualTo(key.OwnerId);
        await Assert.That(actual.UpdatedAt).IsEqualTo(key.UpdatedAt);
        await Assert.That(await verify.ExternalApiKeys.Where(key => key.Id == rows.KeyB.Id)
            .Select(key => key.LastUsedAt).SingleAsync()).IsNull();
    }

    [Test]
    public async Task ScannerCandidateLookupRetainsVersionDigestPairingAcrossRequestTenants()
    {
        await using var rows = await AuthenticationRows.CreateAsync(fixture);
        await using var own = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(own);
        await Assert.That(await own.AdmissionScannerCapabilities.AnyAsync(row => row.Id == rows.Scanner.Id)).IsTrue();
        await using var request = rows.Context(rows.B);
        await rows.AssertOrdinaryRoleAsync(request);
        await Assert.That(await request.AdmissionScannerCapabilities.IgnoreQueryFilters()
            .AnyAsync(row => row.Id == rows.Scanner.Id)).IsFalse();
        await using var global = await AuthenticationGlobalAuthority.CreateAsync(fixture);
        var repository = new AdmissionScannerAuthenticationRepository(global.Factory);
        string otherDigest = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        AdmissionScannerCapabilityDigestCandidate[] crossed =
        [
            new(rows.Scanner.LookupKeyVersion, otherDigest),
            new(rows.Scanner.LookupKeyVersion + 1, rows.Scanner.LookupDigest)
        ];
        await Assert.That(await repository.FindByDigestCandidatesAsync(crossed, CancellationToken.None)).IsNull();
        AdmissionScannerCapability? found = await repository.FindByDigestCandidatesAsync(
            [new(rows.Scanner.LookupKeyVersion, rows.Scanner.LookupDigest)], CancellationToken.None);

        await Assert.That(found?.Id).IsEqualTo(rows.Scanner.Id);
        await Assert.That(found!.AdmissionTargetId).IsEqualTo(rows.Scanner.AdmissionTargetId);
        await Assert.That(found.Actions).IsEqualTo(rows.Scanner.Actions);
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(rows.B);
    }

    [Test]
    public async Task ScannerAmbiguityAcrossTenantsCannotSelectTheFirstMatchingCredential()
    {
        await using var rows = await AuthenticationRows.CreateAsync(fixture);
        await using (var seed = fixture.CreateDbContext())
        {
            var source = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.B,
                Tenant = null!,
                ActorId = rows.Scanner.IssuedByActorId,
                Actor = null!,
                Title = "Second scanner authority",
                EventStatus = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Digital,
                EventFormat = null!,
                CreatedAt = AuthenticationRows.Now,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            seed.Events.Add(source);
            AdmissionTarget target = AdmissionTarget.Create(
                Guid.CreateVersion7(), rows.B, source.Id, AdmissionTargetTypeEnum.Event, null, null);
            seed.AdmissionTargets.Add(target);
            seed.AdmissionScannerCapabilities.Add(AdmissionScannerCapability.Issue(
                Guid.CreateVersion7(), rows.B, Guid.CreateVersion7(), source.Id, target.Id,
                rows.Scanner.LookupKeyVersion, rows.Scanner.LookupDigest, "Second scanner",
                AdmissionScannerCapabilityAction.CheckIn, AuthenticationRows.Now.AddHours(1),
                rows.Scanner.IssuedByActorId, AuthenticationRows.Now));
            await seed.SaveChangesAsync();
        }
        await using var request = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(request);
        await Assert.That(await request.AdmissionScannerCapabilities.CountAsync()).IsEqualTo(1);
        await using var global = await AuthenticationGlobalAuthority.CreateAsync(fixture);
        var repository = new AdmissionScannerAuthenticationRepository(global.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await repository.FindByDigestCandidatesAsync(
                [new(rows.Scanner.LookupKeyVersion, rows.Scanner.LookupDigest)], CancellationToken.None);
        });
    }

    [Test]
    [Arguments("key")]
    [Arguments("usage")]
    [Arguments("scanner")]
    [Arguments("membership")]
    public async Task OrdinaryRuntimeCannotExecuteGlobalAuthenticationCapabilities(string purpose)
    {
        await using var rows = await AuthenticationRows.CreateAsync(fixture);
        await using var request = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(request);
        await Assert.That(await request.ExternalApiKeys.AnyAsync(key => key.Id == rows.KeyA.Id)).IsTrue();
        await using var command = request.Database.GetDbConnection().CreateCommand();
        switch (purpose)
        {
            case "key":
                command.CommandText = $"SELECT * FROM {PostgresExternalApiKeyAuthenticationContract.FunctionSql(request)}(@key)";
                Add("key", rows.KeyA.KeyId);
                break;
            case "usage":
                command.CommandText = $"SELECT {PostgresExternalApiKeyAuthenticationContract.UsageFunctionSql(request)}(@id, @at, @ip, @threshold)";
                Add("id", rows.KeyA.Id);
                Add("at", AuthenticationRows.Now);
                Add("ip", "192.0.2.1");
                Add("threshold", AuthenticationRows.Now.AddMinutes(-5));
                break;
            case "scanner":
                command.CommandText = $"SELECT * FROM {PostgresAdmissionScannerAuthenticationContract.FunctionSql(request)}(@versions, @digests)";
                Add("versions", new[] { rows.Scanner.LookupKeyVersion });
                Add("digests", new[] { rows.Scanner.LookupDigest });
                break;
            case "membership":
                command.CommandText = $"SELECT * FROM {PostgresUserTenantMembershipContract.FunctionSql(request)}(@user)";
                Add("user", rows.UserId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        PostgresException denied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await command.ExecuteScalarAsync();
        });

        await Assert.That(denied.SqlState).IsEqualTo("42501");

        void Add(string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }

    [Test]
    public async Task ScannerAuthenticationRejectsUnknownExecutorWithoutLosingPairedLookup()
    {
        await using var rows = await AuthenticationRows.CreateAsync(fixture);
        await using var migrator = fixture.CreateDbContext();
        await using var outsider = await PostgresTenantApplicationTableIsolationTests.GlobalLookupAuthority.CreateAsync(fixture);
        await using var global = await AuthenticationGlobalAuthority.CreateAsync(fixture);
        string function = PostgresAdmissionScannerAuthenticationContract.FunctionSql(migrator);
        await migrator.Database.ExecuteSqlRawAsync(
            $"GRANT EXECUTE ON FUNCTION {function}(integer[], text[]) TO \"{outsider.Role}\"");
        try
        {
            var repository = new AdmissionScannerAuthenticationRepository(outsider.Factory);
            RetryLimitExceededException rejected = await Assert.ThrowsAsync<RetryLimitExceededException>(async () =>
                await repository.FindByDigestCandidatesAsync(
                    [new(rows.Scanner.LookupKeyVersion, rows.Scanner.LookupDigest)], CancellationToken.None));
            await Assert.That(rejected.InnerException is PostgresException).IsTrue();
            var entry = (PostgresException)rejected.InnerException!;
            await Assert.That(entry.SqlState).IsEqualTo("55000");
            PostgresException reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall.SqlState).IsEqualTo("55000");
            await Assert.That(await migrator.Database.SqlQueryRaw<bool>(
                $"SELECT pg_catalog.has_function_privilege('{outsider.Role}', '{function}(integer[], text[])', 'EXECUTE') AS \"Value\"")
                .SingleAsync()).IsTrue();
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync(
                $"REVOKE EXECUTE ON FUNCTION {function}(integer[], text[]) FROM \"{outsider.Role}\"");
        }
        await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        var found = await new AdmissionScannerAuthenticationRepository(global.Factory).FindByDigestCandidatesAsync(
            [new(rows.Scanner.LookupKeyVersion, rows.Scanner.LookupDigest)], CancellationToken.None);
        await Assert.That(found!.Id).IsEqualTo(rows.Scanner.Id);
        await Assert.That(found.TenantId).IsEqualTo(rows.A);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ApiKeyAuthenticationRejectsUnknownLookupOrUsageExecutor(bool usage)
    {
        await using var rows = await AuthenticationRows.CreateAsync(fixture);
        await using var migrator = fixture.CreateDbContext();
        await using var outsider = await PostgresTenantApplicationTableIsolationTests.GlobalLookupAuthority.CreateAsync(fixture);
        await using var global = await AuthenticationGlobalAuthority.CreateAsync(fixture);
        string signature = usage
            ? PostgresExternalApiKeyAuthenticationContract.UsageFunctionSql(migrator) +
                "(uuid, timestamp with time zone, text, timestamp with time zone)"
            : PostgresExternalApiKeyAuthenticationContract.FunctionSql(migrator) + "(text)";
        await migrator.Database.ExecuteSqlRawAsync($"GRANT EXECUTE ON FUNCTION {signature} TO \"{outsider.Role}\"");
        try
        {
            var repository = new ExternalApiKeyAuthenticationRepository(outsider.Factory);
            RetryLimitExceededException rejected = await Assert.ThrowsAsync<RetryLimitExceededException>(async () =>
            {
                if (usage)
                    await repository.TouchUsageMetadata(rows.KeyA.Id, AuthenticationRows.Now, "192.0.2.1", TimeSpan.FromMinutes(5));
                else
                    await repository.GetByKeyIdForAuthentication(rows.KeyA.KeyId);
            });
            await Assert.That(rejected.InnerException is PostgresException).IsTrue();
            var entry = (PostgresException)rejected.InnerException!;
            await Assert.That(entry.SqlState).IsEqualTo("55000");
            PostgresException reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall.SqlState).IsEqualTo("55000");
            await Assert.That(await migrator.Database.SqlQueryRaw<bool>(
                $"SELECT pg_catalog.has_function_privilege('{outsider.Role}', '{signature}', 'EXECUTE') AS \"Value\"")
                .SingleAsync()).IsTrue();
            await Assert.That(await migrator.ExternalApiKeys.AsNoTracking().Where(row => row.Id == rows.KeyA.Id)
                .Select(row => row.LastUsedAt).SingleAsync()).IsNull();
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync($"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{outsider.Role}\"");
        }
        await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        var allowed = new ExternalApiKeyAuthenticationRepository(global.Factory);
        await Assert.That((await allowed.GetByKeyIdForAuthentication(rows.KeyA.KeyId))!.Id).IsEqualTo(rows.KeyA.Id);
        await Assert.That(await allowed.TouchUsageMetadata(
            rows.KeyA.Id, AuthenticationRows.Now, "192.0.2.1", TimeSpan.FromMinutes(5))).IsTrue();
    }

    [Test]
    [Arguments("instance")]
    [Arguments("tenant")]
    [Arguments("organization")]
    [Arguments("group")]
    [Arguments("integration")]
    [Arguments("health")]
    public async Task NativeObservationRejectsUnknownExecutorBeforeReplacement(string purpose)
    {
        await using var migrator = fixture.CreateDbContext();
        (string Signature, string Role, string Query, int Columns, Func<ExploreDbContext, Task> Install) contract = purpose switch
        {
            "instance" => (PostgresAdministrativeAuthorityContract.InstanceFunctionSql(migrator) + "(uuid)",
                PostgresAdministrativeAuthorityContract.GlobalRole,
                $"SELECT {PostgresAdministrativeAuthorityContract.InstanceFunctionSql(migrator)}(@subject)", 1,
                context => PostgresAdministrativeAuthorityContract.ApplyAsync(context)),
            "tenant" => (PostgresAdministrativeAuthorityContract.TenantFunctionSql(migrator) + "(uuid)",
                PostgresAdministrativeAuthorityContract.GlobalRole,
                $"SELECT * FROM {PostgresAdministrativeAuthorityContract.TenantFunctionSql(migrator)}(@subject)", 1,
                context => PostgresAdministrativeAuthorityContract.ApplyAsync(context)),
            "organization" => (PostgresAdministrativeAuthorityContract.OrganizationFunctionSql(migrator) + "(uuid, uuid)",
                PostgresAdministrativeAuthorityContract.GlobalRole,
                $"SELECT * FROM {PostgresAdministrativeAuthorityContract.OrganizationFunctionSql(migrator)}(@subject, NULL)", 1,
                context => PostgresAdministrativeAuthorityContract.ApplyAsync(context)),
            "group" => (PostgresAdministrativeAuthorityContract.GroupFunctionSql(migrator) + "(uuid, uuid)",
                PostgresAdministrativeAuthorityContract.GlobalRole,
                $"SELECT * FROM {PostgresAdministrativeAuthorityContract.GroupFunctionSql(migrator)}(@subject, NULL)", 1,
                context => PostgresAdministrativeAuthorityContract.ApplyAsync(context)),
            "integration" => (PostgresIntegrationSyncDiscoveryContract.FunctionSql(migrator) +
                "(timestamp with time zone, timestamp with time zone, integer)",
                PostgresIntegrationSyncDiscoveryContract.RuntimeRole,
                $"SELECT * FROM {PostgresIntegrationSyncDiscoveryContract.FunctionSql(migrator)}(@now, @stale, 1)", 2,
                context => PostgresIntegrationSyncDiscoveryContract.ApplyAsync(context)),
            "health" => (PostgresQueueDrainHealthContract.FunctionSql(migrator) +
                "(timestamp with time zone, timestamp with time zone)",
                PostgresQueueDrainHealthContract.GlobalRole,
                $"SELECT * FROM {PostgresQueueDrainHealthContract.FunctionSql(migrator)}(@now, @stale)", 13,
                context => PostgresQueueDrainHealthContract.ApplyAsync(context)),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };
        await contract.Install(migrator);
        await using var outsider = await PostgresTenantApplicationTableIsolationTests.GlobalLookupAuthority.CreateAsync(fixture);
        await using var global = await AuthenticationGlobalAuthority.CreateAsync(fixture, contract.Role);
        await migrator.Database.ExecuteSqlRawAsync(
            $"GRANT EXECUTE ON FUNCTION {contract.Signature} TO \"{outsider.Role}\"");
        try
        {
            await using var rejected = await outsider.Factory.CreateDbContextAsync();
            await rejected.Database.OpenConnectionAsync();
            await using var invocation = rejected.Database.GetDbConnection().CreateCommand();
            Configure(invocation);
            PostgresException entry = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var reader = await invocation.ExecuteReaderAsync();
            });
            await Assert.That(entry.SqlState).IsEqualTo("55000");
            PostgresException reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await contract.Install(migrator));
            await Assert.That(reinstall.SqlState).IsEqualTo("55000");
            await Assert.That(await migrator.Database.SqlQueryRaw<bool>(
                $"SELECT pg_catalog.has_function_privilege('{outsider.Role}', '{contract.Signature}', 'EXECUTE') AS \"Value\"")
                .SingleAsync()).IsTrue();
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync(
                $"REVOKE EXECUTE ON FUNCTION {contract.Signature} FROM \"{outsider.Role}\"");
        }
        await contract.Install(migrator);
        await using var restored = await global.Factory.CreateDbContextAsync();
        await restored.Database.OpenConnectionAsync();
        await using var allowed = restored.Database.GetDbConnection().CreateCommand();
        Configure(allowed);
        await using var result = await allowed.ExecuteReaderAsync();
        await Assert.That(result.FieldCount).IsEqualTo(contract.Columns);
        if (purpose == "instance")
        {
            await Assert.That(await result.ReadAsync()).IsTrue();
            await Assert.That(result.GetBoolean(0)).IsFalse();
        }

        void Configure(System.Data.Common.DbCommand command)
        {
            command.CommandText = contract.Query;
            foreach ((string name, object value) in new (string, object)[]
            {
                ("subject", Guid.CreateVersion7()), ("now", AuthenticationRows.Now),
                ("stale", AuthenticationRows.Now.AddMinutes(-5))
            })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
        }
    }

    internal sealed class AuthenticationGlobalAuthority(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        private readonly string _role = fixture.NativeQueueEnrollment.GlobalLogin;
        private string[] _grantedRoles = [];
        private string _connectionString = null!;
        internal GlobalExploreDbContextFactory Factory { get; private set; } = null!;

        internal static async Task<AuthenticationGlobalAuthority> CreateAsync(
            PostgreSqlContainerFixture fixture, params string[] additionalRoles)
        {
            var authority = new AuthenticationGlobalAuthority(fixture);
            await using var metadata = fixture.CreateDbContext();
            authority.Factory = new GlobalExploreDbContextFactory(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
            await using var context = await authority.Factory.CreateDbContextAsync();
            authority._connectionString = context.Database.GetConnectionString()
                ?? throw new InvalidOperationException("The enrolled Global connection is missing.");
            try
            {
                await using var administration = new NpgsqlConnection(fixture.ConnectionString);
                await administration.OpenAsync();
                foreach (string role in additionalRoles.Distinct(StringComparer.Ordinal))
                {
                    await using var grant = administration.CreateCommand();
                    grant.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid = pg_catalog.to_regrole(@role) AND member = pg_catalog.to_regrole(@member))";
                    grant.Parameters.AddWithValue("role", role);
                    grant.Parameters.AddWithValue("member", authority._role);
                    if ((bool)(await grant.ExecuteScalarAsync())!) continue;
                    grant.CommandText = $"GRANT {Quote(role)} TO {Quote(authority._role)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE";
                    grant.Parameters.Clear();
                    await grant.ExecuteNonQueryAsync();
                    authority._grantedRoles = [.. authority._grantedRoles, role];
                }
                await context.Database.OpenConnectionAsync();
                await using var probe = context.Database.GetDbConnection().CreateCommand();
                probe.CommandText = "SELECT current_user, rolsuper, rolbypassrls FROM pg_catalog.pg_roles WHERE rolname = current_user";
                await using (var reader = await probe.ExecuteReaderAsync())
                {
                    await Assert.That(await reader.ReadAsync()).IsTrue();
                    await Assert.That(reader.GetString(0)).IsEqualTo(authority._role);
                    await Assert.That(reader.GetBoolean(1)).IsFalse();
                    await Assert.That(reader.GetBoolean(2)).IsFalse();
                }
                foreach (Type type in new[] { typeof(ExternalApiKey), typeof(AdmissionScannerCapability),
                    typeof(TenantUser), typeof(UserPii) })
                {
                    var entity = metadata.Model.FindEntityType(type)!;
                    string table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
                    probe.CommandText = $"SELECT 1 FROM {table} LIMIT 1";
                    PostgresException denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    {
                        await probe.ExecuteScalarAsync();
                    });
                    await Assert.That(denied.SqlState).IsEqualTo("42501");
                }
                return authority;
            }
            catch
            {
                await authority.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(_connectionString);
            NpgsqlConnection.ClearPool(pool);
            if (_grantedRoles.Length == 0) return;
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"REVOKE {string.Join(", ", _grantedRoles.Select(Quote))} FROM {Quote(_role)}";
            await revoke.ExecuteNonQueryAsync();
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    internal sealed class AuthenticationRows(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        internal static readonly DateTime Now = new(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc);
        internal Guid A { get; } = Guid.CreateVersion7();
        internal Guid B { get; } = Guid.CreateVersion7();
        internal Guid Inactive { get; } = Guid.CreateVersion7();
        internal Guid UserId { get; } = Guid.CreateVersion7();
        internal Guid OtherUserId { get; } = Guid.CreateVersion7();
        internal ExternalApiKey KeyA { get; private set; } = null!;
        internal ExternalApiKey KeyB { get; private set; } = null!;
        internal ExternalApiKey PlatformKey { get; private set; } = null!;
        internal AdmissionScannerCapability Scanner { get; private set; } = null!;
        internal ProviderAccountKey Account { get; } = new(
            AuthenticationProviderKind.Google, $"identity-{Guid.CreateVersion7():N}");
        internal string Schema { get; private set; } = null!;
        internal PrimaryDatabaseConnectionOptions RuntimeOptions { get; private set; } = null!;
        private readonly string _role = $"authentication_runtime_{Guid.CreateVersion7():N}";
        private readonly string _password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        private readonly List<string> _tables = [];
        private string _connectionString = null!;

        internal static async Task<AuthenticationRows> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            await fixture.ResetAsync();
            var rows = new AuthenticationRows(fixture);
            await using var seed = fixture.CreateDbContext();
            var inactive = Tenant(rows.Inactive);
            inactive.TenantStatusId = (int)TenantStatusEnum.Suspended;
            seed.Tenants.AddRange(Tenant(rows.A), Tenant(rows.B), inactive);
            seed.Users.AddRange(User(rows.UserId), User(rows.OtherUserId));
            await seed.SaveChangesAsync();
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(),
                UserId = rows.UserId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Authentication owner" },
                CreatedAt = Now
            };
            seed.Actors.Add(actor);
            seed.TenantUsers.AddRange(
                Membership(rows.A, rows.UserId, Now.AddDays(-3)),
                Membership(rows.B, rows.UserId, Now.AddDays(-2)),
                Membership(rows.Inactive, rows.UserId, Now.AddDays(-1)),
                Membership(rows.B, rows.OtherUserId, Now));
            rows.KeyA = Key(rows.A);
            rows.KeyB = Key(rows.B);
            rows.PlatformKey = Key(null);
            seed.ExternalApiKeys.AddRange(rows.KeyA, rows.KeyB, rows.PlatformKey);
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = rows.UserId,
                User = null!,
                AuthenticationProviderId = (int)rows.Account.ProviderKind,
                AuthenticationProvider = null!,
                ProviderKey = rows.Account.Value,
                CreatedAt = Now
            });
            var source = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.A,
                Tenant = null!,
                ActorId = actor.Id,
                Actor = actor,
                Title = "Authentication authority",
                EventStatus = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Digital,
                EventFormat = null!,
                CreatedAt = Now,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            seed.Events.Add(source);
            AdmissionTarget target = AdmissionTarget.Create(
                Guid.CreateVersion7(), rows.A, source.Id, AdmissionTargetTypeEnum.Event, null, null);
            seed.AdmissionTargets.Add(target);
            rows.Scanner = AdmissionScannerCapability.Issue(
                Guid.CreateVersion7(), rows.A, Guid.CreateVersion7(), source.Id, target.Id, 7,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "Authentication scanner",
                AdmissionScannerCapabilityAction.CheckIn, Now.AddHours(1), actor.Id, Now);
            seed.AdmissionScannerCapabilities.Add(rows.Scanner);
            await seed.SaveChangesAsync();
            foreach (Type type in new[] { typeof(ExternalApiKey), typeof(AdmissionScannerCapability),
                typeof(TenantUser), typeof(TenantUserProfile), typeof(Tenant), typeof(TenantStatus),
                typeof(User), typeof(UserPii), typeof(UserExternalLogin) })
            {
                var entity = seed.Model.FindEntityType(type)!;
                rows.Schema = entity.GetSchema()!;
                rows._tables.Add($"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
            }
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var grant = admin.CreateCommand();
            grant.CommandText = $"""
                CREATE ROLE "{rows._role}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{rows._password}';
                GRANT USAGE ON SCHEMA "{rows.Schema}" TO "{rows._role}";
                GRANT SELECT ON {string.Join(", ", rows._tables)} TO "{rows._role}";
                GRANT UPDATE (last_used_at, last_used_ip) ON {rows._tables[0]} TO "{rows._role}";
                """;
            await grant.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Username = rows._role,
                Password = rows._password
            };
            rows._connectionString = connection.ConnectionString;
            rows.RuntimeOptions = new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = rows._role,
                Password = rows._password,
                Schema = rows.Schema,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            };
            return rows;
        }

        internal ExploreDbContext Context(Guid? tenant) => new(
            TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql(_connectionString).UseSnakeCaseNamingConvention()
                .AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance).Options)
        {
            TenantContext = tenant is { } id ? new FixedTenant(id) : null
        };

        internal async Task AssertOrdinaryRoleAsync(ExploreDbContext context)
        {
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT current_user, rolsuper, rolbypassrls FROM pg_catalog.pg_roles WHERE rolname = current_user";
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetString(0)).IsEqualTo(_role);
            await Assert.That(reader.GetBoolean(1)).IsFalse();
            await Assert.That(reader.GetBoolean(2)).IsFalse();
        }

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(_connectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var revoke = admin.CreateCommand();
            revoke.CommandText = $"""
                REVOKE ALL ON {string.Join(", ", _tables)} FROM "{_role}";
                REVOKE UPDATE (last_used_at, last_used_ip) ON {_tables[0]} FROM "{_role}";
                REVOKE USAGE ON SCHEMA "{Schema}" FROM "{_role}";
                DROP ROLE "{_role}";
                """;
            await revoke.ExecuteNonQueryAsync();
        }

        private static Tenant Tenant(Guid id) => new()
        {
            Id = id,
            FullName = $"Authentication {id:N}",
            Slug = $"auth-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = Now
        };

        private static User User(Guid id) => new()
        {
            Id = id,
            Pii = new UserPii { Email = $"{id:N}@example.test", FirstName = "Identity", LastName = "Owner" },
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = Now
        };

        private static TenantUser Membership(Guid tenantId, Guid userId, DateTime joinedAt) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = joinedAt,
            CreatedAt = joinedAt
        };

        private static ExternalApiKey Key(Guid? tenantId) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = "Authentication key",
            KeyId = ApiKeyHashing.CreateKeyId(),
            SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
            Scopes = "events:read",
            OwnerType = tenantId.HasValue ? ExternalApiKeyOwnerType.Tenant : ExternalApiKeyOwnerType.InstanceAdmin,
            OwnerId = tenantId ?? Guid.CreateVersion7(),
            ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.PendingRotation,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
            ExternalApiKeyCreditPeriod = null!,
            CreatedAt = Now
        };

        private sealed record FixedTenant(Guid TenantId) : ITenantContext;
    }
}
