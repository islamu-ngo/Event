using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class LocalizationSecretStatusNativeQuery
{
    internal static Task<bool>? ReadAsync(
        ExploreDbContext context, IServiceProvider services, CancellationToken cancellationToken) =>
        context.Database.IsNpgsql()
            ? PostgresLocalizationSecretStatusContract.ReadAsync(context,
                services.GetRequiredService<PostgresNativeQueueEnrollment>(), cancellationToken)
            : null;
}
