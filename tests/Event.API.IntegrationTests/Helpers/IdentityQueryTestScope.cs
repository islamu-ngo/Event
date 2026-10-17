using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseInMemoryDatabase(nameof(IdentityQueryTestScope), new InMemoryDatabaseRoot()).Options;
        services.AddSingleton<IGlobalExploreDbContextFactory>(new IdentityQueryInMemoryContextFactory(options));
        services.AddScoped<IExternalLoginAuthenticationRepository, ExternalLoginAuthenticationRepository>();
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

/// <summary>Owns real independent EF contexts for controller-only tests; PostgreSQL authority tests use the production factory.</summary>
internal sealed class IdentityQueryInMemoryContextFactory(DbContextOptions<ExploreDbContext> options)
    : PooledDbContextFactory<ExploreDbContext>(options, poolSize: 1), IGlobalExploreDbContextFactory
{
}
