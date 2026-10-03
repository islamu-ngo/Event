#:property RestorePackagesWithLockFile=false
#pragma warning disable CA1050 // File-based CI scripts intentionally keep helper policy types in the script file.

using System.Net.Http.Headers;
using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;

var eventPath = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
if (string.IsNullOrWhiteSpace(eventPath) || !File.Exists(eventPath))
{
    Console.WriteLine("Missing GitHub event payload path.");
    return 1;
}

using var eventDocument = JsonDocument.Parse(File.ReadAllText(eventPath));
var pullRequest = eventDocument.RootElement.GetProperty("pull_request");
var body = pullRequest.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;
var author = pullRequest.GetProperty("user").GetProperty("login").GetString() ?? string.Empty;
var isBot = pullRequest.GetProperty("user").TryGetProperty("type", out var accountType)
    && accountType.GetString() == "Bot";
var expectedFileCount = pullRequest.TryGetProperty("changed_files", out var countElement)
    ? countElement.GetInt32() : 0;
Uri? filesUri = pullRequest.TryGetProperty("url", out var urlElement)
    && Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var pullRequestUri)
    && pullRequestUri.Scheme == Uri.UriSchemeHttps && pullRequestUri.Host == "api.github.com"
    ? new Uri(pullRequestUri.AbsoluteUri + "/files") : null;

var trustedBots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "dependabot[bot]",
    "github-actions[bot]",
};

if (isBot && trustedBots.Contains(author))
{
    Console.WriteLine("Release impact check skipped for trusted bot author " + author + ".");
    return 0;
}

List<GitHubChangedFile>? changedFiles;
try
{
    changedFiles = await GetChangedFilesAsync(filesUri);
}
catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
{
    Console.WriteLine("Release impact validation failed: changed-file metadata is unavailable.");
    return 1;
}
if (changedFiles is null || expectedFileCount <= 0 || changedFiles.Count != expectedFileCount
    || changedFiles.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != expectedFileCount)
{
    Console.WriteLine("Release impact validation failed: complete changed-file metadata is required.");
    return 1;
}

var requiredCategories = ClassifyRequiredCategories(changedFiles.SelectMany(file =>
    file.PreviousPath is { } previousPath ? new[] { file.Path, previousPath } : new[] { file.Path }));
if (isBot && author == "imgbot[bot]" && requiredCategories.Count == 0
    && changedFiles.All(file => file.Kind == GitHubFileChangeKind.Modified
        && file.PreviousPath is null && IsReviewedImagePath(file.Path)))
{
    Console.WriteLine("Release impact validation passed: verified ImgBot modified only existing reviewed image assets.");
    return 0;
}
var failures = new List<string>();

var releaseImpactSection = ExtractSection(body, "Release Impact");
if (string.IsNullOrWhiteSpace(releaseImpactSection))
{
    failures.Add("PR body must include the Release Impact section from the pull request template.");
}

var notApplicableChecked = HasCheckedLine(releaseImpactSection, "Not applicable");
var checkedCategories = new HashSet<ReleaseImpactCategory>();
foreach (var (category, rule) in ReleaseImpactPolicy.Rules)
{
    if (HasCheckedLine(releaseImpactSection, rule.Label))
    {
        checkedCategories.Add(category);
    }
}

var details = ExtractDetails(releaseImpactSection);
if (requiredCategories.Count > 0 && notApplicableChecked)
{
    failures.Add("Release Impact cannot be marked Not applicable because changed files require release-impact evidence: "
        + string.Join(", ", requiredCategories.Order().Select(category => ReleaseImpactPolicy.Rules[category].Label)) + ".");
}

foreach (var category in requiredCategories.Order())
{
    if (!checkedCategories.Contains(category))
    {
        failures.Add("Missing checked Release Impact item: " + ReleaseImpactPolicy.Rules[category].Label + ".");
    }
}

