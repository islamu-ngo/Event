using Explore.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class InstancePolicySetConfiguration : IEntityTypeConfiguration<InstancePolicySet>
{
    public void Configure(EntityTypeBuilder<InstancePolicySet> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        ConfigureModules(builder);
        ConfigureEvents(builder);
        ConfigureOrganizations(builder);
        ConfigureBranding(builder);
        ConfigureDomains(builder);
        ConfigureTenantDelegation(builder);
        ConfigureRenderPolicy(builder);
    }

    private static void ConfigureModules(EntityTypeBuilder<InstancePolicySet> builder)
    {
        builder.OwnsOne(x => x.Modules, modules =>
        {
            ConfigurePolicySlot(modules, m => m.EnableIslamicModule);
            ConfigurePolicySlot(modules, m => m.EnableTechModule);
        });
    }

    private static void ConfigureEvents(EntityTypeBuilder<InstancePolicySet> builder)
    {
        builder.OwnsOne(x => x.Events, events =>
        {
            ConfigurePolicySlot(events, e => e.AllowUserSubmittedEvents);
            ConfigurePolicySlot(events, e => e.AllowOrganizationSubmittedEvents);
            ConfigurePolicySlot(events, e => e.AllowGroupSubmittedEvents);
            ConfigurePolicySlot(events, e => e.EventCardClickOpensDetailPage);
        });
    }

    private static void ConfigureOrganizations(EntityTypeBuilder<InstancePolicySet> builder)
    {
        builder.OwnsOne(x => x.Organizations, orgs =>
        {
            ConfigurePolicySlot(orgs, o => o.RequireVerification);
            ConfigurePolicySlot(orgs, o => o.AllowTenantToOmitVerification);
            ConfigurePolicySlot(orgs, o => o.AllowSelfRegistration);
            ConfigurePolicySlot(orgs, o => o.AllowGroupSelfRegistration);
        });
    }

    private static void ConfigureBranding(EntityTypeBuilder<InstancePolicySet> builder)
    {
        builder.OwnsOne(x => x.Branding, branding =>
        {
            ConfigurePolicySlot(branding, b => b.DisplayName);
            ConfigurePolicySlot(branding, b => b.LogoUrl);
            ConfigurePolicySlot(branding, b => b.FaviconUrl);
            ConfigurePolicySlot(branding, b => b.CustomCssUrl);
        });
    }

    private static void ConfigureDomains(EntityTypeBuilder<InstancePolicySet> builder)
    {
        builder.OwnsOne(x => x.Domains, domains =>
        {
            ConfigurePolicySlot(domains, d => d.InstanceBaseDomain);
            ConfigurePolicySlot(domains, d => d.AllowTenantCustomDomains);
            ConfigurePolicySlot(domains, d => d.LockTenantSubdomain);
            ConfigurePolicySlot(domains, d => d.LockTenantCustomDomain);
        });
    }

    private static void ConfigureTenantDelegation(EntityTypeBuilder<InstancePolicySet> builder)
    {
        builder.OwnsOne(x => x.TenantDelegation, delegation =>
        {
            ConfigurePolicySlot(delegation, d => d.AllowSelfServiceRegistration);
            ConfigurePolicySlot(delegation, d => d.AllowWhiteLabeling);
            ConfigurePolicySlot(delegation, d => d.DefaultPublicHomePage);
            ConfigurePolicySlot(delegation, d => d.LockTenantSmtp);
            ConfigurePolicySlot(delegation, d => d.LockTenantStorage);
            ConfigurePolicySlot(delegation, d => d.LockTenantAnalytics);
            ConfigurePolicySlot(delegation, d => d.AuthorizationProvider);
        });
    }

    private static void ConfigureRenderPolicy(EntityTypeBuilder<InstancePolicySet> builder)
    {
        builder.OwnsOne(x => x.RenderPolicy, rp =>
        {
            ConfigurePolicySlot(rp, r => r.Version);
            ConfigurePolicySlot(rp, r => r.Preset);
            ConfigurePolicySlot(rp, r => r.EnableAdvancedOverrides);
            ConfigurePolicySlot(rp, r => r.GlobalRenderMode);
            ConfigurePolicySlot(rp, r => r.GlobalPrerenderEnabled);
            ConfigurePolicySlot(rp, r => r.PublicSeoRenderMode);
            ConfigurePolicySlot(rp, r => r.PublicSeoPrerenderEnabled);
            ConfigurePolicySlot(rp, r => r.OperationalRenderMode);
            ConfigurePolicySlot(rp, r => r.OperationalPrerenderEnabled);
            ConfigurePolicySlot(rp, r => r.AdminRenderMode);
            ConfigurePolicySlot(rp, r => r.AdminPrerenderEnabled);
            ConfigurePolicySlot(rp, r => r.OnboardingRenderMode);
            ConfigurePolicySlot(rp, r => r.OnboardingPrerenderEnabled);
            rp.OwnsOne(r => r.DisallowInteractiveServerOnOnboarding, slot =>
            {
                slot.Property(value => value.LocalValue)
                    .HasColumnName("render_policy_disallow_interactive_onboarding_local_value");
                slot.Property(value => value.OverrideMode)
                    .HasColumnName("render_policy_disallow_interactive_onboarding_override_mode");
            });
            ConfigurePolicySlot(rp, r => r.AllowTenantOverride);
            ConfigurePolicySlot(rp, r => r.LockTenantPublicSeo);
            ConfigurePolicySlot(rp, r => r.LockTenantOperational);
            ConfigurePolicySlot(rp, r => r.LockTenantAdmin);
        });
    }

    private static void ConfigurePolicySlot<TOwner, T>(
        OwnedNavigationBuilder<TOwner, PolicySlot<T>> slotBuilder)
        where TOwner : class
    {
        slotBuilder.Property(s => s.LocalValue);
        slotBuilder.Property(s => s.OverrideMode);
    }

    private static void ConfigurePolicySlot<TOwner, TPolicy, T>(
        OwnedNavigationBuilder<TOwner, TPolicy> policyBuilder,
        System.Linq.Expressions.Expression<Func<TPolicy, PolicySlot<T>?>> slotSelector)
        where TOwner : class
        where TPolicy : class
    {
        policyBuilder.OwnsOne(slotSelector);
    }
}
