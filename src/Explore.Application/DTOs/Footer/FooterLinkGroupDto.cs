namespace Explore.Application.DTOs.Footer;

public sealed record FooterLinkGroupDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public int Order { get; init; }
    public IReadOnlyList<FooterLinkItemDto> Links { get; init; } = [];
}
