// ABOUTME: Validates that skipped API contract tests are listed with owners and removal criteria.
// ABOUTME: Keeps deferred OpenAPI/HATEOAS contract enforcement from becoming invisible test debt.
#:property RestorePackagesWithLockFile=false

using System.Text.RegularExpressions;

var root = args.Length > 0 ? args[0] : ".";
var integrationTestRoot = Path.Combine(root, "tests", "Event.API.IntegrationTests");
var inventoryPath = File.Exists(Path.Combine(root, "docs", "internal", "API_CONTRACT_TEST_DEBT.md"))
    ? Path.Combine(root, "docs", "internal", "API_CONTRACT_TEST_DEBT.md")
    : Path.Combine(root, "docs", "API_CONTRACT_TEST_DEBT.md");

if (!Directory.Exists(integrationTestRoot))
{
    Console.Error.WriteLine($"Integration test directory was not found: {integrationTestRoot}");
    return 1;
}

if (!File.Exists(inventoryPath))
{
    Console.Error.WriteLine($"API contract test debt inventory was not found: {inventoryPath}");
    return 1;
}

var skippedTests = FindSkippedApiContractTests(integrationTestRoot, root).ToList();
var inventory = ParseInventory(inventoryPath);
var failures = new List<string>();

foreach (var skippedTest in skippedTests)
{
    if (!skippedTest.SkipReason.Contains("Removal:", StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{skippedTest.RelativePath}:{skippedTest.LineNumber} {skippedTest.MethodName} has API contract skip reason without a Removal clause.");
    }

    if (!inventory.TryGetValue(skippedTest.MethodName, out var item))
    {
        failures.Add($"{skippedTest.MethodName} is skipped as API contract debt but is missing from docs/API_CONTRACT_TEST_DEBT.md.");
        continue;
    }

    if (!string.Equals(item.RelativePath, skippedTest.RelativePath, StringComparison.Ordinal))
    {
        failures.Add($"{skippedTest.MethodName} inventory path '{item.RelativePath}' does not match source path '{skippedTest.RelativePath}'.");
    }

    if (string.IsNullOrWhiteSpace(item.Owner) || item.Owner.Equals("TBD", StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{skippedTest.MethodName} inventory entry must have a concrete owner.");
    }

    if (string.IsNullOrWhiteSpace(item.RemovalCondition) || item.RemovalCondition.Equals("TBD", StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{skippedTest.MethodName} inventory entry must have a concrete removal condition.");
    }
}

foreach (var staleItem in inventory.Values.Where(item => skippedTests.All(test => !string.Equals(test.MethodName, item.MethodName, StringComparison.Ordinal))))
{
    failures.Add($"{staleItem.MethodName} is listed in docs/API_CONTRACT_TEST_DEBT.md but no matching skipped API contract test exists.");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("API contract skip inventory validation failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine($"API contract skip inventory is current for {skippedTests.Count} skipped API contract test(s).");
return 0;

static IEnumerable<SkippedTest> FindSkippedApiContractTests(string integrationTestRoot, string root)
{
    foreach (var file in Directory.EnumerateFiles(integrationTestRoot, "*.cs", SearchOption.AllDirectories)
                 .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                 .OrderBy(path => path, StringComparer.Ordinal))
    {
        string? pendingSkipReason = null;
        var pendingSkipLine = 0;
        var lines = File.ReadAllLines(file);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var skipMatch = SkipAttributeRegex().Match(line);
            if (skipMatch.Success)
            {
                var reason = skipMatch.Groups["reason"].Value.Replace("\\\"", "\"", StringComparison.Ordinal);
                if (reason.Contains("Category: API contract", StringComparison.OrdinalIgnoreCase))
                {
                    pendingSkipReason = reason;
                    pendingSkipLine = index + 1;
                }
                continue;
            }

            if (pendingSkipReason is null)
            {
                continue;
            }

            var methodMatch = TestMethodRegex().Match(line);
            if (!methodMatch.Success)
            {
                continue;
            }

            yield return new SkippedTest(
                ToRepositoryPath(root, file),
                pendingSkipLine,
                methodMatch.Groups["name"].Value,
                pendingSkipReason);

            pendingSkipReason = null;
            pendingSkipLine = 0;
        }
    }
}

static Dictionary<string, InventoryItem> ParseInventory(string inventoryPath)
{
    var items = new Dictionary<string, InventoryItem>(StringComparer.Ordinal);

    foreach (var line in File.ReadLines(inventoryPath))
    {
        if (!line.StartsWith("| `", StringComparison.Ordinal))
        {
            continue;
        }

        var columns = line.Split('|').Select(column => column.Trim()).ToArray();
        if (columns.Length < 8)
        {
            continue;
        }

        var methodName = TrimBackticks(columns[1]);
        var relativePath = TrimBackticks(columns[2]);
        var owner = columns[4];
        var removalCondition = columns[5];

        if (!string.IsNullOrWhiteSpace(methodName))
        {
            items[methodName] = new InventoryItem(methodName, relativePath, owner, removalCondition);
        }
    }

    return items;
}

static string TrimBackticks(string value)
{
    return value.Trim().Trim('`');
}

static string ToRepositoryPath(string root, string file)
{
    return Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
}

internal sealed record SkippedTest(string RelativePath, int LineNumber, string MethodName, string SkipReason);

internal sealed record InventoryItem(string MethodName, string RelativePath, string Owner, string RemovalCondition);

partial class Program
{
    [GeneratedRegex(@"\[Skip\(\""(?<reason>(?:[^\""\\]|\\.)*)\""\)\]")]
    private static partial Regex SkipAttributeRegex();

    [GeneratedRegex(@"\b(?:public|internal|private)\s+(?:async\s+)?(?:Task|ValueTask|void)\s+(?<name>[A-Za-z0-9_]+)\s*\(")]
    private static partial Regex TestMethodRegex();
}
