using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Launches only publication commands from an authenticated promotion, using a private runtime
/// stage and the independently installed launcher's promotion authority, never candidate code.
/// Child output is discarded because it may contain operator inputs or secrets.
/// </summary>
public static class PromotedReleaseCommand
{
    private const string Engine = "ISLAMU.ReleaseEngineering";
    private const string PromotionSigners = Engine + ".promotion-allowed-signers";
    private static readonly TimeSpan ProcessTimeout = PublicationInventoryVerificationBudget.PromotedProcessTimeout;

    public static int Run(string[] args, TextWriter output, string repositoryRoot)
    {
        if (args.Length < 2 || args[0] != "run-promoted" ||
            args[1] is not ("sync-public-changelog" or "prepare-publication-inventory"))
        {
            output.WriteLine("promoted_command_invalid_arguments");
            return Program.UsageError;
        }

        string? stage = null;
        try
        {
            string Required(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;
            string candidate = Path.GetFullPath(repositoryRoot);
            var request = new TrustedBundleVerificationRequest(
                Required("ISLAMU_RELEASE_TRUSTED_BUNDLE"), candidate,
                new PromotionAuthorityInput(Required("ISLAMU_RELEASE_PROMOTION_RECEIPT"),
                    Required("ISLAMU_RELEASE_PROMOTION_SIGNATURE"), Required("ISLAMU_RELEASE_PROMOTION_PRINCIPAL")),
                Required("ISLAMU_RELEASE_BUNDLE_ID"), Required("ISLAMU_RELEASE_BUNDLE_VERSION"),
                Required("ISLAMU_RELEASE_POLICY_VERSION"), Required("ISLAMU_RELEASE_CONFIG_VERSION"),
                Required("ISLAMU_RELEASE_TRUST_VERSION"))
            { ExpectedManifestDigest = Required("ISLAMU_RELEASE_MANIFEST_SHA256") };
            TrustedBundleResult trusted = TrustedBundlePolicy.Verify(request);
            if (!trusted.IsValid || trusted.Bundle is null)
                return Reject(output, "promoted_command_trusted_bundle_invalid");

            string bundle = trusted.Bundle.Root;
            string runtime = Path.GetFullPath(AppContext.BaseDirectory);
            if (Overlaps(runtime, candidate) || Overlaps(runtime, bundle))
                return Reject(output, "promoted_command_runtime_overlap");

            byte[] manifestBytes = File.ReadAllBytes(Path.Join(bundle, "trusted-bundle.manifest.json"));
            if (Digest(manifestBytes) != trusted.ManifestDigest)
                return Reject(output, "promoted_command_trusted_bundle_invalid");
            using JsonDocument manifest = JsonDocument.Parse(manifestBytes);
            Dictionary<string, string> files = manifest.RootElement.GetProperty("files").EnumerateArray()
                .ToDictionary(item => item.GetProperty("path").GetString()!,
                    item => item.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
            string[] required = [$"bin/{Engine}.dll", $"bin/{Engine}.deps.json", $"bin/{Engine}.runtimeconfig.json"];
            if (required.Any(path => !files.ContainsKey(path)) || files.ContainsKey("bin/" + PromotionSigners))
                return Reject(output, "promoted_command_runtime_incomplete");

            stage = Path.Join(Path.GetTempPath(), $"islamu-promoted-{Guid.NewGuid():N}");
            if (Overlaps(stage, candidate) || Overlaps(stage, bundle) || Overlaps(stage, runtime) ||
                !SafeParents(stage))
                return Reject(output, "promoted_command_stage_invalid");
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(stage);
            else Directory.CreateDirectory(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            foreach ((string path, string hash) in files.Where(item => item.Key.StartsWith("bin/", StringComparison.Ordinal)))
            {
                byte[] bytes = File.ReadAllBytes(Path.Join(bundle, path));
                if (bytes.LongLength > TrustedBundlePolicy.MaximumFileBytes || Digest(bytes) != hash)
                    return Reject(output, "promoted_command_trusted_bundle_invalid");
                string destination = Path.Join(stage, path[4..]);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
            }

            // Published .NET dependency assets use package-relative paths in deps.json, while
            // publish output places managed/native runtime files beside the entry assembly.
            using JsonDocument deps = JsonDocument.Parse(File.ReadAllBytes(Path.Join(stage, Engine + ".deps.json")));
            string targetName = deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
            foreach (JsonProperty library in deps.RootElement.GetProperty("targets").GetProperty(targetName).EnumerateObject())
            {
                foreach (string group in new[] { "runtime", "native", "resources", "runtimeTargets" }
                    .Where(group => library.Value.TryGetProperty(group, out _)))
                {
                    JsonElement assets = library.Value.GetProperty(group);
                    foreach (JsonProperty asset in assets.EnumerateObject())
                    {
                        string name = Path.GetFileName(asset.Name);
                        string relative = group == "resources"
                            ? asset.Value.GetProperty("locale").GetString() + "/" + name
                            : name;
                        if (!files.ContainsKey("bin/" + relative) || !File.Exists(Path.Join(stage, relative)))
                            return Reject(output, "promoted_command_runtime_incomplete");
                    }
                }
            }

            using JsonDocument config = JsonDocument.Parse(File.ReadAllBytes(Path.Join(stage, Engine + ".runtimeconfig.json")));
            if (config.RootElement.GetProperty("runtimeOptions").TryGetProperty("additionalProbingPaths", out _))
                return Reject(output, "promoted_command_runtime_incomplete");

            // Verification above authenticates this fixed file, independently of the bundle.
            File.Copy(Path.Join(runtime, PromotionSigners), Path.Join(stage, PromotionSigners));
            if (!TrustedBundlePolicy.Verify(request).IsValid)
                return Reject(output, "promoted_command_trusted_bundle_invalid");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(ReleaseToolPaths.Dotnet)
                {
                    WorkingDirectory = candidate,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add(Path.Join(stage, Engine + ".dll"));
            foreach (string argument in args.Skip(1)) process.StartInfo.ArgumentList.Add(argument);
            // Runtime injection must not load unauthenticated assemblies from inherited settings.
            foreach (string name in process.StartInfo.Environment.Keys.Where(name =>
                name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                process.StartInfo.Environment.Remove(name);
            }
            process.Start();
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(ProcessTimeout);
            Task stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            Task stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            try
            {
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
                Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return Reject(output, "promoted_command_timeout");
            }
            output.WriteLine("promoted_command_completed");
            return process.ExitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or KeyNotFoundException)
        {
            return Reject(output, "promoted_command_failed");
        }
        finally
        {
            if (stage is not null && Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool Overlaps(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        return left.Equals(right, comparison) ||
            left.StartsWith(right + Path.DirectorySeparatorChar, comparison) ||
            right.StartsWith(left + Path.DirectorySeparatorChar, comparison);
    }

    private static bool SafeParents(string path)
    {
        for (DirectoryInfo? parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
            if (parent.LinkTarget is not null) return false;
        return true;
    }

    private static int Reject(TextWriter output, string diagnostic)
    {
        output.WriteLine(diagnostic);
        return Program.ToolchainRejected;
    }
}
