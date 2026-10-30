using System.Globalization;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Learning;

/// <summary>
/// Keeps Local identity colocated with the application while independently selecting
/// external privacy authority storage. Administrative credentials arrange storage only;
/// the host receives the separately enrolled authority Runtime login.
/// </summary>
public sealed class LearningExternalAuthorityPostgresFixture : IAsyncInitializer, IAsyncDisposable
{
    internal PostgreSqlContainerFixture Application { get; } = new();
    private readonly PostgreSqlContainer authorityContainer = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("learning_privacy_authority")
        .WithUsername("postgres")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
        .Build();
    internal string AuthorityRuntimeUsername { get; } = $"learning_authority_{Guid.CreateVersion7():N}";
    private readonly string authorityRuntimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private bool disposed;

    public async Task InitializeAsync()
    {
        try
        {
            await Application.InitializeAsync();
            await authorityContainer.StartAsync();
            await using var admin = CreateAuthorityAdminDbContext();
            await admin.Database.ExecuteSqlRawAsync(PrivacyErasureAuthorityDatabaseContract.RoleProvisioningSql);
            await admin.Database.ExecuteSqlRawAsync(PrivacyErasureAuthorityDatabaseContract.RoleIsolationSql);
            // This disposable profile exercises current-schema admission, not migration parity.
            await admin.Database.EnsureCreatedAsync();
            await ExploreDatabaseMigrator.ApplyExternalPrivacyErasureAuthorityContractAsync(admin);
            await admin.Database.OpenConnectionAsync();
            await using var command = admin.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"""
                CREATE ROLE "{AuthorityRuntimeUsername}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                    NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD '{authorityRuntimePassword}';
                GRANT "{PrivacyErasureAuthorityDatabaseContract.RuntimeRole}" TO "{AuthorityRuntimeUsername}"
                    WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
                """;
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    internal Task<AuthenticatedSettingsFixture> CreateAuthenticationAsync() =>
        AuthenticatedSettingsFixture.CreateWithExternalPrivacyAuthorityAsync(
            CreateInstanceSeed, Application, ConfigureAuthority);

    private ExploreDbContext CreateInstanceSeed()
    {
        var context = Application.CreateDbContext();
        context.ClearTenantFilterBypass();
        return context;
    }

    private PrivacyErasureAuthorityDbContext CreateAuthorityAdminDbContext()
    {
        var options = TestDbContextOptions.Create<PrivacyErasureAuthorityDbContext>()
            .UseNpgsql(authorityContainer.GetConnectionString(), postgres =>
                postgres.MigrationsHistoryTable(PrivacyErasureAuthorityDatabaseConfiguration.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention().Options;
        return new PrivacyErasureAuthorityDbContext(options);
    }

    private void ConfigureAuthority(Dictionary<string, string?> configuration)
    {
        configuration["Testing:UseRealDatabase"] = "true";
        var address = new NpgsqlConnectionStringBuilder(authorityContainer.GetConnectionString());
        // These keys follow the existing dynamic fixture Environment secret authority.
        // No administrative connection or fallback credential is passed to the host.
        configuration["Database:Erasure:Provider"] = "PostgreSql";
        configuration["Database:Erasure:Host"] = address.Host;
        configuration["Database:Erasure:Port"] = address.Port.ToString(CultureInfo.InvariantCulture);
        configuration["Database:Erasure:Database"] = address.Database;
        configuration["Database:Erasure:TlsMode"] = "Disabled";
        configuration["Database:Erasure:Runtime:Username"] = AuthorityRuntimeUsername;
        configuration["Database:Erasure:Runtime:Password"] = authorityRuntimePassword;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { await Application.DisposeAsync(); }
        finally { await authorityContainer.DisposeAsync(); }
        GC.SuppressFinalize(this);
    }
}
