using Explore.Application.Contracts.Operations;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed partial class AuthenticatedSettingsFixture
{
    /// <summary>Uses real Local authentication and the actual dedicated instance-email selection.</summary>
    internal async Task<T> RunInstanceEmailAsync<T>(Func<Session, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var request = host.Services.CreateAsyncScope();
        var accessor = request.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = request.ServiceProvider };
        accessor.HttpContext = context;
        try
        {
            context.Request.Headers.Authorization = "Bearer " + accessToken;
            var authenticated = await context.AuthenticateAsync();
            if (!authenticated.Succeeded)
                throw new InvalidOperationException("The actual API rejected its Local email owner credential.");
            context.User = authenticated.Principal!;
            await using var selected = await request.ServiceProvider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(PolicyMutationScope.EmailInstance, cancellationToken);
            return await operation(new Session(selected.Services, ActorId, tenantId: null));
        }
        finally { accessor.HttpContext = null; }
    }

    internal async Task<(string Value, long Revision)> ReadInstanceEmailPolicyAsync(CancellationToken cancellationToken)
    {
        await using ExploreDbContext observer = createDatabase();
        observer.BindTenantLease();
        string value = await observer.SystemSettings.AsNoTracking()
            .Where(row => row.SettingKey == GovernanceSettingKeys.Email.FromName).Select(row => row.Value)
            .SingleAsync(cancellationToken);
        long revision = await observer.EmailDispatchProcessorStates.AsNoTracking()
            .Select(row => row.DeliveryPolicyRevision).SingleAsync(cancellationToken);
        return (value, revision);
    }
}
