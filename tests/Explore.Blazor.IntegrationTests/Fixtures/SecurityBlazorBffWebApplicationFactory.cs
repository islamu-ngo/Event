using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Explore.Blazor.IntegrationTests.Fixtures;

/// <summary>
/// BFF WebApplicationFactory configured for security integration tests.
/// Uses real OIDC authentication against a containerized Keycloak instance.
/// The DynamicAuthSchemeManager will register Keycloak from environment variables
/// pointing to the container, enabling the full OIDC challenge/callback flow.
/// </summary>
public class SecurityBlazorBffWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _keycloakAuthority;
    private readonly string _keycloakMetadataAddress;
    private readonly string _keycloakClientId;
    private readonly string _keycloakClientSecret;

    public SecurityBlazorBffWebApplicationFactory(
        string keycloakAuthority,
        string keycloakMetadataAddress,
        string keycloakClientId,
        string keycloakClientSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keycloakClientSecret);
        _keycloakAuthority = keycloakAuthority;
        _keycloakMetadataAddress = keycloakMetadataAddress;
        _keycloakClientId = keycloakClientId;
        _keycloakClientSecret = keycloakClientSecret;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["SecretProvider:Provider"] = "Environment" }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = _keycloakAuthority,
                ["Keycloak:Realm"] = "ISLAMU",
                ["Keycloak:ClientId"] = _keycloakClientId,
                ["Keycloak:ClientSecret"] = _keycloakClientSecret,
                ["Keycloak:MetadataAddress"] = _keycloakMetadataAddress,
                ["Keycloak:RequireHttpsMetadata"] = "false",
                ["Deployment:Mode"] = "SingleTenant",
                ["Deployment:DefaultTenantId"] = "018e4e5c-7f00-7000-8000-000000000001",
                ["ExploreApi:BaseUrl"] = "http://localhost:9999/",
                ["S3Settings:Region"] = "us-east-1",
                ["S3Settings:BucketName"] = "test-bucket",
                ["S3Settings:Endpoint"] = "https://s3.example.com",
            };

            config.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>>();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            services.RemoveAll<IInstanceOnboardingClient>();
            var onboardingClient = NSubstitute.Substitute.For<IInstanceOnboardingClient>();
            onboardingClient.GetInstanceOnboardingAuthProviderConfigurationAsync(
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<HalResourceOfAuthProviderConfigurationDto>(null!));
            services.AddSingleton(onboardingClient);

            services.RemoveAll<IBffResolverConfigurationProvider>();
            var mockResolverConfig = NSubstitute.Substitute.For<IBffResolverConfigurationProvider>();
            mockResolverConfig.GetConfigurationAsync(Arg.Any<CancellationToken>())
                .Returns(new ResolverConfigurationDto
                {
                    PathEnabled = false
                });
            services.AddSingleton(mockResolverConfig);

            services.RemoveAll<IBffOnboardingStatusProvider>();
            var mockOnboarding = NSubstitute.Substitute.For<IBffOnboardingStatusProvider>();
            mockOnboarding.GetStatusAsync(Arg.Any<CancellationToken>())
                .Returns(new BffOnboardingStatus(
                    IsCompleted: true,
                    State: "Completed",
                    Mode: "Interactive",
                    Provider: null,
                    Generation: 0,
                    Disposition: BffOnboardingDisposition.Completed));
            services.AddSingleton(mockOnboarding);
        });
    }
}
