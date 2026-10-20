using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Services;

/// <summary>Reads only binding existence on the original selected database owner.</summary>
public sealed class LocalizationSecretStatusReader(
    ExploreDbContext context, IServiceProvider services) : ILocalizationSecretStatusReader
{
    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) =>
        LocalizationSecretStatusNativeQuery.ReadAsync(context, services, cancellationToken);
}
