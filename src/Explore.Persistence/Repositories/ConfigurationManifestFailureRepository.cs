using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ConfigurationManifestFailureRepository(
    IDbContextFactory<ExploreDbContext> contextFactory)
    : IConfigurationManifestFailureRecorder
{
    public async Task RecordAsync(
        ConfigurationManifestOperation failedOperation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failedOperation);
        if (failedOperation.Status != ConfigurationManifestOperationStatus.Failed)
        {
            throw new ArgumentException(
                "The isolated recorder accepts only failed manifest operations.",
                nameof(failedOperation));
        }

        await using ExploreDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        context.ConfigurationManifestOperations.Add(failedOperation);
        await context.SaveChangesAsync(cancellationToken);
    }
}
