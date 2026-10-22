using System.Data.Common;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Rows = Event.Persistence.IntegrationTests.TenantIsolation.PostgresAuthenticationLookupAuthorityTests.AuthenticationRows;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresMachineMutationAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(ExternalApiKeyOwnerType.User, false)]
    [Arguments(ExternalApiKeyOwnerType.User, true)]
    [Arguments(ExternalApiKeyOwnerType.Tenant, false)]
    [Arguments(ExternalApiKeyOwnerType.Tenant, true)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, false)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, true)]
    public async Task OrdinaryRuntimeRetainsFooterAuthorityAndHoldsItsTargetThroughCommit(ExternalApiKeyOwnerType owner, bool linkPatch)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var grant = await FooterRows.CreateAsync(fixture, rows, owner);
        await using var db = grant.Context();
        await rows.AssertOrdinaryRoleAsync(db);
        var authority = grant.Authority(db, new ControlledClock(DateTimeOffset.UtcNow));

        await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
        {
            await Assert.That(await authority.AuthorizeFooterPatchAsync(rows.A, grant.GroupId, linkPatch ? grant.LinkId : null, token)).IsTrue();
            await using var contender = fixture.CreateDbContext();
            var conflict = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                $"SELECT id FROM {grant.GroupTable} WHERE id = {{0}} FOR UPDATE NOWAIT", [grant.GroupId], token));
            await Assert.That(conflict!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            await Assert.That(await db.ExternalApiKeys.IgnoreQueryFilters()
                .AnyAsync(key => key.Id == rows.PlatformKey.Id, token)).IsFalse();
            var transaction = db.Database.CurrentTransaction!;
            await transaction.CreateSavepointAsync("runtime_private_key_denial", token);
            try
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                    db.ExternalApiKeys.IgnoreQueryFilters().Where(key => key.Id == rows.PlatformKey.Id)
                        .Select(key => key.SecretHash).SingleOrDefaultAsync(token));
                await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
            finally
            {
                await transaction.RollbackToSavepointAsync("runtime_private_key_denial", token);
                await transaction.ReleaseSavepointAsync("runtime_private_key_denial", token);
            }
            return true;
        });
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.User, "revoked")]
    [Arguments(ExternalApiKeyOwnerType.Tenant, "revoked")]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, "revoked")]
    [Arguments(ExternalApiKeyOwnerType.User, "scope")]
    [Arguments(ExternalApiKeyOwnerType.Tenant, "scope")]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, "scope")]
    [Arguments(ExternalApiKeyOwnerType.User, "owner")]
    [Arguments(ExternalApiKeyOwnerType.Tenant, "owner")]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, "owner")]
    [Arguments(ExternalApiKeyOwnerType.User, "expired")]
    [Arguments(ExternalApiKeyOwnerType.Tenant, "expired")]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, "expired")]
    public async Task CurrentPersistedKeyDriftDeniesTheAuthenticatedSnapshot(ExternalApiKeyOwnerType owner, string drift)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var grant = await FooterRows.CreateAsync(fixture, rows, owner);
        await using var db = grant.Context();
        var authority = grant.Authority(db, new ControlledClock(DateTimeOffset.UtcNow));
        await using (var changed = fixture.CreateDbContext())
        {
            changed.BindTenantLease();
            var key = await changed.ExternalApiKeys.SingleAsync(row => row.Id == grant.Key.Id);
            switch (drift)
            {
                case "revoked": key.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked; break;
                case "scope": key.Scopes = ExternalApiKeyScopes.EventsRead; break;
                case "owner": key.OwnerId = rows.OtherUserId; break;
                case "expired": key.ExpiresAt = DateTime.UtcNow.AddDays(-1); break;
                default: throw new ArgumentOutOfRangeException(nameof(drift));
            }
            await changed.SaveChangesAsync();
        }

        bool allowed = await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(
            token => authority.AuthorizeFooterPatchAsync(rows.A, grant.GroupId, grant.LinkId, token));

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.User)]
    [Arguments(ExternalApiKeyOwnerType.Tenant)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    public async Task ExpiryIsRecheckedAfterContendedFooterTargetAcquisition(ExternalApiKeyOwnerType owner)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var grant = await FooterRows.CreateAsync(fixture, rows, owner);
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        grant.Key.ExpiresAt = clock.GetUtcNow().AddHours(1).UtcDateTime;
        var entered = new TargetAcquisitionSignal(grant.GroupTable, owner == ExternalApiKeyOwnerType.InstanceAdmin);
        await using var db = grant.Context(entered);
        var authority = grant.Authority(db, clock);
        await using var blocker = fixture.CreateDbContext();
        await using var held = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync(
            $"SELECT id FROM {grant.GroupTable} WHERE id = {{0}} FOR UPDATE", grant.GroupId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task signal = entered.Entered.Task;

        Task<bool> decision = new EfCoreUnitOfWork(db).ExecuteSerializableAsync(
            token => authority.AuthorizeFooterPatchAsync(rows.A, grant.GroupId, grant.LinkId, token), deadline.Token);
        try
        {
            await signal.WaitAsync(deadline.Token);
            await Assert.That(decision.IsCompleted).IsFalse();
            clock.Set(grant.Key.ExpiresAt.Value.AddTicks(1));
        }
        finally
        {
            await held.RollbackAsync(deadline.Token);
        }

        await Assert.That(await decision.WaitAsync(deadline.Token)).IsFalse();
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.User, false)]
    [Arguments(ExternalApiKeyOwnerType.User, true)]
    [Arguments(ExternalApiKeyOwnerType.Tenant, false)]
    [Arguments(ExternalApiKeyOwnerType.Tenant, true)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, false)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, true)]
    public async Task CommittedTargetDriftDuringAcquisitionCannotRetainOldAuthority(ExternalApiKeyOwnerType owner, bool linkDrift)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var grant = await FooterRows.CreateAsync(fixture, rows, owner);
        await using var blocker = fixture.CreateDbContext();
        Guid foreignGroup = Guid.CreateVersion7();
        blocker.BindTenantLease();
        blocker.TenantFooterLinkGroups.Add(new TenantFooterLinkGroup
        {
            Id = foreignGroup,
            TenantId = rows.B,
            Title = "Other tenant",
            IsActive = true
        });
        await blocker.SaveChangesAsync();
        await using var held = await blocker.Database.BeginTransactionAsync();
        if (linkDrift)
            await blocker.TenantFooterLinks.Where(link => link.Id == grant.LinkId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.FooterLinkGroupId, foreignGroup));
        else
            await blocker.TenantFooterLinkGroups.Where(group => group.Id == grant.GroupId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(group => group.TenantId, (Guid?)rows.B));
        var entered = new TargetAcquisitionSignal(linkDrift ? grant.LinkTable : grant.GroupTable,
            owner == ExternalApiKeyOwnerType.InstanceAdmin);
        await using var db = grant.Context(entered);
        var authority = grant.Authority(db, new ControlledClock(DateTimeOffset.UtcNow));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task signal = entered.Entered.Task;

        var decision = new EfCoreUnitOfWork(db).ExecuteSerializableAsync(
            token => authority.AuthorizeFooterPatchAsync(rows.A, grant.GroupId, grant.LinkId, token), deadline.Token);
        await signal.WaitAsync(deadline.Token);
        await held.CommitAsync(deadline.Token);

        await Assert.That(await decision.WaitAsync(deadline.Token)).IsFalse();
        await using var verify = fixture.CreateDbContext();
        await Assert.That((await verify.TenantFooterLinks.AsNoTracking().SingleAsync(link => link.Id == grant.LinkId)).Label)
            .IsEqualTo("Owned link");
    }

    [Test]
    public async Task FixedFooterFunctionUsesLiveDatabaseTimeRatherThanTransactionStart()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var grant = await FooterRows.CreateAsync(fixture, rows, ExternalApiKeyOwnerType.InstanceAdmin);
        await using var db = grant.Context();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        DateTime started = await db.Database.SqlQueryRaw<DateTime>("SELECT transaction_timestamp() AS \"Value\"").SingleAsync();
        DateTime observed = await db.Database.SqlQueryRaw<DateTime>("SELECT clock_timestamp() AS \"Value\"").SingleAsync();
        await Assert.That(observed).IsGreaterThan(started);
        // An earlier authenticated deadline remains binding even if the current row
        // has since been extended. Keep application time behind it to isolate SQL time.
        grant.Key.ExpiresAt = observed;
        var authority = grant.Authority(db, new ControlledClock(new DateTimeOffset(started.AddHours(-1))));

        await Assert.That(await authority.AuthorizeFooterPatchAsync(rows.A, grant.GroupId, grant.LinkId, default)).IsFalse();
    }

    [Test]
    [Arguments("identity")]
    [Arguments("snapshot-scope")]
    [Arguments("snapshot-expiry")]
    [Arguments("tenant")]
    public async Task FixedFooterPurposeRejectsWrongIdentitySnapshotAndTenant(string change)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var grant = await FooterRows.CreateAsync(fixture, rows, ExternalApiKeyOwnerType.InstanceAdmin);
        await using var db = grant.Context();
        if (change == "identity") grant.Key.KeyId = Guid.CreateVersion7().ToString("N");
        if (change == "snapshot-scope") grant.Key.Scopes = ExternalApiKeyScopes.EventsRead;
        if (change == "snapshot-expiry") grant.Key.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        var authority = grant.Authority(db, new ControlledClock(DateTimeOffset.UtcNow));

        bool allowed = await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(
            token => authority.AuthorizeFooterPatchAsync(change == "tenant" ? rows.B : rows.A, grant.GroupId, grant.LinkId, token));

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    [Arguments("global", false)]
    [Arguments("global", true)]
    [Arguments("authentication", false)]
    [Arguments("authentication", true)]
    [Arguments("scanner", false)]
    [Arguments("scanner", true)]
    [Arguments("read", false)]
    [Arguments("read", true)]
    [Arguments("write", false)]
    [Arguments("write", true)]
    [Arguments("issuance", false)]
    [Arguments("issuance", true)]
    [Arguments("report", false)]
    [Arguments("report", true)]
    public async Task FooterReadinessRejectsBorrowedFrozenPurposeExecution(string purpose, bool directGrant)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var grant = await FooterRows.CreateAsync(fixture, rows, ExternalApiKeyOwnerType.InstanceAdmin);
        await using var administration = fixture.CreateDbContext();
        await PostgresTenantLookupContract.ApplyAsync(administration);
        await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(administration, fixture.NativeQueueEnrollment);
        await PostgresInstanceApiKeyManagementContract.ApplyAsync(administration);
        await PostgresInstanceApiKeyIssuanceContract.ApplyAsync(administration);
        await PostgresPlatformApiKeyUsageReportContract.ApplyAsync(administration);
        await using var db = grant.Context();
        await db.Database.OpenConnectionAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await PostgresMachineMutationAuthorityContract.RequireReadyAsync(db, default);
        await transaction.CommitAsync();
        var sql = new InstanceApiKeyCapabilitySql(db);
        InstanceApiKeySqlFunction? fixedFunction = purpose switch
        {
            "read" => PostgresInstanceApiKeyManagementContract.Functions(db).Single(function =>
                function.Name == PostgresInstanceApiKeyManagementContract.ReadFunction),
            "write" => PostgresInstanceApiKeyManagementContract.Functions(db).Single(function =>
                function.Name == PostgresInstanceApiKeyManagementContract.RevokeFunction),
            "issuance" => PostgresInstanceApiKeyIssuanceContract.Functions(db).Single(function =>
                function.Name == PostgresInstanceApiKeyIssuanceContract.AuthorityFunction),
            "report" => PostgresPlatformApiKeyUsageReportContract.Function(db),
            _ => null
        };
        (string executor, string signature) = fixedFunction is not null
            ? (fixedFunction.Executor, sql.Function(fixedFunction.Name) + "(" + fixedFunction.ArgumentTypes + ")")
            : purpose switch
            {
                "global" => (PostgresTenantLookupContract.GlobalRole, PostgresTenantLookupContract.FunctionSql(db) + "()"),
                "authentication" => (PostgresExternalApiKeyAuthenticationContract.GlobalRole, string.Empty),
                "scanner" => (PostgresAdmissionScannerAuthenticationContract.GlobalRole, string.Empty),
                _ => throw new ArgumentOutOfRangeException(nameof(purpose))
            };
        bool directAuthenticationColumn = directGrant && (purpose is "authentication" or "scanner");
        string runtimeLogin = rows.RuntimeOptions.Username!;
        string quotedRuntime = "\"" + runtimeLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string sourceTable = string.Empty;
        string sourceColumn = string.Empty;
        string change;
        string restore;
        if (directAuthenticationColumn)
        {
            bool scanner = purpose == "scanner";
            sourceTable = scanner ? sql.Table<AdmissionScannerCapability>() : sql.Table<ExternalApiKey>();
            sourceColumn = db.Model.FindEntityType(scanner ? typeof(AdmissionScannerCapability) : typeof(ExternalApiKey))!
                .FindProperty(scanner ? nameof(AdmissionScannerCapability.LookupDigest) : nameof(ExternalApiKey.SecretHash))!
                .GetColumnName()!;
            string quotedColumn = scanner
                ? sql.Column<AdmissionScannerCapability>(nameof(AdmissionScannerCapability.LookupDigest))
                : sql.Column<ExternalApiKey>(nameof(ExternalApiKey.SecretHash));
            await Assert.That(await db.Database.SqlQuery<bool>($"""
                SELECT CURRENT_USER::text = {runtimeLogin}
                    AND NOT pg_catalog.has_column_privilege(CURRENT_USER, {sourceTable}, {sourceColumn}, 'SELECT') AS "Value"
                """).SingleAsync()).IsTrue().Because($"purpose={purpose}; directGrant={directGrant}; stage=before_column_grant");
            change = $"GRANT SELECT ({quotedColumn}) ON {sourceTable} TO {quotedRuntime}";
            restore = $"REVOKE SELECT ({quotedColumn}) ON {sourceTable} FROM {quotedRuntime}";
        }
        else
        {
            change = directGrant
                ? $"GRANT EXECUTE ON FUNCTION {signature} TO {quotedRuntime}"
                : $"GRANT {executor} TO {quotedRuntime} WITH INHERIT TRUE, SET FALSE";
            restore = directGrant
                ? $"REVOKE EXECUTE ON FUNCTION {signature} FROM {quotedRuntime}"
                : $"REVOKE {executor} FROM {quotedRuntime}";
        }
        await administration.Database.ExecuteSqlRawAsync(change);
        try
        {
            if (directAuthenticationColumn)
                await Assert.That(await db.Database.SqlQuery<bool>($"""
                    SELECT CURRENT_USER::text = {runtimeLogin}
                        AND pg_catalog.has_column_privilege(CURRENT_USER, {sourceTable}, {sourceColumn}, 'SELECT') AS "Value"
                    """).SingleAsync()).IsTrue().Because($"purpose={purpose}; directGrant={directGrant}; stage=after_column_grant");
            await using var changedTransaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresMachineMutationAuthorityContract.RequireReadyAsync(db, default));
        }
        finally
        {
            await administration.Database.ExecuteSqlRawAsync(restore);
        }
    }

    [Test]
    [Arguments("body")]
    [Arguments("owner")]
    [Arguments("search-path")]
    [Arguments("public")]
    [Arguments("executor-secret")]
    [Arguments("owner-secret")]
    public async Task FooterReadinessRejectsChangedPrimitiveAuthority(string mutation)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var grant = await FooterRows.CreateAsync(fixture, rows, ExternalApiKeyOwnerType.InstanceAdmin);
        await using var db = grant.Context();
        await db.Database.OpenConnectionAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await PostgresMachineMutationAuthorityContract.RequireReadyAsync(db, default);
        await transaction.CommitAsync();
        var sql = new InstanceApiKeyCapabilitySql(db);
        var function = PostgresMachineMutationAuthorityContract.Function(db);
        string signature = sql.Function(function.Name) + "(" + function.ArgumentTypes + ")";
        await using var administration = fixture.CreateDbContext();
        string change = mutation switch
        {
            "body" => sql.Define(function.Name, function.Arguments, function.Returns,
                "BEGIN RETURN true; END", function.Owner, function.Executor),
            "owner" => $"ALTER FUNCTION {signature} OWNER TO \"{rows.RuntimeOptions.Username}\"",
            "search-path" => $"ALTER FUNCTION {signature} SET search_path = public",
            "public" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
            "executor-secret" => $"GRANT SELECT ({sql.Column<ExternalApiKey>(nameof(ExternalApiKey.SecretHash))}) ON {sql.Table<ExternalApiKey>()} TO {function.Executor}",
            "owner-secret" => $"GRANT SELECT ({sql.Column<ExternalApiKey>(nameof(ExternalApiKey.SecretHash))}) ON {sql.Table<ExternalApiKey>()} TO {function.Owner}",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await administration.Database.ExecuteSqlRawAsync(change);
        try
        {
            await using var changedTransaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresMachineMutationAuthorityContract.RequireReadyAsync(db, default));
        }
        finally
        {
            if (mutation == "owner")
                await administration.Database.ExecuteSqlRawAsync($"ALTER FUNCTION {signature} OWNER TO {function.Owner}");
            await PostgresMachineMutationAuthorityContract.ApplyAsync(administration);
        }
    }

    private sealed class ControlledClock(DateTimeOffset initial) : TimeProvider
    {
        private long _ticks = initial.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        internal void Set(DateTime instant) => Interlocked.Exchange(ref _ticks, instant.Ticks);
    }

    private sealed class TargetAcquisitionSignal(string table, bool instance) : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command.CommandText);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command.CommandText);
            return ValueTask.FromResult(result);
        }
        private void Observe(string command)
        {
            if (instance
                ? command.Contains("machine_footer_patch_authority", StringComparison.Ordinal)
                : command.Contains(table, StringComparison.Ordinal) && command.Contains("UPDATE", StringComparison.Ordinal))
                Entered.TrySetResult();
        }
    }

    private sealed class FooterRows(PostgreSqlContainerFixture fixture, Rows rows, ExternalApiKey key,
        Guid groupId, Guid linkId, string groupTable, string linkTable, bool ownsPurposeMembership) : IAsyncDisposable
    {
        internal ExternalApiKey Key { get; } = key;
        internal Guid GroupId { get; } = groupId;
        internal Guid LinkId { get; } = linkId;
        internal string GroupTable { get; } = groupTable;
        internal string LinkTable { get; } = linkTable;

        internal static async Task<FooterRows> CreateAsync(
            PostgreSqlContainerFixture fixture, Rows rows, ExternalApiKeyOwnerType owner)
        {
            await using var seed = fixture.CreateDbContext();
            seed.BindTenantLease();
            await PostgresMachineMutationAuthorityContract.ApplyAsync(seed);
            await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
            var key = await seed.ExternalApiKeys.SingleAsync(row => row.Id ==
                (owner == ExternalApiKeyOwnerType.InstanceAdmin ? rows.PlatformKey.Id : rows.KeyA.Id));
            key.OwnerType = owner;
            key.OwnerId = owner == ExternalApiKeyOwnerType.Tenant ? rows.A : rows.UserId;
            key.Scopes = owner == ExternalApiKeyOwnerType.InstanceAdmin ? ExternalApiKeyScopes.AdminInstance : ExternalApiKeyScopes.AdminTenant;
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = rows.UserId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            Guid groupId = Guid.CreateVersion7();
            Guid linkId = Guid.CreateVersion7();
            seed.TenantFooterLinkGroups.Add(new TenantFooterLinkGroup
            {
                Id = groupId,
                TenantId = rows.A,
                Title = "Owned group",
                IsActive = true
            });
            seed.TenantFooterLinks.Add(new TenantFooterLink
            {
                Id = linkId,
                FooterLinkGroupId = groupId,
                Label = "Owned link",
                Url = "/owned",
                IsActive = true
            });
            await seed.SaveChangesAsync();
            var helper = seed.GetService<ISqlGenerationHelper>();
            var groupEntity = seed.Model.FindEntityType(typeof(TenantFooterLinkGroup))!;
            var linkEntity = seed.Model.FindEntityType(typeof(TenantFooterLink))!;
            string groupTable = helper.DelimitIdentifier(groupEntity.GetTableName()!, groupEntity.GetSchema());
            string linkTable = helper.DelimitIdentifier(linkEntity.GetTableName()!, linkEntity.GetSchema());
            string login = $"\"{rows.RuntimeOptions.Username}\"";
            bool ownsPurposeMembership = !await rows.HasDirectPurposeMembershipAsync(
                seed, PostgresMachineMutationAuthorityContract.RuntimeRole);
            if (ownsPurposeMembership)
                await seed.Database.ExecuteSqlRawAsync(
                    $"GRANT {PostgresMachineMutationAuthorityContract.RuntimeRole} TO {login} WITH INHERIT TRUE, SET FALSE");
            return new(fixture, rows, key, groupId, linkId, groupTable, linkTable, ownsPurposeMembership);
        }

        internal ExploreDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, rows.RuntimeOptions);
            options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var context = new ExploreDbContext(options.Options) { TenantContext = new TenantScope(rows.A) };
            context.BindTenantLease();
            return context;
        }

        internal MachineMutationCommitAuthority Authority(ExploreDbContext context, TimeProvider clock)
        {
            var claims = new List<Claim>
            {
                new(ApiAuthenticationClaimTypes.AuthMethod, "api_key"),
                new(ApiAuthenticationClaimTypes.ApiKeyId, Key.KeyId),
                new(ApiAuthenticationClaimTypes.OwnerType, Key.OwnerType.ToString()),
                new(ApiAuthenticationClaimTypes.OwnerId, Key.OwnerId.ToString("D"))
            };
            if (Key.TenantId is { } tenant) claims.Add(new(ApiAuthenticationClaimTypes.TenantId, tenant.ToString("D")));
            if (Key.OwnerType == ExternalApiKeyOwnerType.User) claims.Add(new(PlatformIdentityClaimTypes.InternalUserId, Key.OwnerId.ToString("D")));
            claims.AddRange(Key.Scopes.Split(' ').Select(scope => new Claim(ApiAuthenticationClaimTypes.Scope, scope)));
            var identity = new ClaimsIdentity(claims, ApiAuthenticationSchemeNames.ApiKey);
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
            http.HttpContext.Features.Set(new PersistedApiKeyAuthenticationProof(identity, Key));
            return new MachineMutationCommitAuthority(context, new MachinePrincipalAccessor(http), http, clock);
        }

        public async ValueTask DisposeAsync()
        {
            if (!ownsPurposeMembership) return;
            await using var administration = fixture.CreateDbContext();
            string login = $"\"{rows.RuntimeOptions.Username}\"";
            await administration.Database.ExecuteSqlRawAsync(
                $"REVOKE {PostgresMachineMutationAuthorityContract.RuntimeRole} FROM {login}");
        }

        private sealed record TenantScope(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;
    }
}
