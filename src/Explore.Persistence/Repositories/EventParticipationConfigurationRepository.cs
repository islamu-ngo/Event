using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventParticipationConfigurationRepository(ExploreDbContext dbContext)
    : IEventParticipationConfigurationRepository
{
    public Task<EventParticipationConfiguration?> GetByEventAndTenantAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return DetailsQuery()
            .FirstOrDefaultAsync(
                configuration => configuration.Id == eventId && configuration.TenantId == tenantId,
                cancellationToken);
    }

    public async Task UpdateAsync(
        EventParticipationConfiguration configuration,
        CancellationToken cancellationToken)
    {
        dbContext.Entry(configuration).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<EventParticipationConfiguration> DetailsQuery() =>
        dbContext.EventParticipationConfigurations
            .Include(configuration => configuration.ParticipationHandlingMode)
            .Include(configuration => configuration.AdvanceRegistrationObligation)
            .Include(configuration => configuration.IdentityAccessMode)
            .Include(configuration => configuration.RequirementAttachments)
            .ThenInclude(attachment => attachment.RegistrationRequirement)
            .ThenInclude(requirement => requirement!.Channels)
            .Include(configuration => configuration.RequirementAttachments)
            .ThenInclude(attachment => attachment.RegistrationFormVersion);
}
