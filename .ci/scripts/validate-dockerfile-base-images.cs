#:property RestorePackagesWithLockFile=false

using System.Text.RegularExpressions;

var dockerfiles = args.Length > 0
    ? args
    : Directory.EnumerateFiles(".", "Dockerfile", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray();

var failures = new List<string>();
foreach (var dockerfile in dockerfiles)
{
    if (!File.Exists(dockerfile))
    {
        failures.Add($"{dockerfile}: file does not exist");
        continue;
    }

    failures.AddRange(ValidateDockerfile(dockerfile));
}

if (failures.Count > 0)
{
    Console.WriteLine("Dockerfile base image validation failed:");
    foreach (var failure in failures)
    {
        Console.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("Dockerfile base images are pinned to explicit tag-plus-digest references.");
return 0;

static IEnumerable<string> ValidateDockerfile(string path)
{
    var failures = new List<string>();
    var fromPattern = new Regex(@"^\s*FROM\s+(?<source>\S+)(?:\s+AS\s+(?<alias>\S+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    var digestPattern = new Regex(@"@sha256:[0-9a-fA-F]{64}$", RegexOptions.Compiled);
    var knownStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var lineNumber = 0;
    foreach (var line in File.ReadLines(path))
    {
        lineNumber++;
        var match = fromPattern.Match(line);
        if (!match.Success)
        {
            continue;
        }

        var source = match.Groups["source"].Value.Trim();
        var alias = match.Groups["alias"].Value.Trim();

        if (knownStages.Contains(source))
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                knownStages.Add(alias);
            }

            continue;
        }

        if (!digestPattern.IsMatch(source))
        {
            failures.Add($"{path}:{lineNumber}: external base image '{source}' must include a sha256 digest pin");
        }

        var imageWithoutDigest = source.Split('@')[0];
        if (!HasExplicitTag(imageWithoutDigest))
        {
            failures.Add($"{path}:{lineNumber}: external base image '{source}' must keep a human-readable tag before the digest");
        }

        if (!string.IsNullOrWhiteSpace(alias))
        {
            knownStages.Add(alias);
        }
    }

    return failures;
}

static bool HasExplicitTag(string imageReference)
{
    var lastSlash = imageReference.LastIndexOf('/');
    var lastColon = imageReference.LastIndexOf(':');
    return lastColon > lastSlash;
}
