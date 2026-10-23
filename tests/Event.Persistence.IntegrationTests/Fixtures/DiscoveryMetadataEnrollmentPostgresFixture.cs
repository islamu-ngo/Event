using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>
/// Fresh model/RLS deployment with original quoted identities, without unrelated native capabilities.
/// Metadata permissions are installed by the test, not hidden in fixture initialization.
/// </summary>
internal sealed class DiscoveryMetadataEnrollmentPostgresFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;
    private readonly Dictionary<PrimaryDatabaseRole, (string Username, string Password)> _logins;

    internal DiscoveryMetadataEnrollmentPostgresFixture(PrimaryDatabaseRole quotedRole)
    {
        if (quotedRole is not (PrimaryDatabaseRole.Runtime or PrimaryDatabaseRole.Global))
            throw new ArgumentOutOfRangeException(nameof(quotedRole));
        _container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("discovery_metadata_enrollment")
            .WithUsername("postgres")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
            .Build();
        string suffix = Guid.CreateVersion7().ToString("N")[..12];
        _logins = new[] { PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global,
            PrimaryDatabaseRole.EmailPolicyInstanceMutation, PrimaryDatabaseRole.EmailPolicyTenantMutation }
            .ToDictionary(role => role, role => (
                Username: role == quotedRole ? $"Meta$guard$'\\_{suffix}" : $"metadata_{(int)role}_{suffix}",
                Password: Convert.ToHexString(RandomNumberGenerator.GetBytes(24))));
        Enrollment = new("postgres", _logins[PrimaryDatabaseRole.Global].Username,
            _logins[PrimaryDatabaseRole.Runtime].Username,
            _logins[PrimaryDatabaseRole.EmailPolicyInstanceMutation].Username,
            _logins[PrimaryDatabaseRole.EmailPolicyTenantMutation].Username);
        Enrollment.Validate();
    }

    internal PostgresNativeQueueEnrollment Enrollment { get; }
    internal Guid FirstTenant { get; } = Guid.CreateVersion7();
    internal Guid SecondTenant { get; } = Guid.CreateVersion7();

    internal async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            foreach (var login in _logins.Values)
            {
                await using var command = connection.CreateCommand();
                string identifier = "\"" + login.Username.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
                string password = "'" + login.Password.Replace("'", "''", StringComparison.Ordinal) + "'";
                command.CommandText = $"CREATE ROLE {identifier} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD {password}";
                await command.ExecuteNonQueryAsync();
            }
        }

        await using var migration = CreateMigrator();
        await migration.Database.MigrateAsync();
        await PostgresModelConstraintApplier.ApplyAsync(migration);
        await PostgresTenantRowLevelSecurityModel.ApplyAsync(migration);

        // Migrator-only fixture rows exercise real FK/check/RLS storage. This is
        // not an application creator workflow or a fabricated creation receipt.
        var names = new HumanPrincipalSourceSql(migration);
        string Columns<T>(params string[] properties) => string.Join(',', properties.Select(property =>
            names.Column(typeof(T), property)));
        await migration.Database.ExecuteSqlRawAsync($"""
            INSERT INTO {names.Table(typeof(TenantStatus))}
                ({Columns<TenantStatus>("Id", "MasterCode", "FullName", "IsActiveState")})
            VALUES (@status, 'ACTIVE', 'Active', TRUE)
            """, new NpgsqlParameter("status", (int)TenantStatusEnum.Active));
        foreach (Guid tenant in new[] { FirstTenant, SecondTenant })
        {
            await migration.Database.ExecuteSqlRawAsync($"""
                INSERT INTO {names.Table(typeof(Tenant))}
                    ({Columns<Tenant>("Id", "FullName", "Slug", "TenantStatusId", "CreatedAt")})
                VALUES (@tenant, 'Metadata enrollment fixture', @slug, @status, @created)
                """, new NpgsqlParameter("tenant", tenant), new NpgsqlParameter("slug", $"metadata-{tenant:N}"),
                new NpgsqlParameter("status", (int)TenantStatusEnum.Active), new NpgsqlParameter("created", DateTime.UtcNow));
            await migration.Database.ExecuteSqlRawAsync($"""
                INSERT INTO {names.Table(typeof(EventDiscoveryRevision))}
                    ({Columns<EventDiscoveryRevision>("Id", "TenantId", "IdentityEpoch", "DisclosureEpoch")})
                VALUES (@id, @tenant, 7, 11)
                """, new NpgsqlParameter("id", Guid.CreateVersion7()), new NpgsqlParameter("tenant", tenant));
        }
    }

    internal ExploreDbContext CreateMigrator()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, Options(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(builder.Options);
        context.BindTenantLease();
        return context;
    }

    internal Task<ExploreDbContext> CreateGlobalAsync() =>
        new GlobalExploreDbContextFactory(Options(PrimaryDatabaseRole.Global)).CreateDbContextAsync();

    internal ExploreDbContext CreateRuntime(Guid tenantId)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, Options(PrimaryDatabaseRole.Runtime));
        builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        var context = new ExploreDbContext(builder.Options) { TenantContext = new SelectedTenant(tenantId) };
        context.BindTenantLease();
        return context;
    }

    private PrimaryDatabaseConnectionOptions Options(PrimaryDatabaseRole role)
    {
        var container = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        return new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = container.Host,
            Port = container.Port,
            Database = container.Database,
            Schema = RelationalModelNamespace.DefaultSchema,
            Username = role == PrimaryDatabaseRole.Migrator ? container.Username : _logins[role].Username,
            Password = role == PrimaryDatabaseRole.Migrator ? container.Password : _logins[role].Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        };
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
