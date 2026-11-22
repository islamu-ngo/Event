using System.Data;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Database;

public sealed partial class ExternalTenantEnrollmentPermissionTests
{
    [Test]
    public async Task ReinstallPreservesTheCompleteAuthorityImageAndPurposeSeparation()
    {
        Guid tenant = Guid.CreateVersion7();
        await using var installer = Open(PrimaryDatabaseRole.Migrator, tenant);
        await installer.Database.OpenConnectionAsync();
        string before = await AuthorityImageAsync(installer);
        await PostgresExternalTenantEnrollmentPermissions.ApplyAsync(
            installer, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(await AuthorityImageAsync(installer)).IsEqualTo(before);

        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenant);
        await runtime.Database.OpenConnectionAsync();
        await PostgresExternalTenantEnrollmentPermissions.RequireReadyAsync(
            runtime, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresExternalTenantEnrollmentPermissions.ApplyAsync(
                runtime, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated));
        await using var global = Open(PrimaryDatabaseRole.Global, null);
        await global.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresExternalTenantEnrollmentPermissions.RequireReadyAsync(
                global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated));
        await PostgresExternalLoginReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        await PostgresUserTenantNavigationReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task ActorReadinessObservesCommittedEnrollmentDriftOnTheSameConnector()
    {
        Guid tenant = Guid.CreateVersion7();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenant);
        await runtime.Database.OpenConnectionAsync();
        await using (var original = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await PostgresAiActorContextReadContract.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);
            await original.CommitAsync();
        }

        await using var installer = Open(PrimaryDatabaseRole.Migrator, tenant);
        await installer.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(installer);
        string relation = names.Table(typeof(UserPii));
        string email = names.Column(typeof(UserPii), nameof(UserPii.Email));
        string login = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        await ExecuteAsync(installer, $"GRANT SELECT ({email}) ON {relation} TO {login}");
        try
        {
            await using var changed = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresAiActorContextReadContract.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment));
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await changed.RollbackAsync();
        }
        finally
        {
            await ExecuteAsync(installer, $"REVOKE SELECT ({email}) ON {relation} FROM {login}");
        }

        await using var restored = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await PostgresAiActorContextReadContract.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);
        await restored.CommitAsync();
    }

    [Test]
    [Arguments("partial")]
    [Arguments("table")]
    [Arguments("grant-option")]
    [Arguments("foreign-grantor")]
    [Arguments("public")]
    [Arguments("pii-read")]
    [Arguments("role-membership")]
    [Arguments("rls")]
    [Arguments("email-unique")]
    [Arguments("email-parent")]
    public async Task InstallerRejectsPermissionAndGraphDriftWithoutRepair(string change)
    {
        await using var installer = Open(PrimaryDatabaseRole.Migrator, Guid.CreateVersion7());
        await installer.Database.OpenConnectionAsync();
        await using var transaction = await installer.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(installer);
        string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string binding = names.Table(typeof(UserExternalLogin));
        string providerKey = names.Column(typeof(UserExternalLogin), nameof(UserExternalLogin.ProviderKey));
        string pii = names.Table(typeof(UserPii));
        string email = names.Column(typeof(UserPii), nameof(UserPii.Email));
        string mutation;
        if (change == "foreign-grantor")
        {
            string helper = names.Quote($"enrollment_grantor_{Guid.CreateVersion7():N}");
            mutation = $"""
                CREATE ROLE {helper} NOLOGIN NOINHERIT;
                GRANT USAGE ON SCHEMA {names.Quote(names.Schema)} TO {helper};
                GRANT SELECT ({providerKey}) ON {binding} TO {helper} WITH GRANT OPTION;
                SET LOCAL ROLE {helper};
                GRANT SELECT ({providerKey}) ON {binding} TO {runtime};
                RESET ROLE;
                """;
        }
        else if (change == "email-unique")
        {
            var index = installer.Model.FindEntityType(typeof(UserIdentityEmailClaim))!.GetIndexes()
                .Single(candidate => candidate.IsUnique
                    && candidate.Properties.Select(property => property.Name).SequenceEqual(["NormalizedEmail"]));
            mutation = $"DROP INDEX {names.Name(index.GetDatabaseName()!)}";
        }
        else if (change == "email-parent")
        {
            var foreignKey = installer.Model.FindEntityType(typeof(UserIdentityEmailEvidence))!.GetForeignKeys()
                .Single(key => key.PrincipalEntityType.ClrType == typeof(UserIdentityEmailClaim));
            mutation = $"ALTER TABLE {names.Table(typeof(UserIdentityEmailEvidence))} DROP CONSTRAINT {names.Quote(foreignKey.GetConstraintName()!)}";
        }
        else
        {
            mutation = change switch
            {
                "partial" => $"REVOKE INSERT ({email}) ON {pii} FROM {runtime}",
                "table" => $"GRANT SELECT ON {binding} TO {runtime}",
                "grant-option" => $"GRANT SELECT ({providerKey}) ON {binding} TO {runtime} WITH GRANT OPTION",
                "public" => $"GRANT SELECT ({email}) ON {pii} TO PUBLIC",
                "pii-read" => $"GRANT SELECT ({email}) ON {pii} TO {runtime}",
                "role-membership" => $"GRANT {names.Quote(fixture.NativeQueueEnrollment.GlobalLogin)} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                _ => $"ALTER TABLE {names.Table(typeof(TenantUser))} NO FORCE ROW LEVEL SECURITY"
            };
        }
        await ExecuteAsync(installer, mutation);
        string before = await AuthorityImageAsync(installer);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresExternalTenantEnrollmentPermissions.ApplyAsync(
                installer, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated));
        await Assert.That(failure!.Message).IsEqualTo("external_enrollment_permissions_drift");
        await Assert.That(await AuthorityImageAsync(installer)).IsEqualTo(before);
        await transaction.RollbackAsync();
    }

    private static async Task<string> AuthorityImageAsync(ExploreDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT COALESCE(string_agg(t.oid::text || ':' || COALESCE(t.relacl::text,'')
                || ':' || a.attnum::text || ':' || COALESCE(a.attacl::text,''),
                E'\n' ORDER BY t.oid,a.attnum),'')
            FROM pg_catalog.pg_class t
            JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
            JOIN pg_catalog.pg_attribute a ON a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped
            WHERE n.nspname=@schema
            """;
        var schema = command.CreateParameter();
        schema.ParameterName = "schema";
        schema.Value = new HumanPrincipalSourceSql(context).Schema;
        command.Parameters.Add(schema);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
