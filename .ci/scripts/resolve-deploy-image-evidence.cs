#:property RestorePackagesWithLockFile=false

using System.Text.Json.Nodes;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: dotnet run .ci/scripts/resolve-deploy-image-evidence.cs -- <artifact-root> <image-name> <immutable-tag-prefix>");
    return 1;
}

var artifactRoot = args[0];
var imageName = args[1];
var immutableTagPrefix = args[2];
var githubSha = GetRequiredEnvironmentVariable("GITHUB_SHA");
var expectedTagSuffix = $":{immutableTagPrefix}{githubSha}";

if (!Directory.Exists(artifactRoot))
{
    Console.Error.WriteLine($"Container evidence artifact root does not exist: {artifactRoot}");
    return 1;
}

var promotionPath = Directory
    .EnumerateFiles(artifactRoot, $"{imageName}-promotion.json", SearchOption.AllDirectories)
    .Order(StringComparer.Ordinal)
    .FirstOrDefault();

if (promotionPath is null)
{
    Console.Error.WriteLine($"Could not find {imageName}-promotion.json under {artifactRoot}.");
    return 1;
}

JsonNode? document;
try
{
    document = JsonNode.Parse(File.ReadAllText(promotionPath));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not parse promotion evidence {promotionPath}: {ex.Message}");
    return 1;
}

var digest = document?["digest"]?.GetValue<string>();
if (string.IsNullOrWhiteSpace(digest))
{
    Console.Error.WriteLine($"Promotion evidence {promotionPath} does not contain a non-empty digest.");
    return 1;
}

var immutableTags = document?["immutableDeploymentTags"]?.AsArray()
    .Select(static tag => tag?.GetValue<string>())
    .Where(static tag => !string.IsNullOrWhiteSpace(tag))
    .Select(static tag => tag!)
    .ToArray() ?? [];

var expectedImageTag = immutableTags
    .SingleOrDefault(tag => tag.EndsWith(expectedTagSuffix, StringComparison.Ordinal));

if (expectedImageTag is null)
{
    Console.Error.WriteLine($"Promotion evidence {promotionPath} does not contain exactly one tag ending with {expectedTagSuffix}.");
    Console.Error.WriteLine("Available immutable tags:");
    foreach (var tag in immutableTags)
    {
        Console.Error.WriteLine($"- {tag}");
    }

    return 1;
}

var normalizedPromotionPath = promotionPath.Replace(Path.DirectorySeparatorChar, '/');
Console.WriteLine($"Resolved {imageName} deploy evidence:");
Console.WriteLine($"- Expected tag: {expectedImageTag}");
Console.WriteLine($"- Expected digest: {digest}");
Console.WriteLine($"- Promotion evidence: {normalizedPromotionPath}");

var githubOutput = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
if (!string.IsNullOrWhiteSpace(githubOutput))
{
    File.AppendAllLines(githubOutput,
    [
        $"expected-image-tag={expectedImageTag}",
        $"expected-image-digest={digest}",
        $"promotion-evidence-path={normalizedPromotionPath}",
    ]);
}

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
