using System.Globalization;

namespace Event.Testing;

internal enum TestSuiteKind
{
    UnitPure,
    HostInProcess,
    ContainerBacked,
    DistributedAppHost
}

internal enum ExecutionProfileKind
{
    Constrained,
    Balanced,
    Performance
}

internal enum ParallelLimitSource
{
    CommandLine,
    TUnitEnv,
    RepoEnv,
    Profile,
    Adaptive
}

/// <summary>Declares the resource profile classification of a TUnit test assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
internal sealed class TestSuiteKindAttribute(string suiteKind) : Attribute
{
    public TestSuiteKind SuiteKind { get; } = TestExecutionProfile.ParseSuiteKind(suiteKind);
}

internal sealed record TestExecutionEnvironment(
    int ProcessorCount,
    long? EffectiveAvailableMemoryBytes,
    IReadOnlyList<string> CommandLineArgs,
    Func<string, string?> GetEnvironmentVariable);

internal sealed record TestExecutionDecision(
    int EffectiveLimit,
    ExecutionProfileKind Profile,
    ParallelLimitSource Source,
    double? EffectiveAvailableGiB,
    bool ShouldConfigureTUnitSettings);

/// <summary>
/// Resolves conservative memory-aware TUnit parallelism defaults while yielding cleanly
/// to CLI flags, TUnit environment variables, repository overrides, and execution profiles.
/// </summary>
internal static class TestExecutionProfile
{
    private const long BytesPerKiB = 1024L;
    private const long BytesPerGiB = 1024L * 1024L * 1024L;

    internal const string ProfileEnvironmentVariable = "ISLAMU_EXECUTION_PROFILE";
    internal const string RepoParallelLimitEnvironmentVariable = "ISLAMU_TEST_PARALLEL_LIMIT";
    internal const string TUnitMaxParallelTestsEnvironmentVariable = "TUNIT_MAX_PARALLEL_TESTS";
    internal const string ContainerBudgetGiBEnvironmentVariable = "ISLAMU_CONTAINER_BUDGET_GIB";

    internal static TestSuiteKind ParseSuiteKind(string? raw) =>
        Enum.TryParse<TestSuiteKind>(raw?.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported TestSuiteKind '{raw}'.");

    internal static ExecutionProfileKind ParseProfile(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ExecutionProfileKind.Balanced;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "constrained" => ExecutionProfileKind.Constrained,
            "balanced" => ExecutionProfileKind.Balanced,
            "performance" => ExecutionProfileKind.Performance,
            _ => ExecutionProfileKind.Balanced
        };
    }

    internal static int? ParseCommandLineMaxParallelTests(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return null;
        }

        int? resolved = null;
        for (int index = 0; index < args.Count; index++)
        {
            string current = args[index];
            if (string.Equals(current, "--maximum-parallel-tests", StringComparison.Ordinal))
            {
                if (index + 1 < args.Count && TryParsePositiveInt(args[index + 1], out int parsed))
                {
                    resolved = parsed;
                    index++;
                }

                continue;
            }

            const string prefix = "--maximum-parallel-tests=";
            if (current.StartsWith(prefix, StringComparison.Ordinal) &&
                TryParsePositiveInt(current[prefix.Length..], out int inlineParsed))
            {
                resolved = inlineParsed;
            }
        }

