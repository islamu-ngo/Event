using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

bool dryRun = args.Contains("--dry-run");
string? singleFile = null;
int singleIdx = Array.IndexOf(args, "--single");
if (singleIdx >= 0 && singleIdx + 1 < args.Length)
{
    singleFile = args[singleIdx + 1];
}

string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
if (!File.Exists(Path.Combine(repoRoot, "Explore.slnx")))
{
    repoRoot = Directory.GetCurrentDirectory();
}

Console.WriteLine($"Repository Root: {repoRoot}");
Console.WriteLine($"Mode: {(dryRun ? "DRY-RUN (no files will be modified)" : "EXECUTE (modifying files)")}");
if (singleFile != null) Console.WriteLine($"Target Single File: {singleFile}");

var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".git",
    ".agents",
    ".omo",
    "docs/internal",
    "dev",
    "islamic-value-sensitive-design",
    ".codex",
    "schemas",
    "bin",
    "obj",
    "artifacts",
    "TestResults",
    "StrykerOutput",
    "BenchmarkDotNet.Artifacts",
    ".gemini"
};

var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "src/Explore.Diagnostic/AgentInventory/AiAgentContractInventoryGenerator.cs",
    "tests/Event.Architecture.Tests/AtprotoDependencyBoundaryTests.cs",
    "tests/Event.Architecture.Tests/SetupAssistantArchitectureTests.cs",
    "tests/Event.API.IntegrationTests/Features/SetupSecretFlowTests.cs",
    "tests/Event.Architecture.Tests/TestAssets/contract-privacy.json",
    "tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenConstraintClassifier.fixture",
    "tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenDirectAdo.fixture",
    "tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenInternalImport.fixture",
    "tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenPhysicalNames.fixture",
    "tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenProviderLiteral.fixture",
    "tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenRawSql.fixture",
    "eng/release/dependencies/terminal-gui/patches/0001-remove-textmate-grammars.patch",
    "eng/release/tests/ISLAMU.ReleaseEngineering.Tests/Fixtures/untrusted-text-corpus.txt",
    "eng/setup-assistant/generated/browser-release-capabilities.json",
    "eng/setup-assistant/generated/composition-scale-profiles.json",
    "eng/setup-assistant/generated/environment-catalogue.json",
    "eng/setup-assistant/generated/frozen-contract-baseline.json",
    "eng/setup-assistant/generated/setup-live-release-capabilities.json",
    "eng/release/dependencies/terminal-gui/approval.json",
    "eng/release/dependencies/terminal-gui/generated/package-evidence.json",
    "eng/release/dependencies/terminal-gui/source.json",
    ".ci/release/provider-definition.schema.json",
    ".github/copilot-instructions.md",
    "AGENTS.md",
    "docs/README.md",
    "eng/tools/StripAboutMeHeaders.cs"
};

var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".cs", ".razor", ".css", ".csproj", ".props", ".js", ".sql", ".proto", ".resx", ".yaml", ".yml", ".sh", ".toml", ".md", ".txt"
};

int totalChecked = 0;
int totalModified = 0;

void ProcessDirectory(string dir)
{
    string relDir = Path.GetRelativePath(repoRoot, dir).Replace('\\', '/');
    if (excludedDirs.Any(e => relDir.Equals(e, StringComparison.OrdinalIgnoreCase) || relDir.StartsWith(e + "/", StringComparison.OrdinalIgnoreCase)))
    {
        return;
    }

    foreach (var file in Directory.GetFiles(dir))
    {
        ProcessFile(file);
    }

    foreach (var subDir in Directory.GetDirectories(dir))
    {
        ProcessDirectory(subDir);
    }
}

