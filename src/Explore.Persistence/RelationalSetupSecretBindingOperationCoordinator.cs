namespace Explore.Persistence;

using System.Globalization;
using Explore.Application.Contracts.SetupLive;
using Explore.Persistence.Database;

public sealed class RelationalSetupSecretBindingOperationCoordinator(
    ExploreDbContext dbContext) : ISetupSecretBindingOperationCoordinator
{
    public Task<IAsyncDisposable> AcquireAsync(
        SetupSecretBindingCoordinationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RelationalNamedLock.AcquireSessionAsync(
            dbContext,
            string.Create(
                CultureInfo.InvariantCulture,
                $"explore:setup-secret-binding:{request.TenantId:D}:{request.EnrollmentId:D}:{request.EnrollmentGeneration}"),
            cancellationToken);
    }
}
