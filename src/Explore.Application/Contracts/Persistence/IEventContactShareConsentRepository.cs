using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IEventContactShareConsentRepository : IGenericRepository<EventContactShareConsent, Guid>
{
    Task<EventContactShareConsent> CreateWithHistory(
        EventContactShareConsent consent,
        EventContactShareConsentHistory history);

    Task UpdateWithHistory(
        EventContactShareConsent consent,
        EventContactShareConsentHistory history);

    Task<EventContactShareConsent?> GetByScope(Guid tenantId, Guid userId, Guid recipientActorId, string purposeCode);

    /// <summary>
    /// Finds the unique consent row for a tenant + user + recipient actor + purpose scope.
    /// Returns null if no consent exists for this scope.
    /// </summary>
    Task<EventContactShareConsent?> GetByScope(Guid tenantId, int subjectTypeId, Guid subjectId, Guid recipientActorId, string purposeCode);

    /// <summary>
    /// Gets all granted consents for a specific recipient actor (organisation), optionally filtered by event.
    /// </summary>
    Task<(List<EventContactShareConsent> Items, int TotalCount)> GetGrantedForRecipient(
        Guid tenantId, Guid recipientActorId, Guid? eventId, string? emailSearch, int pageNumber, int pageSize);

    /// <summary>
    /// Gets all consents (any status) for a specific user.
    /// </summary>
    Task<List<EventContactShareConsent>> GetByUser(Guid tenantId, Guid userId);

    /// <summary>
    /// Gets granted consents for a recipient, suitable for export (no pagination).
    /// </summary>
    Task<List<EventContactShareConsent>> GetGrantedForExport(
        Guid tenantId,
        Guid recipientActorId,
        Guid? eventId,
        string consentPurposeCode);
}
