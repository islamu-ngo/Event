using System.Security.Cryptography;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed partial class AuthenticatedSettingsFixture
{
    internal static Task<AuthenticatedSettingsFixture> CreateAsync(
        Func<ExploreDbContext> createDatabase, PostgreSqlContainerFixture? postgres,
        IInterceptor? interceptor = null)
    {
        return CreateCoreAsync(createDatabase, interceptor, postgres is null ? null : ConfigureAsync);

        async Task ConfigureAsync(ExploreDbContext seed, Dictionary<string, string?> configuration)
        {
            foreach (var role in new[]
            {
                PrimaryDatabaseRole.Migrator, PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global,
                PrimaryDatabaseRole.EmailPolicyInstanceMutation, PrimaryDatabaseRole.EmailPolicyTenantMutation,
                PrimaryDatabaseRole.InstanceKeyRead, PrimaryDatabaseRole.InstanceKeyWrite,
                PrimaryDatabaseRole.PlatformKeyReport
            })
            {
                var options = postgres!.GetNativeQueueOptions(role,
                    seed.Model.FindEntityType(typeof(SystemSetting))!.GetSchema());
                configuration["Database:Provider"] = "PostgreSql";
                configuration["Database:Host"] = options.Host;
                configuration["Database:Port"] = options.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                configuration["Database:Database"] = options.Database;
                configuration["Database:Schema"] = options.Schema;
                configuration[$"Database:{role}:Username"] = options.Username;
                configuration[$"Database:{role}:Password"] = options.Password;
                configuration[$"Database:{role}:TlsMode"] = "Disabled";
            }
            // Retain the separately enrolled production issuance capability, never an alias
            // to Global, Runtime or the key-management writer.
            string issuanceLogin = $"settings_issuance_{Guid.CreateVersion7():N}";
            string issuancePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await PostgresInstanceApiKeyIssuanceContract.ApplyAsync(seed);
            await seed.Database.OpenConnectionAsync();
            await using (var command = seed.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = $"CREATE ROLE \"{issuanceLogin}\" LOGIN NOSUPERUSER NOCREATEDB "
                    + $"NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD '{issuancePassword}'; "
                    + $"GRANT \"{PostgresInstanceApiKeyIssuanceContract.ExecutorRole}\" TO \"{issuanceLogin}\" "
                    + "WITH INHERIT TRUE, SET FALSE, ADMIN FALSE";
                await command.ExecuteNonQueryAsync();
            }
            await seed.Database.CloseConnectionAsync();
            configuration["Database:InstanceKeyIssuance:Username"] = issuanceLogin;
            configuration["Database:InstanceKeyIssuance:Password"] = issuancePassword;
            configuration["Database:InstanceKeyIssuance:TlsMode"] = "Disabled";
        }
    }

    internal static async Task<AuthenticatedSettingsFixture> CreatePolicyAsync(
        PostgreSqlContainerFixture postgres, string schema)
    {
        await using (var original = postgres.CreateDbContext())
        await using (var seed = postgres.CreateDbContextForSchema(schema))
        {
            // The fence fixture intentionally begins with only three source tables. Add
            // real identity/authority tables before authenticating; never substitute a
            // principal-source fence or grant an anonymous settings capability.
            var sql = seed.GetService<ISqlGenerationHelper>();
            string[] existing =
            [
                seed.Model.FindEntityType(typeof(RolePermission))!.GetTableName()!,
                seed.Model.FindEntityType(typeof(SystemSetting))!.GetTableName()!,
                seed.Model.FindEntityType(typeof(TenantSetting))!.GetTableName()!
            ];
            await seed.Database.OpenConnectionAsync();
            foreach (var table in original.Model.GetRelationalModel().Tables.Where(table => !existing.Contains(table.Name)))
            {
                await using var command = seed.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"CREATE TABLE {sql.DelimitIdentifier(table.Name, schema)} "
                    + $"(LIKE {sql.DelimitIdentifier(table.Name, table.Schema)} INCLUDING ALL)";
                await command.ExecuteNonQueryAsync();
            }
            await seed.Database.CloseConnectionAsync();
            await PostgresModelConstraintApplier.ApplyAsync(seed);
            await PostgresTenantRowLevelSecurityModel.ApplyAsync(seed);
            await PostgresExternalLoginReadPermissions.ApplyAsync(seed, postgres.NativeQueueEnrollment);
            await PostgresAdministrativeAuthorityContract.ApplyAsync(seed);
            await PostgresHumanAdministratorCommitAuthorityContract.ApplyAsync(seed, postgres.NativeQueueEnrollment);
            await PostgresPolicySourceSetContract.ApplyAsync(seed, postgres.NativeQueueEnrollment);
            await PostgresHumanPrincipalSourceContract.ApplyAsync(seed, postgres.NativeQueueEnrollment);
            await PostgresHumanAuthorizationRouteContract.ApplyAsync(seed, postgres.NativeQueueEnrollment);
            await PostgresHumanSupportAccessSourceContract.ApplyAsync(seed, postgres.NativeQueueEnrollment);
            await PostgresOrdinarySettingsContract.ApplyAsync(seed, postgres.NativeQueueEnrollment);
            await PostgresMixedSettingsContract.ApplyAsync(seed, postgres.NativeQueueEnrollment);
            await LookupTableSeeder.SeedAsync(seed);
        }
        var fixture = await CreateAsync(() => postgres.CreateDbContextForSchema(schema), postgres);
        try
        {
            await using var seed = postgres.CreateDbContextForSchema(schema);
            // Preserve the absent-route/empty-settings precondition after Local sign-in.
            await seed.SystemSettings.ExecuteDeleteAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }
}
