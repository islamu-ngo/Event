using Explore.Application.Contracts.Admissions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public sealed class AdmissionCheckInDatabaseHealthProbe(ExploreDbContext dbContext)
    : IAdmissionCheckInHealthProbe
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        dbContext.Database.CanConnectAsync(cancellationToken);
}
