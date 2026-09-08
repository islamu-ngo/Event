using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

public static class Program
{
    public const int Success = 0;
    public const int ToolchainNotConfigured = 1;
    public const int ToolchainRejected = 2;
    public const int UsageError = 64;

    private const int MaximumProcessOutput = 4096;
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static int Main(string[] args) => Run(args, Console.Out);

    public static int Run(string[] args, TextWriter output)
    {
        string lockPath = Path.Combine(Environment.CurrentDirectory, "eng", "release", "toolchain.lock.json");
        string? bundlePath = Environment.GetEnvironmentVariable("ISLAMU_RELEASE_TOOL_BUNDLE");
        return Run(args, output, lockPath, bundlePath, GetPlatform(), ProcessTimeout);
    }

    public static int Run(
        string[] args,
        TextWriter output,
        string lockPath,
        string? bundlePath,
        string platform,
        TimeSpan timeout)
    {
        if (args.Length == 0)
        {
            return WriteUsage(output);
        }

        if (string.Equals(args[0], "prepare", StringComparison.Ordinal))
        {
            return PrepareCommand.Run(args, output, Environment.CurrentDirectory, GetPlatform(), ProcessTimeout);
        }

        if (string.Equals(args[0], "verify-candidate", StringComparison.Ordinal))
        {
            return CandidateCommand.Run(args, output, Environment.CurrentDirectory, GetPlatform(), ProcessTimeout);
        }

        if (string.Equals(args[0], "verify-tag", StringComparison.Ordinal) || string.Equals(args[0], "tag-message", StringComparison.Ordinal))
        {
            return TagCommand.Run(args, output, Environment.CurrentDirectory, GetPlatform(), ProcessTimeout);
        }

        if (string.Equals(args[0], "verify-main", StringComparison.Ordinal))
        {
            return MainCommand.Run(args, output, Environment.CurrentDirectory, ProcessTimeout);
        }

        if (string.Equals(args[0], "verify-baseline", StringComparison.Ordinal))
        {
            return BaselineCommand.Run(args, output, Environment.CurrentDirectory, ProcessTimeout);
        }

        if (string.Equals(args[0], "open-maintenance-line", StringComparison.Ordinal))
        {
            return MaintenanceLineCommand.Run(args, output, Environment.CurrentDirectory, GetPlatform(), ProcessTimeout);
        }

        if (string.Equals(args[0], "activate-trust", StringComparison.Ordinal))
        {
            return TrustActivationCommand.Run(args, output, Environment.CurrentDirectory);
        }

        if (args[0] is "allocate-change-id" or
            "create-change" or
            "preflight-commit" or
            "preflight-staged" or
            "preflight-range" or
            "rename-change" or
            "install-change-hooks")
        {
            return ChangeWorkflowCommand.Run(
                args,
                output,
                Environment.CurrentDirectory,
                ProcessTimeout);
        }

        if (!string.Equals(args[0], "verify-tools", StringComparison.Ordinal))
        {
            output.WriteLine("unknown_command: run without arguments to see supported commands");
            return UsageError;
        }

        if (args.Length != 1)
        {
            output.WriteLine("invalid_arguments: verify-tools accepts no arguments");
            return WriteUsage(output);
        }

        if (!File.Exists(lockPath))
        {
            output.WriteLine("untrusted_tool: toolchain lock is not configured");
            return ToolchainNotConfigured;
        }

        if (string.IsNullOrWhiteSpace(bundlePath) || !Directory.Exists(bundlePath))
        {
            return Reject(output, "local tool bundle directory is required");
        }

        ToolchainLock toolchain;
        try
        {
            toolchain = JsonSerializer.Deserialize<ToolchainLock>(
                File.ReadAllText(lockPath),
                JsonOptions)
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return Reject(output, "toolchain lock is malformed");
        }

        if (toolchain.SchemaVersion != 1 ||
            !string.Equals(toolchain.Tool, "git-cliff", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(toolchain.Version) ||
            toolchain.Platforms is null)
        {
            return Reject(output, "toolchain lock is malformed");
        }

        ToolPlatform[] matches = toolchain.Platforms
            .Where(candidate => string.Equals(candidate.Platform, platform, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length > 1)
        {
            return Reject(output, "toolchain lock is malformed");
        }

        ToolPlatform? approved = matches.SingleOrDefault();
        if (approved is null)
        {
            return Reject(output, $"platform is not approved: {platform}");
        }

        if (string.IsNullOrWhiteSpace(approved.Executable) ||
            Path.GetFileName(approved.Executable) != approved.Executable ||
            approved.ExecutableSha256?.Length != 64)
        {
            return Reject(output, "toolchain lock is malformed");
        }

        string executablePath = Path.Combine(bundlePath, approved.Executable);
        if (!File.Exists(executablePath))
        {
            return Reject(output, $"required executable is missing: {approved.Executable}");
        }

        string actualDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executablePath)));
        if (!string.Equals(actualDigest, approved.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Reject(output, $"executable digest mismatch: {approved.Executable}");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        try
        {
            process.StartInfo.ArgumentList.Add("--version");
            process.Start();

            using var timeoutSource = new CancellationTokenSource(timeout);
            Task<string> standardOutput = ReadBoundedAsync(process.StandardOutput, timeoutSource.Token);
            Task<string> standardError = ReadBoundedAsync(process.StandardError, timeoutSource.Token);
            process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
            string versionOutput = standardOutput.GetAwaiter().GetResult().Trim();
            string errorOutput = standardError.GetAwaiter().GetResult().Trim();

            if (process.ExitCode != 0 || errorOutput.Length != 0)
            {
                return Reject(output, "version probe failed");
            }

            if (!string.Equals(versionOutput, $"git-cliff {toolchain.Version}", StringComparison.Ordinal))
            {
                return Reject(output, $"version mismatch: expected git-cliff {toolchain.Version}");
            }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return Reject(output, "version probe timed out or exceeded output limit");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return Reject(output, "version probe failed");
        }

        output.WriteLine($"trusted_tool: git-cliff {toolchain.Version} ({platform})");
        return Success;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new char[MaximumProcessOutput];
        int count = 0;
        while (count < result.Length)
        {
            int read = await reader.ReadAsync(result.AsMemory(count, result.Length - count), cancellationToken);
            if (read == 0)
            {
                return new string(result, 0, count);
            }

            count += read;
        }

        if (await reader.ReadAsync(new char[1], cancellationToken) != 0)
        {
            throw new OperationCanceledException("Process output exceeded the configured limit.");
        }

        return new string(result);
    }

    private static string GetPlatform()
    {
        string architecture = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : "unsupported";
        string operatingSystem = OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsWindows() ? "windows" : "unsupported";
        return $"{operatingSystem}-{architecture}";
    }

    private static int Reject(TextWriter output, string reason)
    {
        output.WriteLine($"untrusted_tool: {reason}");
        return ToolchainRejected;
    }

    private static int WriteUsage(TextWriter output)
    {
        output.WriteLine("usage: release-engine allocate-change-id --target <ref> | create-change --type <type> --scope <scope> --title <title> --summary <summary> [--group <group>] [--target <ref>] | preflight-commit <message-file> [--target <ref>] | preflight-staged [--target <ref>] | preflight-range --target <ref> [--head <ref>] | rename-change --commit <oid> --from <id> [--to <id>] --reason <reason> | install-change-hooks [--target <ref>] | verify-tools | prepare <release-directory> | verify-candidate <release-directory> <candidate-oid> | tag-message <release-directory> | verify-tag <release-directory> <tag-name> | verify-main <release-directory> <expected-old-origin-main-oid> <tag-object-oid> | verify-baseline <baseline-ref> <target-oid> <tag-object-oid> | open-maintenance-line <release-directory> <tag-object-oid> | activate-trust --release-principal <name> --release-key <public-key> --promotion-principal <name> --promotion-key <public-key> --valid-from <yyyy-MM-dd> --valid-until <yyyy-MM-dd> --output <trust-directory> [--replace]");
        return UsageError;
    }

    private sealed record ToolchainLock(int SchemaVersion, string? Tool, string? Version, ToolPlatform[]? Platforms);
    private sealed record ToolPlatform(string? Platform, string? Executable, string? ExecutableSha256);
}
