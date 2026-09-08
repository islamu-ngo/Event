// ABOUTME: EF Core repository for exact tenant-scoped event participation configuration updates.
// ABOUTME: Loads normalized lookups for entity consumers and saves the tracked concurrency boundary.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
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

    public async Task<IReadOnlyList<EventParticipationConfiguration>> GetAccountRequiredAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        // Instance policy changes affect inherited tenants even when no override row exists.
        return await dbContext.EventParticipationConfigurations
            .IgnoreTenantFilter("Visitor policy mutation evaluates AccountRequired configurations across affected tenant scopes; optional tenant selection uses an exact predicate.")
            .AsNoTracking()
            .Where(configuration => configuration.IdentityAccessModeId == (int)IdentityAccessModeEnum.AccountRequired
                && (!tenantId.HasValue || configuration.TenantId == tenantId.Value)
                && configuration.Event != null && !configuration.Event.IsDeleted)
            .ToListAsync(cancellationToken);
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
