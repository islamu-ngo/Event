using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Persistence.Security;

/// <summary>Registers only the independent mixed source. Installation remains a migrator concern.</summary>
public static class HumanMixedSettingsSourceRegistration
{
    public static IServiceCollection AddHumanMixedSettingsSources(
        this IServiceCollection services, IdentityDatabaseTopology identityTopology)
    {
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        services.AddScoped<IHumanMixedSettingsSourceFactory>(provider => new Factory(provider, identityTopology));
        services.AddScoped<IMixedSettingsBatchStore, Services.MixedSettingsBatchStore>();
        services.AddScoped<ITypedSettingsOriginalStore, Services.TypedSettingsOriginalStore>();
        services.AddScoped<IEventResourceProviderOriginalStore, Services.EventResourceProviderOriginalStore>();
        services.TryAddScoped<Services.EmailDeliverySettingsWriter>();
        return services;
    }

    private sealed class Factory(IServiceProvider provider, IdentityDatabaseTopology topology)
        : IHumanMixedSettingsSourceFactory
    {
        public async Task<IHumanMixedSettingsSourceProtocol> CreateAsync(
            TypedSettingsOriginal original, CancellationToken cancellationToken)
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            var human = lease.HumanScope ?? throw new UnauthorizedAccessException("The original typed subject is required.");
            var lifetime = lease.HumanLifetime ?? throw new UnauthorizedAccessException("The original typed credential is required.");
            if (original.Actor == Guid.Empty || original.Actor != human.Subject.PrincipalId
                || original.TenantId != lease.SelectedTenantId
                || lease.PolicyPurpose != PolicyMutationPurpose.MixedSettings
                || human.Purpose != (original.TenantId.HasValue
                    ? HumanAdministratorCommitPurpose.MixedSettingsTenant : HumanAdministratorCommitPurpose.MixedSettingsInstance)
                || human.Subject.AccountKey is null || human.Subject.AccountKey != lifetime.AccountKey
                || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
                || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed)
                throw new UnauthorizedAccessException("Typed observation requires its original idle physical selection.");
            HumanSourceProviderOperations.RequirePostgresOrSqlite(context, "typed_settings_native_provider_not_supported");
            if (lifetime.LocalSession is not null && topology == IdentityDatabaseTopology.External)
                throw new NotSupportedException("typed_settings_external_local_identity_not_supported");
            var forwarding = await HumanSupportForwardingObservation.CaptureAsync(human.Subject,
                provider.GetRequiredService<ISupportAccessSessionService>(),
                provider.GetRequiredService<ITenantContextAccessor>(), cancellationToken);
            lease.RequireOriginalOwner(context);
            return new HumanMixedSettingsSourceProtocol(context, lease, forwarding, original,
                provider.GetRequiredService<ITypedSettingsOriginalStore>());
        }

        public async Task<IHumanMixedSettingsSourceProtocol> CreateAsync(
            UpdateSettingBatchCommand original, CancellationToken cancellationToken)
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            var human = lease.HumanScope ?? throw new UnauthorizedAccessException("The original mixed subject is required.");
            var lifetime = lease.HumanLifetime ?? throw new UnauthorizedAccessException("The original mixed credential is required.");
            if (!MixedSettingsBatchMutation.RequiresCombinedSource(original)
                || lease.PolicyPurpose != PolicyMutationPurpose.MixedSettings
                || human.Purpose is not (HumanAdministratorCommitPurpose.MixedSettingsInstance or HumanAdministratorCommitPurpose.MixedSettingsTenant)
                || human.Subject.AccountKey is null || human.Subject.AccountKey != lifetime.AccountKey
                || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
                || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed)
                throw new UnauthorizedAccessException("Mixed observation requires its original idle physical selection.");
            HumanSourceProviderOperations.RequirePostgresOrSqlite(context, "mixed_settings_native_provider_not_supported");
            if (lifetime.LocalSession is not null && topology == IdentityDatabaseTopology.External)
                throw new NotSupportedException("mixed_settings_external_local_identity_not_supported");
            var forwarding = await HumanSupportForwardingObservation.CaptureAsync(human.Subject,
                provider.GetRequiredService<ISupportAccessSessionService>(),
                provider.GetRequiredService<ITenantContextAccessor>(), cancellationToken);
            lease.RequireOriginalOwner(context);
            return new HumanMixedSettingsSourceProtocol(context, lease, forwarding, original);
        }
    }
}
