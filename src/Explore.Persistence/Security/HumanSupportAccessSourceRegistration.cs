using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>Opt-in selected-owner composition; no Global factory replacement or installer invocation.</summary>
public static class HumanSupportAccessSourceRegistration
{
    public static IServiceCollection AddHumanSupportAccessSources(
        this IServiceCollection services, IdentityDatabaseTopology identityTopology)
    {
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        services.AddScoped<IHumanSupportAccessSourceFactory>(provider => new SelectedFactory(provider, identityTopology));
        return services;
    }

    private sealed class SelectedFactory(IServiceProvider provider, IdentityDatabaseTopology identityTopology)
        : IHumanSupportAccessSourceFactory
    {
        public async Task<IHumanSupportAccessSourceFence> CreateAsync(CancellationToken cancellationToken = default)
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("human_support_capture_requires_no_transaction");
            if (lease.PolicyPurpose is PolicyMutationPurpose.EmailDelivery or PolicyMutationPurpose.MixedSettings
                || lease.HumanScope?.Purpose is HumanAdministratorCommitPurpose.EmailInstance
                    or HumanAdministratorCommitPurpose.EmailTenant or HumanAdministratorCommitPurpose.MixedSettingsInstance
                    or HumanAdministratorCommitPurpose.MixedSettingsTenant)
                throw new InvalidOperationException("Email-only owners cannot acquire support-access sources.");
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException("Support sources require the original resolved human.");
            var lifetime = lease.HumanLifetime
                ?? throw new UnauthorizedAccessException("Support sources require validated credential custody.");
            if (lifetime.LocalSession is not null && identityTopology == IdentityDatabaseTopology.External)
                throw new NotSupportedException("human_support_external_identity_requires_original_owner_contract");
            var contextId = context.ContextId;
            var connection = RelationalConnectionIdentity.Capture(context);
            Guid? tenant = context.BindTenantLease();
            var forwarding = await HumanSupportForwardingObservation.CaptureAsync(subject,
                provider.GetRequiredService<ISupportAccessSessionService>(),
                provider.GetRequiredService<ITenantContextAccessor>(), cancellationToken);
            lease.RequireOriginalOwner(context);
            context.ValidateTenantLease();
            if (context.ContextId != contextId || context.TenantFilterTenantId != tenant
                || !connection.Matches(context))
                throw new InvalidOperationException("human_support_capture_owner_changed");
            var purpose = context.IsConfigurationInstanceImportOwner ? PolicySourceSetPurpose.ConfigurationImport
                : lease.SelectedTenantId.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
            return new HumanSupportAccessSourceFence(context, subject, forwarding, purpose,
                context.Database.IsNpgsql() ? provider.GetRequiredService<PostgresNativeQueueEnrollment>() : null,
                lifetime.LocalSession is null ? null : context);
        }
    }
}
