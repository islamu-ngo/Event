#:property RestorePackagesWithLockFile=false

var root = args.Length > 0 ? args[0] : ".github/workflows";
if (!Directory.Exists(root))
{
    Console.WriteLine($"{root}: workflow directory does not exist");
    return 1;
}

var workflowFiles = Directory.EnumerateFiles(root, "*.yml")
    .Concat(Directory.EnumerateFiles(root, "*.yaml"))
    .Order(StringComparer.Ordinal)
    .ToArray();

var failures = new List<string>();
var deployOrPublishNameFragments = new[]
{
    "deploy",
    "container",
    "release",
};

foreach (var file in workflowFiles)
{
    var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file).Replace('\\', '/');
    var fileName = Path.GetFileName(file);
    var isPrivilegedWorkflow = deployOrPublishNameFragments.Any(fragment => fileName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    var lines = File.ReadAllLines(file);

    for (var index = 0; index < lines.Length; index++)
    {
        var lineNumber = index + 1;
        var line = lines[index];
        var trimmed = line.Trim();

        if (trimmed.StartsWith("uses: actions/cache@", StringComparison.Ordinal))
        {
            failures.Add($"{relativePath}:{lineNumber}: actions/cache is not approved; use tool-specific caches only after threat-model review");
        }

        if (trimmed.StartsWith("cache-to:", StringComparison.Ordinal) && trimmed.Contains("type=gha", StringComparison.OrdinalIgnoreCase))
        {
            if (fileName != "_container-build.yml")
            {
                failures.Add($"{relativePath}:{lineNumber}: Docker GHA cache writes are only approved in _container-build.yml");
            }
        }

        if (trimmed == "cache: true" && IsInsideSetupDotnetStep(lines, index))
        {
            if (isPrivilegedWorkflow)
            {
                failures.Add($"{relativePath}:{lineNumber}: setup-dotnet cache is not approved in deploy, container, or release workflows");
            }
        }
    }
}

if (failures.Count > 0)
{
    Console.WriteLine("Workflow cache policy validation failed:");
    foreach (var failure in failures)
    {
        Console.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("Workflow cache policy keeps cache writes out of privileged deploy/publish paths.");
return 0;

static bool IsInsideSetupDotnetStep(string[] lines, int cacheLineIndex)
{
    var cacheIndent = CountIndent(lines[cacheLineIndex]);
    for (var index = cacheLineIndex - 1; index >= 0; index--)
    {
        var line = lines[index];
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var indent = CountIndent(line);
        if (indent < cacheIndent && line.Trim().StartsWith("uses:", StringComparison.Ordinal))
        {
            return line.Contains("actions/setup-dotnet@", StringComparison.Ordinal);
        }

        if (indent <= cacheIndent - 2 && line.TrimStart().StartsWith("- name:", StringComparison.Ordinal))
        {
            return false;
        }
    }

    return false;
}

static int CountIndent(string line)
{
    var count = 0;
    foreach (var character in line)
    {
        if (character != ' ')
        {
            break;
        }

        count++;
    }

    return count;
}
