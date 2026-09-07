#:property RestorePackagesWithLockFile=false

using System.Text.RegularExpressions;

var configPath = args.Length > 0 ? args[0] : ".github/dependabot.yml";
if (!File.Exists(configPath))
{
    Console.WriteLine($"{configPath}: missing Dependabot configuration");
    return 1;
}

var lines = File.ReadAllLines(configPath);
var githubActionsBlock = ExtractUpdateBlock(lines, "github-actions");
if (githubActionsBlock is null)
{
    Console.WriteLine($"{configPath}: missing github-actions Dependabot update block");
    return 1;
}

var failures = ValidateGithubActionsBlock(githubActionsBlock);
failures.AddRange(ValidateDockerBlocks(lines));
if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.WriteLine($"{configPath}: {failure}");
    }

    return 1;
}

Console.WriteLine("Dependabot update policy covers GitHub Actions SHA pins and Docker base image digests.");
return 0;

static string[]? ExtractUpdateBlock(string[] lines, string ecosystem)
{
    var startIndex = -1;
    var ecosystemPattern = new Regex($@"^\s*-\s+package-ecosystem:\s+{Regex.Escape(ecosystem)}\s*$", RegexOptions.Compiled);
    for (var index = 0; index < lines.Length; index++)
    {
        if (ecosystemPattern.IsMatch(lines[index]))
        {
            startIndex = index;
            break;
        }
    }

    if (startIndex < 0)
    {
        return null;
    }

    var endIndex = lines.Length;
    var nextBlockPattern = new Regex(@"^\s*-\s+package-ecosystem:\s+", RegexOptions.Compiled);
    for (var index = startIndex + 1; index < lines.Length; index++)
    {
        if (nextBlockPattern.IsMatch(lines[index]))
        {
            endIndex = index;
            break;
        }
    }

    return lines[startIndex..endIndex];
}

static List<string> ValidateGithubActionsBlock(string[] block)
{
    var blockText = string.Join('\n', block);
    var failures = new List<string>();
    var requiredPatterns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["directory must be repository root"] = @"^\s+directory:\s+[""']?/[""']?\s*$",
        ["schedule interval must be weekly"] = @"^\s+interval:\s+weekly\s*$",
        ["commit message prefix must be ci"] = @"^\s+prefix:\s+[""']?ci[""']?\s*$",
        ["commit message must include dependency scope"] = @"^\s+include:\s+scope\s*$",
        ["all GitHub Actions updates must be grouped"] = @"^\s+github-actions:\s*$",
        ["GitHub Actions group must match every action"] = @"^\s+-\s+[""']?\*[""']?\s*$",
    };

    foreach (var (description, pattern) in requiredPatterns)
    {
        if (!Regex.IsMatch(blockText, pattern, RegexOptions.Multiline))
        {
            failures.Add(description);
        }
    }

    var limitMatch = Regex.Match(blockText, @"^\s+open-pull-requests-limit:\s+(\d+)\s*$", RegexOptions.Multiline);
    if (!limitMatch.Success)
    {
        failures.Add("open-pull-requests-limit must be set");
    }
    else
    {
        var limit = int.Parse(limitMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (limit is < 1 or > 10)
        {
            failures.Add("open-pull-requests-limit must stay between 1 and 10");
        }
    }

    return failures;
}

static IEnumerable<string> ValidateDockerBlocks(string[] lines)
{
    var blocks = ExtractUpdateBlocks(lines, "docker");
    var failures = new List<string>();
    var requiredDirectories = new HashSet<string>(StringComparer.Ordinal)
    {
        "/Explore.API",
        "/Explore.Blazor",
    };

    foreach (var block in blocks)
    {
        var blockText = string.Join('\n', block);
        var directoryMatch = Regex.Match(blockText, @"^\s+directory:\s+[""']?(?<directory>[^""'\s]+)[""']?\s*$", RegexOptions.Multiline);
        if (!directoryMatch.Success)
        {
            failures.Add("docker update block must declare a directory");
            continue;
        }

        var directory = directoryMatch.Groups["directory"].Value;
        requiredDirectories.Remove(directory);

        failures.AddRange(ValidateDockerBlock(blockText, directory));
    }

    foreach (var missingDirectory in requiredDirectories.Order(StringComparer.Ordinal))
    {
        failures.Add($"missing docker Dependabot update block for {missingDirectory}");
    }

    return failures;
}

static IEnumerable<string> ValidateDockerBlock(string blockText, string directory)
{
    var failures = new List<string>();
    var requiredPatterns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [$"{directory} docker schedule interval must be weekly"] = @"^\s+interval:\s+weekly\s*$",
        [$"{directory} docker commit message prefix must be deps"] = @"^\s+prefix:\s+[""']?deps[""']?\s*$",
        [$"{directory} docker commit message must include dependency scope"] = @"^\s+include:\s+scope\s*$",
        [$"{directory} docker updates must be grouped"] = @"^\s+docker-base-images:\s*$",
        [$"{directory} docker group must match every base image"] = @"^\s+-\s+[""']?\*[""']?\s*$",
    };

    foreach (var (description, pattern) in requiredPatterns)
    {
        if (!Regex.IsMatch(blockText, pattern, RegexOptions.Multiline))
        {
            failures.Add(description);
        }
    }

    var limitMatch = Regex.Match(blockText, @"^\s+open-pull-requests-limit:\s+(\d+)\s*$", RegexOptions.Multiline);
    if (!limitMatch.Success)
    {
        failures.Add($"{directory} docker open-pull-requests-limit must be set");
    }
    else
    {
        var limit = int.Parse(limitMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (limit is < 1 or > 10)
        {
            failures.Add($"{directory} docker open-pull-requests-limit must stay between 1 and 10");
        }
    }

    return failures;
}

static List<string[]> ExtractUpdateBlocks(string[] lines, string ecosystem)
{
    var blocks = new List<string[]>();
    var ecosystemPattern = new Regex($@"^\s*-\s+package-ecosystem:\s+{Regex.Escape(ecosystem)}\s*$", RegexOptions.Compiled);
    for (var index = 0; index < lines.Length; index++)
    {
        if (!ecosystemPattern.IsMatch(lines[index]))
        {
            continue;
        }

        var endIndex = lines.Length;
        var nextBlockPattern = new Regex(@"^\s*-\s+package-ecosystem:\s+", RegexOptions.Compiled);
        for (var nextIndex = index + 1; nextIndex < lines.Length; nextIndex++)
        {
            if (nextBlockPattern.IsMatch(lines[nextIndex]))
            {
                endIndex = nextIndex;
                break;
            }
        }

        blocks.Add(lines[index..endIndex]);
        index = endIndex - 1;
    }

    return blocks;
}
