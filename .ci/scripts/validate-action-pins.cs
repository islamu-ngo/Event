#:property RestorePackagesWithLockFile=false

using System.Text.RegularExpressions;

var workflowRoot = args.Length > 0 ? args[0] : ".github/workflows";

if (!Directory.Exists(workflowRoot))
{
    Console.WriteLine($"{workflowRoot}: workflow directory does not exist");
    return 1;
}

var failures = new List<string>();
foreach (var workflowFile in EnumerateWorkflowFiles(workflowRoot))
{
    failures.AddRange(ValidateFile(workflowFile));
}

if (failures.Count > 0)
{
    Console.WriteLine("GitHub Actions pin validation failed:");
    foreach (var failure in failures)
    {
        Console.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("All external GitHub Actions references are pinned to full SHAs with version comments.");
return 0;

static IEnumerable<string> EnumerateWorkflowFiles(string root)
{
    return Directory.EnumerateFiles(root, "*.yml")
        .Concat(Directory.EnumerateFiles(root, "*.yaml"))
        .Order(StringComparer.Ordinal);
}

static IEnumerable<string> ValidateFile(string path)
{
    var failures = new List<string>();
    var usesPattern = new Regex(@"^\s*uses:\s*(?<value>[^\s#]+)(?<suffix>.*)$", RegexOptions.Compiled);
    var fullShaPattern = new Regex(@"^[0-9a-fA-F]{40}$", RegexOptions.Compiled);
    var versionCommentPattern = new Regex(@"#\s*v\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    var lineNumber = 0;
    foreach (var line in File.ReadLines(path))
    {
        lineNumber++;
        var match = usesPattern.Match(line);
        if (!match.Success)
        {
            continue;
        }

        var usesValue = NormalizeUsesValue(match.Groups["value"].Value);
        var suffix = match.Groups["suffix"].Value;

        if (usesValue.StartsWith("./", StringComparison.Ordinal))
        {
            continue;
        }

        if (!usesValue.Contains('@', StringComparison.Ordinal))
        {
            failures.Add($"{path}:{lineNumber}: external action '{usesValue}' is missing an @ref");
            continue;
        }

        var actionRef = usesValue[(usesValue.LastIndexOf('@') + 1)..];
        if (!fullShaPattern.IsMatch(actionRef))
        {
            failures.Add($"{path}:{lineNumber}: external action '{usesValue}' is not pinned to a full 40-character SHA");
            continue;
        }

        if (!versionCommentPattern.IsMatch(suffix))
        {
            failures.Add($"{path}:{lineNumber}: external action '{usesValue}' is missing a same-line version comment such as '# v4'");
        }
    }

    return failures;
}

static string NormalizeUsesValue(string rawValue)
{
    return rawValue.Trim().Trim('\'', '"');
}
