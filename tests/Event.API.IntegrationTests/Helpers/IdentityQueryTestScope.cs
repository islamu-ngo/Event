using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Helpers;

/// <summary>A real protected identity query and repository for directly constructed controller tests.</summary>
internal sealed class IdentityQueryTestScope : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public IdentityQueryTestScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ExploreDbContext>(options =>
            options.UseInMemoryDatabase(nameof(IdentityQueryTestScope), new InMemoryDatabaseRoot()));
        services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
        services.AddSingleton<IAuthorizationProvider, UnexpectedPolicy>();
        services.AddScoped<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>, ResolveCurrentUserIdByIdentityRequestHandler>();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _scope = _provider.CreateScope();
    }

    public IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> Query =>
        _scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private sealed class UnexpectedPolicy : IAuthorizationProvider
    {
        public Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Identity resolution does not own a PDP capability.");
        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Identity resolution does not own a PDP capability.");
    }
}
