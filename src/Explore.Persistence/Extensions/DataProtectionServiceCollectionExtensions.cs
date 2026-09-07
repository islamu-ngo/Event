using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Extensions;

/// <summary>
/// Extension methods to register ASP.NET Core Data Protection with a database-backed key ring.
/// </summary>
/// <remarks>
/// Keys persisted via <c>PersistKeysToDbContext&lt;DataProtectionKeyContext&gt;()</c> are shared across
/// Blazor BFF instances without requiring a shared filesystem or Redis.
///
/// <para>
/// Security note: storing Data Protection keys in the same database as protected cookies is an
/// availability and horizontal-scale choice, not a full database-compromise defense. Operators still
/// need database access controls and backups.
/// </para>
///
/// <para>
/// This extension belongs in BFF composition roots. API and shared persistence registrations should
/// not call it because auth-cookie and anti-forgery key persistence is a Blazor BFF concern.
/// </para>
/// </remarks>
public static class DataProtectionServiceCollectionExtensions
{
    /// <summary>
    /// Default application name for the Data Protection key ring.
    /// Must match across all Blazor BFF hosts that share keys.
    /// </summary>
    public const string DefaultApplicationName = "islamu-event";

    /// <summary>
    /// Registers ASP.NET Core Data Protection with keys persisted in the provider-selected primary database
    /// via <see cref="DataProtectionKeyContext"/>. Sets a stable application name so Blazor BFF
    /// instances share the same key ring.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="applicationName">
    /// Optional application name override. Defaults to <see cref="DefaultApplicationName"/>.
    /// Must be identical across every host that shares the key ring.
    /// </param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddExploreDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        string? applicationName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var databaseOptions = PrimaryDatabaseConfiguration.BindRuntime(configuration);

        services.AddDbContext<DataProtectionKeyContext>(options =>
            PrimaryDatabaseProviderComposition.ConfigureDataProtection(options, databaseOptions));

        services
            .AddDataProtection()
            .SetApplicationName(applicationName ?? DefaultApplicationName)
            .PersistKeysToDbContext<DataProtectionKeyContext>();

        return services;
    }
}
