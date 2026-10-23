using Event.Persistence.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Retained PostgreSQL profiles reuse native purpose-role composition and lease-aware contexts.
/// The caller initializes and disposes the enrolled database; disposing a host does not reset it.
/// Header authentication remains a protocol transport, not native human mutation authority.
/// </summary>
public class PostgreSqlApiWebApplicationFactory : AuthenticatedWebApplicationFactory
{
    private readonly Action<IServiceCollection>? _configureTestServices;

    public PostgreSqlApiWebApplicationFactory(
        PostgreSqlContainerFixture database,
        Dictionary<string, string?>? additionalConfig = null,
        Action<IServiceCollection>? configureTestServices = null)
        : base(database)
    {
        if (additionalConfig is not null)
            foreach (var pair in additionalConfig)
                AdditionalConfiguration[pair.Key] = pair.Value;
        AdditionalConfiguration["Testing:UseRealDatabase"] = "true";
        AuthorizationProviderOverride = new StubAuthorizationProvider();
        _configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => _configureTestServices?.Invoke(services));
    }
}
