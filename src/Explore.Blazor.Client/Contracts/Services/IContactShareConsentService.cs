// Wraps API calls for checking, listing, and withdrawing organizer email-sharing consents.

namespace Explore.Blazor.Client.Contracts.Services;

/// <summary>
/// Service interface for managing contact-sharing consents between users and organizations.
/// Used by registration dialogs (to check/display consent status) and the Connected Apps settings page.
/// </summary>
public interface IContactShareConsentService
{
    /// <summary>
    /// Checks whether the current user has an active (granted) consent for the given organizer.
    /// Used during registration to decide between showing the checkbox or an info notice.
    /// </summary>
    Task<bool> CheckConsentForOrganizerAsync(Guid organizerActorId, CancellationToken ct = default);

    /// <summary>
    /// Gets all contact-sharing consents for the current user.
    /// Used by the Connected Apps / Third-party Access settings page.
    /// </summary>
    Task<List<UserConsentViewModel>> GetMyConsentsAsync(CancellationToken ct = default);

    /// <summary>
    /// Withdraws a previously granted consent.
    /// </summary>
    Task<bool> WithdrawConsentAsync(Guid consentId, CancellationToken ct = default);

    /// <summary>
    /// Gets shared contacts for an organization's events (organizer view).
    /// Only accessible by authorized organization members.
    /// </summary>
    Task<List<SharedContactViewModel>> GetOrganizationSharedContactsAsync(
        Guid organizationActorId,
        Guid? eventId = null,
        string? searchEmail = null,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Exports shared contacts for an organization as a file download (CSV or TSV).
    /// Returns the file bytes and suggested filename.
    /// </summary>
    Task<(byte[] FileBytes, string FileName)?> ExportSharedContactsAsync(
        Guid organizationActorId,
        string format = "csv",
        Guid? eventId = null,
        CancellationToken ct = default);
}

/// <summary>
/// View model for displaying a user's consent on the Connected Apps page.
/// </summary>
public sealed class UserConsentViewModel
{
    public Guid Id { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public Guid OrganizationActorId { get; set; }
    public string? OrganizationProfilePictureUri { get; set; }
    public string PurposeCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string EmailSnapshot { get; set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public string? SourceEventTitle { get; set; }
}

/// <summary>
/// View model for displaying a shared contact in the organizer contacts page.
/// </summary>
public sealed class SharedContactViewModel
{
    public Guid ConsentId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; set; }
    public Guid? SourceEventId { get; set; }
    public string? SourceEventTitle { get; set; }
    public string PurposeCode { get; set; } = string.Empty;
}
