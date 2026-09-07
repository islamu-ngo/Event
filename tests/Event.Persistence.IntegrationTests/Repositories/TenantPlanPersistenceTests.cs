using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class TenantPlanPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task LookupSeeder_ShouldSeedTenantPlanStatuses()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();

        var planStatuses = await context.TenantPlanStatuses.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var assignmentStatuses = await context.TenantPlanAssignmentStatuses.AsNoTracking().ToDictionaryAsync(x => x.Id);

        await Assert.That(planStatuses[(int)TenantPlanStatusEnum.Draft].MasterCode).IsEqualTo("DRAFT");
        await Assert.That(planStatuses[(int)TenantPlanStatusEnum.Published].MasterCode).IsEqualTo("PUBLISHED");
        await Assert.That(planStatuses[(int)TenantPlanStatusEnum.Archived].MasterCode).IsEqualTo("ARCHIVED");
        await Assert.That(assignmentStatuses[(int)TenantPlanAssignmentStatusEnum.Active].MasterCode).IsEqualTo("ACTIVE");
    }

    [Test]
    public async Task TenantPlanVersion_ShouldPersistNormalizedSettingsAndQuotas()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var plan = NewPlan("community");
        var version = NewVersion(plan);
        version.Settings.Add(new TenantPlanVersionSetting
        {
            SettingKey = GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
            JsonValue = "5368709120",
            IsLocked = true,
        });
        version.Quotas.Add(new TenantPlanVersionQuota
        {
            QuotaKey = "storage.bytes",
            Limit = 5368709120,
        });

        context.TenantPlans.Add(plan);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var saved = await context.TenantPlanVersions
            .AsNoTracking()
            .Include(x => x.Settings)
            .Include(x => x.Quotas)
            .SingleAsync(x => x.Id == version.Id);

        await Assert.That(saved.Settings.Single().SettingKey).IsEqualTo(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes);
        await Assert.That(saved.Settings.Single().IsLocked).IsTrue();
        await Assert.That(saved.Quotas.Single().QuotaKey).IsEqualTo("storage.bytes");
        await Assert.That(saved.Quotas.Single().Limit).IsEqualTo(5368709120);
    }

    [Test]
    public async Task TenantPlanVersion_ShouldEnforceUniqueSettingAndQuotaKeysPerVersion()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var plan = NewPlan("enterprise");
        var version = NewVersion(plan);
        version.Settings.Add(new TenantPlanVersionSetting
        {
            SettingKey = GovernanceSettingKeys.AiAssistant.Enabled,
            JsonValue = "true",
            IsLocked = false,
        });
        version.Settings.Add(new TenantPlanVersionSetting
        {
            SettingKey = GovernanceSettingKeys.AiAssistant.Enabled,
            JsonValue = "false",
            IsLocked = true,
        });

        context.TenantPlans.Add(plan);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task TenantPlanAssignment_ShouldAllowOnlyOneActiveAssignmentPerTenant()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "plan-assignment");
        var firstPlan = NewPlan("starter");
        var secondPlan = NewPlan("pro");
        var firstVersion = NewVersion(firstPlan);
        var secondVersion = NewVersion(secondPlan);
        context.TenantPlans.AddRange(firstPlan, secondPlan);
        context.TenantPlanAssignments.Add(new TenantPlanAssignment
        {
            TenantId = tenant.Id,
            TenantPlan = firstPlan,
            TenantPlanVersion = firstVersion,
            TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
            AssignedByUserId = Guid.NewGuid(),
            AssignedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        context.TenantPlanAssignments.Add(new TenantPlanAssignment
        {
            TenantId = tenant.Id,
            TenantPlan = secondPlan,
            TenantPlanVersion = secondVersion,
            TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
            AssignedByUserId = Guid.NewGuid(),
            AssignedAt = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    private static TenantPlan NewPlan(string key)
    {
        var plan = new TenantPlan
        {
            Key = key,
            DisplayName = $"{key} plan",
            Description = $"{key} SaaS tier",
        };
        return plan;
    }

    private static TenantPlanVersion NewVersion(TenantPlan plan)
    {
        var version = new TenantPlanVersion
        {
            TenantPlan = plan,
            VersionNumber = 1,
            TenantPlanStatusId = (int)TenantPlanStatusEnum.Published,
            PriceAmount = 49m,
            CurrencyCode = "EUR",
            BillingPeriod = "monthly",
            IsActiveForProvisioning = true,
        };

        plan.Versions.Add(version);
        return version;
    }

    private static async Task<Tenant> SeedTenantAsync(ExploreDbContext context, string slugPrefix)
    {
        var tenant = new Tenant
        {
            FullName = $"Tenant Plan {slugPrefix}",
            Slug = $"tenant-plan-{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = 2,
            TenantStatus = null!,
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }
}
