using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// File-based C# compilation check hook for ISLAMU Event (.NET 10).
// Targets modified projects recorded by ContextTracker before falling back to Explore.slnx,
// builds in Release configuration, and enforces single-node / non-reused MSBuild & Roslyn caps
// when ISLAMU_EXECUTION_PROFILE=constrained. Non-blocking hook: always exits with code 0.

try
{
    var options = ParseOptions(args);
    string workingDir = Directory.GetCurrentDirectory();
    string rootDir = FindSolutionRoot(workingDir);
    string cacheRoot = Path.Combine(rootDir, ".claude", "build-cache");

    string profile = Environment.GetEnvironmentVariable("ISLAMU_EXECUTION_PROFILE")?.Trim() ?? string.Empty;
    bool isConstrained = string.Equals(profile, "constrained", StringComparison.OrdinalIgnoreCase);

    IReadOnlyList<string> targets = ResolveTargets(rootDir, cacheRoot, options);

    if (options.DryRun)
    {
        PrintDryRun(rootDir, cacheRoot, profile, isConstrained, targets);
        Environment.Exit(0);
    }

    Console.WriteLine($"Checking compilation ({targets.Count} target(s), Release, profile={(string.IsNullOrEmpty(profile) ? "default" : profile)})...");

    var errorLogs = new StringBuilder();
    bool anyFailure = false;

    foreach (string target in targets)
    {
        var psi = CreateBuildStartInfo(rootDir, target, isConstrained);
        using var process = Process.Start(psi);
        if (process is null)
        {
            Console.WriteLine($"Unable to launch dotnet for '{target}'.");
            continue;
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            Console.WriteLine($"Compilation succeeded: {GetDisplayPath(rootDir, target)}");
            continue;
        }

        anyFailure = true;
        errorLogs.AppendLine(CultureInfo.InvariantCulture, $"DATE: {DateTime.UtcNow:O}");
        errorLogs.AppendLine(CultureInfo.InvariantCulture, $"TARGET: {target}");
        errorLogs.AppendLine("STDOUT:");
        errorLogs.AppendLine(stdout);
        errorLogs.AppendLine("STDERR:");
        errorLogs.AppendLine(stderr);
        errorLogs.AppendLine(new string('-', 60));
    }

    if (!anyFailure)
    {
        Environment.Exit(0);
    }

    Console.WriteLine("Compilation error detected (non-blocking).");
    Directory.CreateDirectory(cacheRoot);

    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
    string logFileName = $"error-{timestamp}.txt";
    string combined = errorLogs.ToString();

    File.WriteAllText(Path.Combine(cacheRoot, logFileName), combined);
    File.WriteAllText(Path.Combine(cacheRoot, "last-errors.txt"), combined);

    IEnumerable<string> preview = combined
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Where(l => l.Contains("error CS", StringComparison.Ordinal) || l.Contains(": error", StringComparison.Ordinal))
        .Take(5);

    Console.WriteLine($"Log saved: {logFileName}");
    Console.WriteLine("--- Preview ---");
    foreach (string line in preview)
    {
        Console.WriteLine(line);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Hook error: {ex.Message}");
}

Environment.Exit(0);

static HookOptions ParseOptions(string[] rawArgs)
{
    bool dryRun = false;
    string? sessionId = Environment.GetEnvironmentVariable("CLAUDE_SESSION_ID");
    var explicitProjects = new List<string>();

    for (int i = 0; i < rawArgs.Length; i++)
    {
        string arg = rawArgs[i];
        if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
        {
            dryRun = true;
        }
        else if (string.Equals(arg, "--session", StringComparison.OrdinalIgnoreCase) && i + 1 < rawArgs.Length)
        {
            sessionId = rawArgs[++i];
        }
        else if (string.Equals(arg, "--project", StringComparison.OrdinalIgnoreCase) && i + 1 < rawArgs.Length)
        {
            explicitProjects.Add(rawArgs[++i]);
        }
    }

    return new HookOptions(dryRun, sessionId, explicitProjects);
}

static string FindSolutionRoot(string startPath)
{
    var current = new DirectoryInfo(startPath);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "Explore.slnx")) ||
            File.Exists(Path.Combine(current.FullName, "Explore.sln")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return startPath;
}

static IReadOnlyList<string> ResolveTargets(string rootDir, string cacheRoot, HookOptions options)
{
    var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (string explicitPath in options.ExplicitProjects)
    {
        string candidate = Path.IsPathRooted(explicitPath)
            ? Path.GetFullPath(explicitPath)
            : Path.GetFullPath(Path.Combine(rootDir, explicitPath));
        if (File.Exists(candidate))
        {
            resolved.Add(candidate);
        }
    }

    if (resolved.Count > 0)
    {
        return resolved.ToArray();
    }

    if (Directory.Exists(cacheRoot))
    {
        IEnumerable<string> sessionDirs = ResolveSessionDirectories(cacheRoot, options.SessionId);
        foreach (string sessionDir in sessionDirs)
        {
            CollectTargetsFromEditedFilesLog(rootDir, Path.Combine(sessionDir, "edited-files.log"), resolved);
        }

        if (resolved.Count == 0)
        {
            foreach (string sessionDir in sessionDirs)
            {
                CollectTargetsFromAffectedLayers(rootDir, Path.Combine(sessionDir, "affected-layers.txt"), resolved);
            }
        }
    }

    if (resolved.Count > 0)
    {
        return resolved.ToArray();
    }

    string solutionPath = Path.Combine(rootDir, "Explore.slnx");
    if (!File.Exists(solutionPath))
    {
        solutionPath = Path.Combine(rootDir, "Explore.sln");
    }

    return File.Exists(solutionPath)
        ? [solutionPath]
        : [rootDir];
}

