#:property RestorePackagesWithLockFile=false

using System.Text.Json;
using System.Text.Json.Nodes;

Directory.CreateDirectory("artifacts/container");

var imageName = GetRequiredEnvironmentVariable("IMAGE_NAME");
var tags = GetRequiredEnvironmentVariable("TAGS")
    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var metadata = ParseMetadata(Environment.GetEnvironmentVariable("METADATA") ?? "{}");

var evidence = new JsonObject
{
    ["imageName"] = imageName,
    ["dockerfile"] = GetRequiredEnvironmentVariable("DOCKERFILE"),
    ["context"] = GetRequiredEnvironmentVariable("CONTEXT"),
    ["digest"] = GetRequiredEnvironmentVariable("DIGEST"),
    ["tags"] = new JsonArray(tags.Select(static tag => JsonValue.Create(tag)).ToArray<JsonNode?>()),
    ["metadata"] = metadata,
    ["github"] = new JsonObject
    {
        ["repository"] = GetRequiredEnvironmentVariable("GITHUB_REPOSITORY"),
        ["sha"] = GetRequiredEnvironmentVariable("GITHUB_SHA"),
        ["ref"] = GetRequiredEnvironmentVariable("GITHUB_REF"),
        ["runId"] = GetRequiredEnvironmentVariable("GITHUB_RUN_ID"),
        ["runAttempt"] = GetRequiredEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
    },
};

var outputPath = Path.Combine("artifacts", "container", $"{imageName}-digest.json");
File.WriteAllText(outputPath, evidence.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
Console.WriteLine($"Wrote container digest evidence to {outputPath}.");
return 0;

static JsonNode ParseMetadata(string metadataRaw)
{
    try
    {
        return JsonNode.Parse(metadataRaw) ?? new JsonObject();
    }
    catch (JsonException)
    {
        return new JsonObject
        {
            ["raw"] = metadataRaw,
        };
    }
}

static string GetRequiredEnvironmentVariable(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        Console.Error.WriteLine($"Required environment variable {name} is missing.");
        Environment.Exit(1);
    }

    return value;
}
