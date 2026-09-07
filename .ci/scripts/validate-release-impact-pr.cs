#:property RestorePackagesWithLockFile=false
#pragma warning disable CA1050 // File-based CI scripts intentionally keep helper policy types in the script file.

using System.Net.Http.Headers;
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
var filesUrl = pullRequest.TryGetProperty("url", out var urlElement) ? (urlElement.GetString() ?? string.Empty) + "/files" : string.Empty;

var trustedBots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "dependabot[bot]",
    "github-actions[bot]",
};

if (trustedBots.Contains(author))
{
    Console.WriteLine("Release impact check skipped for trusted bot author " + author + ".");
    return 0;
}

var changedFiles = await GetChangedFilesAsync(filesUrl);
var requiredCategories = ClassifyRequiredCategories(changedFiles);
var failures = new List<string>();

var releaseImpactSection = ExtractSection(body, "Release Impact");
if (string.IsNullOrWhiteSpace(releaseImpactSection))
{
    failures.Add("PR body must include the Release Impact section from the pull request template.");
}

var notApplicableChecked = HasCheckedLine(releaseImpactSection, "Not applicable");
var checkedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var category in ReleaseImpactPolicy.CategoryLabels.Keys)
{
    if (HasCheckedLine(releaseImpactSection, ReleaseImpactPolicy.CategoryLabels[category]))
    {
        checkedCategories.Add(category);
    }
}

var details = ExtractDetails(releaseImpactSection);
if (requiredCategories.Count > 0 && notApplicableChecked)
{
    failures.Add("Release Impact cannot be marked Not applicable because changed files require release-impact evidence: " + string.Join(", ", requiredCategories.Order(StringComparer.OrdinalIgnoreCase)) + ".");
}

foreach (var category in requiredCategories.Order(StringComparer.OrdinalIgnoreCase))
{
    if (!checkedCategories.Contains(category))
    {
        failures.Add("Missing checked Release Impact item: " + ReleaseImpactPolicy.CategoryLabels[category] + ".");
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

static async Task<List<string>> GetChangedFilesAsync(string filesUrl)
{
    var overrideFiles = Environment.GetEnvironmentVariable("RELEASE_IMPACT_CHANGED_FILES");
    if (!string.IsNullOrWhiteSpace(overrideFiles))
    {
        return overrideFiles
            .Split(ReleaseImpactPolicy.FileSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(filesUrl))
    {
        Console.WriteLine("Warning: changed files unavailable; validating PR body checklist only.");
        return new List<string>();
    }

    using var client = new HttpClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("islamu-release-impact-validator");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

    var files = new List<string>();
    var page = 1;
    while (true)
    {
        var separator = filesUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        using var response = await client.GetAsync(filesUrl + separator + "per_page=100&page=" + page);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine("Warning: could not read PR files from GitHub API (" + (int)response.StatusCode + "). Validating PR body checklist only.");
            return new List<string>();
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var pageFiles = document.RootElement;
        if (pageFiles.GetArrayLength() == 0)
        {
            break;
        }

        foreach (var file in pageFiles.EnumerateArray())
        {
            if (file.TryGetProperty("filename", out var filenameElement))
            {
                var filename = filenameElement.GetString();
                if (!string.IsNullOrWhiteSpace(filename))
                {
                    files.Add(filename);
                }
            }
        }

        if (pageFiles.GetArrayLength() < 100)
        {
            break;
        }

        page++;
    }

    return files;
}

static HashSet<string> ClassifyRequiredCategories(IEnumerable<string> changedFiles)
{
    var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in changedFiles)
    {
        var path = file.Replace('\\', '/').ToLowerInvariant();
        if (path.Contains("security", StringComparison.Ordinal)
            || path.Contains("authorization", StringComparison.Ordinal)
            || path.Contains("auth", StringComparison.Ordinal)
            || path.Contains("cerbos", StringComparison.Ordinal)
            || path.Contains("keycloak", StringComparison.Ordinal)
            || path.Contains("cla", StringComparison.Ordinal)
            || path.Contains("secret", StringComparison.Ordinal))
        {
            categories.Add("security");
        }

        if (path.Contains("/migrations/", StringComparison.Ordinal)
            || path.Contains("migration", StringComparison.Ordinal)
            || path.Contains("seed", StringComparison.Ordinal))
        {
            categories.Add("migration");
        }

        if (path.Contains("configuration", StringComparison.Ordinal)
            || path.Contains("config", StringComparison.Ordinal)
            || path.Contains("secrets", StringComparison.Ordinal)
            || path.Contains("appsettings", StringComparison.Ordinal)
            || path.EndsWith("dockerfile", StringComparison.Ordinal)
            || path.Contains("docker-compose", StringComparison.Ordinal)
            || path.Contains(".github/workflows/deploy", StringComparison.Ordinal)
            || path.Contains(".ci/actions/deploy", StringComparison.Ordinal))
        {
            categories.Add("configuration");
        }

        if (path.Contains("schemas/openapi_islamu-event.json", StringComparison.Ordinal)
            || path.Contains("api_changelog", StringComparison.Ordinal)
            || path.Contains("api_contract", StringComparison.Ordinal)
            || path.Contains("eventapiclient.g.cs", StringComparison.Ordinal)
            || path.Contains("explore.api/controllers", StringComparison.Ordinal))
        {
            categories.Add("openapi");
        }

        if (path.Contains("self_hosting", StringComparison.Ordinal)
            || path.Contains("backup_restore_upgrade", StringComparison.Ordinal)
            || path.Contains("release_checklist", StringComparison.Ordinal)
            || path.Contains("operations", StringComparison.Ordinal)
            || path.Contains("deployment", StringComparison.Ordinal)
            || path.Contains("deploy", StringComparison.Ordinal)
            || path.EndsWith("dockerfile", StringComparison.Ordinal)
            || path.Contains("docker-compose", StringComparison.Ordinal))
        {
            categories.Add("operator");
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
    public static readonly char[] FileSeparators = ['\n', '\r', ','];

    public static readonly Dictionary<string, string> CategoryLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["security"] = "Security/auth impact documented",
        ["migration"] = "Migration/data/rollback impact documented",
        ["configuration"] = "Configuration/secrets/deployment impact documented",
        ["openapi"] = "OpenAPI/client contract impact documented",
        ["operator"] = "Operator/self-hosting/release-note impact documented",
    };
}
