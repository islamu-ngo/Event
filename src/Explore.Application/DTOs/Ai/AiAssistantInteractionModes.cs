namespace Explore.Application.DTOs.Ai;

public static class AiAssistantInteractionModes
{
    public const string Ask = "ask";
    public const string Build = "build";

    public static string Normalize(string? mode)
        => string.Equals(mode?.Trim(), Ask, StringComparison.OrdinalIgnoreCase)
            ? Ask
            : Build;

    public static bool IsValid(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return true;
        }

        var normalized = mode.Trim();
        return string.Equals(normalized, Ask, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, Build, StringComparison.OrdinalIgnoreCase);
    }

    public static bool AllowsToolProposals(string? mode)
        => string.Equals(Normalize(mode), Build, StringComparison.Ordinal);
}
