namespace Explore.Application.DTOs.ContactShareConsent;

public sealed record SharedContactDto
{
    public Guid ConsentId { get; init; }
    public string Email { get; init; } = string.Empty;
    public DateTime GrantedAt { get; init; }
    public Guid? SourceEventId { get; init; }
    public string? SourceEventTitle { get; init; }
    public string PurposeCode { get; init; } = string.Empty;
}
