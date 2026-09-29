#:property RestorePackagesWithLockFile=false

using System.Text;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --file eng/setup-assistant/ValidateSetupAssistantSkill.cs -- <SKILL.md>");
    return 64;
}

try
{
    string path = Path.GetFullPath(args[0]);
    string text = await File.ReadAllTextAsync(path, Encoding.UTF8);
    string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
    if (normalized.Contains("<!-- ABOUT" + "ME:", StringComparison.Ordinal))
        throw new InvalidDataException("skill-synthetic-header-forbidden");
    if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        throw new InvalidDataException("skill-frontmatter-missing");

    int end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
    if (end < 0)
        throw new InvalidDataException("skill-frontmatter-unterminated");

    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (string line in normalized[4..end].Split('\n'))
    {
        int separator = line.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
            throw new InvalidDataException("skill-frontmatter-invalid");
        string key = line[..separator].Trim();
        string value = line[(separator + 1)..].Trim().Trim('"');
        if (!fields.TryAdd(key, value) || value.Length == 0)
            throw new InvalidDataException("skill-frontmatter-invalid");
    }

    string? parent = Path.GetDirectoryName(path);
    if (parent is null)
        throw new InvalidDataException("skill-parent-missing");
    string directoryName = Path.GetFileName(parent);
    Require(fields, "name", directoryName);
    RequireOneOf(fields, "type", ["guardrail", "pattern", "reference", "workflow"]);
    RequireOneOf(fields, "enforcement", ["block", "suggest", "inform"]);
    RequireOneOf(fields, "priority", ["critical", "high", "medium", "low"]);
    if (!fields.TryGetValue("description", out string? description)
        || description.Length < 20)
        throw new InvalidDataException("skill-description-invalid");
    if (normalized.Split('\n').Length > 250)
        throw new InvalidDataException("skill-line-limit-exceeded");

    Console.WriteLine("setup-assistant-skill-valid");
    return 0;
}
catch (Exception exception) when (exception is IOException
    or UnauthorizedAccessException
    or InvalidDataException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static void Require(
    IReadOnlyDictionary<string, string> fields,
    string name,
    string expected)
{
    if (!fields.TryGetValue(name, out string? actual)
        || !string.Equals(actual, expected, StringComparison.Ordinal))
    {
        throw new InvalidDataException($"skill-{name}-invalid");
    }
}

static void RequireOneOf(
    IReadOnlyDictionary<string, string> fields,
    string name,
    IReadOnlyCollection<string> allowed)
{
    if (!fields.TryGetValue(name, out string? actual)
        || !allowed.Contains(actual, StringComparer.Ordinal))
    {
        throw new InvalidDataException($"skill-{name}-invalid");
    }
}
