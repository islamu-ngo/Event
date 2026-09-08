#:property RestorePackagesWithLockFile=false

using System.Text.Json;

var repoRoot = args.Length > 0 ? args[0] : ".";

var aiBomPath = Path.Combine(repoRoot, "ai-bom.v1.json");
var conformityDocPath = Path.Combine(repoRoot, "docs", "legal", "EU_AI_ACT_CONFORMITY.md");

var failures = new List<string>();

if (!File.Exists(aiBomPath))
{
    failures.Add("ai-bom.v1.json is missing in repository root.");
}
else
{
    try
    {
        var json = File.ReadAllText(aiBomPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("bomFormat", out var bomFormat) || bomFormat.GetString() != "CycloneDX")
        {
            failures.Add("ai-bom.v1.json: 'bomFormat' must be 'CycloneDX'.");
        }

        if (!root.TryGetProperty("specVersion", out var specVersion) || specVersion.GetString() != "1.6")
        {
            failures.Add("ai-bom.v1.json: 'specVersion' must be '1.6'.");
        }

        if (root.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("licenses", out var licenses))
        {
            var licenseId = licenses.EnumerateArray().FirstOrDefault().GetProperty("license").GetProperty("id").GetString();
            if (licenseId != "AGPL-3.0-only")
            {
                failures.Add($"ai-bom.v1.json: platform metadata license must be 'AGPL-3.0-only' (found '{licenseId}').");
            }
        }
        else
        {
            failures.Add("ai-bom.v1.json: metadata.licenses is required and must declare 'AGPL-3.0-only'.");
        }

        if (!root.TryGetProperty("components", out var components) || components.GetArrayLength() == 0)
        {
            failures.Add("ai-bom.v1.json: 'components' must contain declared AI models and frameworks.");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"ai-bom.v1.json is not valid JSON: {ex.Message}");
    }
}

if (!File.Exists(conformityDocPath))
{
    failures.Add("docs/legal/EU_AI_ACT_CONFORMITY.md is missing.");
}
else
{
    var text = File.ReadAllText(conformityDocPath);
    if (!text.Contains("Article 50", StringComparison.OrdinalIgnoreCase))
    {
        failures.Add("docs/legal/EU_AI_ACT_CONFORMITY.md must document Article 50 Transparency obligations.");
    }
    if (!text.Contains("Annex IV", StringComparison.OrdinalIgnoreCase))
    {
        failures.Add("docs/legal/EU_AI_ACT_CONFORMITY.md must document Annex IV Technical Documentation.");
    }
}

if (failures.Count > 0)
{
    Console.WriteLine("EU AI Act & AI-BOM Conformity validation failed:");
    foreach (var failure in failures)
    {
        Console.WriteLine($"- {failure}");
    }
    return 1;
}

Console.WriteLine("EU AI Act technical documentation and AI-BOM CycloneDX 1.6 specifications validated successfully.");
return 0;
