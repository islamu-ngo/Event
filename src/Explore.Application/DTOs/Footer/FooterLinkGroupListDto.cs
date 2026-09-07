namespace Explore.Application.DTOs.Footer;

public sealed record FooterLinkGroupListDto
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public string Title { get; init; } = string.Empty;
    public int Order { get; init; }
    public bool IsActive { get; init; }
    public int LinkCount { get; init; }
}
