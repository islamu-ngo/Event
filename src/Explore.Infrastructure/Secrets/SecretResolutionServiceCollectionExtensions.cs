using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Infrastructure.Secrets;

public static class SecretResolutionServiceCollectionExtensions
{
    /// <summary>Registers native secret reads and commitment support around the selected authority.</summary>
    public static IServiceCollection AddNativeSecretResolution(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMemoryCache();
        services.AddMetrics();
        services.TryAddSingleton<SecretResolverMetrics>();
        services.TryAddSingleton<SecretResolverCacheState>();
        services.AddSingleton<ISecretSource, EnvironmentSecretSource>();
        services.AddSingleton<ISecretSource, InfisicalSecretSource>();
        services.AddScoped<ISecretResolver, SecretResolver>();
        services.AddScoped<IRetainedSecretResolver>(provider =>
            (IRetainedSecretResolver)provider.GetRequiredService<ISecretResolver>());
        services.AddSingleton<IAuthenticationCredentialProfileReader, AuthenticationCredentialProfileReader>();
        services.AddScoped<ISecretAuthorityStatusReader, SecretAuthorityStatusReader>();
        services.AddScoped<IPrivacyIdentityFenceKeyProvider, PrivacyIdentityFenceKeyProvider>();
        services.AddScoped<ISetupSecretBindingCommitmentAuthority, SetupSecretBindingCommitmentAuthority>();
        services.AddSingleton<ISetupSecretBindingCommitBarrier, ImmediateSetupSecretBindingCommitBarrier>();
        return services;
    }
}
