namespace ISLAMU.Wire.Contracts.ConfigurationPortability;

public static class LegalMarkdownContentLimits
{
    public const int MaximumDocumentsPerScope = 16;
    public const int MaximumLocalesPerDocument = 32;
    public const int MaximumMarkdownUtf8BytesPerLocale = 256 * 1024;
    public const int MaximumLinksPerLocale = 128;
    public const int MaximumPlaceholdersPerLocale = 64;
    public const int MaximumTitleLength = 200;
    public const int MaximumSummaryLength = 500;
    public const int MaximumLanguageTagLength = 35;
    public const int MaximumLinkLength = 2048;
    public const int MaximumIdentityValueLength = 500;
}
