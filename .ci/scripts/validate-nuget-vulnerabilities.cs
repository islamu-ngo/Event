#:property RestorePackagesWithLockFile=false

using System.Text.Json;
using System.Text;
using System.Globalization;

var reportPath = args.Length > 0 ? args[0] : "nuget-vulnerabilities.json";
if (!File.Exists(reportPath))
{
    Console.WriteLine($"{reportPath}: NuGet vulnerability report does not exist");
    return 1;
}

using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
var findings = new List<Finding>();
var suppressedFindings = new List<Finding>();
Visit(report.RootElement, project: string.Empty, framework: string.Empty, relationship: "Unknown", findings, suppressedFindings);
WriteSummary(reportPath, findings, suppressedFindings);

if (findings.Count > 0)
{
    Console.WriteLine("NuGet vulnerabilities detected:");
    WriteConsoleBreakdown(findings);
    foreach (var finding in findings)
    {
        var location = string.Join(" / ", new[] { finding.Project, finding.Framework }.Where(static part => !string.IsNullOrWhiteSpace(part)));
        Console.WriteLine($"- {finding.PackageId} [{finding.Relationship}] ({(string.IsNullOrWhiteSpace(location) ? "unknown project" : location)})");
        foreach (var vulnerability in finding.Vulnerabilities)
        {
            Console.WriteLine($"  - {vulnerability.Severity}: {vulnerability.AdvisoryUrl}");
        }
    }

    return 1;
}

Console.WriteLine($"No unapproved NuGet vulnerabilities detected. Approved suppressed package graphs: {suppressedFindings.Count}.");
return 0;

static void Visit(
    JsonElement node,
    string project,
    string framework,
    string relationship,
    List<Finding> findings,
    List<Finding> suppressedFindings)
{
    switch (node.ValueKind)
    {
        case JsonValueKind.Object:
            var currentProject = GetStringProperty(node, "path") ?? project;
            var currentFramework = GetStringProperty(node, "framework") ?? framework;
            var packageId = GetStringProperty(node, "id") ?? "<unknown>";
            var resolvedVersion = GetStringProperty(node, "resolvedVersion") ?? "<unknown>";

            if (node.TryGetProperty("vulnerabilities", out var vulnerabilitiesElement)
                && vulnerabilitiesElement.ValueKind == JsonValueKind.Array
                && vulnerabilitiesElement.GetArrayLength() > 0)
            {
                var vulnerabilities = vulnerabilitiesElement.EnumerateArray().Select(ReadVulnerability).ToList();
                AddFinding(findings, currentProject, currentFramework, packageId, resolvedVersion, relationship,
                    vulnerabilities.Where(vulnerability => !IsApprovedSuppression(packageId, resolvedVersion, vulnerability.AdvisoryUrl)).ToList());
                AddFinding(suppressedFindings, currentProject, currentFramework, packageId, resolvedVersion, relationship,
                    vulnerabilities.Where(vulnerability => IsApprovedSuppression(packageId, resolvedVersion, vulnerability.AdvisoryUrl)).ToList());
            }

            foreach (var property in node.EnumerateObject())
            {
                var childRelationship = property.Name switch
                {
                    "topLevelPackages" => "Direct",
                    "transitivePackages" => "Transitive",
                    _ => relationship
                };

                Visit(property.Value, currentProject, currentFramework, childRelationship, findings, suppressedFindings);
            }

            break;

        case JsonValueKind.Array:
            foreach (var item in node.EnumerateArray())
            {
                Visit(item, project, framework, relationship, findings, suppressedFindings);
            }

            break;
    }
}

static void AddFinding(
    ICollection<Finding> findings,
    string project,
    string framework,
    string packageId,
    string resolvedVersion,
    string relationship,
    IReadOnlyList<Vulnerability> vulnerabilities)
{
    if (vulnerabilities.Count > 0)
    {
        findings.Add(new Finding(project, framework, packageId, resolvedVersion, relationship, vulnerabilities));
    }
}

static bool IsApprovedSuppression(string packageId, string resolvedVersion, string advisoryUrl)
{
    return packageId.Equals("AutoMapper", StringComparison.OrdinalIgnoreCase)
        && resolvedVersion.Equals("14.0.0", StringComparison.OrdinalIgnoreCase)
        && advisoryUrl.Equals("https://github.com/advisories/GHSA-rvv3-g6hj-g44x", StringComparison.OrdinalIgnoreCase);
}

static Vulnerability ReadVulnerability(JsonElement vulnerability)
{
    var severity = GetStringProperty(vulnerability, "severity") ?? "unknown";
    var advisoryUrl = GetStringProperty(vulnerability, "advisoryUrl")
        ?? GetStringProperty(vulnerability, "advisoryurl")
        ?? "no advisory URL";

    return new Vulnerability(severity, advisoryUrl);
}

