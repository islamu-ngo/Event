namespace Explore.Application.DTOs.ContactShareConsent;

public sealed record UserContactShareConsentDto
{
    public Guid Id { get; init; }
    public Guid RecipientActorId { get; init; }
    public string? OrganizationName { get; init; }
    public Guid? SourceEventId { get; init; }
    public string? SourceEventTitle { get; init; }
    public string PurposeCode { get; init; } = string.Empty;
    public int Status { get; init; }
    public string EmailSnapshot { get; init; } = string.Empty;
    public DateTime GrantedAt { get; init; }
    public DateTime? WithdrawnAt { get; init; }
}
