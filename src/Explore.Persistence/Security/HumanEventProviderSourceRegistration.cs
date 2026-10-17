using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

public static class HumanEventProviderSourceRegistration
{
    public static IServiceCollection AddHumanEventProviderSources(
        this IServiceCollection services, IdentityDatabaseTopology identityTopology)
    {
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        services.AddScoped<IHumanEventProviderSourceFactory>(provider => new SelectedFactory(provider, identityTopology));
        return services;
    }

    private sealed class SelectedFactory(IServiceProvider provider, IdentityDatabaseTopology identityTopology)
        : IHumanEventProviderSourceFactory, IDisposable
    {
        private readonly List<HumanEventProviderSourceFence> _sources = [];
        public IHumanEventProviderSourceFence Create(HumanEventProviderSourceTarget target)
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            if (lease.SelectedTenantId != target.TenantId || lease.PolicyPurpose == PolicyMutationPurpose.EmailDelivery
                || lease.HumanScope?.Purpose is HumanAdministratorCommitPurpose.EmailInstance or HumanAdministratorCommitPurpose.EmailTenant)
                throw new InvalidOperationException("Event-provider sources require the original selected tenant owner.");
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException("Event-provider sources require the original human.");
            var lifetime = lease.HumanLifetime
                ?? throw new UnauthorizedAccessException("Event-provider sources require credential custody.");
            if (lifetime.LocalSession is not null && identityTopology == IdentityDatabaseTopology.External)
                throw new NotSupportedException("human_event_provider_external_identity_requires_original_owner_contract");
            var source = new HumanEventProviderSourceFence(context, subject, target,
                provider.GetRequiredService<IRegistrationProviderRegistry>(),
                HumanSourceProviderOperations.BindEnrollment(context, () => provider.GetRequiredService<IConfiguration>()),
                lifetime.LocalSession is null ? null : context);
            _sources.Add(source);
            return source;
        }

        public void Dispose()
        {
            foreach (var source in _sources) source.ReleaseScopedWriteGuard();
            _sources.Clear();
        }
    }
}
