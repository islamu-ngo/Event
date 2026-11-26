namespace ISLAMU.ReleaseEngineering;

public static class Program
{
    public const int Success = 0;
    public const int ToolchainNotConfigured = 1;
    public const int ToolchainRejected = 2;
    public const int UsageError = 64;

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(5);

    public static int Main(string[] args) => Run(args, Console.Out);

    public static int Run(string[] args, TextWriter output)
    {
        string lockPath = Path.Combine(Environment.CurrentDirectory, "eng", "release", "toolchain.lock.json");
        string? bundlePath = Environment.GetEnvironmentVariable(GitCliffToolchainStore.DefaultBundleEnvironmentVariable);
        string platform = GitCliffToolchainStore.GetCurrentPlatform();
        return Run(args, output, lockPath, bundlePath, platform, ProcessTimeout);
    }

    public static int Run(
        string[] args,
        TextWriter output,
        string lockPath,
        string? bundlePath,
        string platform,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (args.Length == 0)
        {
            return WriteUsage(output);
        }

        if (string.Equals(args[0], "select-security-changes", StringComparison.Ordinal))
        {
            return SecurityChangeSelection.Run(args, output);
        }

        if (string.Equals(args[0], "select-build-test-changes", StringComparison.Ordinal))
        {
            return SecurityChangeSelection.RunBuildTest(args, output);
        }

        if (string.Equals(args[0], "verify-security-register", StringComparison.Ordinal))
        {
            return SecurityControlRegister.Run(args, output);
        }

        if (string.Equals(args[0], "ensure-git-cliff", StringComparison.Ordinal))
        {
            return GitCliffToolchainStore.RunEnsureCommand(args, output, lockPath, bundlePath, platform, timeout);
        }

        if (string.Equals(args[0], "verify-release-tag", StringComparison.Ordinal))
        {
            return GitTagSignatureVerifier.RunCli(args, output, Environment.CurrentDirectory, timeout);
        }

        if (string.Equals(args[0], "diff-contracts", StringComparison.Ordinal))
        {
            return ContractDiffCommand.Run(args, output, Environment.CurrentDirectory, timeout);
        }

        if (string.Equals(args[0], "sync-public-changelog", StringComparison.Ordinal))
        {
            return SyncPublicChangelogCommand.Run(args, output, Environment.CurrentDirectory);
        }

        if (string.Equals(args[0], "prepare", StringComparison.Ordinal))
        {
            return PrepareCommand.Run(args, output, Environment.CurrentDirectory, platform, timeout);
        }

        if (string.Equals(args[0], "seal-artifacts", StringComparison.Ordinal))
        {
            return ReleaseArtifactManifest.RunCli(args, output, Environment.CurrentDirectory);
        }

        if (args[0] is "allocate-change-id" or
            "create-change" or
            "preflight-commit" or
            "preflight-staged" or
            "preflight-range" or
            "install-change-hooks")
        {
            return ChangeWorkflowCommand.Run(
                args,
                output,
                Environment.CurrentDirectory,
                timeout);
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

        string effectiveBundlePath = bundlePath is not null
            ? bundlePath
            : GitCliffToolchainStore.ResolveConfiguredDirectory(lockPath, null, platform);

        ToolchainVerificationResult verification = GitCliffToolchainStore.VerifyInstalled(
            lockPath,
            effectiveBundlePath,
            platform,
            timeout);
        if (!verification.IsValid || verification.Executable is null)
        {
            output.WriteLine($"untrusted_tool: {verification.Diagnostic}");
            return verification.IsMissingLock ? ToolchainNotConfigured : ToolchainRejected;
        }

        output.WriteLine($"trusted_tool: git-cliff {verification.Executable.Version} ({verification.Executable.Platform})");
        return Success;
    }

    private static int WriteUsage(TextWriter output)
    {
        output.WriteLine(
            "usage: release-engine ensure-git-cliff [--output <directory>] | verify-tools | verify-release-tag --tag <tag-name> [--target <oid>] [--event-sha <oid>] [--allowed-signers <path>] [--signer <principal>] [--repository <path>] | diff-contracts <base-oid> <head-oid> <output-path> | prepare <release-directory> [--image-manifest <path>] | seal-artifacts --directory <path> --version <version> --tag <tag-name> --commit <oid> [--output-dir <path>] | sync-public-changelog --publication-base <https-url> [--allowed-signers <path>] [--sealed-artifacts-root <path>] [--check] | select-security-changes <paths-file> | select-build-test-changes <paths-file> | verify-security-register <register-json> | allocate-change-id --target <ref> | create-change --type <type> --scope <scope> --title <title> --summary <summary> [--group <group>] [--target <ref>] | preflight-commit <message-file> [--target <ref>] | preflight-staged [--target <ref>] | preflight-range --target <ref> [--head <ref>] | install-change-hooks [--target <ref>]");
        return UsageError;
    }
}
