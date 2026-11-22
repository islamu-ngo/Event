using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Health;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TenantLookupReadOwnershipTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task LookupReadsActiveDirectoryAndResolvesDomainsWithoutPromotingRuntimeCaller()
    {
        Guid firstId = Guid.CreateVersion7();
        Guid secondId = Guid.CreateVersion7();
        Guid inactiveId = Guid.CreateVersion7();
        Guid[] ownedIds = [firstId, secondId, inactiveId];
        string firstSlug = $"lookup-{firstId:N}";
        string secondSlug = $"lookup-{secondId:N}";
        string inactiveSlug = $"lookup-{inactiveId:N}";
        string firstDomain = $"{firstSlug}.example.test";
        string secondDomain = $"{secondSlug}.example.test";
        string inactiveDomain = $"{inactiveSlug}.example.test";

        var services = new ServiceCollection();
        services.AddSingleton<IGlobalExploreDbContextFactory>(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
        services.AddSingleton(fixture.NativeQueueEnrollment);
        services.AddScoped<ITenantLookupSource, TenantLookupSource>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<ITenantLookupSource>();
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync();
        bool legacyMembershipRevoked = false;

        try
        {
            await SeedTenant(firstId, firstSlug, firstDomain, TenantStatusEnum.Active);
            await SeedTenant(secondId, secondSlug, secondDomain, TenantStatusEnum.Active);
            await SeedTenant(inactiveId, inactiveSlug, inactiveDomain, TenantStatusEnum.Suspended);

            // The shared fixture grants an obsolete role that production does not.
            // Remove only that membership so its extra authority cannot mask the Red.
            await using (var migrator = CreateMigrator())
            {
                var sql = migrator.GetService<ISqlGenerationHelper>();
                string legacyRole = sql.DelimitIdentifier(PostgresTenantLookupContract.GlobalRole);
                string globalLogin = sql.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
                await ExecuteAsync(migrator, $"REVOKE {legacyRole} FROM {globalLogin}");
                legacyMembershipRevoked = true;
            }

            var revoked = await Assert.ThrowsAsync<PostgresException>(async () =>
                await source.GetTenantLookupsAsync());
            await Assert.That(revoked?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await using (var migrator = CreateMigrator())
            {
                var sql = migrator.GetService<ISqlGenerationHelper>();
                string role = sql.DelimitIdentifier(PostgresTenantLookupContract.GlobalRole);
                string global = sql.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
                await ExecuteAsync(migrator,
                    $"GRANT {role} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE");
                legacyMembershipRevoked = false;
            }

            // Canonical startup must enroll this same fixed read purpose; the
            // fresh migration test exercises that without the fixture grant.
            var directory = await source.GetTenantLookupsAsync();
            var owned = directory.Where(row => ownedIds.Contains(row.TenantId)).ToList();
            await Assert.That(owned.Count).IsEqualTo(2);
            var first = owned.Single(row => row.TenantId == firstId);
            var second = owned.Single(row => row.TenantId == secondId);
            await Assert.That(first.Slug).IsEqualTo(firstSlug);
            await Assert.That(first.Subdomain).IsEqualTo(firstSlug);
            await Assert.That(first.CustomDomain).IsEqualTo(firstDomain);
            await Assert.That(second.Slug).IsEqualTo(secondSlug);
            await Assert.That(second.Subdomain).IsEqualTo(secondSlug);
            await Assert.That(second.CustomDomain).IsEqualTo(secondDomain);
            await Assert.That(directory.Any(row => row.TenantId == inactiveId)).IsFalse();

            await Assert.That(await source.GetCurrentDomainTenantAsync(firstDomain.ToUpperInvariant(), true))
                .IsEqualTo((Guid?)firstId);
            await Assert.That(await source.GetCurrentDomainTenantAsync(firstSlug.ToUpperInvariant(), false))
                .IsEqualTo((Guid?)firstId);
            await Assert.That(await source.GetCurrentDomainTenantAsync(secondDomain.ToUpperInvariant(), true))
                .IsEqualTo((Guid?)secondId);
            await Assert.That(await source.GetCurrentDomainTenantAsync(secondSlug.ToUpperInvariant(), false))
                .IsEqualTo((Guid?)secondId);
            await Assert.That(await source.GetCurrentDomainTenantAsync(inactiveDomain.ToUpperInvariant(), true))
                .IsNull();
            await Assert.That(await source.GetCurrentDomainTenantAsync(inactiveSlug.ToUpperInvariant(), false))
                .IsNull();

            await using (var migrator = CreateMigrator(secondId))
            {
                await migrator.TenantSettingOverrides.IgnoreQueryFilters()
                    .Where(row => row.TenantId == secondId
                        && row.SettingKey == GovernanceSettingKeys.Domains.TenantCustomDomain)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        row => row.Value, SettingValueSerializer.Serialize(firstDomain.ToUpperInvariant())));
                await migrator.TenantSettingOverrides.IgnoreQueryFilters()
                    .Where(row => row.TenantId == secondId
                        && row.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        row => row.Value, SettingValueSerializer.Serialize(firstSlug.ToUpperInvariant())));
            }
            await Assert.That(await source.GetCurrentDomainTenantAsync(firstDomain, true)).IsNull();
            await Assert.That(await source.GetCurrentDomainTenantAsync(firstSlug, false)).IsNull();

            string tenantTable = new HumanPrincipalSourceSql(runtime).Table(typeof(Tenant));
            var privateReadDenial = await Assert.ThrowsAsync<PostgresException>(async () =>
                await ExecuteAsync(runtime, $"SELECT description FROM {tenantTable} WHERE id = @id",
                    new NpgsqlParameter("id", firstId)));
            await Assert.That(privateReadDenial?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

            // Runtime may reject the table outright or hide all foreign settings
            // through RLS. Neither outcome may expose the seeded general data.
            try
            {
                var leaked = await runtime.TenantSettingOverrides.IgnoreQueryFilters()
                    .Where(row => ownedIds.Contains(row.TenantId))
                    .Select(row => row.Value).ToListAsync();
                await Assert.That(leaked).IsEmpty();
            }
            catch (PostgresException denial) when (denial.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
            }
            await Assert.That(runtime.TenantFilterTenantId).IsNull();
            await Assert.That(runtime.IsTenantFilterBypassed).IsFalse();
            await Assert.That(runtime.CurrentUserService).IsNull();
            await Assert.That(runtime.Database.CurrentTransaction).IsNull();
            await Assert.That(runtime.ChangeTracker.Entries()).IsEmpty();
        }
        finally
        {
            await using var cleanup = CreateMigrator();
            try
            {
                await cleanup.TenantSettingOverrides.IgnoreQueryFilters()
                    .Where(row => ownedIds.Contains(row.TenantId)).ExecuteDeleteAsync();
                await cleanup.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
                    .Where(row => ownedIds.Contains(row.TenantId)).ExecuteDeleteAsync();
                await cleanup.Tenants.Where(row => ownedIds.Contains(row.Id)).ExecuteDeleteAsync();
            }
            finally
            {
                if (legacyMembershipRevoked)
                {
                    var sql = cleanup.GetService<ISqlGenerationHelper>();
                    string legacyRole = sql.DelimitIdentifier(PostgresTenantLookupContract.GlobalRole);
                    string globalLogin = sql.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
                    await ExecuteAsync(cleanup,
                        $"GRANT {legacyRole} TO {globalLogin} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE");
                }
            }
        }
    }

    private static async Task ExecuteAsync(
        ExploreDbContext context, string sql, params NpgsqlParameter[] parameters)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedTenant(Guid id, string slug, string domain, TenantStatusEnum status)
    {
        await using var migrator = CreateMigrator(id);
        var tenant = new Tenant
        {
            Id = id,
            FullName = $"Lookup ownership {id:N}",
            Slug = slug,
            Description = $"Private tenant description {id:N}",
            TenantStatusId = (int)status,
            TenantStatus = null!
        };
        migrator.Tenants.Add(tenant);
        migrator.TenantSettingOverrides.AddRange(
            Setting(GovernanceSettingKeys.Domains.TenantSubdomain, slug),
            Setting(GovernanceSettingKeys.Domains.TenantCustomDomain, domain),
            Setting(GovernanceSettingKeys.Branding.DisplayName, $"Private branding {id:N}"));
        await migrator.SaveChangesAsync();

        TenantSetting Setting(string key, string value) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = id,
            Tenant = tenant,
            SettingKey = key,
            Value = SettingValueSerializer.Serialize(value)
        };
    }

    private ExploreDbContext CreateMigrator(Guid? tenantId = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options)
        {
            TenantContext = tenantId is { } id ? new SeedTenantContext(id) : null
        };
        context.BindTenantLease();
        return context;
    }

    private sealed record SeedTenantContext(Guid TenantId) : ITenantContext;
}
