using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanEmailNativeSourceProtocolTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EnrolledEmailLoginReadsActualSourcesAndHoldsOriginalAnchors(bool tenant)
    {
        var row = await SeedAsync();
        await using var context = Open(tenant ? PrimaryDatabaseRole.EmailPolicyTenantMutation
            : PrimaryDatabaseRole.EmailPolicyInstanceMutation, tenant ? row.Tenant : null);
        using var observed = JsonDocument.Parse(await ReadAsync(context, row.User, tenant, false, stamp: row.Stamp));
        await Assert.That(await context.Database.SqlQueryRaw<string>("SELECT session_user::text AS \"Value\"").SingleAsync())
            .IsEqualTo(tenant ? fixture.NativeQueueEnrollment.EmailTenantMutationLogin
                : fixture.NativeQueueEnrollment.EmailInstanceMutationLogin);
        await Assert.That(observed.RootElement.GetProperty("Principal").GetProperty("q0")[0]
            .GetProperty("Id").GetGuid()).IsEqualTo(row.User);
        await Assert.That(observed.RootElement.GetProperty("Principal").GetProperty("q4")[0]
            .GetProperty("TenantId").GetGuid()).IsEqualTo(row.Tenant);
        await Assert.That(observed.RootElement.GetProperty("Principal").GetProperty("l0")[0]
            .GetProperty("StampMatches").GetBoolean()).IsTrue();
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var acquired = JsonDocument.Parse(await ReadAsync(context, row.User, tenant, true, stamp: row.Stamp));
        await Assert.That(acquired.RootElement.GetProperty("Route").GetProperty("System").GetArrayLength())
            .IsGreaterThan(0);
        await using var contender = fixture.CreateDbContext();
        foreach (string anchor in new[] { "policy_source_set", "human_principal_source_epoch", "human_support_access_source_epoch" })
        {
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var denied = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                $"SELECT id FROM {new HumanPrincipalSourceSql(contender).Name(anchor)} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(denied!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
        }
        await owner.RollbackAsync();
        // A function executor does not receive raw Local credential or anchor access.
        var rawDenied = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
            $"SELECT revision FROM {new HumanPrincipalSourceSql(context).Name("human_principal_source_epoch")}"));
        await Assert.That(rawDenied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task CompleteReadyCredentialUsesOriginalStampAndSupportContributors()
    {
        var row = await SeedAsync();
        var subject = await HumanCommitSubject.ResolveAsync(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", row.User.ToString("D")),
                new Claim("auth_provider", "local"), new Claim("iss", LocalIdentityOptions.Issuer)], "source-control")),
            new ResolveCurrentUserIdByIdentityRequestHandler(new ExternalLoginAuthenticationRepository(
                new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)), fixture.NativeQueueEnrollment)), default)
            ?? throw new InvalidOperationException("Persisted original Local subject required.");
        var local = new LocalSessionAuthority(row.User, row.Stamp, true);
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyTenantMutation, row.Tenant);
        // Use the production complete Local decoder without claiming to authenticate a SQL caller.
        string payload = await ReadAsync(context, row.User, true, false, stamp: row.Stamp, session: row.Session);
        using var original = JsonDocument.Parse(payload);
        await Assert.That(original.RootElement.GetProperty("Support").GetProperty("q0")[0]
            .GetProperty("Id").GetGuid()).IsEqualTo(row.Session);
        await Assert.That(original.RootElement.GetProperty("Support").GetProperty("q1").GetArrayLength()).IsGreaterThan(0);
        await Assert.That(original.RootElement.GetProperty("Principal").GetProperty("l2")[0]
            .GetProperty("Stage").GetInt32()).IsEqualTo((int)LocalCredentialOperationStage.Replaced);
        var principal = HumanEmailSourceProjection.ReadPrincipal(original.RootElement.GetProperty("Principal"),
            subject, row.Tenant, true);
        HumanPrincipalSourceValidation.RequireCurrent(subject, local, principal);
        await using (var writer = fixture.CreateDbContext())
            await writer.LocalIdentityUsers.Where(user => user.Id == row.User)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.SecurityStamp, Guid.CreateVersion7().ToString("D")));
        using var changed = JsonDocument.Parse(await ReadAsync(context, row.User, true, false, stamp: row.Stamp, session: row.Session));
        var revoked = HumanEmailSourceProjection.ReadPrincipal(changed.RootElement.GetProperty("Principal"),
            subject, row.Tenant, true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        {
            HumanPrincipalSourceValidation.RequireCurrent(subject, local, revoked);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task StaleSnapshotCannotHideNewPrincipalContributor()
    {
        var row = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(context, row.User, false, false);
        await using (var writer = fixture.CreateDbContext())
        {
            writer.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = row.User,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            await writer.SaveChangesAsync();
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, row.User, false, true));
        await Assert.That(denied!.SqlState).IsEqualTo("40001");
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OrdinaryAndPrivilegedLoginsCannotClaimDedicatedEmailPurpose(PrimaryDatabaseRole role)
    {
        var row = await SeedAsync();
        await using var context = Open(role, role == PrimaryDatabaseRole.Runtime ? row.Tenant : null);
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, row.User, false, false));
    }

    [Test]
    public async Task TenantLoginCannotClaimInstancePurposeOrAnotherTenant()
    {
        var row = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyTenantMutation, row.Tenant);
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, row.User, false, false));
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, row.User, true, false, Guid.CreateVersion7()));
    }

    [Test]
    public async Task DedicatedLoginCannotAcquireWithoutSerializableOwner()
    {
        var row = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, row.User, false, true));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task ForeignNamespaceOwnerGrantInvalidatesAdmission()
    {
        var row = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string schema = "email_source_control_" + Guid.CreateVersion7().ToString("N");
        await migration.Database.ExecuteSqlRawAsync(
            $"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE "{schema}".foreign_source (id integer);
            GRANT USAGE ON SCHEMA "{schema}" TO {PostgresHumanEmailSourceContract.OwnerRole};
            GRANT SELECT ON "{schema}".foreign_source TO {PostgresHumanEmailSourceContract.OwnerRole}
            """);
        try
        {
            await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, row.User, false, false));
            await Assert.That(denied!.SqlState).IsEqualTo("55000");
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync($"DROP TABLE \"{schema}\".foreign_source; DROP SCHEMA \"{schema}\"");
        }
    }

    [Test]
    public async Task RestampedEmptyProjectionDoesNotBecomeACompleteSource()
    {
        var row = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string name = PostgresHumanEmailSourceContract.ObserveFunctionSql(migration);
        string signature = name + "(uuid,integer,text,integer,uuid,text,uuid,uuid,boolean)";
        string definition = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string stamp = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION {name}(p_actor uuid,p_provider integer,p_account text,p_purpose integer,
                p_tenant uuid,p_stamp text,p_session uuid,p_resolved uuid,p_route boolean)
            RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS 'BEGIN RETURN pg_catalog.jsonb_build_object(); END';
            """);
        try
        {
            await migration.Database.ExecuteSqlRawAsync($"""
                DO $restamp$ BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', '{signature.Replace("'", "''", StringComparison.Ordinal)}',
                        'human-email-source:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex'))
                        FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure('{signature.Replace("'", "''", StringComparison.Ordinal)}'));
                END $restamp$;
                """);
            await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, row.User, false, false));
            await Assert.That(denied!.SqlState).IsEqualTo("55000");
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(definition);
            await migration.Database.ExecuteSqlRawAsync(
                $"COMMENT ON FUNCTION {signature} IS '{stamp.Replace("'", "''", StringComparison.Ordinal)}'");
        }
    }

    [Test]
    [Arguments("extra-select")]
    [Arguments("owner-login")]
    [Arguments("disabled-writer")]
    [Arguments("public-execute")]
    [Arguments("missing-anchor")]
    public async Task SourceOrAuthorityDriftIsRejectedRatherThanRepaired(string drift)
    {
        var row = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string signature = PostgresHumanEmailSourceContract.ObserveFunctionSql(migration)
            + "(uuid,integer,text,integer,uuid,text,uuid,uuid,boolean)";
        var names = new HumanPrincipalSourceSql(migration);
        string change = drift switch
        {
            "extra-select" => $"GRANT SELECT ON {names.Table(typeof(SystemSetting))} TO {PostgresHumanEmailSourceContract.OwnerRole}",
            "owner-login" => $"ALTER ROLE {PostgresHumanEmailSourceContract.OwnerRole} LOGIN",
            "disabled-writer" => $"ALTER TABLE {names.Table(typeof(User))} DISABLE TRIGGER human_principal_source_write",
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
            "missing-anchor" => $"DELETE FROM {names.Name("human_principal_source_epoch")} WHERE id = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        string restore = drift switch
        {
            "extra-select" => $"REVOKE SELECT ON {names.Table(typeof(SystemSetting))} FROM {PostgresHumanEmailSourceContract.OwnerRole}",
            "owner-login" => $"ALTER ROLE {PostgresHumanEmailSourceContract.OwnerRole} NOLOGIN",
            "disabled-writer" => $"ALTER TABLE {names.Table(typeof(User))} ENABLE ALWAYS TRIGGER human_principal_source_write",
            "public-execute" => $"REVOKE EXECUTE ON FUNCTION {signature} FROM PUBLIC",
            "missing-anchor" => $"INSERT INTO {names.Name("human_principal_source_epoch")} (id,revision) VALUES (1,false)",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        try
        {
            await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
            await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, row.User, false, false));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresHumanEmailSourceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
    }

    private async Task<Seeded> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresHumanEmailSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Email", LastName = "Source" }
        };
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Email source",
            Slug = $"email-source-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var member = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            User = user,
            UserId = user.Id,
            Tenant = tenant,
            TenantId = tenant.Id,
            StatusId = (int)TenantUserStatusEnum.Active,
            CreatedAt = DateTime.UtcNow,
            JoinedAt = DateTime.UtcNow
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Email source" },
            CreatedAt = DateTime.UtcNow
        };
        var identity = new LocalIdentityUser
        {
            Id = user.Id,
            FirstName = "Email",
            LastName = "Source",
            EmailConfirmed = true,
            PasswordHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            SecurityStamp = Guid.CreateVersion7().ToString("D")
        };
        Guid login = Guid.CreateVersion7();
        Guid operation = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var session = SupportAccessSession.Start(user.Id, tenant.Id, SupportAccessModeEnum.Write,
            "source", "Original source control", "fixture", now.AddMinutes(-1), now.AddHours(1));
        context.AddRange(user, tenant, member, actor, identity, session, new UserExternalLogin
        {
            Id = login,
            UserId = user.Id,
            User = user,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = user.Id.ToString("D"),
            CreatedAt = DateTime.UtcNow
        }, new IdentityUserToken<Guid>
        {
            UserId = user.Id,
            LoginProvider = LocalCredentialStateMetadata.TokenLoginProvider,
            Name = LocalCredentialStateMetadata.TokenName,
            Value = JsonSerializer.Serialize(new LocalCredentialStateMetadata(1, LocalCredentialState.Ready, operation, user.Id))
        }, new LocalIdentityCredentialOperation(new LocalCredentialOperationReceipt(operation,
            LocalCredentialOperationKind.Create, LocalCredentialOperationStage.ProvisioningPending,
            user.Id, user.Id, actor.Id, login, DateTime.UtcNow), Guid.CreateVersion7()));
        await context.SaveChangesAsync();
        await context.Set<LocalIdentityCredentialOperation>().Where(row => row.Id == operation)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Stage, LocalCredentialOperationStage.Replaced));
        return new(user.Id, tenant.Id, identity.SecurityStamp, session.Id);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid? tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options) { TenantContext = tenant is { } id ? new TenantIdentity(id) : null };
    }

    private async Task<string> ReadAsync(ExploreDbContext context, Guid actor, bool tenant,
        bool acquire, Guid? targetOverride = null, string? stamp = null, Guid? session = null)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = PostgresHumanEmailSourceContract.AdmissionSql(context, fixture.NativeQueueEnrollment);
        await command.ExecuteNonQueryAsync();
        command.CommandText = $"SELECT {(acquire ? PostgresHumanEmailSourceContract.AcquireFunctionSql(context)
            : PostgresHumanEmailSourceContract.ObserveFunctionSql(context))}"
            + "(@actor,@provider,@account,@purpose,@tenant,@stamp,@session,@resolved,@route)";
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = actor });
        command.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Integer) { Value = (int)AuthenticationProviderKind.Local });
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Text) { Value = actor.ToString("D") });
        command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = tenant ? 2 : 1 });
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
        { Value = (object?)targetOverride ?? (tenant ? context.TenantFilterTenantId : null) ?? (object)DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("stamp", NpgsqlDbType.Text) { Value = stamp ?? Guid.CreateVersion7().ToString("D") });
        command.Parameters.Add(new NpgsqlParameter("session", NpgsqlDbType.Uuid) { Value = (object?)session ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("resolved", NpgsqlDbType.Uuid)
        { Value = (object?)context.TenantFilterTenantId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("route", NpgsqlDbType.Boolean) { Value = true });
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("No native facts."));
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private sealed record Seeded(Guid User, Guid Tenant, string Stamp, Guid Session);
}
