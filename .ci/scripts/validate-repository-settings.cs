#:property RestorePackagesWithLockFile=false
#pragma warning disable CA1050 // File-based CI scripts intentionally keep helper types in one file.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
if (string.IsNullOrWhiteSpace(repository))
{
    repository = args.Length > 0 ? args[0] : string.Empty;
}

var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
var outputDirectory = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("REPOSITORY_SETTINGS_EVIDENCE_DIR") ?? "artifacts/repository-settings";

if (string.IsNullOrWhiteSpace(repository) || !repository.Contains('/', StringComparison.Ordinal))
{
    Console.Error.WriteLine("Repository must be supplied through GITHUB_REPOSITORY or as the first argument, for example islamu-ngo/Event.");
    return 1;
}

if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("GITHUB_TOKEN or GH_TOKEN is required to read repository settings.");
    return 1;
}

Directory.CreateDirectory(outputDirectory);

using var http = new HttpClient
{
    BaseAddress = new Uri("https://api.github.com/", UriKind.Absolute)
};
http.DefaultRequestHeaders.UserAgent.ParseAdd("islamu-event-ci-settings-validator");
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

var findings = new List<Finding>();
var evidence = new JsonObject
{
    ["repository"] = repository,
    ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    ["checks"] = new JsonObject()
};

var checks = evidence["checks"]!.AsObject();

await CheckRepositorySecurityAsync(repository, checks, findings);
await CheckBranchProtectionAsync(repository, "main", checks, findings);
await CheckBranchProtectionAsync(repository, "develop", checks, findings);
await CheckRulesetsAsync(repository, checks, findings);
await CheckEnvironmentsAsync(repository, checks, findings);
await CheckActionsPolicyAsync(repository, checks, findings);
await CheckCodeOwnersAsync(repository, checks, findings);

