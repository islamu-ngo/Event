namespace Explore.Persistence.Repositories;

using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class ConfigurationManagedApplyScheduleRepository(
    ExploreDbContext dbContext)
    : IConfigurationManagedApplyScheduleRepository
{
    public async Task AddAsync(
        ConfigurationManagedApplySchedule schedule,
        CancellationToken cancellationToken)
    {
        await dbContext.Set<ConfigurationManagedApplySchedule>()
            .AddAsync(schedule, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ConfigurationManagedApplySchedule?> GetForUpdateAsync(
        Guid scheduleId,
        string targetAuthorityKey,
        CancellationToken cancellationToken) =>
        dbContext.Set<ConfigurationManagedApplySchedule>()
            .SingleOrDefaultAsync(
                schedule => schedule.Id == scheduleId
                    && schedule.TargetAuthorityKey == targetAuthorityKey,
                cancellationToken);

    public async Task UpdateAsync(
        ConfigurationManagedApplySchedule schedule,
        CancellationToken cancellationToken)
    {
        if (dbContext.Entry(schedule).State == EntityState.Detached)
            dbContext.Update(schedule);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