void ProcessFile(string filePath)
{
    string relPath = Path.GetRelativePath(repoRoot, filePath).Replace('\\', '/');
    if (excludedFiles.Contains(relPath))
    {
        return;
    }

    string ext = Path.GetExtension(filePath);
    bool isSpecialFile = Path.GetFileName(filePath) switch
    {
        "Dockerfile" => true,
        "docker-compose.yml" => true,
        "Directory.Packages.props" => true,
        "Directory.Build.props" => true,
        "NuGet.Config" => true,
        ".dockerignore" => true,
        ".gitattributes" => true,
        ".gitignore" => true,
        ".gitleaks.toml" => true,
        "CODEOWNERS" => true,
        "CLA.md" => true,
        "CONTRIBUTING.md" => true,
        "SECURITY.md" => true,
        _ => false
    };

    if (!allowedExtensions.Contains(ext) && !isSpecialFile && !relPath.StartsWith("cerbos/policies/", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    totalChecked++;

    byte[] bytes = File.ReadAllBytes(filePath);
    bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    var encoding = new UTF8Encoding(hasBom);
    string content = encoding.GetString(bytes);

    if (!content.Contains("ABOUTME:", StringComparison.Ordinal))
    {
        return;
    }

    string newLine = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
    if (lines.Count == 0) return;

    int startIndex = (lines[0].StartsWith("#!", StringComparison.Ordinal)) ? 1 : 0;
    if (startIndex >= lines.Count) return;

    bool modified = false;

    while (startIndex < lines.Count)
    {
        string line = lines[startIndex].TrimStart();

        // Single-line comments: // or # or --
        if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('#') || line.StartsWith("--", StringComparison.Ordinal))
        {
            if (line.Contains("ABOUTME:", StringComparison.Ordinal))
            {
                lines.RemoveAt(startIndex);
                modified = true;
                continue;
            }
        }

        // Razor comments: @* ... *@
        if (line.StartsWith("@*", StringComparison.Ordinal))
        {
            if (line.Contains("*@", StringComparison.Ordinal))
            {
                if (line.Contains("ABOUTME:", StringComparison.Ordinal))
                {
                    lines.RemoveAt(startIndex);
                    modified = true;
                    continue;
                }
            }
            else
            {
                int closeIdx = -1;
                bool containsAboutMe = line.Contains("ABOUTME:", StringComparison.Ordinal);
                for (int j = startIndex + 1; j < Math.Min(lines.Count, startIndex + 10); j++)
                {
                    if (lines[j].Contains("ABOUTME:", StringComparison.Ordinal))
                        containsAboutMe = true;
                    if (lines[j].Contains("*@", StringComparison.Ordinal))
                    {
                        closeIdx = j;
                        break;
                    }
                }
                if (containsAboutMe && closeIdx != -1)
                {
                    int count = closeIdx - startIndex + 1;
                    lines.RemoveRange(startIndex, count);
                    modified = true;
                    continue;
                }
            }
        }

        // C-style / CSS comments: /* ... */
        if (line.StartsWith("/*", StringComparison.Ordinal))
        {
            if (line.Contains("*/", StringComparison.Ordinal))
            {
                if (line.Contains("ABOUTME:", StringComparison.Ordinal))
                {
                    lines.RemoveAt(startIndex);
                    modified = true;
                    continue;
                }
            }
            else
            {
                int closeIdx = -1;
                bool containsAboutMe = line.Contains("ABOUTME:", StringComparison.Ordinal);
                for (int j = startIndex + 1; j < Math.Min(lines.Count, startIndex + 10); j++)
                {
                    if (lines[j].Contains("ABOUTME:", StringComparison.Ordinal))
                        containsAboutMe = true;
                    if (lines[j].Contains("*/", StringComparison.Ordinal))
                    {
                        closeIdx = j;
                        break;
                    }
                }
                if (containsAboutMe && closeIdx != -1)
                {
                    int count = closeIdx - startIndex + 1;
                    lines.RemoveRange(startIndex, count);
                    modified = true;
                    continue;
                }
            }
        }

        // XML / HTML comments: <!-- ... -->
        if (line.StartsWith("<!--", StringComparison.Ordinal))
        {
            if (line.Contains("-->", StringComparison.Ordinal))
            {
                if (line.Contains("ABOUTME:", StringComparison.Ordinal))
                {
                    lines.RemoveAt(startIndex);
                    modified = true;
                    continue;
                }
            }
            else
            {
                int closeIdx = -1;
                bool containsAboutMe = line.Contains("ABOUTME:", StringComparison.Ordinal);
                for (int j = startIndex + 1; j < Math.Min(lines.Count, startIndex + 10); j++)
                {
                    if (lines[j].Contains("ABOUTME:", StringComparison.Ordinal))
                        containsAboutMe = true;
                    if (lines[j].Contains("-->", StringComparison.Ordinal))
                    {
                        closeIdx = j;
                        break;
                    }
                }
                if (containsAboutMe && closeIdx != -1)
                {
                    int count = closeIdx - startIndex + 1;
                    lines.RemoveRange(startIndex, count);
                    modified = true;
                    continue;
                }
            }
        }

        break;
    }

    if (!modified)
    {
        return;
    }

    if (lines.Count > startIndex && string.IsNullOrWhiteSpace(lines[startIndex]))
    {
        lines.RemoveAt(startIndex);
    }

    totalModified++;
    if (!dryRun)
    {
        string updatedContent = string.Join(newLine, lines);
        File.WriteAllText(filePath, updatedContent, encoding);
    }
}

if (singleFile != null)
{
    string target = Path.IsPathRooted(singleFile) ? singleFile : Path.Combine(repoRoot, singleFile);
    if (File.Exists(target))
    {
        ProcessFile(target);
    }
    else
    {
        Console.WriteLine($"File not found: {target}");
    }
    Console.WriteLine($"\nCompleted single file! Checked: {totalChecked} files. Identified for modification: {totalModified} files.");
    return;
}

string[] roots = { "src", "tests", "cerbos", "eng", ".ci", "docker", "deploy", ".github" };
foreach (var root in roots)
{
    string fullPath = Path.Combine(repoRoot, root);
    if (Directory.Exists(fullPath))
    {
        ProcessDirectory(fullPath);
    }
}

string[] rootFiles = {
    "Directory.Packages.props",
    "Directory.Build.props",
    "docker-compose.yml",
    "NuGet.Config",
    ".gitignore",
    ".gitattributes",
    ".dockerignore",
    ".gitleaks.toml",
    "CLA.md",
    "CONTRIBUTING.md",
    "SECURITY.md"
};

foreach (var rf in rootFiles)
{
    string fullPath = Path.Combine(repoRoot, rf);
    if (File.Exists(fullPath))
    {
        ProcessFile(fullPath);
    }
}

Console.WriteLine($"\nCompleted! Checked: {totalChecked} files. Identified for modification: {totalModified} files.");
