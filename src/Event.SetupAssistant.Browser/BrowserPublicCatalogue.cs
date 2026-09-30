namespace ISLAMU.Event.SetupAssistant.Browser;

using System.Text.Json;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Environment;

public sealed record BrowserCatalogueEntry(
    string Key,
    string Category,
    string Requirement,
    string? SafeDefault,
    string HelpKey);

public sealed record BrowserCatalogue(
    IReadOnlyList<BrowserCatalogueEntry> Entries,
    string DownloadHref);

public static class BrowserPublicCatalogue
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static BrowserCatalogue Create()
    {
        BrowserCatalogueEntry[] entries = PlatformEnvironmentCatalogue.Catalogue.Definitions
            .Where(definition => definition.Sensitivity == EnvironmentVariableSensitivity.Public)
            .Select(definition => new BrowserCatalogueEntry(
                definition.Key,
                definition.Category.ToString(),
                definition.Requirement.ToString(),
                definition.SafeDefault,
                definition.HelpKey))
            .ToArray();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entries, SerializerOptions);
        if (!SetupArtifactPolicy.TryProjectPublic(
                SetupArtifactKind.PublicCatalogue, bytes, out ReadOnlyMemory<byte> publicBytes))
            throw new InvalidOperationException("public-catalogue-classification-failed");

        return new BrowserCatalogue(
            Array.AsReadOnly(entries),
            $"data:application/json;base64,{Convert.ToBase64String(publicBytes.Span)}");
    }
}