static void WriteConsoleBreakdown(IReadOnlyCollection<Finding> findings)
{
    Console.WriteLine("Relationship summary:");
    foreach (var group in findings.GroupBy(static finding => finding.Relationship).OrderBy(static group => group.Key))
    {
        Console.WriteLine($"- {group.Key}: {group.Count()} package(s)");
    }

    Console.WriteLine("Severity summary:");
    foreach (var group in findings.SelectMany(static finding => finding.Vulnerabilities).GroupBy(static vulnerability => vulnerability.Severity).OrderBy(static group => group.Key))
    {
        Console.WriteLine($"- {group.Key}: {group.Count()} advisory/advisories");
    }
}

static void WriteSummary(string reportPath, IReadOnlyList<Finding> findings, IReadOnlyList<Finding> suppressedFindings)
{
    var summaryPath = Environment.GetEnvironmentVariable("NUGET_VULNERABILITY_SUMMARY_PATH");
    if (string.IsNullOrWhiteSpace(summaryPath))
    {
        summaryPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? Directory.GetCurrentDirectory(), "nuget-vulnerability-summary.md");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(summaryPath)) ?? Directory.GetCurrentDirectory());

    var builder = new StringBuilder();
    builder.AppendLine("## NuGet Vulnerability Audit");
    builder.AppendLine();
    builder.Append("Actionable vulnerable package graph count: ");
    builder.AppendLine(findings.Count.ToString(CultureInfo.InvariantCulture));
    builder.Append("Approved suppressed package graph count: ");
    builder.AppendLine(suppressedFindings.Count.ToString(CultureInfo.InvariantCulture));
    builder.AppendLine();

    if (findings.Count == 0)
    {
        builder.AppendLine("No unapproved vulnerable direct or transitive packages were reported by `dotnet list package --vulnerable`.");
    }

    else
    {
        builder.AppendLine("### Relationship Summary");
        builder.AppendLine();
        foreach (var group in findings.GroupBy(static finding => finding.Relationship).OrderBy(static group => group.Key))
        {
            builder.Append("- ");
            builder.Append(group.Key);
            builder.Append(": ");
            builder.Append(group.Count().ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(" package(s)");
        }

        builder.AppendLine();
        builder.AppendLine("### Severity Summary");
        builder.AppendLine();
        foreach (var group in findings.SelectMany(static finding => finding.Vulnerabilities).GroupBy(static vulnerability => vulnerability.Severity).OrderBy(static group => group.Key))
        {
            builder.Append("- ");
            builder.Append(group.Key);
            builder.Append(": ");
            builder.Append(group.Count().ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(" advisory/advisories");
        }

        builder.AppendLine();
        builder.AppendLine("### Findings");
        builder.AppendLine();
        foreach (var finding in findings.OrderBy(static finding => finding.Relationship).ThenBy(static finding => finding.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var location = string.Join(" / ", new[] { finding.Project, finding.Framework }.Where(static part => !string.IsNullOrWhiteSpace(part)));
            builder.Append("- `");
            builder.Append(finding.PackageId);
            builder.Append("` (");
            builder.Append(finding.Relationship);
            builder.Append("; ");
            builder.Append(string.IsNullOrWhiteSpace(location) ? "unknown project" : location);
            builder.AppendLine(")");
            foreach (var vulnerability in finding.Vulnerabilities)
            {
                builder.Append("  - ");
                builder.Append(vulnerability.Severity);
                builder.Append(": ");
                builder.AppendLine(vulnerability.AdvisoryUrl);
            }
        }
    }

    if (suppressedFindings.Count > 0)
    {
        builder.AppendLine();
        builder.AppendLine("### Approved Suppressions");
        builder.AppendLine();
        builder.AppendLine("- `AutoMapper` 14.0.0 / `GHSA-rvv3-g6hj-g44x`: global `MaxDepth(64)` mitigation; see `docs/CI_CD_GOVERNANCE.md`.");
    }

    File.WriteAllText(summaryPath, builder.ToString());

    var stepSummaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
    if (!string.IsNullOrWhiteSpace(stepSummaryPath))
    {
        File.AppendAllText(stepSummaryPath, builder.ToString());
    }
}

static string? GetStringProperty(JsonElement element, string propertyName)
{
    return element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

internal sealed record Finding(string Project, string Framework, string PackageId, string ResolvedVersion, string Relationship, IReadOnlyList<Vulnerability> Vulnerabilities);

internal sealed record Vulnerability(string Severity, string AdvisoryUrl);
