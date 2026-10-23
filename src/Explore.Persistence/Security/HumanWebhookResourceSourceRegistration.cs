using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>Opt-in composition within the already selected human owner; no replacement Global factory.</summary>
public static class HumanWebhookResourceSourceRegistration
{
    public static IServiceCollection AddHumanWebhookResourceSources(
        this IServiceCollection services, IdentityDatabaseTopology identityTopology)
    {
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        services.AddScoped<IHumanWebhookResourceSourceFactory>(provider => new SelectedFactory(provider, identityTopology));
        return services;
    }

    private sealed class SelectedFactory(IServiceProvider provider, IdentityDatabaseTopology identityTopology)
        : IHumanWebhookResourceSourceFactory
    {
        public IHumanWebhookResourceSourceFence Create(HumanWebhookResourceTarget target)
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            if (lease.PolicyPurpose == PolicyMutationPurpose.EmailDelivery
                || lease.HumanScope?.Purpose is HumanAdministratorCommitPurpose.EmailInstance or HumanAdministratorCommitPurpose.EmailTenant)
                throw new InvalidOperationException("Email-only owners cannot acquire webhook resource sources.");
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException("Webhook sources require the original resolved human.");
            var lifetime = lease.HumanLifetime
                ?? throw new UnauthorizedAccessException("Webhook sources require validated credential custody.");
            if (lifetime.LocalSession is not null && identityTopology == IdentityDatabaseTopology.External)
                throw new NotSupportedException("human_webhook_external_identity_requires_original_owner_contract");
            var purpose = context.IsConfigurationInstanceImportOwner ? PolicySourceSetPurpose.ConfigurationImport
                : lease.SelectedTenantId.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
            return new HumanWebhookResourceSourceFence(context, subject, target, purpose,
                HumanSourceProviderOperations.BindEnrollment(context, () => provider.GetRequiredService<IConfiguration>()),
                lifetime.LocalSession is null ? null : context);
        }
    }
}
