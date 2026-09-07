using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventContactShareConsentRepository : GenericRepository<EventContactShareConsent, Guid>, IEventContactShareConsentRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventContactShareConsentRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventContactShareConsent> CreateWithHistory(
        EventContactShareConsent consent,
        EventContactShareConsentHistory history)
    {
        await _dbContext.EventContactShareConsents.AddAsync(consent);
        await _dbContext.EventContactShareConsentHistory.AddAsync(history);
        await _dbContext.SaveChangesAsync();
        return consent;
    }

    public async Task UpdateWithHistory(
        EventContactShareConsent consent,
        EventContactShareConsentHistory history)
    {
        _dbContext.Entry(consent).State = EntityState.Modified;
        await _dbContext.EventContactShareConsentHistory.AddAsync(history);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<EventContactShareConsent?> GetByScope(Guid tenantId, int subjectTypeId, Guid subjectId, Guid recipientActorId, string purposeCode)
    {
        return await _dbContext.EventContactShareConsents
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.SubjectTypeId == subjectTypeId &&
                c.SubjectId == subjectId &&
                c.RecipientActorId == recipientActorId &&
                c.PurposeCode == purposeCode);
    }

    public Task<EventContactShareConsent?> GetByScope(Guid tenantId, Guid userId, Guid recipientActorId, string purposeCode) =>
        GetByScope(tenantId, (int)ContactShareConsentSubjectTypeEnum.User, userId, recipientActorId, purposeCode);

    public async Task<(List<EventContactShareConsent> Items, int TotalCount)> GetGrantedForRecipient(
        Guid tenantId, Guid recipientActorId, Guid? eventId, string? emailSearch, int pageNumber, int pageSize)
    {
        var query = _dbContext.EventContactShareConsents
            .AsNoTracking()
            .AsSplitQuery()
            .Where(c => c.TenantId == tenantId &&
                        c.RecipientActorId == recipientActorId &&
                        c.Status == ConsentStatus.Granted);

        if (!string.IsNullOrWhiteSpace(emailSearch))
            query = query.Where(c => c.EmailNormalizedSnapshot.Contains(emailSearch.ToLowerInvariant()));

        if (eventId is { } sourceEventId)
        {
            query = query.Where(consent => _dbContext.EventContactShareConsentHistory.Any(history =>
                history.TenantId == tenantId && history.ConsentId == consent.Id &&
                history.SourceEventId == sourceEventId));
        }

        query = query.OrderByDescending(c => c.GrantedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<List<EventContactShareConsent>> GetByUser(Guid tenantId, Guid userId)
    {
        return await _dbContext.EventContactShareConsents
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.RecipientActor)
                .ThenInclude(a => a.Pii)
            .Include(c => c.RecipientActor)
                .ThenInclude(a => a.Organization)
                    .ThenInclude(o => o!.Pii)
            .Where(c => c.TenantId == tenantId && c.SubjectTypeId == (int)ContactShareConsentSubjectTypeEnum.User && c.SubjectId == userId)
            .OrderByDescending(c => c.GrantedAt)
            .ToListAsync();
    }

    public async Task<List<EventContactShareConsent>> GetGrantedForExport(
        Guid tenantId,
        Guid recipientActorId,
        Guid? eventId,
        string consentPurposeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consentPurposeCode);
        string normalizedPurposeCode = consentPurposeCode.Trim().ToUpperInvariant();
        var query = _dbContext.EventContactShareConsents
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.RecipientActor)
                .ThenInclude(a => a.Organization)
                    .ThenInclude(o => o!.Pii)
            .Where(c => c.TenantId == tenantId &&
                        c.RecipientActorId == recipientActorId &&
                        c.Status == ConsentStatus.Granted &&
                        c.PurposeCode == normalizedPurposeCode);

        if (eventId is { } sourceEventId)
        {
            query = query.Where(consent => _dbContext.EventContactShareConsentHistory.Any(history =>
                history.TenantId == tenantId && history.ConsentId == consent.Id &&
                history.SourceEventId == sourceEventId));
        }

        return await query.OrderByDescending(c => c.GrantedAt).ToListAsync();
    }
}
