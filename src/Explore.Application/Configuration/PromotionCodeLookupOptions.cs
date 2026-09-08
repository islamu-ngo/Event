namespace Explore.Application.Configuration;

public sealed class PromotionCodeLookupOptions
{
    public const string SectionName = "Promotions:CodeLookup";

    public int ActiveKeyVersion { get; set; } = 1;
}
