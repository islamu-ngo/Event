using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>
/// Opt-in composition for the parent. Resolves only the existing selected owner; it never
/// creates a context/scope, resolves Global credentials or registers an authorization consumer.
/// </summary>
public static class HumanEventAssignmentSourceRegistration
{
    public static IServiceCollection AddHumanEventAssignmentSources(
        this IServiceCollection services, IdentityDatabaseTopology identityTopology)
    {
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        services.AddScoped<IHumanEventAssignmentSourceFactory>(provider =>
            new SelectedFactory(provider, identityTopology));
        services.AddScoped<IHumanAiModerationProposalTargetSourceFactory>(provider =>
            new HumanAiModerationProposalTargetFactory(provider, identityTopology));
        return services;
    }

    private sealed class SelectedFactory(IServiceProvider provider, IdentityDatabaseTopology identityTopology)
        : IHumanEventAssignmentSourceFactory
    {
        public IHumanEventAssignmentSourceFence Create(Guid tenantId, IReadOnlyCollection<Guid> eventIds)
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            if (lease.PolicyPurpose == PolicyMutationPurpose.EmailDelivery
                || lease.HumanScope?.Purpose is HumanAdministratorCommitPurpose.EmailInstance
                    or HumanAdministratorCommitPurpose.EmailTenant)
                throw new InvalidOperationException("Email-only owners cannot acquire event assignment sources.");
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException("Assignment sources require the original resolved human.");
            var lifetime = lease.HumanLifetime
                ?? throw new UnauthorizedAccessException("Assignment sources require validated credential custody.");
            if (lifetime.LocalSession is not null && identityTopology == IdentityDatabaseTopology.External)
                throw new NotSupportedException("human_assignment_external_identity_requires_original_owner_source_contract");
            var purpose = context.IsConfigurationInstanceImportOwner ? PolicySourceSetPurpose.ConfigurationImport
                : lease.SelectedTenantId.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
            return new HumanEventAssignmentSourceFence(context, subject, purpose, tenantId, eventIds,
                HumanSourceProviderOperations.BindEnrollment(context, () => provider.GetRequiredService<IConfiguration>()),
                lifetime.LocalSession is null ? null : context);
        }
    }
}
