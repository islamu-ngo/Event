namespace Explore.Domain.Policies;

public sealed class BrandingPolicy
{
    public PolicySlot<string> DisplayName { get; set; } = new(string.Empty);
    public PolicySlot<string> LogoUrl { get; set; } = new(string.Empty);
    public PolicySlot<string> FaviconUrl { get; set; } = new(string.Empty);
    public PolicySlot<string> CustomCssUrl { get; set; } = new(string.Empty);
}
