using System.Data;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

/// <summary>Real restricted-login regression source for prepared human route observations.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanAuthorizationRouteSourceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RestrictedObservationReturnsOnlyFixedKeysAndCannotReadUnrelatedSettings()
    {
        await InstallAsync();
        await using var owner = Open();
        var source = new HumanAuthorizationRouteSource(owner, await SubjectAsync(),
            PolicySourceSetPurpose.Global, fixture.NativeQueueEnrollment);
        var facts = await source.ObserveAsync();
        await Assert.That(facts.Settings.Length).IsEqualTo(6);
        await Assert.That(facts.Settings.All(row => HumanAuthorizationRouteFacts.Keys.Contains(row.Key))).IsTrue();
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await owner.SystemSettings.AsNoTracking().ToListAsync());
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task RestrictedObservationPreservesNonNullAllowedValuesJson()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string key = GovernanceSettingKeys.Security.AuthorizationProvider;
        var setting = await migration.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
        bool inserted = setting is null;
        string? original = setting?.AllowedValues;
        if (setting is null)
        {
            setting = new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = "\"local\"",
                ValueType = SettingValueType.String,
                CreatedAt = DateTime.UtcNow
            };
            migration.SystemSettings.Add(setting);
        }
        setting.AllowedValues = "[\"local\",\"cerbos\"]";
        await migration.SaveChangesAsync();
        try
        {
            await using var owner = Open();
            var source = new HumanAuthorizationRouteSource(owner, await SubjectAsync(),
                PolicySourceSetPurpose.Global, fixture.NativeQueueEnrollment);
            var facts = await source.ObserveAsync();
            var selected = facts.Settings.Single(row => row.Key == key).System!;
            using var allowed = System.Text.Json.JsonDocument.Parse(selected.AllowedValues!);
            await Assert.That(allowed.RootElement.GetArrayLength()).IsEqualTo(2);
            await Assert.That(allowed.RootElement[0].GetString()).IsEqualTo("local");
            await Assert.That(allowed.RootElement[1].GetString()).IsEqualTo("cerbos");
        }
        finally
        {
            if (inserted) migration.SystemSettings.Remove(setting);
            else setting.AllowedValues = original;
            await migration.SaveChangesAsync();
        }
    }

    [Test]
    public async Task HeldOriginalOwnerExcludesExternalRouteChangesAndRejectsEndedLease()
    {
        await InstallAsync();
        await using var owner = Open();
        var source = new HumanAuthorizationRouteSource(owner, await SubjectAsync(),
            PolicySourceSetPurpose.Global, fixture.NativeQueueEnrollment);
        var fence = new PolicySourceSetFence(owner, PolicySourceSetPurpose.Global, fixture.NativeQueueEnrollment);
        var prepared = await source.ObserveAsync();
        var deployment = new HumanAuthorizationRouteDeployment("cerbos", "http://localhost:3593", false);
        var route = HumanAuthorizationRouteProjector.Project(prepared, deployment, settingsGovernance: true);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await source.AcquireAsync(await fence.AcquireAsync());
        await Assert.That(HumanAuthorizationRouteProjector.Project(lease.Facts, deployment, true)).IsEqualTo(route);
        // The genuine migrator attempts an upsert even if the route row was absent.
        await using var writer = fixture.CreateDbContext();
        await using var attempt = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
        var blocked = await Assert.ThrowsAsync<PostgresException>(async () =>
            await writer.Database.ExecuteSqlRawAsync(
                $$"""
                INSERT INTO {{Table(writer, typeof(SystemSetting))}}
                (id,setting_key,value,setting_value_type_id,is_locked,display_order,created_at)
                VALUES ({0},{1},{2},0,false,0,CURRENT_TIMESTAMP)
                ON CONFLICT (setting_key) DO UPDATE SET value = EXCLUDED.value
                """,
                Guid.CreateVersion7(), GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\""));
        await Assert.That(blocked!.SqlState).IsEqualTo("55P03");
        await attempt.RollbackAsync();
        await Assert.That(HumanAuthorizationRouteProjector.Project(await lease.ValidateAsync(), deployment, true))
            .IsEqualTo(route);
        await transaction.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync());
    }

    [Test]
    public async Task ObservationInsideTransactionAndReadCommittedAcquisitionAreRejected()
    {
        await InstallAsync();
        await using var owner = Open();
        var source = new HumanAuthorizationRouteSource(owner, await SubjectAsync(),
            PolicySourceSetPurpose.Global, fixture.NativeQueueEnrollment);
        var fence = new PolicySourceSetFence(owner, PolicySourceSetPurpose.Global, fixture.NativeQueueEnrollment);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ObserveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fence.AcquireAsync());
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task NativeBodyReplacementWithMatchingCommentStillFailsExactAttestation()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string routine = PostgresHumanAuthorizationRouteContract.ObserveFunctionSql(migration);
        await migration.Database.OpenConnectionAsync();
        await using var read = migration.Database.GetDbConnection().CreateCommand();
        read.CommandText = $"SELECT pg_catalog.pg_get_functiondef('{routine}(integer,uuid)'::regprocedure)";
        string original = (string)(await read.ExecuteScalarAsync())!;
        read.CommandText = $"SELECT pg_catalog.obj_description('{routine}(integer,uuid)'::regprocedure,'pg_proc')";
        string comment = (string)(await read.ExecuteScalarAsync())!;
        await using var tampering = await migration.Database.BeginTransactionAsync();
        string schema = migration.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(migration.Model.FindEntityType(typeof(SystemSetting))!.GetSchema()!);
        await migration.Database.ExecuteSqlRawAsync($"""
            GRANT CREATE ON SCHEMA {schema} TO {PostgresHumanAuthorizationRouteContract.OwnerRole};
            SET LOCAL ROLE {PostgresHumanAuthorizationRouteContract.OwnerRole};
            CREATE OR REPLACE FUNCTION {routine}(p_purpose integer,p_tenant uuid) RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS 'BEGIN RETURN ''[]''::jsonb; END';
            """);
        await migration.Database.ExecuteSqlRawAsync($"""
            DO $drift$ DECLARE body text; BEGIN
                SELECT prosrc INTO body FROM pg_catalog.pg_proc
                    WHERE oid = pg_catalog.to_regprocedure('{routine}(integer,uuid)');
                EXECUTE pg_catalog.format('COMMENT ON FUNCTION {routine}(integer,uuid) IS %L',
                    'human-authorization-route:v1:' ||
                    pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(body,'UTF8')),'hex'));
            END $drift$;
            SET LOCAL ROLE NONE;
            REVOKE CREATE ON SCHEMA {schema} FROM {PostgresHumanAuthorizationRouteContract.OwnerRole};
            """);
        // Publish the modified function to the real restricted login; restore it in finally.
        await tampering.CommitAsync();
        try
        {
            await using var owner = Open();
            var source = new HumanAuthorizationRouteSource(owner, await SubjectAsync(),
                PolicySourceSetPurpose.Global, fixture.NativeQueueEnrollment);
            await Assert.ThrowsAsync<PostgresException>(() => source.ObserveAsync());
        }
        finally
        {
            await using var restore = await migration.Database.BeginTransactionAsync();
            await migration.Database.ExecuteSqlRawAsync(
                $"GRANT CREATE ON SCHEMA {schema} TO {PostgresHumanAuthorizationRouteContract.OwnerRole}");
            await migration.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {PostgresHumanAuthorizationRouteContract.OwnerRole}");
            await migration.Database.ExecuteSqlRawAsync(original);
            await migration.Database.ExecuteSqlRawAsync(
                $"COMMENT ON FUNCTION {routine}(integer,uuid) IS '{comment.Replace("'", "''", StringComparison.Ordinal)}'");
            await migration.Database.ExecuteSqlRawAsync(
                $"SET LOCAL ROLE NONE; REVOKE CREATE ON SCHEMA {schema} FROM {PostgresHumanAuthorizationRouteContract.OwnerRole}");
            await restore.CommitAsync();
        }
    }

    private async Task InstallAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresPolicySourceSetContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresHumanAuthorizationRouteContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext Open()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options);
    }

    private static async Task<HumanCommitSubject> SubjectAsync() =>
        await HumanCommitSubject.ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "route-source-regression"), new Claim("iss", "https://route.invalid"),
                new Claim("auth_provider", "keycloak")], "fixture")), new ResolvedIdentity(), CancellationToken.None)
        ?? throw new InvalidOperationException("Resolved account required.");

    // Route reads do not enroll or grant this account. Principal fencing remains a parent-owned contract.
    private sealed class ResolvedIdentity : IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>
    {
        public Task<Guid?> QueryAsync(ResolveCurrentUserIdByIdentityRequest query, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"));
    }

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
}