var evidencePath = Path.Combine(outputDirectory, "repository-settings-evidence.json");
var summaryPath = Path.Combine(outputDirectory, "repository-settings-summary.md");
await File.WriteAllTextAsync(evidencePath, evidence.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
await File.WriteAllTextAsync(summaryPath, BuildSummary(repository, findings), Encoding.UTF8);

var stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (!string.IsNullOrWhiteSpace(stepSummary))
{
    await File.AppendAllTextAsync(stepSummary, await File.ReadAllTextAsync(summaryPath, Encoding.UTF8), Encoding.UTF8);
}

if (findings.Count == 0)
{
    Console.WriteLine("Repository settings match the enterprise CI/CD hardening policy.");
    return 0;
}

Console.Error.WriteLine("Repository settings drift detected:");
foreach (var finding in findings)
{
    Console.Error.WriteLine($"- [{finding.Severity}] {finding.Control}: {finding.Message}");
}

Console.Error.WriteLine(FormattableString.Invariant($"Evidence written to {evidencePath} and {summaryPath}."));
return 1;

async Task CheckRepositorySecurityAsync(string repo, JsonObject checksObject, List<Finding> output)
{
    var response = await GetJsonAsync($"repos/{repo}");
    var check = new JsonObject
    {
        ["status"] = StatusText(response.StatusCode),
        ["defaultBranch"] = response.Json?["default_branch"]?.GetValue<string>(),
        ["visibility"] = response.Json?["visibility"]?.GetValue<string>()
    };

    var security = response.Json?["security_and_analysis"]?.AsObject();
    if (security is not null)
    {
        check["securityAndAnalysis"] = security.DeepClone();
        RequireStatus(security, "secret_scanning", "enabled", "Secret scanning", output);
        RequireStatus(security, "secret_scanning_push_protection", "enabled", "Secret scanning push protection", output);
        RequireStatus(security, "dependabot_security_updates", "enabled", "Dependabot security updates", output);
    }
    else
    {
        output.Add(new Finding("Repository security features", "error", "security_and_analysis metadata was not returned by the GitHub API."));
    }

    var dependencyAlerts = await GetRawAsync($"repos/{repo}/vulnerability-alerts");
    check["dependencyAlertsStatus"] = StatusText(dependencyAlerts.StatusCode);
    if (dependencyAlerts.StatusCode != HttpStatusCode.NoContent)
    {
        output.Add(new Finding("Dependency graph / vulnerability alerts", "error", $"Expected HTTP 204; got {(int)dependencyAlerts.StatusCode} {dependencyAlerts.StatusCode}."));
    }

    var automatedSecurityFixes = await GetJsonAsync($"repos/{repo}/automated-security-fixes");
    check["automatedSecurityFixes"] = automatedSecurityFixes.Json?.DeepClone() ?? new JsonObject { ["status"] = StatusText(automatedSecurityFixes.StatusCode) };
    if (automatedSecurityFixes.Json?["enabled"]?.GetValue<bool>() != true)
    {
        output.Add(new Finding("Dependabot security updates", "error", "Automated security fixes are disabled."));
    }

    var codeScanning = await GetJsonAsync($"repos/{repo}/code-scanning/alerts?per_page=1");
    check["codeScanningAlertsStatus"] = StatusText(codeScanning.StatusCode);
    check["codeScanningSampleCount"] = codeScanning.Json is JsonArray alerts ? alerts.Count : 0;
    if (codeScanning.StatusCode != HttpStatusCode.OK)
    {
        output.Add(new Finding("Code scanning alerts", "error", $"Code scanning alerts API returned {(int)codeScanning.StatusCode} {codeScanning.StatusCode}."));
    }

    checksObject["repositorySecurity"] = check;
}

async Task CheckBranchProtectionAsync(string repo, string branch, JsonObject checksObject, List<Finding> output)
{
    var response = await GetJsonAsync($"repos/{repo}/branches/{branch}/protection");
    var check = new JsonObject
    {
        ["status"] = StatusText(response.StatusCode)
    };

    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        output.Add(new Finding($"{branch} branch protection", "error", "Branch protection endpoint returned 404."));
        checksObject[$"branchProtection:{branch}"] = check;
        return;
    }

    if (response.StatusCode != HttpStatusCode.OK || response.Json is null)
    {
        output.Add(new Finding($"{branch} branch protection", "error", $"Branch protection API returned {(int)response.StatusCode} {response.StatusCode}."));
        checksObject[$"branchProtection:{branch}"] = check;
        return;
    }

    var requiredChecks = response.Json["required_status_checks"];
    var contexts = requiredChecks?["contexts"] as JsonArray;
    var checks = requiredChecks?["checks"] as JsonArray;
    var requiredCheckCount = (contexts?.Count ?? 0) + (checks?.Count ?? 0);
    check["requiredStatusCheckCount"] = requiredCheckCount;
    check["hasPullRequestReviews"] = response.Json["required_pull_request_reviews"] is not null;
    check["requiredLinearHistory"] = response.Json["required_linear_history"]?["enabled"]?.GetValue<bool>() ?? false;
    check["requiredConversationResolution"] = response.Json["required_conversation_resolution"]?["enabled"]?.GetValue<bool>() ?? false;

    if (requiredCheckCount == 0)
    {
        output.Add(new Finding($"{branch} required status checks", "error", "No required status checks are configured."));
    }

    if (response.Json["required_pull_request_reviews"] is null)
    {
        output.Add(new Finding($"{branch} pull request review policy", "error", "Required pull request reviews are not configured."));
    }

    checksObject[$"branchProtection:{branch}"] = check;
}

