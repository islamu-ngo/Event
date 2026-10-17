using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence;

internal static class HumanSettingsMutationServicesRegistration
{
    internal static IServiceCollection AddHumanSettingsMutationSources(
        this IServiceCollection services, IdentityDatabaseTopology identityTopology)
    {
        services.AddScoped<ExploreDbContextExecutionLease>();
        services.AddScoped<PolicyMutationExecutionScopeFactory>();
        services.AddScoped<IPolicyMutationExecutionScopeFactory>(provider => provider.GetRequiredService<PolicyMutationExecutionScopeFactory>());
        services.AddScoped<IHumanCommitScopePreparation>(provider => provider.GetRequiredService<PolicyMutationExecutionScopeFactory>());
        services.AddScoped<ISerializableAttemptOwner>(provider => (EfCoreUnitOfWork)provider.GetRequiredService<IUnitOfWork>());
        services.AddScoped<HumanSettingsDataBoundary>();
        services.AddScoped<IOrdinarySettingsMutationStore, OrdinarySettingsMutationStore>();
        services.AddHumanSupportAccessSources(identityTopology);
        services.AddScoped<IInstanceAdministratorCommitAuthority>(HumanAdministratorAuthority);
        services.AddScoped<ITenantAdministratorCommitAuthority>(HumanAdministratorAuthority);
        services.AddScoped<IHumanSubjectCommitAuthority>(HumanAdministratorAuthority);
        services.AddScoped<IPolicySourceSetFence>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            if (lease.PolicyPurpose is PolicyMutationPurpose.EmailDelivery or PolicyMutationPurpose.MixedSettings)
                throw new InvalidOperationException("Email-only owners cannot acquire ordinary policy source sets.");
            var purpose = context.IsConfigurationInstanceImportOwner
                ? PolicySourceSetPurpose.ConfigurationImport
                : lease.SelectedTenantId.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
            return new PolicySourceSetFence(context, purpose, context.Database.IsNpgsql()
                ? provider.GetRequiredService<PostgresNativeQueueEnrollment>() : null);
        });
        services.AddScoped<IHumanPrincipalSourceFence>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException("Principal sources require the original resolved human.");
            var lifetime = lease.HumanLifetime
                ?? throw new UnauthorizedAccessException("Principal sources require validated credential custody.");
            if (lease.PolicyPurpose is PolicyMutationPurpose.EmailDelivery or PolicyMutationPurpose.MixedSettings)
                throw new InvalidOperationException("Email-only owners cannot acquire ordinary principal sources.");
            var purpose = context.IsConfigurationInstanceImportOwner
                ? PolicySourceSetPurpose.ConfigurationImport
                : lease.SelectedTenantId.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
            DbContext? identityContext = lifetime.LocalSession is null ? null
                : identityTopology == IdentityDatabaseTopology.External
                    ? provider.GetRequiredService<ExternalIdentityDbContext>() : context;
            return new HumanPrincipalSourceFence(context, subject, purpose, lifetime.LocalSession, identityContext,
                context.Database.IsNpgsql()
                    ? provider.GetRequiredService<PostgresNativeQueueEnrollment>() : null);
        });
        services.AddScoped<IHumanAuthorizationRouteSource>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException("Route sources require the original resolved human.");
            if (lease.HumanLifetime is null)
                throw new UnauthorizedAccessException("Route sources require validated credential custody.");
            if (lease.PolicyPurpose is PolicyMutationPurpose.EmailDelivery or PolicyMutationPurpose.MixedSettings)
                throw new InvalidOperationException("Email-only owners cannot acquire ordinary route sources.");
            var purpose = context.IsConfigurationInstanceImportOwner
                ? PolicySourceSetPurpose.ConfigurationImport
                : lease.SelectedTenantId.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
            return new HumanAuthorizationRouteSource(context, subject, purpose,
                context.Database.IsNpgsql()
                    ? provider.GetRequiredService<PostgresNativeQueueEnrollment>() : null);
        });
        services.AddScoped<IHumanActionCommitFinalizer, HumanActionCommitFinalizer>();
        static ExternalApiKeyIssuanceAuthority HumanAdministratorAuthority(IServiceProvider provider)
        {
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            var scope = lease.HumanScope
                ?? throw new InvalidOperationException("Human authority requires an authenticated selected owner before construction.");
            return new ExternalApiKeyIssuanceAuthority(provider.GetRequiredService<ExploreDbContext>(),
                provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), scope);
        }
        return services;
    }
}
