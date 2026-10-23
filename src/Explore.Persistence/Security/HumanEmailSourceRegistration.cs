using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>Opt-in dedicated source composition. Does not replace ordinary factories or install SQL.</summary>
public static class HumanEmailSourceRegistration
{
    public static IServiceCollection AddHumanEmailSources(
        this IServiceCollection services, IdentityDatabaseTopology identityTopology)
    {
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        services.AddScoped<IHumanEmailSourceFactory>(provider => new Factory(provider, identityTopology));
        return services;
    }

    private sealed class Factory(IServiceProvider provider, IdentityDatabaseTopology identityTopology) : IHumanEmailSourceFactory
    {
        public async Task<IHumanEmailSourceProtocol> CreateAsync(CancellationToken cancellationToken = default)
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            var human = lease.HumanScope
                ?? throw new UnauthorizedAccessException("human_email_original_subject_required");
            var lifetime = lease.HumanLifetime
                ?? throw new UnauthorizedAccessException("human_email_original_credential_required");
            if (lease.PolicyPurpose != PolicyMutationPurpose.EmailDelivery
                || human.Purpose is not (HumanAdministratorCommitPurpose.EmailInstance or HumanAdministratorCommitPurpose.EmailTenant)
                || human.Subject.AccountKey is null || human.Subject.AccountKey != lifetime.AccountKey
                || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed
                || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("human_email_requires_dedicated_idle_owner");
            if (lifetime.LocalSession is not null && identityTopology == IdentityDatabaseTopology.External)
                throw new NotSupportedException("human_email_external_local_identity_source_not_supported");
            HumanSourceProviderOperations.RequirePostgresOrSqlite(context, "human_email_dedicated_source_native_contract_not_supported");
            var contextId = context.ContextId;
            var connection = RelationalConnectionIdentity.Capture(context);
            Guid? tenant = context.BindTenantLease();
            var forwarding = await HumanSupportForwardingObservation.CaptureAsync(human.Subject,
                provider.GetRequiredService<ISupportAccessSessionService>(),
                provider.GetRequiredService<ITenantContextAccessor>(), cancellationToken);
            lease.RequireOriginalOwner(context);
            if (context.ContextId != contextId || !connection.Matches(context)
                || context.TenantFilterTenantId != tenant || !ReferenceEquals(lease.HumanScope, human)
                || !ReferenceEquals(lease.HumanLifetime, lifetime)
                || tenant.HasValue && forwarding.ResolvedTenantId != tenant)
                throw new InvalidOperationException("human_email_capture_owner_changed");
            return new HumanEmailSourceProtocol(context, lease, forwarding);
        }
    }
}
