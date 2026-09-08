namespace Explore.Application.DTOs.Footer;

public sealed record FooterLinkGroupDetailsDto
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public string Title { get; init; } = string.Empty;
    public int Order { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<FooterLinkItemDto> Links { get; init; } = [];
}
