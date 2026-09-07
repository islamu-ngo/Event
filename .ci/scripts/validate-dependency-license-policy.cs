#:property RestorePackagesWithLockFile=false

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var failures = new List<string>();
var followUps = new List<string>();

var packages = CollectNuGetPackages(root);
ValidateNuGetLicenses(packages, failures, followUps);
ValidateProductNpmManifests(root, failures);
ValidateContainerPackageManagers(root, failures);

if (failures.Count > 0)
{
    Console.WriteLine("Dependency license policy validation failed:");
    foreach (var failure in failures.Order(StringComparer.Ordinal))
    {
        Console.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine($"Dependency license policy passed for {packages.Count} unique NuGet package/version pairs.");
if (followUps.Count > 0)
{
    Console.WriteLine("Temporary or metadata-based license exceptions that remain visible for maintainer review:");
    foreach (var followUp in followUps.Order(StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine($"- {followUp}");
    }
}

WriteStepSummary(packages.Count, followUps);
return 0;

static HashSet<NuGetPackage> CollectNuGetPackages(string root)
{
    var packages = new HashSet<NuGetPackage>();
    foreach (var lockFile in Directory.EnumerateFiles(root, "packages.lock.json", SearchOption.AllDirectories)
                 .Where(path => IsProductDependencyFile(root, path))
                 .Order(StringComparer.Ordinal))
    {
        var node = JsonNode.Parse(File.ReadAllText(lockFile))?.AsObject();
        if (node?["dependencies"] is not JsonObject targetFrameworks)
        {
            continue;
        }

        foreach (var targetFramework in targetFrameworks)
        {
            if (targetFramework.Value is not JsonObject dependencyMap)
            {
                continue;
            }

            foreach (var dependency in dependencyMap)
            {
                if (dependency.Value is not JsonObject dependencyDetails)
                {
                    continue;
                }

                var version = dependencyDetails["resolved"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                packages.Add(new NuGetPackage(dependency.Key, version));
            }
        }
    }

    return packages;
}

static void ValidateNuGetLicenses(IReadOnlyCollection<NuGetPackage> packages, List<string> failures, List<string> followUps)
{
    foreach (var package in packages.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Version, StringComparer.OrdinalIgnoreCase))
    {
        if (LicensePolicy.ApprovedOverrides.TryGetValue(package.Id, out var packageOverride)
            && (packageOverride.Version is null || packageOverride.Version.Equals(package.Version, StringComparison.OrdinalIgnoreCase)))
        {
            if (packageOverride.RequiresFollowUp)
            {
                followUps.Add($"{package.Id} {package.Version}: {packageOverride.Rationale}");
            }

            continue;
        }

        var nuspecPath = FindNuspec(package.Id, package.Version);
        if (nuspecPath is null)
        {
            failures.Add($"{package.Id} {package.Version}: NuGet metadata was not found in the local package cache after restore");
            continue;
        }

        var license = ReadLicenseMetadata(nuspecPath);
        if (license.Expression is { Length: > 0 } expression)
        {
            ValidateLicenseExpression(package, expression, failures);
            continue;
        }

        if (license.Url is { Length: > 0 } url && TryMapLicenseUrl(url, out var mappedExpression))
        {
            ValidateLicenseExpression(package, mappedExpression, failures);
            continue;
        }

        failures.Add($"{package.Id} {package.Version}: license metadata is '{license.DisplayValue}', which requires an explicit package override in validate-dependency-license-policy.cs");
    }
}

static void ValidateLicenseExpression(NuGetPackage package, string expression, List<string> failures)
{
    var licenseIds = ExtractLicenseIds(expression).ToArray();
    if (licenseIds.Length == 0)
    {
        failures.Add($"{package.Id} {package.Version}: license expression '{expression}' contains no recognizable SPDX license identifiers");
        return;
    }

    var denied = licenseIds.Where(id => LicensePolicy.DeniedLicenseIds.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    if (denied.Length > 0)
    {
        failures.Add($"{package.Id} {package.Version}: denied license expression '{expression}' includes {string.Join(", ", denied)}");
        return;
    }

    var unknown = licenseIds.Where(id => !LicensePolicy.AllowedLicenseIds.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    if (unknown.Length > 0)
    {
        failures.Add($"{package.Id} {package.Version}: license expression '{expression}' includes unreviewed license identifiers {string.Join(", ", unknown)}");
    }
}

static IEnumerable<string> ExtractLicenseIds(string expression)
{
    foreach (Match match in Regex.Matches(expression, "[A-Za-z0-9][A-Za-z0-9.+-]*"))
    {
        var token = match.Value;
        if (token.Equals("AND", StringComparison.OrdinalIgnoreCase)
            || token.Equals("OR", StringComparison.OrdinalIgnoreCase)
            || token.Equals("WITH", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        yield return token;
    }
}

static LicenseMetadata ReadLicenseMetadata(string nuspecPath)
{
    var document = XDocument.Load(nuspecPath);
    var metadata = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "metadata");
    var license = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName == "license");
    var licenseUrl = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName == "licenseUrl")?.Value.Trim();
    var type = license?.Attribute("type")?.Value.Trim();
    var value = license?.Value.Trim();

    if (type?.Equals("expression", StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(value))
    {
        return new LicenseMetadata(value, licenseUrl, value);
    }

    if (!string.IsNullOrWhiteSpace(licenseUrl))
    {
        return new LicenseMetadata(null, licenseUrl, licenseUrl);
    }

    return new LicenseMetadata(null, null, string.IsNullOrWhiteSpace(value) ? "missing" : $"{type}:{value}");
}

static string? FindNuspec(string packageId, string version)
{
    var packageRoots = new List<string>();
    var configuredRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
    if (!string.IsNullOrWhiteSpace(configuredRoot))
    {
        packageRoots.Add(configuredRoot);
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (!string.IsNullOrWhiteSpace(home))
    {
        packageRoots.Add(Path.Combine(home, ".nuget", "packages"));
    }

    foreach (var packageRoot in packageRoots.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var packageDirectory = Path.Combine(packageRoot, packageId.ToLowerInvariant(), version.ToLowerInvariant());
        if (!Directory.Exists(packageDirectory))
        {
            continue;
        }

        var exact = Path.Combine(packageDirectory, $"{packageId.ToLowerInvariant()}.nuspec");
        if (File.Exists(exact))
        {
            return exact;
        }

        var fallback = Directory.EnumerateFiles(packageDirectory, "*.nuspec").FirstOrDefault();
        if (fallback is not null)
        {
            return fallback;
        }
    }

    return null;
}

static bool TryMapLicenseUrl(string url, out string expression)
{
    var normalized = url.Trim().TrimEnd('/');
    if (normalized.Equals("https://licenses.nuget.org/MIT", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("http://opensource.org/licenses/mit-license.php", StringComparison.OrdinalIgnoreCase))
    {
        expression = "MIT";
        return true;
    }

    if (normalized.Equals("https://licenses.nuget.org/Apache-2.0", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("http://www.apache.org/licenses/LICENSE-2.0", StringComparison.OrdinalIgnoreCase))
    {
        expression = "Apache-2.0";
        return true;
    }

    expression = string.Empty;
    return false;
}

static void ValidateProductNpmManifests(string root, List<string> failures)
{
    var productLocks = Directory.EnumerateFiles(root, "package-lock.json", SearchOption.AllDirectories)
        .Where(path => IsProductDependencyFile(root, path))
        .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();

    if (productLocks.Length > 0)
    {
        failures.Add($"product npm lock files need a license scanner before they can be release dependencies: {string.Join(", ", productLocks)}");
    }
}

static void ValidateContainerPackageManagers(string root, List<string> failures)
{
    var packageManagerPatterns = new[]
    {
        "apt-get install",
        "apt install",
        "apk add",
        "dnf install",
        "microdnf install",
        "yum install",
    };

    foreach (var dockerfile in Directory.EnumerateFiles(root, "Dockerfile", SearchOption.AllDirectories)
                 .Where(path => IsProductDependencyFile(root, path))
                 .Order(StringComparer.Ordinal))
    {
        var relativePath = Path.GetRelativePath(root, dockerfile).Replace('\\', '/');
        var lines = File.ReadAllLines(dockerfile);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('#'))
            {
                continue;
            }

            if (packageManagerPatterns.Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"{relativePath}:{index + 1}: container OS package installs require a documented license scanning path before release use");
            }
        }
    }
}

static bool IsProductDependencyFile(string root, string path)
{
    var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
    var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var excludedSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".tmp",
        ".claude",
        ".opencode",
        "bin",
        "obj",
        "node_modules",
    };

    if (segments.Any(segment => excludedSegments.Contains(segment)))
    {
        return false;
    }

    if (relative.Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return !relative.StartsWith(".ci/scripts/", StringComparison.OrdinalIgnoreCase);
}

static void WriteStepSummary(int packageCount, IReadOnlyCollection<string> followUps)
{
    var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
    if (string.IsNullOrWhiteSpace(summaryPath))
    {
        return;
    }

    using var writer = File.AppendText(summaryPath);
    writer.WriteLine("## Dependency License Policy");
    writer.WriteLine();
    writer.WriteLine($"- Unique NuGet package/version pairs checked: `{packageCount}`");
    writer.WriteLine($"- Temporary or metadata-based exceptions requiring visibility: `{followUps.Count}`");
    writer.WriteLine("- Product npm lock files outside excluded tooling directories: `0`");
    writer.WriteLine("- Container OS package-manager installs without license scan: `0`");
}

static class LicensePolicy
{
    internal static readonly HashSet<string> AllowedLicenseIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Apache-2.0",
        "BSD-2-Clause",
        "BSD-3-Clause",
        "CC0-1.0",
        "ISC",
        "MIT",
        "MPL-2.0",
        "PostgreSQL",
        "Unicode-DFS-2016",
        "Unlicense",
        "Zlib",
    };

    internal static readonly HashSet<string> DeniedLicenseIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "AGPL-1.0",
        "AGPL-1.0-only",
        "AGPL-1.0-or-later",
        "AGPL-3.0",
        "AGPL-3.0-only",
        "AGPL-3.0-or-later",
        "BUSL-1.1",
        "Commons-Clause",
        "GPL-2.0",
        "GPL-2.0-only",
        "GPL-2.0-or-later",
        "GPL-3.0",
        "GPL-3.0-only",
        "GPL-3.0-or-later",
        "LGPL-2.1",
        "LGPL-2.1-only",
        "LGPL-2.1-or-later",
        "LGPL-3.0",
        "LGPL-3.0-only",
        "LGPL-3.0-or-later",
        "RPL-1.5",
        "SSPL-1.0",
    };

    internal static readonly Dictionary<string, LicenseOverride> ApprovedOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AutoMapper"] = new("RPL-1.5", "temporary exception for existing runtime mapper dependency; replace or obtain legal approval before alternative-license distribution", true),
        ["MediatR"] = new("RPL-1.5", "temporary exception for existing runtime mediator dependency; replace or obtain legal approval before alternative-license distribution", true),
        ["SonarAnalyzer.CSharp"] = new("SONAR-Source-Available-1.0", "build analyzer exception; not shipped with runtime artifacts", true),
        ["Microsoft.VisualStudio.Azure.Containers.Tools.Targets"] = new("Microsoft-EULA", "build tooling exception; not shipped with runtime artifacts", true),
        ["NetArchTest.Rules"] = new("metadata-missing", "architecture-test dependency has no NuGet license metadata; keep visible until package metadata or replacement is chosen", true),
        ["Blazouter"] = new("MIT", "package uses license file metadata", false),
        ["Blazouter.Server"] = new("MIT", "package uses license file metadata", false),
        ["Blazouter.WebAssembly"] = new("MIT", "package uses license file metadata", false),
        ["Bogus"] = new("MIT", "package uses license file metadata", false),
        ["Cerbos.Sdk"] = new("Apache-2.0", "package uses license file metadata", false),
        ["CommandLineParser"] = new("MIT", "package uses license file metadata", false),
        ["DotNetEnv"] = new("MIT", "package uses license file metadata", false),
        ["Fractions"] = new("BSD-3-Clause", "package uses license file metadata", false),
        ["Infisical.Sdk"] = new("MIT", "package uses license file metadata", false),
        ["Microsoft.DotNet.PlatformAbstractions"] = new("Microsoft-.NET-Library", "Microsoft .NET library package uses license file metadata", false),
        ["Microsoft.Testing.Extensions.CodeCoverage"] = new("Microsoft-.NET-Library", "Microsoft .NET testing package uses license file metadata", false),
        ["Microsoft.Data.SqlClient.SNI.runtime"] = new(
            "Microsoft-.NET-Library",
            "exact-version optional SQL Server native runtime under separate Microsoft terms; PostgreSQL-only published artifacts must exclude it",
            true,
            "6.0.2"),
        ["runtime.win-arm64.runtime.native.System.Data.SqlClient.sni"] = new("Microsoft-.NET-Library", "Microsoft native runtime package uses license URL metadata", false),
        ["runtime.win-x64.runtime.native.System.Data.SqlClient.sni"] = new("Microsoft-.NET-Library", "Microsoft native runtime package uses license URL metadata", false),
        ["runtime.win-x86.runtime.native.System.Data.SqlClient.sni"] = new("Microsoft-.NET-Library", "Microsoft native runtime package uses license URL metadata", false),
        ["Microsoft.DotNet.ILCompiler"] = new("MIT", "build-only Native AOT compiler tool; not shipped with runtime artifacts", false),
        ["Microsoft.NET.ILLink.Tasks"] = new("MIT", "build-only linker tool; not shipped with runtime artifacts", false),
        ["Portable.BouncyCastle"] = new("MIT", "package uses legacy license URL metadata", false),
        ["runtime.linux-x64.Microsoft.DotNet.ILCompiler"] = new("MIT", "build-only Native AOT compiler tool runtime; not shipped with runtime artifacts", false),
        ["WebPush"] = new("MPL-2.0", "package uses legacy license URL metadata", false),
        ["Stripe.net"] = new("Apache-2.0", "package uses legacy license URL metadata", false),
    };
}

readonly record struct NuGetPackage(string Id, string Version);
readonly record struct LicenseMetadata(string? Expression, string? Url, string DisplayValue);
readonly record struct LicenseOverride(string LicenseId, string Rationale, bool RequiresFollowUp, string? Version = null);
