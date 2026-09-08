namespace Explore.Application.DTOs.Footer;

public sealed record FooterLinkItemDto
{
    public Guid Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public bool OpenInNewTab { get; init; }
    public bool IsActive { get; init; }
    public int Order { get; init; }
}
