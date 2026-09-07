using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Repositories;

public sealed class EventLocationDisclosureAuditRepository(ExploreDbContext dbContext)
    : IEventLocationDisclosureAuditRepository
{
    public async Task<EventLocationDisclosureAudit> AppendAsync(
        EventLocationDisclosureAudit audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);
        RequireTenant(audit.TenantId);
        var trackedEventLocation = dbContext.ChangeTracker.Entries<EventLocation>()
            .Where(item => item.State != EntityState.Deleted)
            .SingleOrDefault(item =>
                item.Entity.Id == audit.EventLocationId && item.Entity.TenantId == audit.TenantId);
        EventLocation eventLocation = trackedEventLocation?.Entity
            ?? await dbContext.EventLocations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == audit.EventLocationId && item.TenantId == audit.TenantId,
                    cancellationToken)
            ?? throw new InvalidOperationException(
                "Disclosure evidence requires an active EventLocation in the current tenant.");

        int? latestVersion = await dbContext.EventLocationDisclosureAudits
            .AsNoTracking()
            .Where(item => item.EventLocationId == audit.EventLocationId)
            .Select(item => (int?)item.NewPolicyVersion)
            .MaxAsync(cancellationToken);
        bool isValidInitialTransition = latestVersion is null
            && audit.PreviousPolicyVersion == 0
            && audit.NewPolicyVersion == 1
            && eventLocation.PolicyVersion == 1;
        bool isValidSubsequentTransition = latestVersion.HasValue
            && audit.PreviousPolicyVersion == latestVersion.Value
            && audit.NewPolicyVersion == audit.PreviousPolicyVersion + 1
            && eventLocation.PolicyVersion == audit.NewPolicyVersion;
        if (!isValidInitialTransition && !isValidSubsequentTransition)
        {
            if (trackedEventLocation?.State == EntityState.Modified
                && latestVersion >= audit.NewPolicyVersion)
            {
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "The EventLocation disclosure policy was modified by another request. Reload and retry.",
                    nameof(EventLocation),
                    audit.EventLocationId.ToString());
            }

            throw new InvalidOperationException(
                "A disclosure audit must match the EventLocation policy version and continue from persisted history.");
        }

        dbContext.EventLocationDisclosureAudits.Add(audit);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsPolicyVersionUniqueViolation(exception))
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The EventLocation disclosure policy was modified by another request. Reload and retry.",
                nameof(EventLocation),
                audit.EventLocationId.ToString(),
                exception);
        }

        return audit;
    }

    private bool IsPolicyVersionUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: { } constraintName
        } &&
        constraintName == RelationalConstraintDescriptorResolver.UniqueIndex<EventLocationDisclosureAudit>(
            dbContext,
            nameof(EventLocationDisclosureAudit.TenantId),
            nameof(EventLocationDisclosureAudit.EventLocationId),
            nameof(EventLocationDisclosureAudit.NewPolicyVersion)).Name;

    public async Task<IReadOnlyList<EventLocationDisclosureAudit>> GetByEventLocationAsync(
        Guid eventLocationId,
        CancellationToken cancellationToken)
    {
        if (eventLocationId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", nameof(eventLocationId));
        }

        RequireTenant();

        return await dbContext.EventLocationDisclosureAudits
            .AsNoTracking()
            .Where(item => item.EventLocationId == eventLocationId)
            .OrderBy(item => item.NewPolicyVersion)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    private void RequireTenant(Guid? entityTenantId = null)
    {
        if (dbContext.IsTenantFilterBypassed)
        {
            return;
        }

        Guid tenantId = dbContext.TenantFilterTenantId
            ?? throw new InvalidOperationException("A tenant context is required for EventLocation privacy persistence.");
        if (entityTenantId.HasValue && entityTenantId.Value != tenantId)
        {
            throw new InvalidOperationException("EventLocation privacy evidence must belong to the current tenant.");
        }
    }
}
