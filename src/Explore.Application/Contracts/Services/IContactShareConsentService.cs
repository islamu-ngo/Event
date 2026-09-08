using Explore.Application.DTOs.ContactShareConsent;

namespace Explore.Application.Contracts.Services;

public interface IContactShareConsentService
{
    /// <summary>
    /// Processes the email sharing consent during event registration.
    /// Creates or reactivates a per-organizer consent record if the user opts in.
    /// </summary>
    /// <param name="tenantId">Current tenant</param>
    /// <param name="userId">Registering user</param>
    /// <param name="eventId">The event being registered for</param>
    /// <param name="registrationOrderId">The source registration order ID</param>
    /// <param name="shareEmailWithOrganizer">Whether the user checked the consent box</param>
    /// <param name="consentText">Exact consent text shown to the user</param>
    /// <param name="consentUiVersion">UI version identifier</param>
    /// <returns>The consent ID if created/reactivated, null otherwise</returns>
    Task<Guid?> ProcessRegistrationConsent(
        Guid tenantId,
        Guid userId,
        Guid eventId,
        Guid registrationOrderId,
        bool shareEmailWithOrganizer,
        string? consentText,
        string? consentUiVersion);

    /// <summary>
    /// Resolves the organisation represented by a recipient actor.
    /// </summary>
    Task<Guid?> ResolveRecipientOrganizationId(Guid recipientActorId);

    /// <summary>
    /// Checks whether a granted consent already exists for this user + organizer combination.
    /// Used by UI to decide whether to show the checkbox or an info notice.
    /// </summary>
    Task<bool> HasGrantedConsentForOrganizer(Guid tenantId, Guid userId, Guid recipientActorId);

    /// <summary>
    /// Withdraws a previously granted consent.
    /// </summary>
    Task WithdrawConsent(Guid tenantId, Guid userId, Guid consentId);

    /// <summary>
    /// Gets all consents for a user (for the Connected Apps page).
    /// </summary>
    Task<List<UserContactShareConsentDto>> GetUserConsents(Guid tenantId, Guid userId);
}
