using Explore.Application.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Persistence.Identity;

internal static class LocalIdentityPasswordRegistration
{
    internal static IServiceCollection AddLocalPasswordPolicy(
        this IServiceCollection services, LocalIdentityOptions options, bool required)
    {
        if (!LocalIdentityOptions.IsValid(options))
            throw new InvalidOperationException("Local Identity settings are outside their supported ranges.");
        services.AddSingleton(LocalPasswordBlocklistSnapshot.Load(options));
        services.AddOptions<PasswordHasherOptions>()
            .Configure(hasher =>
            {
                hasher.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
                hasher.IterationCount = options.PasswordHashIterations;
            })
            .Validate(hasher => hasher.CompatibilityMode == PasswordHasherCompatibilityMode.IdentityV3
                && hasher.IterationCount is >= LocalIdentityOptions.MinimumHashIterations
                    and <= LocalIdentityOptions.MaximumHashIterations, "Local password hashing parameters are invalid.")
            .ValidateOnStart();
        services.RemoveAll<IPasswordHasher<LocalIdentityUser>>();
        services.AddScoped<IPasswordHasher<LocalIdentityUser>, NormalizedLocalIdentityPasswordHasher>();
        services.RemoveAll<IPasswordValidator<LocalIdentityUser>>();
        services.AddScoped<IPasswordValidator<LocalIdentityUser>, LocalIdentityPasswordValidator>();
        services.AddHealthChecks().Add(new HealthCheckRegistration("local-password-policy",
            provider => new LocalPasswordPolicyHealthCheck(
                provider.GetRequiredService<LocalPasswordBlocklistSnapshot>(),
                provider.GetRequiredService<IOptions<PasswordHasherOptions>>(), required),
            HealthStatus.Unhealthy, ["ready"]));
        return services;
    }
}
