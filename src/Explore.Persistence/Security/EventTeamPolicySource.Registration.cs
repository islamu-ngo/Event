using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

public static class EventTeamPolicySourceRegistration
{
    public static IServiceCollection AddEventTeamPolicySources(this IServiceCollection services)
    {
        services.AddScoped<IEventTeamPolicySource>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            if (lease.PolicyPurpose is not null || context.IsConfigurationInstanceImportOwner
                || !lease.SelectedTenantId.HasValue || lease.HumanLifetime is null)
                throw new InvalidOperationException("Event team sources require the original human Runtime owner.");
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException("Event team sources require original human custody.");
            return new EventTeamPolicySource(context, subject,
                HumanSourceProviderOperations.BindEnrollment(context, () => provider.GetRequiredService<IConfiguration>()));
        });
        return services;
    }
}
