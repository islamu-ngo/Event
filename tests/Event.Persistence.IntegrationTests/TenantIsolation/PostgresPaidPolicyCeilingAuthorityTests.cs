using System.Data;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresPaidPolicyCeilingAuthorityTests(PostgreSqlContainerFixture fixture)
{
    internal const long ExactAmount = 9_007_199_254_740_993;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ForeignActiveCurrencyIncompatibilityRejectsInstanceRevisionAtomically(bool currentTransaction)
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var context = await rows.Runtime.CreateDbContextAsync(rows.A);
        await rows.AssertOrdinaryAsync(context);
        await Assert.That(await context.PaidEventPolicyVersions.IgnoreQueryFilters()
            .AnyAsync(policy => policy.TenantId == rows.B)).IsFalse();
        PaidEventPolicyMutationResult result;
        if (currentTransaction)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            result = await Boundary(context).ReviseInstanceInCurrentTransactionAsync(
                new InstancePaidEventPolicyMutationInput(Policy(["EUR"]), rows.InstanceVersion),
                CancellationToken.None);
            await transaction.CommitAsync();
        }
        else
        {
            result = await Boundary(context).ReviseInstanceAsync(Policy(["EUR"]), CancellationToken.None);
        }

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(PaidEventPolicyMutationFailureCodes.ValidationFailed);
        await rows.AssertUnchangedAsync();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(rows.A);
    }

    [Test]
    [Arguments("USD", false)]
    [Arguments("USD", true)]
    [Arguments("JPY", false)]
    [Arguments("JPY", true)]
    public async Task ForeignMinorUnitCeilingPreservesExactEqualityAndOneUnitBoundary(string currency, bool equal)
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var context = await rows.Runtime.CreateDbContextAsync(rows.A);
        await rows.AssertOrdinaryAsync(context);
        RevisePaidEventPolicyDto proposed = Policy(["EUR", "USD", "JPY"]) with
        {
            CurrencyRiskLimits =
            [
                new PaidEventPolicyCurrencyRiskLimitDto
                {
                    CurrencyCode = currency,
                    PerEventSalesCeilingMinor = equal ? ExactAmount : ExactAmount - 1
                }
            ]
        };

        PaidEventPolicyMutationResult result = await Boundary(context)
            .ReviseInstanceAsync(proposed, CancellationToken.None);

        await Assert.That(result.Success).IsEqualTo(equal);
        if (!equal)
        {
            await Assert.That(result.FailureCode).IsEqualTo(PaidEventPolicyMutationFailureCodes.ValidationFailed);
            await rows.AssertUnchangedAsync();
            return;
        }

        await using var verify = fixture.CreateDbContext();
        PaidEventPolicyVersion active = (await new PaidEventPolicyRepository(verify)
            .GetActiveInstanceAsync(CancellationToken.None))!;
        await Assert.That(active.VersionNumber).IsEqualTo(rows.InstanceVersion + 1);
        await Assert.That(active.CurrencyRiskLimits.Single().CurrencyCode).IsEqualTo(currency);
        await Assert.That(active.CurrencyRiskLimits.Single().PerEventSalesCeilingMinor).IsEqualTo(ExactAmount);
    }

    [Test]
    [Arguments("organizer")]
    [Arguments("verification")]
    [Arguments("currency-order")]
    [Arguments("first-review")]
    [Arguments("far-future")]
    public async Task ForeignActivePolicyStillEnforcesNonAmountCeilingRules(string dimension)
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var context = await rows.Runtime.CreateDbContextAsync(rows.A);
        RevisePaidEventPolicyDto baseline = Policy(["EUR", "USD", "JPY"]);
        RevisePaidEventPolicyDto proposed = dimension switch
        {
            "organizer" => baseline with { AllowedOrganizerKindIds = [(int)ActorTypeEnum.Organization] },
            "verification" => baseline with { RequiresLocalVerification = true },
            "currency-order" => Policy(["EUR", "JPY", "USD"]),
            "first-review" => baseline with { RequiresFirstPaidEventReview = true },
            "far-future" => baseline with { FarFutureReviewThresholdDays = 30 },
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };

        PaidEventPolicyMutationResult result = await Boundary(context)
            .ReviseInstanceAsync(proposed, CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(PaidEventPolicyMutationFailureCodes.ValidationFailed);
        await rows.AssertUnchangedAsync();
    }

    [Test]
    public async Task RetiredForeignPolicyDoesNotBlockCompatibleInstanceRevision()
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using (var seed = fixture.CreateDbContext())
        {
            PaidEventPolicyVersion previous = (await new PaidEventPolicyRepository(seed)
                .GetActiveTenantAsync(rows.B, CancellationToken.None))!;
            PaidEventPolicyVersion revision = previous.CreateRevision(
                false, [ActorTypeEnum.Organization], false, ["EUR"], "EUR",
                previous.RefundProtections, [], false, null);
            seed.PaidEventPolicyVersions.Add(revision);
            await seed.SaveChangesAsync();
        }
        await using var context = await rows.Runtime.CreateDbContextAsync(rows.A);

        PaidEventPolicyMutationResult result = await Boundary(context)
            .ReviseInstanceAsync(Policy(["EUR"]), CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await using var verify = fixture.CreateDbContext();
        var history = await new PaidEventPolicyRepository(verify)
            .ListTenantHistoryAsync(rows.B, CancellationToken.None);
        await Assert.That(history.Length).IsEqualTo(2);
        await Assert.That(history.Single(policy => policy.IsActive).AllowedCurrencyCodes)
            .IsEquivalentTo(["EUR"]);
    }

    [Test]
    public async Task CeilingReadSeesCallerTransactionPolicyRevisionBeforeCommit()
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var context = await rows.Runtime.CreateDbContextAsync(rows.B);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var boundary = Boundary(context);
        PaidEventPolicyMutationResult tenant = await boundary.ReviseTenantInCurrentTransactionAsync(
            new TenantPaidEventPolicyMutationInput(rows.B, Policy(["EUR"]) with
            {
                AllowedOrganizerKindIds = [(int)ActorTypeEnum.Organization]
            }, rows.InstanceVersion),
            CancellationToken.None);
        await Assert.That(tenant.Success).IsTrue();

        PaidEventPolicyMutationResult instance = await boundary.ReviseInstanceInCurrentTransactionAsync(
            new InstancePaidEventPolicyMutationInput(Policy(["EUR"]), rows.InstanceVersion),
            CancellationToken.None);

        await Assert.That(instance.Success).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
        await rows.AssertUnchangedAsync();
        await using var verify = fixture.CreateDbContext();
        await Assert.That((await new PaidEventPolicyRepository(verify)
            .ListTenantHistoryAsync(rows.B, CancellationToken.None)).Length).IsEqualTo(1);
    }

    [Test]
    public async Task InstanceMutationLockCoversRevisionAndCallerRollback()
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var context = await rows.Runtime.CreateDbContextAsync(rows.B);
        await using var contender = await rows.Runtime.CreateDbContextAsync(rows.A);
        await contender.Database.OpenConnectionAsync();
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        long key = RelationalSettingMutationLock.ComputeStableLockKey(PaidEventPolicyMutationLockKeys.Instance);

        await mutationLock.ExecuteOrderedGroupsAsync(
            new[] { new[] { PaidEventPolicyMutationLockKeys.Instance } },
            async token =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await Assert.That(await TryLockAsync(contender, key)).IsFalse();
                PaidEventPolicyMutationResult result = await Boundary(context)
                    .ReviseInstanceInCurrentTransactionAsync(
                        new InstancePaidEventPolicyMutationInput(Policy(["EUR", "USD", "JPY"]), rows.InstanceVersion),
                        token);
                await Assert.That(result.Success).IsTrue();
                await Assert.That(await TryLockAsync(contender, key)).IsFalse();
                await transaction.RollbackAsync(token);
                return true;
            }, CancellationToken.None);

        await Assert.That(await TryLockAsync(contender, key)).IsTrue();
        await contender.Database.ExecuteSqlRawAsync("SELECT pg_catalog.pg_advisory_unlock({0})", key);
        await rows.AssertUnchangedAsync();
    }

    [Test]
    public async Task OrdinaryGlobalAndRuntimeRemainDeniedForeignPolicyTableAuthority()
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var global = await rows.Global.CreateDbContextAsync();
        await rows.AssertOrdinaryAsync(global);
        await Assert.That(global.TenantContext).IsNull();
        foreach (string table in rows.PolicyTables)
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(
                async () => await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {table}"));
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        await using var runtime = await rows.Runtime.CreateDbContextAsync(rows.A);
        await rows.AssertOrdinaryAsync(runtime);
        await Assert.That(await runtime.PaidEventPolicyVersions.IgnoreQueryFilters()
            .Where(policy => policy.TenantId == rows.B)
            .ExecuteUpdateAsync(setters => setters.SetProperty(policy => policy.CreatedAt, DateTime.UtcNow)))
            .IsEqualTo(0);
        var promotion = await Assert.ThrowsAsync<PostgresException>(
            async () => await runtime.Database.ExecuteSqlRawAsync("SET ROLE postgres"));
        await Assert.That(promotion!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task BoundedCeilingPagesCarryForeignAggregateCollectionsWithoutTracking()
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var context = await rows.Runtime.CreateDbContextAsync(rows.A);
        var repository = new PaidEventPolicyRepository(context);

        PaidEventPolicyVersion[] first = await repository.ListActiveTenantsAsync(0, 1, CancellationToken.None);
        PaidEventPolicyVersion[] second = await repository.ListActiveTenantsAsync(1, 1, CancellationToken.None);

        await Assert.That(first.Concat(second).Select(policy => policy.TenantId))
            .IsEquivalentTo(new Guid?[] { rows.A, rows.B });
        PaidEventPolicyVersion foreign = first.Concat(second).Single(policy => policy.TenantId == rows.B);
        await Assert.That(foreign.AllowedCurrencyCodes).IsEquivalentTo(["USD", "JPY"]);
        await Assert.That(foreign.AllowedOrganizerKinds)
            .IsEquivalentTo(new[] { ActorTypeEnum.Organization, ActorTypeEnum.Group });
        await Assert.That(foreign.RefundProtections).IsEquivalentTo(Enum.GetValues<PaidEventRefundProtection>());
        await Assert.That(foreign.CurrencyRiskLimits.Select(limit => limit.PerEventSalesCeilingMinor))
            .IsEquivalentTo(new long?[] { ExactAmount, ExactAmount });
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
    }

    [Test]
    public async Task GlobalCeilingCapabilityReturnsOnlyFixedActiveValidationAggregates()
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var global = await rows.Global.CreateDbContextAsync();
        await rows.AssertOrdinaryAsync(global);

        PaidEventPolicyVersion[] page = await new PaidEventPolicyRepository(global)
            .ListActiveTenantsAsync(0, 2, CancellationToken.None);

        await Assert.That(page.Select(policy => policy.TenantId)).IsEquivalentTo(new Guid?[] { rows.A, rows.B });
        await Assert.That(page.All(policy => policy.IsActive && policy.VersionNumber == 1)).IsTrue();
        await using var verify = fixture.CreateDbContext();
        Guid foreignId = (await new PaidEventPolicyRepository(verify)
            .GetActiveTenantAsync(rows.B, CancellationToken.None))!.Id;
        await Assert.That(page.Single(policy => policy.TenantId == rows.B).Id).IsEqualTo(foreignId);
        await Assert.That(global.TenantContext).IsNull();
        await Assert.That(global.ChangeTracker.Entries()).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingCeilingCapabilityFailsClosedWithoutFilterBypassFallback(bool globalAuthority)
    {
        await using var rows = await CeilingRows.CreateAsync(fixture, grantCapability: false);
        await using var context = globalAuthority
            ? await rows.Global.CreateDbContextAsync()
            : await rows.Runtime.CreateDbContextAsync(rows.A);
        await rows.AssertOrdinaryAsync(context);

        var denied = await Assert.ThrowsAsync<PostgresException>(
            async () => await new PaidEventPolicyRepository(context)
                .ListActiveTenantsAsync(0, 2, CancellationToken.None));

        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(globalAuthority ? (Guid?)null : rows.A);
        await rows.AssertUnchangedAsync();
    }

    [Test]
    public async Task CeilingReadAuthorityPreservesRuntimeAdmissionAndTenantPolicyFingerprints()
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var context = await rows.Runtime.CreateDbContextAsync(rows.A);
        await rows.AssertOrdinaryAsync(context);
        var tables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Where(table => rows.PolicyTables.Contains($"\"{table.Schema}\".\"{table.TableName}\""))
            .ToArray();
        await Assert.That(tables.Length).IsEqualTo(5);
        foreach (var table in tables)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT obj_description(policy.oid, 'pg_policy') =
                    @fingerprint || md5(policy.polqual::text) || ':' || md5(policy.polwithcheck::text)
                FROM pg_catalog.pg_policy policy
                JOIN pg_catalog.pg_class relation ON relation.oid = policy.polrelid
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE namespace.nspname = @schema AND relation.relname = @table
                    AND policy.polname = 'tenant_isolation'
                """;
            Add(command, "schema", table.Schema!);
            Add(command, "table", table.TableName);
            Add(command, "fingerprint", PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(table, table.Schema));
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
        }
        await using var authority = context.Database.GetDbConnection().CreateCommand();
        authority.CommandText = """
            SELECT NOT pg_has_role(current_user, @owner, 'USAGE')
                AND NOT pg_has_role(current_user, @owner, 'SET')
                AND NOT pg_has_role(current_user, @migrator, 'USAGE')
                AND NOT pg_has_role(current_user, @migrator, 'SET')
            """;
        Add(authority, "owner", PostgresPaidPolicyCeilingContract.OwnerRole);
        Add(authority, "migrator", PostgresPaidPolicyCeilingContract.MigratorRole);
        await Assert.That((bool)(await authority.ExecuteScalarAsync())!).IsTrue();
    }

    [Test]
    [Arguments(-1, 1)]
    [Arguments(0, 0)]
    [Arguments(0, 257)]
    public async Task NativeCeilingCapabilityRejectsUnboundedPageRequests(int offset, int limit)
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var global = await rows.Global.CreateDbContextAsync();

        var denied = await Assert.ThrowsAsync<PostgresException>(
            async () => await global.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresPaidPolicyCeilingContract.FunctionSql(global)}({{0}}, {{1}})", offset, limit));

        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InvalidParameterValue);
        await rows.AssertUnchangedAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedCeilingReadPolicyCannotSilentlyHideForeignValidationRows(bool childPolicy)
    {
        await using var rows = await CeilingRows.CreateAsync(fixture);
        await using var context = await rows.Runtime.CreateDbContextAsync(rows.A);
        await rows.AssertOrdinaryAsync(context);
        string table = rows.PolicyTables[childPolicy ? 2 : 0];
        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync();
        await using var policy = admin.CreateCommand();
        policy.CommandText = """
            SELECT polname FROM pg_catalog.pg_policy
            WHERE polrelid = @table::regclass AND polcmd = 'r'
                AND polroles = ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = @owner)]
            """;
        policy.Parameters.AddWithValue("table", table);
        policy.Parameters.AddWithValue("owner", PostgresPaidPolicyCeilingContract.OwnerRole);
        string name = (string)(await policy.ExecuteScalarAsync())!;
        await using var tamper = admin.CreateCommand();
        tamper.CommandText = $"ALTER POLICY \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\" ON {table} USING (false)";
        await tamper.ExecuteNonQueryAsync();
        try
        {
            var rejected = await Assert.ThrowsAsync<PostgresException>(
                async () => await Boundary(context)
                    .ReviseInstanceAsync(Policy(["EUR"]), CancellationToken.None));

            await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
            await rows.AssertUnchangedAsync();
        }
        finally
        {
            await using var repair = fixture.CreateDbContext();
            await PostgresPaidPolicyCeilingContract.ApplyAsync(repair);
        }
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    internal static PaidEventPolicyMutationBoundary Boundary(ExploreDbContext context)
    {
        var unitOfWork = new EfCoreUnitOfWork(context);
        return new PaidEventPolicyMutationBoundary(
            new PaidEventPolicyRepository(context), unitOfWork,
            new RelationalSettingMutationLock(context, unitOfWork));
    }

    internal static RevisePaidEventPolicyDto Policy(IReadOnlyList<string> currencies) => new()
    {
        IsPaymentsEnabled = false,
        AllowedOrganizerKindIds = [(int)ActorTypeEnum.Organization, (int)ActorTypeEnum.Group],
        AllowedCurrencyCodes = currencies,
        DefaultCurrencyCode = currencies[0],
        RefundProtectionIds = Enum.GetValues<PaidEventRefundProtection>()
            .Select(protection => (int)protection).ToArray()
    };

    private static async Task<bool> TryLockAsync(ExploreDbContext context, long key)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_catalog.pg_try_advisory_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    internal sealed class CeilingRows(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        internal Guid A { get; } = Guid.CreateVersion7();
        internal Guid B { get; } = Guid.CreateVersion7();
        internal int InstanceVersion { get; private set; }
        internal Guid InstanceId { get; private set; }
        internal GlobalExploreDbContextFactory Global { get; private set; } = null!;
        internal TenantExploreDbContextFactory Runtime { get; private set; } = null!;
        internal IReadOnlyList<string> PolicyTables => _tables;
        private readonly string _runtimeRole = $"paid_ceiling_runtime_{Guid.CreateVersion7():N}";
        private readonly string _globalRole = $"paid_ceiling_global_{Guid.CreateVersion7():N}";
        private readonly List<string> _tables = [];
        private readonly List<string> _connections = [];
        private string _schema = null!;
        private bool _grantedCapability;

        internal static async Task<CeilingRows> CreateAsync(PostgreSqlContainerFixture fixture, bool grantCapability = true)
        {
            await fixture.ResetAsync();
            var rows = new CeilingRows(fixture);
            await using var seed = fixture.CreateDbContext();
            seed.Tenants.AddRange(Tenant(rows.A), Tenant(rows.B));
            PaidEventPolicyVersion instance = PaidEventPolicyVersion.CreateDefaultInstance().CreateRevision(
                false, [ActorTypeEnum.Organization, ActorTypeEnum.Group], false,
                ["EUR", "USD", "JPY"], "EUR", Enum.GetValues<PaidEventRefundProtection>(), [], false, null);
            rows.InstanceId = instance.Id;
            rows.InstanceVersion = instance.VersionNumber;
            seed.PaidEventPolicyVersions.AddRange(
                instance,
                PaidEventPolicyVersion.CreateTenant(rows.A, false, [ActorTypeEnum.Organization], true,
                    ["EUR"], "EUR", Enum.GetValues<PaidEventRefundProtection>(), [], true, 30),
                PaidEventPolicyVersion.CreateTenant(rows.B, false, [ActorTypeEnum.Organization, ActorTypeEnum.Group], false,
                    ["USD", "JPY"], "USD", Enum.GetValues<PaidEventRefundProtection>(),
                    [
                        PaidEventPolicyCurrencyRiskLimit.Create("USD", ExactAmount, null, null, null, null, null),
                        PaidEventPolicyCurrencyRiskLimit.Create("JPY", ExactAmount, null, null, null, null, null)
                    ], false, null));
            await seed.SaveChangesAsync();
            rows._schema = seed.Model.FindEntityType(typeof(PaidEventPolicyVersion))!.GetSchema()!;
            foreach (Type type in new[] { typeof(PaidEventPolicyVersion), typeof(PaidEventPolicyAllowedOrganizerKind),
                typeof(PaidEventPolicyAllowedCurrency), typeof(PaidEventPolicyRefundProtection), typeof(PaidEventPolicyCurrencyRiskLimitRow) })
            {
                var entity = seed.Model.FindEntityType(type)!;
                rows._tables.Add($"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
            }
            await PostgresPaidPolicyCeilingContract.ApplyAsync(seed);
            string runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var grant = admin.CreateCommand();
            grant.CommandText = $"""
                CREATE ROLE "{rows._runtimeRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{runtimePassword}';
                CREATE ROLE "{rows._globalRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{globalPassword}';
                GRANT USAGE ON SCHEMA "{rows._schema}" TO "{rows._runtimeRole}", "{rows._globalRole}";
                GRANT SELECT, INSERT, UPDATE ON {string.Join(", ", rows._tables)} TO "{rows._runtimeRole}";
                """;
            if (grantCapability)
            {
                grant.CommandText += $"""

                    GRANT "{PostgresPaidPolicyCeilingContract.RuntimeRole}"
                        TO "{rows._runtimeRole}", "{rows._globalRole}" WITH INHERIT TRUE, SET FALSE;
                    """;
                rows._grantedCapability = true;
            }
            await grant.ExecuteNonQueryAsync();
            rows.Runtime = new TenantExploreDbContextFactory(
                rows.Options(PrimaryDatabaseRole.Runtime, rows._runtimeRole, runtimePassword), poolSize: 1);
            rows.Global = new GlobalExploreDbContextFactory(
                rows.Options(PrimaryDatabaseRole.Global, rows._globalRole, globalPassword), poolSize: 1);
            return rows;
        }

        internal async Task AssertOrdinaryAsync(ExploreDbContext context)
        {
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                    AND NOT rolcreaterole AND NOT rolreplication
                FROM pg_catalog.pg_roles WHERE rolname = current_user
                """;
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
        }

        internal async Task AssertUnchangedAsync()
        {
            await using var verify = fixture.CreateDbContext();
            PaidEventPolicyVersion[] instances = await verify.PaidEventPolicyVersions.IgnoreQueryFilters()
                .AsNoTracking().Where(policy => policy.TenantId == null).ToArrayAsync();
            await Assert.That(instances.Length).IsEqualTo(1);
            await Assert.That(instances[0].Id).IsEqualTo(InstanceId);
            await Assert.That(instances[0].IsActive).IsTrue();
            await Assert.That(instances[0].VersionNumber).IsEqualTo(InstanceVersion);
            await Assert.That(await verify.PaidEventPolicyVersions.IgnoreQueryFilters()
                .CountAsync(policy => policy.TenantId == B && policy.IsActive)).IsEqualTo(1);
        }

        private PrimaryDatabaseConnectionOptions Options(PrimaryDatabaseRole role, string login, string password)
        {
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Username = login,
                Password = password
            };
            _connections.Add(connection.ConnectionString);
            return new PrimaryDatabaseConnectionOptions
            {
                Role = role,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Schema = _schema,
                Username = login,
                Password = password,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            };
        }

        public async ValueTask DisposeAsync()
        {
            foreach (string connection in _connections)
            {
                using var pool = new NpgsqlConnection(connection);
                NpgsqlConnection.ClearPool(pool);
            }
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var revoke = admin.CreateCommand();
            revoke.CommandText = $"""
                REVOKE SELECT, INSERT, UPDATE ON {string.Join(", ", _tables)} FROM "{_runtimeRole}";
                REVOKE USAGE ON SCHEMA "{_schema}" FROM "{_runtimeRole}", "{_globalRole}";
                """;
            if (_grantedCapability)
                revoke.CommandText += $"\nREVOKE \"{PostgresPaidPolicyCeilingContract.RuntimeRole}\" FROM \"{_runtimeRole}\", \"{_globalRole}\";";
            revoke.CommandText += $"""

                DROP ROLE "{_runtimeRole}";
                DROP ROLE "{_globalRole}";
                """;
            await revoke.ExecuteNonQueryAsync();
        }

        private static Tenant Tenant(Guid id) => new()
        {
            Id = id,
            FullName = "Paid ceiling authority",
            Slug = $"paid-ceiling-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        };
    }
}
