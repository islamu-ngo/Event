using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresManagedTenantDomainAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false, true, false, false)]
    [Arguments(true, true, false, false)]
    [Arguments(false, false, false, false)]
    [Arguments(false, true, true, false)]
    [Arguments(false, true, false, true)]
    public async Task ForeignReservedHostIsFoundWithoutBorrowingRequestTenantAuthority(
        bool absentTenant, bool capability, bool tamperPolicy, bool delegateRuntime)
    {
        await using var rows = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        string host = $"managed-{Guid.CreateVersion7():N}.example.test";
        string privateHost = $"private-{Guid.CreateVersion7():N}.example.test";
        string table;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.B,
                Tenant = null!,
                SettingKey = "domains.tenant_custom_domain",
                Value = SettingValueSerializer.Serialize(host.ToUpperInvariant() + "."),
                CreatedAt = DateTime.UtcNow
            });
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(), TenantId = rows.B, Tenant = null!,
                SettingKey = "email.smtp_host", Value = SettingValueSerializer.Serialize(privateHost),
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
            var entity = seed.Model.FindEntityType(typeof(TenantSetting))!;
            table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
        }
        string role = rows.RuntimeOptions.Username ?? throw new InvalidOperationException("The native fixture requires a Runtime login.");
        string globalRole = $"managed_domain_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var options = rows.RuntimeOptions with { Role = PrimaryDatabaseRole.Global, Username = globalRole, Password = password };
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var grant = administration.CreateCommand())
        {
            grant.CommandText = $"""
                GRANT SELECT ON {table} TO "{role}";
                CREATE ROLE "{globalRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
                GRANT USAGE ON SCHEMA "{rows.Schema}" TO "{globalRole}";
                """;
            if (capability)
                grant.CommandText += $"\nGRANT \"{PostgresManagedTenantDomainContract.GlobalRole}\" TO \"{globalRole}\" WITH INHERIT TRUE, SET FALSE;";
            await grant.ExecuteNonQueryAsync();
        }
        try
        {
            await using var context = rows.Context(absentTenant ? null : rows.A);
            await rows.AssertOrdinaryRoleAsync(context);
            await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
                .AnyAsync(setting => setting.TenantId == rows.B)).IsFalse();
            if (tamperPolicy || delegateRuntime)
            {
                await using var change = administration.CreateCommand();
                change.CommandText = tamperPolicy
                    ? $"ALTER POLICY managed_domain_authority ON {table} USING (true)"
                    : $"GRANT \"{PostgresManagedTenantDomainContract.GlobalRole}\" TO \"{role}\" WITH INHERIT TRUE, SET FALSE";
                await change.ExecuteNonQueryAsync();
                await using var rejected = delegateRuntime
                    ? rows.Context(rows.A)
                    : await new GlobalExploreDbContextFactory(options).CreateDbContextAsync();
                var denied = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await rejected.Database.OpenConnectionAsync());
                await Assert.That(denied!.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
                return;
            }
            var factory = new GlobalExploreDbContextFactory(options);
            await using (var global = await factory.CreateDbContextAsync())
            {
                if (capability)
                {
                    var visible = await global.TenantSettingOverrides.IgnoreQueryFilters().AsNoTracking().ToListAsync();
                    await Assert.That(visible.All(setting => setting.SettingKey is
                        "domains.tenant_subdomain" or "domains.tenant_custom_domain")).IsTrue();
                    await Assert.That(visible.Any(setting => setting.Value == SettingValueSerializer.Serialize(privateHost))).IsFalse();
                    var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                        await global.TenantSettingOverrides.IgnoreQueryFilters()
                            .Where(setting => setting.Id == Guid.Empty).ExecuteDeleteAsync());
                    await Assert.That(denied!.SqlState).IsEqualTo("42501");
                }
                else
                {
                    var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                        await global.TenantSettingOverrides.IgnoreQueryFilters().AsNoTracking().ToListAsync());
                    await Assert.That(denied!.SqlState).IsEqualTo("42501");
                }
                await Assert.That(global.TenantContext).IsNull();
            }
            var repository = new ManagedTenantDomainRepository(factory);
            if (!capability)
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await repository.GetByDomainHostAsync(host));
                await Assert.That(denied!.SqlState).IsEqualTo("42501");
                return;
            }
            TenantSetting? collision = await repository.GetByDomainHostAsync("  " + host + ".  ");
            await Assert.That(collision).IsNotNull();
            await Assert.That(collision!.TenantId).IsEqualTo(rows.B);
            await Assert.That(await repository.GetByDomainHostAsync(privateHost)).IsNull();
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(absentTenant ? (Guid?)null : rows.A);
        }
        finally
        {
            if (tamperPolicy)
            {
                await using var installer = fixture.CreateDbContext();
                await PostgresManagedTenantDomainContract.ApplyAsync(installer);
            }
            using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE SELECT ON {table} FROM "{role}";
                REVOKE USAGE ON SCHEMA "{rows.Schema}" FROM "{globalRole}";
                """;
            if (capability)
                revoke.CommandText += $"\nREVOKE \"{PostgresManagedTenantDomainContract.GlobalRole}\" FROM \"{globalRole}\";";
            if (delegateRuntime)
                revoke.CommandText += $"\nREVOKE \"{PostgresManagedTenantDomainContract.GlobalRole}\" FROM \"{role}\";";
            revoke.CommandText += $"\nDROP ROLE \"{globalRole}\";";
            await revoke.ExecuteNonQueryAsync();
        }
    }
}