static IEnumerable<string> ResolveSessionDirectories(string cacheRoot, string? sessionId)
{
    if (!string.IsNullOrWhiteSpace(sessionId))
    {
        string requestedDir = Path.Combine(cacheRoot, sessionId);
        if (Directory.Exists(requestedDir))
        {
            return [requestedDir];
        }
    }

    return Directory.GetDirectories(cacheRoot)
        .OrderByDescending(Directory.GetLastWriteTimeUtc);
}

static void CollectTargetsFromEditedFilesLog(string rootDir, string logPath, ISet<string> resolved)
{
    if (!File.Exists(logPath))
    {
        return;
    }

    foreach (string line in File.ReadAllLines(logPath))
    {
        string? editedPath = ExtractEditedFilePath(line);
        if (string.IsNullOrWhiteSpace(editedPath))
        {
            continue;
        }

        string? projectPath = FindOwningProject(rootDir, editedPath);
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            resolved.Add(projectPath);
        }
    }
}

static string? ExtractEditedFilePath(string logLine)
{
    if (string.IsNullOrWhiteSpace(logLine))
    {
        return null;
    }

    // Format from ContextTracker.cs: "<unix_timestamp>:<filePath>:<layer>"
    int firstColon = logLine.IndexOf(':', StringComparison.Ordinal);
    int lastColon = logLine.LastIndexOf(':');
    if (firstColon < 0 || lastColon <= firstColon)
    {
        return null;
    }

    return logLine.Substring(firstColon + 1, lastColon - firstColon - 1).Trim();
}

static string? FindOwningProject(string rootDir, string editedPath)
{
    string fullPath = Path.IsPathRooted(editedPath)
        ? Path.GetFullPath(editedPath)
        : Path.GetFullPath(Path.Combine(rootDir, editedPath));

    string normalizedRoot = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar);
    if (!fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(fullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    string? startDir = File.Exists(fullPath) || !Directory.Exists(fullPath)
        ? Path.GetDirectoryName(fullPath)
        : fullPath;

    var current = startDir is null ? null : new DirectoryInfo(startDir);

    while (current is not null)
    {
        string[] projects = Directory.Exists(current.FullName)
            ? Directory.GetFiles(current.FullName, "*.csproj", SearchOption.TopDirectoryOnly)
            : [];

        if (projects.Length > 0)
        {
            return projects[0];
        }

        if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        current = current.Parent;
    }

    return null;
}

static void CollectTargetsFromAffectedLayers(string rootDir, string layersPath, ISet<string> resolved)
{
    if (!File.Exists(layersPath))
    {
        return;
    }

    foreach (string layer in File.ReadAllLines(layersPath))
    {
        string? relativeProject = layer.Trim() switch
        {
            "Domain" => "src/Explore.Domain/Explore.Domain.csproj",
            "Application" => "src/Explore.Application/Explore.Application.csproj",
            "Infrastructure" => "src/Explore.Infrastructure/Explore.Infrastructure.csproj",
            "API" => "src/Explore.API/Explore.API.csproj",
            "Frontend" => "src/Explore.Blazor/Explore.Blazor.csproj",
            _ => null
        };

        if (relativeProject is null)
        {
            continue;
        }

        string candidate = Path.Combine(rootDir, relativeProject);
        if (File.Exists(candidate))
        {
            resolved.Add(candidate);
        }
    }
}

static ProcessStartInfo CreateBuildStartInfo(string rootDir, string target, bool isConstrained)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = rootDir
    };

    psi.ArgumentList.Add("build");
    psi.ArgumentList.Add(target);
    psi.ArgumentList.Add("--configuration");
    psi.ArgumentList.Add("Release");
    psi.ArgumentList.Add("--nologo");
    psi.ArgumentList.Add("--verbosity");
    psi.ArgumentList.Add("quiet");

    if (isConstrained)
    {
        psi.ArgumentList.Add("-m:1");
        psi.ArgumentList.Add("-nr:false");
        psi.ArgumentList.Add("-p:UseSharedCompilation=false");
        psi.ArgumentList.Add("-p:BuildInParallel=false");

        psi.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_GCConserveMemory"] = "5";
    }

    return psi;
}

static void PrintDryRun(
    string rootDir,
    string cacheRoot,
    string profile,
    bool isConstrained,
    IReadOnlyList<string> targets)
{
    Console.WriteLine("[BuildCheck --dry-run]");
    Console.WriteLine($"Profile: {(string.IsNullOrEmpty(profile) ? "default" : profile)} (constrained={isConstrained})");
    Console.WriteLine($"Cache: {GetDisplayPath(rootDir, cacheRoot)}");
    Console.WriteLine($"ChildEnv: {(isConstrained ? "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 MSBUILDDISABLENODEREUSE=1 DOTNET_GCConserveMemory=5" : "(inherited)")}");

    foreach (string target in targets)
    {
        var psi = CreateBuildStartInfo(rootDir, target, isConstrained);
        Console.WriteLine($"Command: dotnet {string.Join(' ', psi.ArgumentList.Select(FormatArg))}");
    }
}

static string FormatArg(string value)
    => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

static string GetDisplayPath(string rootDir, string path)
    => Path.GetRelativePath(rootDir, path);

internal sealed record HookOptions(bool DryRun, string? SessionId, IReadOnlyList<string> ExplicitProjects);