if (requiredCategories.Count == 0 && !notApplicableChecked && checkedCategories.Count == 0)
{
    failures.Add("Release Impact must either mark Not applicable or check at least one impact category.");
}

if ((!notApplicableChecked || checkedCategories.Count > 0) && string.IsNullOrWhiteSpace(details))
{
    failures.Add("Release Impact Details must explain the impact, release-note location, or why no release note is needed.");
}

if (failures.Count > 0)
{
    Console.WriteLine("Release impact validation failed:");
    foreach (var failure in failures)
    {
        Console.WriteLine("- " + failure);
    }

    Console.WriteLine();
    Console.WriteLine("Required section: ## Release Impact");
    Console.WriteLine("Check every applicable category and fill Details when release-impact evidence is needed.");
    return 1;
}

Console.WriteLine("Release impact validation passed.");
if (changedFiles.Count > 0)
{
    Console.WriteLine("Changed files inspected: " + changedFiles.Count + ".");
}

return 0;

static async Task<List<GitHubChangedFile>?> GetChangedFilesAsync(Uri? filesUri)
{
    var metadata = Environment.GetEnvironmentVariable("RELEASE_IMPACT_FILE_METADATA");
    if (!string.IsNullOrWhiteSpace(metadata))
    {
        using var document = JsonDocument.Parse(metadata);
        return ReadChangedFiles(document.RootElement);
    }

    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrWhiteSpace(token) || filesUri is null)
    {
        return null;
    }

    using var client = new HttpClient();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("islamu-release-impact-validator");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

    var files = new List<GitHubChangedFile>();
    var page = 1;
    while (page <= 30)
    {
        var pageUri = new UriBuilder(filesUri) { Query = $"per_page=100&page={page}" }.Uri;
        using var response = await client.GetAsync(pageUri, deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine("Changed-file metadata request failed with HTTP status " + (int)response.StatusCode + ".");
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var pageFiles = document.RootElement;
        if (pageFiles.GetArrayLength() == 0)
        {
            break;
        }

        var parsedFiles = ReadChangedFiles(pageFiles);
        if (parsedFiles is null) return null;
        files.AddRange(parsedFiles);

        if (pageFiles.GetArrayLength() < 100)
        {
            break;
        }

        page++;
    }

    return files;
}

static List<GitHubChangedFile>? ReadChangedFiles(JsonElement metadata)
{
    if (metadata.ValueKind != JsonValueKind.Array) return null;
    var files = new List<GitHubChangedFile>();
    foreach (var file in metadata.EnumerateArray())
    {
        if (!file.TryGetProperty("filename", out var name) || name.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(name.GetString())
            || !file.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
            || !ReleaseImpactPolicy.FileChangeKinds.TryGetValue(status.GetString() ?? string.Empty, out var kind))
            return null;
        string? previous = file.TryGetProperty("previous_filename", out var previousName)
            ? previousName.GetString() : null;
        if (kind == GitHubFileChangeKind.Renamed && string.IsNullOrWhiteSpace(previous)) return null;
        files.Add(new(name.GetString()!, kind, previous));
    }
    return files;
}

static bool IsReviewedImagePath(string path) =>
    !path.Contains('\\', StringComparison.Ordinal) && !path.Split('/').Any(segment => segment is "." or "..")
    && (path.StartsWith("assets/", StringComparison.Ordinal)
        || path.StartsWith("docs/internal/assets/diagrams/", StringComparison.Ordinal))
    && (path.EndsWith(".png", StringComparison.Ordinal) || path.EndsWith(".svg", StringComparison.Ordinal));