        return resolved;
    }

    internal static long? ParseLinuxMemInfoAvailableBytes(string? memInfoContent)
    {
        if (string.IsNullOrWhiteSpace(memInfoContent))
        {
            return null;
        }

        using var reader = new StringReader(memInfoContent);
        while (reader.ReadLine() is { } line)
        {
            const string key = "MemAvailable:";
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ReadOnlySpan<char> remainder = line.AsSpan(key.Length).Trim();
            int spaceIndex = remainder.IndexOfAny(' ', '\t');
            ReadOnlySpan<char> numberSpan = spaceIndex >= 0 ? remainder[..spaceIndex] : remainder;
            if (long.TryParse(numberSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out long kiloBytes) &&
                kiloBytes >= 0 &&
                kiloBytes <= long.MaxValue / BytesPerKiB)
            {
                return kiloBytes * BytesPerKiB;
            }

            return null;
        }

        return null;
    }

    internal static long? ParseLinuxCgroupV2AvailableBytes(string? memoryMaxContent, string? memoryCurrentContent)
    {
        if (string.IsNullOrWhiteSpace(memoryMaxContent))
        {
            return null;
        }

        string trimmedMax = memoryMaxContent.Trim();
        if (string.Equals(trimmedMax, "max", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!long.TryParse(trimmedMax, NumberStyles.Integer, CultureInfo.InvariantCulture, out long maxBytes) ||
            maxBytes <= 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(memoryCurrentContent) &&
            long.TryParse(memoryCurrentContent.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long currentBytes) &&
            currentBytes >= 0)
        {
            return Math.Max(0L, maxBytes - currentBytes);
        }

        return maxBytes;
    }

    internal static long? ComputeEffectiveAvailableMemoryBytes(
        long? hostMemAvailableBytes,
        long? cgroupAvailableBytes,
        long gcTotalAvailableBytes)
    {
        long? bounded = null;

        if (hostMemAvailableBytes is >= 0)
        {
            bounded = hostMemAvailableBytes.Value;
        }

        if (cgroupAvailableBytes is >= 0)
        {
            bounded = bounded is { } current
                ? Math.Min(current, cgroupAvailableBytes.Value)
                : cgroupAvailableBytes.Value;
        }

        if (bounded is not null)
        {
            return bounded;
        }

        return gcTotalAvailableBytes > 0 ? gcTotalAvailableBytes : null;
    }

    internal static TestExecutionDecision Resolve(TestSuiteKind suiteKind, TestExecutionEnvironment environment)
    {
        int cores = Math.Max(1, environment.ProcessorCount);
        ExecutionProfileKind profile = ParseProfile(environment.GetEnvironmentVariable(ProfileEnvironmentVariable));
        long? effectiveBytes = ApplyContainerBudgetCap(
            suiteKind,
            environment.EffectiveAvailableMemoryBytes,
            environment.GetEnvironmentVariable(ContainerBudgetGiBEnvironmentVariable));
        double? effectiveGiB = effectiveBytes is { } bytes
            ? Math.Round(bytes / (double)BytesPerGiB, 2, MidpointRounding.AwayFromZero)
            : null;

        if (ParseCommandLineMaxParallelTests(environment.CommandLineArgs) is { } cliLimit)
        {
            return new TestExecutionDecision(
                cliLimit,
                profile,
                ParallelLimitSource.CommandLine,
                effectiveGiB,
                ShouldConfigureTUnitSettings: false);
        }

        if (TryParsePositiveInt(environment.GetEnvironmentVariable(TUnitMaxParallelTestsEnvironmentVariable), out int tunitEnvLimit))
        {
            return new TestExecutionDecision(
                tunitEnvLimit,
                profile,
                ParallelLimitSource.TUnitEnv,
                effectiveGiB,
                ShouldConfigureTUnitSettings: false);
        }

        if (TryParsePositiveInt(environment.GetEnvironmentVariable(RepoParallelLimitEnvironmentVariable), out int repoEnvLimit))
        {
            return new TestExecutionDecision(
                repoEnvLimit,
                profile,
                ParallelLimitSource.RepoEnv,
                effectiveGiB,
                ShouldConfigureTUnitSettings: true);
        }

        if (profile == ExecutionProfileKind.Constrained)
        {
            int limit = suiteKind == TestSuiteKind.UnitPure ? 2 : 1;
            return new TestExecutionDecision(
                limit,
                profile,
                ParallelLimitSource.Profile,
                effectiveGiB,
                ShouldConfigureTUnitSettings: true);
        }

        if (profile == ExecutionProfileKind.Performance)
        {
            int limit = suiteKind switch
            {
                TestSuiteKind.UnitPure => Math.Clamp(cores, 4, 16),
                TestSuiteKind.HostInProcess or TestSuiteKind.ContainerBacked => Math.Clamp(cores / 2, 4, 8),
                TestSuiteKind.DistributedAppHost => 1,
                _ => 1
            };

            return new TestExecutionDecision(
                limit,
                profile,
                ParallelLimitSource.Profile,
                effectiveGiB,
                ShouldConfigureTUnitSettings: true);
        }

        int adaptiveLimit = suiteKind switch
        {
            TestSuiteKind.UnitPure => effectiveGiB is null or < 10.0 ? 2 : Math.Clamp(cores / 2, 2, 6),
            TestSuiteKind.HostInProcess => effectiveGiB switch
            {
                null or < 18.0 => 1,
                < 40.0 => 2,
                _ => 3
            },
            TestSuiteKind.ContainerBacked => effectiveGiB switch
            {
                null or < 24.0 => 1,
                < 48.0 => 2,
                _ => 3
            },
            TestSuiteKind.DistributedAppHost => 1,
            _ => 1
        };

        return new TestExecutionDecision(
            adaptiveLimit,
            ExecutionProfileKind.Balanced,
            ParallelLimitSource.Adaptive,
            effectiveGiB,
            ShouldConfigureTUnitSettings: true);
    }

    internal static TestExecutionDecision ResolveFromCurrentEnvironment(TestSuiteKind suiteKind)
    {
        var environment = new TestExecutionEnvironment(
            ProcessorCount: Environment.ProcessorCount,
            EffectiveAvailableMemoryBytes: DetectEffectiveAvailableMemoryBytes(),
            CommandLineArgs: Environment.GetCommandLineArgs(),
            GetEnvironmentVariable: Environment.GetEnvironmentVariable);

        return Resolve(suiteKind, environment);
    }

    internal static string FormatStartupDiagnostic(
        string assemblyName,
        TestSuiteKind suiteKind,
        TestExecutionDecision decision)
    {
        string profileName = decision.Profile switch
        {
            ExecutionProfileKind.Constrained => "constrained",
            ExecutionProfileKind.Performance => "performance",
            _ => "balanced"
        };
        string sourceName = decision.Source switch
        {
            ParallelLimitSource.CommandLine => "cli",
            ParallelLimitSource.TUnitEnv => "env:tunit",
            ParallelLimitSource.RepoEnv => "env:repo",
            ParallelLimitSource.Profile => "profile",
            _ => "adaptive"
        };
        string memText = decision.EffectiveAvailableGiB is { } gib
            ? gib.ToString("0.##", CultureInfo.InvariantCulture)
            : "unknown";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"[TestExecutionProfile] Assembly={assemblyName} Suite={suiteKind} Profile={profileName} EffectiveLimit={decision.EffectiveLimit} Source={sourceName} MemAvailableGiB={memText}");
    }

    private static long? ApplyContainerBudgetCap(
        TestSuiteKind suiteKind,
        long? availableBytes,
        string? budgetGiBRaw)
    {
        if (suiteKind is not (TestSuiteKind.ContainerBacked or TestSuiteKind.HostInProcess))
        {
            return availableBytes;
        }

        if (string.IsNullOrWhiteSpace(budgetGiBRaw) ||
            !double.TryParse(budgetGiBRaw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double budgetGiB) ||
            double.IsNaN(budgetGiB) ||
            double.IsInfinity(budgetGiB) ||
            budgetGiB <= 0)
        {
            return availableBytes;
        }

        double cappedDouble = Math.Min(budgetGiB * BytesPerGiB, long.MaxValue);
        long budgetBytes = (long)cappedDouble;
        return availableBytes is { } detected
            ? Math.Min(detected, budgetBytes)
            : budgetBytes;
    }

    private static bool TryParsePositiveInt(string? raw, out int value)
    {
        if (!string.IsNullOrWhiteSpace(raw) &&
            int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
            parsed > 0)
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }

    private static long? DetectEffectiveAvailableMemoryBytes()
    {
        long? hostAvailable = null;
        long? cgroupAvailable = null;

        if (OperatingSystem.IsLinux())
        {
            hostAvailable = ParseLinuxMemInfoAvailableBytes(TryReadText("/proc/meminfo"));
            cgroupAvailable = DetectLinuxCgroupV2AvailableBytes();
        }

        long gcAvailable = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return ComputeEffectiveAvailableMemoryBytes(hostAvailable, cgroupAvailable, gcAvailable);
    }

    private static long? DetectLinuxCgroupV2AvailableBytes()
    {
        return ResolveMinimumCgroupV2AvailableBytes(
            ResolveLinuxCgroupV2Directory(),
            "/sys/fs/cgroup",
            TryReadText);
    }

    internal static long? ResolveMinimumCgroupV2AvailableBytes(
        string? startDirectory,
        string rootDirectory,
        Func<string, string?> readText)
    {
        string normalizedRoot = rootDirectory.Replace('\\', '/').TrimEnd('/');
        string current = string.IsNullOrWhiteSpace(startDirectory)
            ? normalizedRoot
            : startDirectory.Replace('\\', '/').TrimEnd('/');

        if (!current.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            current = normalizedRoot;
        }

        long? minimum = null;

        while (true)
        {
            long? scoped = ParseLinuxCgroupV2AvailableBytes(
                readText($"{current}/memory.max"),
                readText($"{current}/memory.current"));
            if (scoped is { } value)
            {
                minimum = minimum is { } existing ? Math.Min(existing, value) : value;
            }

            if (string.Equals(current, normalizedRoot, StringComparison.Ordinal))
            {
                break;
            }

            int lastSlash = current.LastIndexOf('/');
            if (lastSlash <= 0)
            {
                break;
            }

            current = current[..lastSlash];
            if (!current.StartsWith(normalizedRoot, StringComparison.Ordinal))
            {
                current = normalizedRoot;
            }
        }

        return minimum;
    }

    private static string? ResolveLinuxCgroupV2Directory()
    {
        string? procCgroup = TryReadText("/proc/self/cgroup");
        if (string.IsNullOrWhiteSpace(procCgroup))
        {
            return null;
        }

        using var reader = new StringReader(procCgroup);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("0::", StringComparison.Ordinal))
            {
                continue;
            }

            string relative = line[3..].Trim().TrimStart('/');
            if (string.IsNullOrEmpty(relative))
            {
                return "/sys/fs/cgroup";
            }

            string candidate = Path.Combine("/sys/fs/cgroup", relative);
            return Directory.Exists(candidate) ? candidate : null;
        }

        return null;
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
