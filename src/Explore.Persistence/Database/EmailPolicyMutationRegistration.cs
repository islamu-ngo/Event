using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Operations;
using Explore.Persistence.Services;
using Explore.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Persistence.Database;

public static class EmailPolicyMutationRegistration
{
    /// <summary>Call after persistence registrations with four explicitly bound authorities.</summary>
    public static IServiceCollection AddEmailPolicyMutationPersistence(
        this IServiceCollection services,
        PrimaryDatabaseConnectionOptions runtime,
        PrimaryDatabaseConnectionOptions global,
        PrimaryDatabaseConnectionOptions instanceMutation,
        PrimaryDatabaseConnectionOptions tenantMutation)
    {
        services.AddSingleton(new EmailPolicyMutationDbContextFactories(runtime, global, instanceMutation, tenantMutation));
        services.AddScoped<EmailDeliverySettingsWriter>();
        services.Replace(ServiceDescriptor.Scoped<IEmailDeliverySettingsWriter>(provider =>
            new EmailPolicyMutationSettingsWriter(
                provider.GetRequiredService<IPolicyMutationExecutionScopeFactory>(),
                provider.GetRequiredService<ITenantContextAccessor>(),
                selected => selected.GetRequiredService<EmailDeliverySettingsWriter>())));
        return services;
    }
}