static HashSet<ReleaseImpactCategory> ClassifyRequiredCategories(IEnumerable<string> changedFiles)
{
    var categories = new HashSet<ReleaseImpactCategory>();
    foreach (var file in changedFiles)
    {
        var path = file.Replace('\\', '/').ToLowerInvariant();
        foreach (var (category, rule) in ReleaseImpactPolicy.Rules)
        {
            if (rule.Fragments.Any(fragment => path.Contains(fragment, StringComparison.Ordinal))
                || rule.Suffixes.Any(suffix => path.EndsWith(suffix, StringComparison.Ordinal)))
                categories.Add(category);
        }
    }

    return categories;
}

static string ExtractSection(string body, string heading)
{
    var pattern = new Regex("(?ims)^##\\s+" + Regex.Escape(heading) + "\\s*\\r?\\n(?<content>.*?)(?=^##\\s+|\\z)");
    var match = pattern.Match(body);
    return match.Success ? match.Groups["content"].Value : string.Empty;
}

static bool HasCheckedLine(string section, string label)
{
    if (string.IsNullOrWhiteSpace(section))
    {
        return false;
    }

    return Regex.IsMatch(section, @"(?im)^\s*-\s*\[[xX]\]\s*" + Regex.Escape(label));
}

static string ExtractDetails(string section)
{
    if (string.IsNullOrWhiteSpace(section))
    {
        return string.Empty;
    }

    var match = Regex.Match(section, @"(?ims)^\s*Details:\s*$\s*(?<details>.*)\z");
    if (!match.Success)
    {
        return string.Empty;
    }

    return string.Join(
        '\n',
        match.Groups["details"].Value
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("<!--", StringComparison.Ordinal)));
}

static class ReleaseImpactPolicy
{
    public static readonly FrozenDictionary<ReleaseImpactCategory, ReleaseImpactRule> Rules =
        new Dictionary<ReleaseImpactCategory, ReleaseImpactRule>
    {
        [ReleaseImpactCategory.Security] = new("Security/auth impact documented",
            ["security", "authorization", "auth", "cerbos", "keycloak", "cla", "secret"], []),
        [ReleaseImpactCategory.Migration] = new("Migration/data/rollback impact documented",
            ["/migrations/", "migration", "seed"], []),
        [ReleaseImpactCategory.Configuration] = new("Configuration/secrets/deployment impact documented",
            ["configuration", "config", "secrets", "appsettings", "docker-compose", ".github/workflows/deploy", ".ci/actions/deploy"],
            ["dockerfile"]),
        [ReleaseImpactCategory.OpenApi] = new("OpenAPI/client contract impact documented",
            ["schemas/openapi_islamu-event.json", "api_changelog", "api_contract", "eventapitagclients.g.cs", "explore.api/controllers"], []),
        [ReleaseImpactCategory.Operator] = new("Operator/self-hosting/release-note impact documented",
            ["self_hosting", "backup_restore_upgrade", "release_checklist", "operations", "deployment", "deploy", "docker-compose"],
            ["dockerfile"]),
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, GitHubFileChangeKind> FileChangeKinds =
        new Dictionary<string, GitHubFileChangeKind>(StringComparer.Ordinal)
        {
            ["added"] = GitHubFileChangeKind.Added,
            ["modified"] = GitHubFileChangeKind.Modified,
            ["removed"] = GitHubFileChangeKind.Removed,
            ["renamed"] = GitHubFileChangeKind.Renamed,
            ["copied"] = GitHubFileChangeKind.Copied,
            ["changed"] = GitHubFileChangeKind.Changed,
            ["unchanged"] = GitHubFileChangeKind.Unchanged
        }.ToFrozenDictionary(StringComparer.Ordinal);
}

enum ReleaseImpactCategory { Security, Migration, Configuration, OpenApi, Operator }
enum GitHubFileChangeKind { Unknown, Added, Modified, Removed, Renamed, Copied, Changed, Unchanged }
sealed record GitHubChangedFile(string Path, GitHubFileChangeKind Kind, string? PreviousPath);
sealed record ReleaseImpactRule(string Label, IReadOnlyList<string> Fragments, IReadOnlyList<string> Suffixes);
