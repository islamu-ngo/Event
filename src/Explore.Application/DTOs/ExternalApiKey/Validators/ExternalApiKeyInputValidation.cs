namespace Explore.Application.DTOs.ExternalApiKey.Validators;

internal static class ExternalApiKeyInputValidation
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1000;

    public static bool DoesNotContainControlCharacters(string? value)
        => value is null || !value.Any(char.IsControl);

    public static string NormalizeRequiredText(string value)
        => value.Trim();

    public static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
