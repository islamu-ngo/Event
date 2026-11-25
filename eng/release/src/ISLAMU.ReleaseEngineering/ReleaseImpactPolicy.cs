using System.Collections.Frozen;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public enum ReleaseImpactCategory
{
    Security,
    Migration,
    Configuration,
    OpenApi,
    Operator,
}

public enum ReleaseImpactFileChangeKind
{
    Unknown,
    Added,
    Modified,
    Removed,
    Renamed,
    Copied,
    Changed,
    Unchanged,
}

public sealed record ReleaseImpactChangedFile(
    string Path,
    ReleaseImpactFileChangeKind Kind,
    string? PreviousPath);

public sealed record ReleaseImpactRule(
    string Label,
    IReadOnlyList<string> Fragments,
    IReadOnlyList<string> Suffixes);

public sealed record ReleaseImpactEvaluationRequest(
    string Author,
    bool IsBot,
    string PullRequestBody,
    int ExpectedFileCount,
    IReadOnlyList<ReleaseImpactChangedFile>? ChangedFiles);

public sealed record ReleaseImpactEvaluationResult(
    bool IsValid,
    string Message,
    IReadOnlyList<string> Failures);

/// <summary>
/// Evaluates pull-request release-impact documentation obligations in process without spawning compilers.
/// </summary>
public static class ReleaseImpactPolicy
{
    private const long MaximumMetadataBytes = 16L * 1024 * 1024;

    private static readonly HashSet<string> TrustedBots = new(StringComparer.OrdinalIgnoreCase)
    {
        "dependabot[bot]",
        "github-actions[bot]",
    };

