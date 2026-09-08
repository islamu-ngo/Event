#:property RestorePackagesWithLockFile=false

using System.Text.Json;
using System.Text.Json.Nodes;

Directory.CreateDirectory(Path.Combine("artifacts", "container"));

var imageName = GetRequiredEnvironmentVariable("IMAGE_NAME");
var registry = GetRequiredEnvironmentVariable("REGISTRY").TrimEnd('/');
var registryUser = GetRequiredEnvironmentVariable("REGISTRY_USER").Trim('/');
var digest = GetRequiredEnvironmentVariable("DIGEST");
var repository = $"{registry}/{registryUser}/{imageName}";
var tags = GetRequiredEnvironmentVariable("TAGS")
    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var immutableTags = tags
    .Where(tag => tag.StartsWith(repository + ":", StringComparison.Ordinal))
    .Where(static tag =>
    {
        var tagName = tag[(tag.LastIndexOf(':') + 1)..];
        return tagName.StartsWith("sha-", StringComparison.Ordinal) || tagName.StartsWith("dev-", StringComparison.Ordinal);
    })
    .Distinct(StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();

if (immutableTags.Length == 0)
{
    Console.Error.WriteLine($"No immutable deployment tags were found for primary registry repository {repository}.");
    Console.Error.WriteLine("Expected a primary-registry tag with prefix 'sha-' or 'dev-'.");
    return 1;
}

var evidence = new JsonObject
{
    ["imageName"] = imageName,
    ["registry"] = registry,
    ["repository"] = repository,
    ["digest"] = digest,
    ["immutableDeploymentTags"] = new JsonArray(immutableTags.Select(static tag => JsonValue.Create(tag)).ToArray<JsonNode?>()),
    ["github"] = new JsonObject
    {
        ["repository"] = GetRequiredEnvironmentVariable("GITHUB_REPOSITORY"),
        ["sha"] = GetRequiredEnvironmentVariable("GITHUB_SHA"),
        ["ref"] = GetRequiredEnvironmentVariable("GITHUB_REF"),
        ["runId"] = GetRequiredEnvironmentVariable("GITHUB_RUN_ID"),
        ["runAttempt"] = GetRequiredEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
    },
};

var artifactRoot = Path.Combine("artifacts", "container");
var evidencePath = Path.Combine(artifactRoot, $"{imageName}-promotion.json");
var tagsPath = Path.Combine(artifactRoot, $"{imageName}-promotion-tags.txt");

File.WriteAllText(evidencePath, evidence.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
File.WriteAllLines(tagsPath, immutableTags);

Console.WriteLine($"Wrote immutable tag promotion evidence to {evidencePath}.");
Console.WriteLine($"Wrote {immutableTags.Length} immutable tag reference(s) to {tagsPath}.");
return 0;

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
