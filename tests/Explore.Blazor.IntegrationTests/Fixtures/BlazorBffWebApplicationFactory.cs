using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Fixtures;

public class BlazorBffWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly ResolverConfigurationDto _resolverConfiguration;

    public BlazorBffWebApplicationFactory()
        : this(new ResolverConfigurationDto
        {
            PathEnabled = true,
            PathPrefix = "/t"
        })
    {
    }

    private BlazorBffWebApplicationFactory(ResolverConfigurationDto resolverConfiguration)
    {
        _resolverConfiguration = resolverConfiguration;
    }

    public BlazorBffWebApplicationFactory WithResolverConfiguration(ResolverConfigurationDto resolverConfiguration)
    {
        return new BlazorBffWebApplicationFactory(resolverConfiguration);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:cache", "localhost:6379,abortConnect=false,connectTimeout=100");
        builder.UseSetting("Keycloak:Authority", "https://auth.example.com");
        builder.UseSetting("Keycloak:Realm", "explore");
        builder.UseSetting("Deployment:Mode", "SingleTenant");
        builder.UseSetting("Deployment:DefaultTenantId", "018e4e5c-7f00-7000-8000-000000000001");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var inMemoryConfig = new Dictionary<string, string?>
            {
                ["ConnectionStrings:cache"] = "localhost:6379,abortConnect=false,connectTimeout=100",
                ["Keycloak:Authority"] = "https://auth.example.com",
                ["Keycloak:Realm"] = "explore",
                ["Deployment:Mode"] = "SingleTenant",
                ["Deployment:DefaultTenantId"] = "018e4e5c-7f00-7000-8000-000000000001"
            };

            config.AddInMemoryCollection(inMemoryConfig);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IConfigureOptions<KeyManagementOptions>>();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.RemoveAll<IClaimsTransformation>();
            services.RemoveAll<BffAdminClaimsTransformation>();
            services.AddSingleton<IClaimsTransformation, PassthroughClaimsTransformation>();

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            });

            services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.ForwardAuthenticate = TestAuthHandler.SchemeName;
                options.ForwardChallenge = TestAuthHandler.SchemeName;
            });

            services.RemoveAll<IDynamicAuthSchemeManager>();
            var mockSchemeManager = Substitute.For<IDynamicAuthSchemeManager>();
            mockSchemeManager.GetRegisteredProviderSchemesAsync().Returns(new List<string>());
            services.AddSingleton(mockSchemeManager);

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            services.RemoveAll<IBffResolverConfigurationProvider>();
            var mockResolverConfig = Substitute.For<IBffResolverConfigurationProvider>();
            mockResolverConfig.GetConfigurationAsync(Arg.Any<CancellationToken>())
                .Returns(_resolverConfiguration);
            services.AddSingleton(mockResolverConfig);

            services.RemoveAll<IBffOnboardingStatusProvider>();
            var mockOnboarding = Substitute.For<IBffOnboardingStatusProvider>();
            mockOnboarding.GetStatusAsync(Arg.Any<CancellationToken>())
                .Returns(new BffOnboardingStatus(
                    IsCompleted: true,
                    State: "Completed",
                    Mode: "Interactive",
                    Provider: null,
                    Generation: 0,
                    Disposition: BffOnboardingDisposition.Completed));
            services.AddSingleton(mockOnboarding);

            services.RemoveAll<IInstanceOnboardingClient>();
            var onboardingClient = Substitute.For<IInstanceOnboardingClient>();
            onboardingClient.GetInstanceOnboardingStatusAsync(
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new HalResourceOfInstanceOnboardingStatusDto
                {
                    IsCompleted = true,
                    State = "Completed",
                    Mode = "Interactive",
                    Generation = 1,
                    SelectedDeploymentMode = "SingleTenant"
                }));
            services.AddSingleton(onboardingClient);

            var testAssembly = typeof(TenantTestController).Assembly;
            services.AddControllers().AddApplicationPart(testAssembly);
        });
    }

    private sealed class PassthroughClaimsTransformation : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            return Task.FromResult(principal);
        }
    }
}
