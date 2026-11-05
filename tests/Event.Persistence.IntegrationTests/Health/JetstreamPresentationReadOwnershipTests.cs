using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services.Federation;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Health;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class JetstreamPresentationReadOwnershipTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task PresentationReadReturnsOnlyEnabledActiveTenantsWithoutPromotingRuntimeCaller()
    {
        await fixture.ResetAsync();
        Guid enabledActive = Guid.CreateVersion7();
        Guid disabledActive = Guid.CreateVersion7();
        Guid enabledSuspended = Guid.CreateVersion7();
        DateTime observedAt = DateTime.UtcNow;
        string capabilityKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;

        AtprotoFederationInstanceSetting? originalCapability;
        SystemSetting? originalMode;
        await using (var snapshot = CreateSeedContext())
        {
            originalCapability = await snapshot.Set<AtprotoFederationInstanceSetting>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.SettingKey == capabilityKey);
            originalMode = await snapshot.SystemSettings.AsNoTracking()
                .SingleOrDefaultAsync(row => row.SettingKey == GovernanceSettingKeys.Deployment.Mode);
        }

        try
        {
            await using (var seed = CreateSeedContext())
            {
                foreach (var (id, status) in new[]
                         {
                             (enabledActive, TenantStatusEnum.Active),
                             (disabledActive, TenantStatusEnum.Active),
                             (enabledSuspended, TenantStatusEnum.Suspended)
                         })
                {
                    seed.Add(new Tenant
                    {
                        Id = id,
                        FullName = "Jetstream presentation ownership tenant",
                        Slug = $"jetstream-presentation-{id:N}",
                        TenantStatusId = (int)status,
                        TenantStatus = null!,
                        CreatedAt = observedAt
                    });
                }

                var capability = await seed.Set<AtprotoFederationInstanceSetting>()
                    .SingleOrDefaultAsync(row => row.SettingKey == capabilityKey);
                if (capability is null)
                {
                    seed.Add(AtprotoFederationInstanceSetting.Create(
                        capabilityKey, "false", false, observedAt, null));
                }
                else
                {
                    capability.SetValue("false", observedAt, null);
                    capability.SetLock(false, observedAt, null);
                }

                var deploymentMode = await seed.SystemSettings.SingleOrDefaultAsync(
                    row => row.SettingKey == GovernanceSettingKeys.Deployment.Mode);
                if (deploymentMode is null)
                {
                    seed.SystemSettings.Add(new SystemSetting
                    {
                        Id = Guid.CreateVersion7(),
                        SettingKey = GovernanceSettingKeys.Deployment.Mode,
                        Value = "\"MultiTenant\"",
                        ValueType = SettingValueType.String,
                        CreatedAt = observedAt
                    });
                }
                else
                {
                    deploymentMode.Value = "\"MultiTenant\"";
                }

                await seed.SaveChangesAsync();
            }

            foreach (var (tenantId, enabled) in new[]
                     {
                         (enabledActive, "true"),
                         (disabledActive, "false"),
                         (enabledSuspended, "true")
                     })
            {
                await using var seed = CreateSeedContext(tenantId);
                seed.Add(AtprotoFederationTenantSetting.Create(
                    tenantId, capabilityKey, enabled, false, observedAt, null));
                await seed.SaveChangesAsync();
            }

            await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
            runtime.BindTenantLease();
            var globalFactory = new GlobalExploreDbContextFactory(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
            var resolver = new AtprotoJetstreamTenantPresentationResolver(
                new InstanceSystemSettingsReader(globalFactory, fixture.NativeQueueEnrollment),
                new AtprotoFederationGlobalReadRepository(globalFactory, fixture.NativeQueueEnrollment));

            try
            {
                IReadOnlyList<Guid> enabled = await resolver.ResolveEnabledTenantIdsAsync(CancellationToken.None);

                await Assert.That(enabled).IsEquivalentTo([enabledActive]);

                string table = new HumanPrincipalSourceSql(runtime).Table(typeof(SystemSetting));
                var denial = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await runtime.Database.ExecuteSqlRawAsync($"SELECT value FROM {table}"));
                await Assert.That(denial.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
            finally
            {
                await Assert.That(runtime.TenantFilterTenantId).IsNull();
                await Assert.That(runtime.IsTenantFilterBypassed).IsFalse();
                await Assert.That(runtime.CurrentUserService).IsNull();
                await Assert.That(runtime.Database.CurrentTransaction).IsNull();
                await Assert.That(runtime.ChangeTracker.Entries()).IsEmpty();
            }
        }
        finally
        {
            await using var restore = CreateSeedContext();
            var capability = await restore.Set<AtprotoFederationInstanceSetting>()
                .SingleOrDefaultAsync(row => row.SettingKey == capabilityKey);
            if (capability is not null)
            {
                if (originalCapability is null)
                    restore.Remove(capability);
                else
                    restore.Entry(capability).CurrentValues.SetValues(originalCapability);
            }
            var mode = await restore.SystemSettings.SingleOrDefaultAsync(
                row => row.SettingKey == GovernanceSettingKeys.Deployment.Mode);
            if (mode is not null)
            {
                if (originalMode is null)
                    restore.Remove(mode);
                else
                    restore.Entry(mode).CurrentValues.SetValues(originalMode);
            }
            await restore.SaveChangesAsync();
        }
    }

    private ExploreDbContext CreateSeedContext(Guid? tenantId = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options)
        {
            TenantContext = tenantId is { } id ? new SeedTenant(id) : null
        };
        context.BindTenantLease();
        return context;
    }

    private sealed record SeedTenant(Guid TenantId) : ITenantContext;
}