async Task CheckRulesetsAsync(string repo, JsonObject checksObject, List<Finding> output)
{
    var list = await GetJsonAsync($"repos/{repo}/rulesets");
    var rulesets = new JsonArray();
    var hasMain = false;
    var hasDevelop = false;
    var hasMergeQueue = false;
    var hasReservedVersionTagGlob = false;

    if (list.Json is JsonArray array)
    {
        foreach (var item in array.OfType<JsonObject>())
        {
            var id = item["id"]?.GetValue<long>();
            if (id is null)
            {
                continue;
            }

            var detail = await GetJsonAsync($"repos/{repo}/rulesets/{id.Value.ToString(CultureInfo.InvariantCulture)}");
            var detailObject = detail.Json?.AsObject();
            rulesets.Add(detailObject?.DeepClone() ?? item.DeepClone());

            var conditions = detailObject?["conditions"]?["ref_name"]?["include"] as JsonArray;
            var includeRefs = conditions?.Select(node => node?.GetValue<string>() ?? string.Empty).ToArray() ?? [];
            hasMain |= includeRefs.Contains("refs/heads/main", StringComparer.Ordinal);
            hasDevelop |= includeRefs.Contains("refs/heads/develop", StringComparer.Ordinal);

            if (detailObject?["rules"] is JsonArray ruleArray)
            {
                hasMergeQueue |= ruleArray.OfType<JsonObject>().Any(rule => string.Equals(rule["type"]?.GetValue<string>(), "merge_queue", StringComparison.Ordinal));

                // Version tags own the v* glob outright. A branch named v0.1 beside tag v0.1.0
                // would make a bare name resolvable to either object, so branch creation in that
                // namespace must be refused by the provider, not disambiguated afterwards.
                hasReservedVersionTagGlob |= includeRefs.Contains("refs/heads/v*", StringComparer.Ordinal) &&
                    ruleArray.OfType<JsonObject>().Any(rule => string.Equals(rule["type"]?.GetValue<string>(), "creation", StringComparison.Ordinal));
            }
        }
    }
    else
    {
        output.Add(new Finding("Repository rulesets", "error", $"Rulesets API returned {(int)list.StatusCode} {list.StatusCode}."));
    }

    checksObject["rulesets"] = new JsonObject
    {
        ["status"] = StatusText(list.StatusCode),
        ["hasMainRuleset"] = hasMain,
        ["hasDevelopRuleset"] = hasDevelop,
        ["hasMergeQueueRule"] = hasMergeQueue,
        ["hasReservedVersionTagGlobRule"] = hasReservedVersionTagGlob,
        ["rulesets"] = rulesets
    };

    if (!hasMain)
    {
        output.Add(new Finding("main ruleset", "error", "No branch ruleset includes refs/heads/main."));
    }

    if (!hasDevelop)
    {
        output.Add(new Finding("develop ruleset", "error", "No branch ruleset includes refs/heads/develop."));
    }

    if (!hasReservedVersionTagGlob)
    {
        output.Add(new Finding("reserved version-tag glob", "error", "No branch ruleset blocks creation under refs/heads/v*; version tags must own that glob."));
    }
}