    private static readonly Regex ReleaseImpactSectionPattern = new(
        "(?ims)^##\\s+Release\\ Impact\\s*\\r?\\n(?<content>.*?)(?=^##\\s+|\\z)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    private static readonly Regex DetailsPattern = new(
        "(?ims)^\\s*Details:\\s*$\\s*(?<details>.*)\\z",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    public static readonly FrozenDictionary<ReleaseImpactCategory, ReleaseImpactRule> Rules =
        new Dictionary<ReleaseImpactCategory, ReleaseImpactRule>
        {
            [ReleaseImpactCategory.Security] = new(
                "Security/auth impact documented",
                ["security", "authorization", "auth", "cerbos", "keycloak", "cla", "secret"],
                []),
            [ReleaseImpactCategory.Migration] = new(
                "Migration/data/rollback impact documented",
                ["/migrations/", "migration", "seed"],
                []),
            [ReleaseImpactCategory.Configuration] = new(
                "Configuration/secrets/deployment impact documented",
                ["configuration", "config", "secrets", "appsettings", "docker-compose", ".github/workflows/deploy", ".ci/actions/deploy"],
                ["dockerfile"]),
            [ReleaseImpactCategory.OpenApi] = new(
                "OpenAPI/client contract impact documented",
                ["schemas/openapi_islamu-event.json", "api_changelog", "api_contract", "eventapitagclients.g.cs", "explore.api/controllers"],
                []),
            [ReleaseImpactCategory.Operator] = new(
                "Operator/self-hosting/release-note impact documented",
                ["self_hosting", "backup_restore_upgrade", "release_checklist", "operations", "deployment", "deploy", "docker-compose"],
                ["dockerfile"]),
        }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, ReleaseImpactFileChangeKind> FileChangeKinds =
        new Dictionary<string, ReleaseImpactFileChangeKind>(StringComparer.Ordinal)
        {
            ["added"] = ReleaseImpactFileChangeKind.Added,
            ["modified"] = ReleaseImpactFileChangeKind.Modified,
            ["removed"] = ReleaseImpactFileChangeKind.Removed,
            ["renamed"] = ReleaseImpactFileChangeKind.Renamed,
            ["copied"] = ReleaseImpactFileChangeKind.Copied,
            ["changed"] = ReleaseImpactFileChangeKind.Changed,
            ["unchanged"] = ReleaseImpactFileChangeKind.Unchanged,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static ReleaseImpactEvaluationResult Evaluate(ReleaseImpactEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsBot && TrustedBots.Contains(request.Author))
        {
            return new ReleaseImpactEvaluationResult(
                true,
                $"Release impact check skipped for trusted bot author {request.Author}.",
                []);
        }

        if (!HasCompleteChangedFileMetadata(request))
        {
            return new ReleaseImpactEvaluationResult(
                false,
                "Release impact validation failed: complete changed-file metadata is required.",
                ["Complete changed-file metadata is required."]);
        }

        HashSet<ReleaseImpactCategory> requiredCategories = ClassifyRequiredCategories(
            request.ChangedFiles!.SelectMany(file =>
                file.PreviousPath is { } previousPath
                    ? new[] { file.Path, previousPath }
                    : [file.Path]));

        if (IsVerifiedImgBotAssetOnlyChange(request, requiredCategories))
        {
            return new ReleaseImpactEvaluationResult(
                true,
                "Release impact validation passed: verified ImgBot modified only existing reviewed image assets.",
                []);
        }

        List<string> failures = ValidateReleaseImpactSection(request.PullRequestBody, requiredCategories);
        return failures.Count > 0
            ? new ReleaseImpactEvaluationResult(false, "Release impact validation failed.", failures)
            : new ReleaseImpactEvaluationResult(true, $"Release impact validation passed. Changed files inspected: {request.ChangedFiles!.Count}.", []);
    }

    private static bool HasCompleteChangedFileMetadata(ReleaseImpactEvaluationRequest request) =>
        request.ChangedFiles is not null &&
        request.ExpectedFileCount > 0 &&
        request.ChangedFiles.Count == request.ExpectedFileCount &&
        request.ChangedFiles.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() == request.ExpectedFileCount;

    private static bool IsVerifiedImgBotAssetOnlyChange(
        ReleaseImpactEvaluationRequest request,
        HashSet<ReleaseImpactCategory> requiredCategories) =>
        request.IsBot &&
        string.Equals(request.Author, "imgbot[bot]", StringComparison.Ordinal) &&
        requiredCategories.Count == 0 &&
        request.ChangedFiles!.All(file =>
            file.Kind == ReleaseImpactFileChangeKind.Modified &&
            file.PreviousPath is null &&
            IsReviewedImagePath(file.Path));

    private static List<string> ValidateReleaseImpactSection(string? pullRequestBody, HashSet<ReleaseImpactCategory> requiredCategories)
    {
        var failures = new List<string>();
        string releaseImpactSection = ExtractReleaseImpactSection(pullRequestBody ?? string.Empty);
        if (string.IsNullOrWhiteSpace(releaseImpactSection))
        {
            failures.Add("PR body must include the Release Impact section from the pull request template.");
        }

        bool notApplicableChecked = HasCheckedLine(releaseImpactSection, "Not applicable");
        HashSet<ReleaseImpactCategory> checkedCategories = Rules
            .Where(pair => HasCheckedLine(releaseImpactSection, pair.Value.Label))
            .Select(pair => pair.Key)
            .ToHashSet();

        if (requiredCategories.Count > 0 && notApplicableChecked)
        {
            failures.Add(
                "Release Impact cannot be marked Not applicable because changed files require release-impact evidence: " +
                string.Join(", ", requiredCategories.Order().Select(category => Rules[category].Label)) + ".");
        }

        foreach (ReleaseImpactCategory missing in requiredCategories.Order().Where(category => !checkedCategories.Contains(category)))
        {
            failures.Add("Missing checked Release Impact item: " + Rules[missing].Label + ".");
        }

        if (requiredCategories.Count == 0 && !notApplicableChecked && checkedCategories.Count == 0)
        {
            failures.Add("Release Impact must either mark Not applicable or check at least one impact category.");
        }

        if ((!notApplicableChecked || checkedCategories.Count > 0) && string.IsNullOrWhiteSpace(ExtractDetails(releaseImpactSection)))
        {
            failures.Add("Release Impact Details must explain the impact, release-note location, or why no release note is needed.");
        }

        return failures;
    }

    public static ReleaseImpactEvaluationResult EvaluateFromJson(string eventJson, string filesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(filesJson);

        using JsonDocument eventDocument = JsonDocument.Parse(eventJson);
        ParsedPullRequestEvent parsed = ParsePullRequestEvent(eventDocument.RootElement);
        using JsonDocument filesDocument = JsonDocument.Parse(filesJson);
        IReadOnlyList<ReleaseImpactChangedFile>? files = ReadChangedFiles(filesDocument.RootElement);

        return Evaluate(new ReleaseImpactEvaluationRequest(
            parsed.Author,
            parsed.IsBot,
            parsed.Body,
            parsed.ExpectedFileCount,
            files));
    }

    public static async Task<int> EvaluateEventFileAsync(
        string? eventPath,
        string? fileMetadataPath,
        string? githubToken,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (string.IsNullOrWhiteSpace(eventPath) || !File.Exists(eventPath))
        {
            await output.WriteLineAsync("Missing GitHub event payload path.");
            return 1;
        }

        using JsonDocument eventDocument = JsonDocument.Parse(await File.ReadAllTextAsync(eventPath));
        ParsedPullRequestEvent parsed = ParsePullRequestEvent(eventDocument.RootElement);

        if (parsed.IsBot && TrustedBots.Contains(parsed.Author))
        {
            await output.WriteLineAsync($"Release impact check skipped for trusted bot author {parsed.Author}.");
            return 0;
        }

        IReadOnlyList<ReleaseImpactChangedFile>? changedFiles;
        try
        {
            changedFiles = await GetChangedFilesAsync(fileMetadataPath, parsed.FilesUri, githubToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException
            or IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync("Release impact validation failed: changed-file metadata is unavailable.");
            return 1;
        }

        ReleaseImpactEvaluationResult result = Evaluate(new ReleaseImpactEvaluationRequest(
            parsed.Author,
            parsed.IsBot,
            parsed.Body,
            parsed.ExpectedFileCount,
            changedFiles));

        if (!result.IsValid)
        {
            await output.WriteLineAsync("Release impact validation failed:");
            foreach (string failure in result.Failures)
            {
                await output.WriteLineAsync("- " + failure);
            }

            await output.WriteLineAsync();
            await output.WriteLineAsync("Required section: ## Release Impact");
            await output.WriteLineAsync("Check every applicable category and fill Details when release-impact evidence is needed.");
            return 1;
        }

        await output.WriteLineAsync(result.Message);
        return 0;
    }

    public static IReadOnlyList<ReleaseImpactChangedFile>? ReadChangedFiles(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var files = new List<ReleaseImpactChangedFile>();
        foreach (JsonElement file in metadata.EnumerateArray())
        {
            if (!file.TryGetProperty("filename", out JsonElement name) ||
                name.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(name.GetString()) ||
                !file.TryGetProperty("status", out JsonElement status) ||
                status.ValueKind != JsonValueKind.String ||
                !FileChangeKinds.TryGetValue(status.GetString() ?? string.Empty, out ReleaseImpactFileChangeKind kind))
            {
                return null;
            }

            string? previous = file.TryGetProperty("previous_filename", out JsonElement previousName)
                ? previousName.GetString()
                : null;
            if (kind == ReleaseImpactFileChangeKind.Renamed && string.IsNullOrWhiteSpace(previous))
            {
                return null;
            }

            files.Add(new ReleaseImpactChangedFile(name.GetString()!, kind, previous));
        }

        return files;
    }

    public static HashSet<ReleaseImpactCategory> ClassifyRequiredCategories(IEnumerable<string> changedFiles)
    {
        var categories = new HashSet<ReleaseImpactCategory>();
        foreach (string file in changedFiles)
        {
            string path = file.Replace('\\', '/').ToLowerInvariant();
            foreach ((ReleaseImpactCategory category, ReleaseImpactRule rule) in Rules)
            {
                if (rule.Fragments.Any(fragment => path.Contains(fragment, StringComparison.Ordinal)) ||
                    rule.Suffixes.Any(suffix => path.EndsWith(suffix, StringComparison.Ordinal)))
                {
                    categories.Add(category);
                }
            }
        }

        return categories;
    }

    private static async Task<IReadOnlyList<ReleaseImpactChangedFile>?> GetChangedFilesAsync(
        string? metadataPath,
        Uri? filesUri,
        string? githubToken)
    {
        if (!string.IsNullOrWhiteSpace(metadataPath))
        {
            if (new FileInfo(metadataPath).Length > MaximumMetadataBytes)
            {
                return null;
            }

            await using FileStream stream = File.OpenRead(metadataPath);
            using JsonDocument document = await JsonDocument.ParseAsync(stream);
            return ReadChangedFiles(document.RootElement);
        }

        if (string.IsNullOrWhiteSpace(githubToken) || filesUri is null)
        {
            return null;
        }

        using var client = new HttpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("islamu-release-impact-validator");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var files = new List<ReleaseImpactChangedFile>();
        int page = 1;
        while (page <= 30)
        {
            Uri pageUri = new UriBuilder(filesUri) { Query = $"per_page=100&page={page}" }.Uri;
            using HttpResponseMessage response = await client.GetAsync(pageUri, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
            JsonElement pageFiles = document.RootElement;
            if (pageFiles.GetArrayLength() == 0)
            {
                break;
            }

            IReadOnlyList<ReleaseImpactChangedFile>? parsedFiles = ReadChangedFiles(pageFiles);
            if (parsedFiles is null)
            {
                return null;
            }

            files.AddRange(parsedFiles);
            if (pageFiles.GetArrayLength() < 100)
            {
                break;
            }

            page++;
        }

        return files;
    }

    private static ParsedPullRequestEvent ParsePullRequestEvent(JsonElement root)
    {
        JsonElement pullRequest = root.GetProperty("pull_request");
        string body = pullRequest.TryGetProperty("body", out JsonElement bodyElement)
            ? bodyElement.GetString() ?? string.Empty
            : string.Empty;
        JsonElement user = pullRequest.GetProperty("user");
        string author = user.GetProperty("login").GetString() ?? string.Empty;
        bool isBot = user.TryGetProperty("type", out JsonElement accountType) &&
            string.Equals(accountType.GetString(), "Bot", StringComparison.Ordinal);
        int expectedFileCount = pullRequest.TryGetProperty("changed_files", out JsonElement countElement)
            ? countElement.GetInt32()
            : 0;
        Uri? filesUri = pullRequest.TryGetProperty("url", out JsonElement urlElement) &&
            Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out Uri? pullRequestUri) &&
            string.Equals(pullRequestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pullRequestUri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
                ? new Uri(pullRequestUri.AbsoluteUri + "/files")
                : null;
        return new ParsedPullRequestEvent(author, isBot, body, expectedFileCount, filesUri);
    }

    private static bool IsReviewedImagePath(string path) =>
        !path.Contains('\\', StringComparison.Ordinal) &&
        !path.Split('/').Any(segment => segment is "." or "..") &&
        (path.StartsWith("assets/", StringComparison.Ordinal) ||
         path.StartsWith("docs/internal/assets/diagrams/", StringComparison.Ordinal)) &&
        (path.EndsWith(".png", StringComparison.Ordinal) || path.EndsWith(".svg", StringComparison.Ordinal));

    private static string ExtractReleaseImpactSection(string body)
    {
        Match match = ReleaseImpactSectionPattern.Match(body);
        return match.Success ? match.Groups["content"].Value : string.Empty;
    }

    private static bool HasCheckedLine(string section, string label) =>
        !string.IsNullOrWhiteSpace(section) &&
        Regex.IsMatch(section, @"(?im)^\s*-\s*\[[xX]\]\s*" + Regex.Escape(label), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static string ExtractDetails(string section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return string.Empty;
        }

        Match match = DetailsPattern.Match(section);
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

    private sealed record ParsedPullRequestEvent(
        string Author,
        bool IsBot,
        string Body,
        int ExpectedFileCount,
        Uri? FilesUri);
}
