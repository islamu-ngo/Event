using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class NotificationPreferenceMatrixPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task LookupSeeder_SeedsNotificationPreferenceMatrixMetadata()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        var categories = await context.NotificationPreferenceCategories
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ToListAsync();
        var channels = await context.NotificationPreferenceChannels
            .AsNoTracking()
            .OrderBy(channel => channel.SortOrder)
            .ToListAsync();

        var accountSecurity = categories.Single(category => category.MasterCode == NotificationPreferenceCategoryCodes.AccountSecurity);
        var marketing = categories.Single(category => category.MasterCode == NotificationPreferenceCategoryCodes.Marketing);
        var trustSafety = categories.Single(category => category.MasterCode == NotificationPreferenceCategoryCodes.TrustSafety);

        await Assert.That(categories.Count).IsEqualTo(Enum.GetValues<NotificationPreferenceCategoryEnum>().Length);
        await Assert.That(channels.Count).IsEqualTo(Enum.GetValues<NotificationPreferenceChannelEnum>().Length);
        await Assert.That(accountSecurity.IsRequired).IsTrue();
        await Assert.That(accountSecurity.DefaultEmailEnabled).IsTrue();
        await Assert.That(accountSecurity.DefaultInAppEnabled).IsTrue();
        await Assert.That(marketing.IsRequired).IsFalse();
        await Assert.That(marketing.DefaultEmailEnabled).IsFalse();
        await Assert.That(marketing.DefaultInAppEnabled).IsFalse();
        await Assert.That(trustSafety.IsRequired).IsFalse();
        await Assert.That(trustSafety.DefaultEmailEnabled).IsTrue();
        await Assert.That(trustSafety.DefaultInAppEnabled).IsTrue();
    }

    [Test]
    public async Task TenantFilter_HidesNotificationPreferenceRowsFromOtherTenants()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("preference-filter-a");
        var tenantB = CreateTenant("preference-filter-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        seedContext.NotificationChannelPreferences.AddRange(
            CreateTenantPreference(tenantA.Id, true),
            CreateTenantPreference(tenantB.Id, false));
        await seedContext.SaveChangesAsync();

        await using var tenantAContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.Id));

        var visiblePreferences = await tenantAContext.NotificationChannelPreferences
            .AsNoTracking()
            .Select(preference => new { preference.TenantId, preference.IsEnabled })
            .ToListAsync();

        await Assert.That(visiblePreferences.Count).IsEqualTo(1);
        await Assert.That(visiblePreferences[0].TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(visiblePreferences[0].IsEnabled).IsTrue();
    }

    [Test]
    public async Task Resolver_ReturnsDefaultsAndRequiredCategoryOverride()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        var tenant = CreateTenant("preference-defaults");
        var user = CreateUser("preference-defaults");
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var resolver = new NotificationPreferenceResolver(context);

        var decisions = await resolver.ResolveBatchAsync(
            [
                new NotificationPreferenceResolveRequest(
                    tenant.Id,
                    user.Id,
                    null,
                    null,
                    NotificationPreferenceCategoryCodes.AccountSecurity,
                    NotificationPreferenceChannelCodes.Email),
                new NotificationPreferenceResolveRequest(
                    tenant.Id,
                    user.Id,
                    null,
                    null,
                    NotificationPreferenceCategoryCodes.Marketing,
                    NotificationPreferenceChannelCodes.Email),
            ]);

        await Assert.That(decisions[0].IsEnabled).IsTrue();
        await Assert.That(decisions[0].IsRequired).IsTrue();
        await Assert.That(decisions[0].IsLocked).IsTrue();
        await Assert.That(decisions[0].IsMuted).IsFalse();
        await Assert.That(decisions[0].EffectiveSourceScope).IsEqualTo("RequiredCategory");
        await Assert.That(decisions[1].IsEnabled).IsFalse();
        await Assert.That(decisions[1].IsRequired).IsFalse();
        await Assert.That(decisions[1].EffectiveSourceScope).IsEqualTo("Default");
    }

    [Test]
    public async Task Resolver_AllowsUserToDisableTrustSafetyEmail()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        var tenant = CreateTenant("preference-trust-safety");
        var user = CreateUser("preference-trust-safety");
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.NotificationChannelPreferences.Add(CreateUserPreference(
            tenant.Id,
            user.Id,
            false,
            NotificationPreferenceCategoryCodes.TrustSafety));
        await context.SaveChangesAsync();

        var decision = await new NotificationPreferenceResolver(context).ResolveAsync(
            new NotificationPreferenceResolveRequest(
                tenant.Id,
                user.Id,
                null,
                null,
                NotificationPreferenceCategoryCodes.TrustSafety,
                NotificationPreferenceChannelCodes.Email));

        await Assert.That(decision.IsEnabled).IsFalse();
        await Assert.That(decision.IsRequired).IsFalse();
        await Assert.That(decision.IsLocked).IsFalse();
        await Assert.That(decision.EffectiveSourceScope).IsEqualTo("User");
    }

    [Test]
    public async Task Resolver_AppliesHierarchyOverridesLocksAndGlobalMute()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        var tenant = CreateTenant("preference-hierarchy");
        var user = CreateUser("preference-hierarchy");
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.NotificationChannelPreferences.AddRange(
            CreateTenantPreference(tenant.Id, true),
            CreateUserPreference(tenant.Id, user.Id, false));
        await context.SaveChangesAsync();

        var resolver = new NotificationPreferenceResolver(context);
        var overridden = await resolver.ResolveAsync(new NotificationPreferenceResolveRequest(
            tenant.Id,
            user.Id,
            null,
            null,
            NotificationPreferenceCategoryCodes.EventUpdates,
            NotificationPreferenceChannelCodes.Email));

        context.NotificationChannelPreferences.Add(CreateTenantPreference(
            tenant.Id,
            true,
            isLocked: true,
            NotificationPreferenceCategoryCodes.OrganizationUpdates));
        context.NotificationPreferenceProfiles.Add(new NotificationPreferenceProfile
        {
            TenantId = tenant.Id,
            Tenant = null!,
            ScopeId = (int)ConfigurationScopeEnum.User,
            Scope = null!,
            UserId = user.Id,
            User = null!,
            IsMuted = true,
        });
        await context.SaveChangesAsync();

        var mutedLocked = await resolver.ResolveAsync(new NotificationPreferenceResolveRequest(
            tenant.Id,
            user.Id,
            null,
            null,
            NotificationPreferenceCategoryCodes.OrganizationUpdates,
            NotificationPreferenceChannelCodes.Email));
        var requiredDespiteMute = await resolver.ResolveAsync(new NotificationPreferenceResolveRequest(
            tenant.Id,
            user.Id,
            null,
            null,
            NotificationPreferenceCategoryCodes.AccountSecurity,
            NotificationPreferenceChannelCodes.Email));

        await Assert.That(overridden.IsEnabled).IsFalse();
        await Assert.That(overridden.EffectiveSourceScope).IsEqualTo("User");
        await Assert.That(mutedLocked.IsEnabled).IsFalse();
        await Assert.That(mutedLocked.IsLocked).IsTrue();
        await Assert.That(mutedLocked.IsMuted).IsTrue();
        await Assert.That(mutedLocked.EffectiveSourceScope).IsEqualTo("Tenant");
        await Assert.That(requiredDespiteMute.IsEnabled).IsTrue();
        await Assert.That(requiredDespiteMute.IsRequired).IsTrue();
        await Assert.That(requiredDespiteMute.IsMuted).IsFalse();
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            FullName = $"Notification Preferences {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static User CreateUser(string emailPrefix)
    {
        return new User
        {
            Pii = new UserPii
            {
                Email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Preference",
                LastName = "Recipient",
            },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
    }

    private static NotificationChannelPreference CreateTenantPreference(
        Guid tenantId,
        bool isEnabled,
        bool isLocked = false,
        string categoryCode = NotificationPreferenceCategoryCodes.EventUpdates)
    {
        return new NotificationChannelPreference
        {
            TenantId = tenantId,
            Tenant = null!,
            ScopeId = (int)ConfigurationScopeEnum.Tenant,
            Scope = null!,
            CategoryId = CategoryId(categoryCode),
            Category = null!,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            Channel = null!,
            IsEnabled = isEnabled,
            IsLocked = isLocked,
        };
    }

    private static NotificationChannelPreference CreateUserPreference(
        Guid tenantId,
        Guid userId,
        bool isEnabled,
        string categoryCode = NotificationPreferenceCategoryCodes.EventUpdates)
    {
        return new NotificationChannelPreference
        {
            TenantId = tenantId,
            Tenant = null!,
            ScopeId = (int)ConfigurationScopeEnum.User,
            Scope = null!,
            UserId = userId,
            User = null!,
            CategoryId = CategoryId(categoryCode),
            Category = null!,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            Channel = null!,
            IsEnabled = isEnabled,
        };
    }

    private static int CategoryId(string categoryCode) => categoryCode switch
    {
        NotificationPreferenceCategoryCodes.EventUpdates => (int)NotificationPreferenceCategoryEnum.EventUpdates,
        NotificationPreferenceCategoryCodes.OrganizationUpdates => (int)NotificationPreferenceCategoryEnum.OrganizationUpdates,
        NotificationPreferenceCategoryCodes.TrustSafety => (int)NotificationPreferenceCategoryEnum.TrustSafety,
        _ => throw new ArgumentOutOfRangeException(nameof(categoryCode), categoryCode, null),
    };

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