async Task CheckEnvironmentsAsync(string repo, JsonObject checksObject, List<Finding> output)
{
    var response = await GetJsonAsync($"repos/{repo}/environments");
    var environments = response.Json?["environments"] as JsonArray;
    var names = environments?.OfType<JsonObject>().Select(env => env["name"]?.GetValue<string>() ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

    checksObject["environments"] = new JsonObject
    {
        ["status"] = StatusText(response.StatusCode),
        ["totalCount"] = response.Json?["total_count"]?.GetValue<int>() ?? 0,
        ["names"] = new JsonArray(names.Order(StringComparer.OrdinalIgnoreCase).Select(name => JsonValue.Create(name)).ToArray())
    };

    foreach (var expected in new[] { "staging", "production" })
    {
        if (!names.Contains(expected))
        {
            output.Add(new Finding($"{expected} environment", "error", $"GitHub Environment `{expected}` is missing."));
        }
    }
}

async Task CheckActionsPolicyAsync(string repo, JsonObject checksObject, List<Finding> output)
{
    var response = await GetJsonAsync($"repos/{repo}/actions/permissions");
    var allowedActions = response.Json?["allowed_actions"]?.GetValue<string>() ?? "unknown";
    checksObject["actionsPolicy"] = new JsonObject
    {
        ["status"] = StatusText(response.StatusCode),
        ["enabled"] = response.Json?["enabled"]?.GetValue<bool>() ?? false,
        ["allowedActions"] = allowedActions
    };

    if (!string.Equals(allowedActions, "selected", StringComparison.OrdinalIgnoreCase))
    {
        output.Add(new Finding("GitHub Actions policy", "warning", $"Repository allowed_actions is `{allowedActions}`; expected selected or organization-enforced restrictions for verified/SHA-pinned actions."));
    }
}

async Task CheckCodeOwnersAsync(string repo, JsonObject checksObject, List<Finding> output)
{
    var codeownersPath = Path.Combine(".github", "CODEOWNERS");
    var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (File.Exists(codeownersPath))
    {
        foreach (var line in await File.ReadAllLinesAsync(codeownersPath, Encoding.UTF8))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            foreach (var token in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
            {
                if (token.StartsWith('@'))
                {
                    owners.Add(token);
                }
            }
        }
    }

    var ownerEvidence = new JsonArray();
    var org = repo.Split('/')[0];
    foreach (var owner in owners.Order(StringComparer.OrdinalIgnoreCase))
    {
        if (!owner.StartsWith($"@{org}/", StringComparison.OrdinalIgnoreCase))
        {
            ownerEvidence.Add((JsonNode)new JsonObject { ["owner"] = owner, ["status"] = "not-checked" });
            continue;
        }

        var slug = owner.Split('/')[1];
        var response = await GetJsonAsync($"orgs/{org}/teams/{slug}");
        ownerEvidence.Add((JsonNode)new JsonObject
        {
            ["owner"] = owner,
            ["status"] = StatusText(response.StatusCode)
        });

        if (response.StatusCode != HttpStatusCode.OK)
        {
            output.Add(new Finding("CODEOWNERS owner resolution", "error", $"{owner} did not resolve through the GitHub Teams API."));
        }
    }

    checksObject["codeowners"] = new JsonObject
    {
        ["path"] = codeownersPath,
        ["owners"] = ownerEvidence
    };
}

async Task<ApiResponse> GetJsonAsync(string path)
{
    var response = await GetRawAsync(path);
    JsonNode? json = null;
    if (!string.IsNullOrWhiteSpace(response.Body))
    {
        try
        {
            json = JsonNode.Parse(response.Body);
        }
        catch (JsonException)
        {
            json = new JsonObject { ["raw"] = response.Body };
        }
    }

    return new ApiResponse(response.StatusCode, json);
}

async Task<RawResponse> GetRawAsync(string path)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    return new RawResponse(response.StatusCode, body);
}

static string BuildSummary(string repo, IReadOnlyList<Finding> findings)
{
    var builder = new StringBuilder();
    builder.AppendLine("## Repository Settings Drift");
    builder.AppendLine();
    builder.Append("- Repository: `").Append(repo).AppendLine("`");
    builder.Append("- Findings: `").Append(findings.Count.ToString(CultureInfo.InvariantCulture)).AppendLine("`");
    builder.AppendLine();

    if (findings.Count == 0)
    {
        builder.AppendLine("Repository settings match the enterprise CI/CD hardening policy.");
        builder.AppendLine();
        return builder.ToString();
    }

    builder.AppendLine("| Severity | Control | Finding |");
    builder.AppendLine("|---|---|---|");
    foreach (var finding in findings)
    {
        builder
            .Append("| ").Append(finding.Severity)
            .Append(" | ").Append(finding.Control.Replace("|", "\\|", StringComparison.Ordinal))
            .Append(" | ").Append(finding.Message.Replace("|", "\\|", StringComparison.Ordinal))
            .AppendLine(" |");
    }

    builder.AppendLine();
    return builder.ToString();
}

static void RequireStatus(JsonObject security, string key, string expected, string control, List<Finding> output)
{
    var actual = security[key]?["status"]?.GetValue<string>() ?? "missing";
    if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
    {
        output.Add(new Finding(control, "error", $"Expected `{expected}`, got `{actual}`."));
    }
}

static string StatusText(HttpStatusCode statusCode) => FormattableString.Invariant($"{(int)statusCode} {statusCode}");

sealed record Finding(string Control, string Severity, string Message);
sealed record ApiResponse(HttpStatusCode StatusCode, JsonNode? Json);
sealed record RawResponse(HttpStatusCode StatusCode, string Body);
