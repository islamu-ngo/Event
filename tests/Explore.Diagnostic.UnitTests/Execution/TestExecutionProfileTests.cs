using System.Globalization;
using Event.Testing;

namespace Explore.Diagnostic.UnitTests.Execution;

public sealed class TestExecutionProfileTests
{
    private const long GiB = 1024L * 1024L * 1024L;

    [Test]
    [Arguments(12, 1, 16)] // < 24 GiB -> 1
    [Arguments(23, 1, 16)]
    [Arguments(24, 2, 16)] // 24-48 GiB -> 2
    [Arguments(47, 2, 16)]
    [Arguments(48, 3, 16)] // >= 48 GiB -> 3
    public async Task Resolve_BalancedContainerBacked_AppliesConservativeMemoryTiers(
        long availableGiB,
        int expectedLimit,
        int processorCount)
    {
        var env = CreateEnvironment(processorCount: processorCount, availableMemoryBytes: availableGiB * GiB);

        var decision = TestExecutionProfile.Resolve(TestSuiteKind.ContainerBacked, env);

        await Assert.That(decision.EffectiveLimit).IsEqualTo(expectedLimit);
        await Assert.That(decision.Profile).IsEqualTo(ExecutionProfileKind.Balanced);
        await Assert.That(decision.Source).IsEqualTo(ParallelLimitSource.Adaptive);
        await Assert.That(decision.ShouldConfigureTUnitSettings).IsTrue();
    }

    [Test]
    [Arguments(12, 1)] // < 18 GiB -> 1
    [Arguments(18, 2)] // 18-40 GiB -> 2
    [Arguments(39, 2)]
    [Arguments(40, 3)] // >= 40 GiB -> 3
    public async Task Resolve_BalancedHostInProcess_AppliesConservativeMemoryTiers(
        long availableGiB,
        int expectedLimit)
    {
        var env = CreateEnvironment(processorCount: 16, availableMemoryBytes: availableGiB * GiB);

        var decision = TestExecutionProfile.Resolve(TestSuiteKind.HostInProcess, env);

        await Assert.That(decision.EffectiveLimit).IsEqualTo(expectedLimit);
        await Assert.That(decision.Source).IsEqualTo(ParallelLimitSource.Adaptive);
    }

    [Test]
    [Arguments(8, 16, 2)]  // < 10 GiB -> 2
    [Arguments(32, 16, 6)] // >= 10 GiB -> Clamp(16/2, 2, 6) = 6
    [Arguments(32, 8, 4)]  // >= 10 GiB -> Clamp(8/2, 2, 6) = 4
    [Arguments(32, 2, 2)]  // >= 10 GiB -> Clamp(2/2, 2, 6) = 2
    public async Task Resolve_BalancedUnitPure_RemainsBoundedAndFast(
        long availableGiB,
        int processorCount,
        int expectedLimit)
    {
        var env = CreateEnvironment(processorCount: processorCount, availableMemoryBytes: availableGiB * GiB);

        var decision = TestExecutionProfile.Resolve(TestSuiteKind.UnitPure, env);

        await Assert.That(decision.EffectiveLimit).IsEqualTo(expectedLimit);
    }

    [Test]
    public async Task Resolve_DistributedAppHost_DefaultsToOneEvenOnHighMemoryHosts()
    {
        var balanced = TestExecutionProfile.Resolve(
            TestSuiteKind.DistributedAppHost,
            CreateEnvironment(processorCount: 32, availableMemoryBytes: 128 * GiB));
        var performance = TestExecutionProfile.Resolve(
            TestSuiteKind.DistributedAppHost,
            CreateEnvironment(
                processorCount: 32,
                availableMemoryBytes: 128 * GiB,
                envVars: new Dictionary<string, string?>
                {
                    [TestExecutionProfile.ProfileEnvironmentVariable] = "performance"
                }));

        await Assert.That(balanced.EffectiveLimit).IsEqualTo(1);
        await Assert.That(performance.EffectiveLimit).IsEqualTo(1);
    }

    [Test]
    public async Task Resolve_ConstrainedProfile_ForcesOneForIntegrationAndTwoForUnitPure()
    {
        var env = CreateEnvironment(
            processorCount: 16,
            availableMemoryBytes: 64 * GiB,
            envVars: new Dictionary<string, string?>
            {
                [TestExecutionProfile.ProfileEnvironmentVariable] = "constrained"
            });

        var containerDecision = TestExecutionProfile.Resolve(TestSuiteKind.ContainerBacked, env);
        var hostDecision = TestExecutionProfile.Resolve(TestSuiteKind.HostInProcess, env);
        var unitDecision = TestExecutionProfile.Resolve(TestSuiteKind.UnitPure, env);

        await Assert.That(containerDecision.EffectiveLimit).IsEqualTo(1);
        await Assert.That(hostDecision.EffectiveLimit).IsEqualTo(1);
        await Assert.That(unitDecision.EffectiveLimit).IsEqualTo(2);
        await Assert.That(containerDecision.Profile).IsEqualTo(ExecutionProfileKind.Constrained);
        await Assert.That(containerDecision.Source).IsEqualTo(ParallelLimitSource.Profile);
    }

    [Test]
    public async Task Resolve_PerformanceProfile_ScalesContainerAndUnitSlots()
    {
        var env = CreateEnvironment(
            processorCount: 16,
            availableMemoryBytes: 64 * GiB,
            envVars: new Dictionary<string, string?>
            {
                [TestExecutionProfile.ProfileEnvironmentVariable] = "performance"
            });

        var containerDecision = TestExecutionProfile.Resolve(TestSuiteKind.ContainerBacked, env);
        var unitDecision = TestExecutionProfile.Resolve(TestSuiteKind.UnitPure, env);

        await Assert.That(containerDecision.EffectiveLimit).IsEqualTo(8);
        await Assert.That(unitDecision.EffectiveLimit).IsEqualTo(16);
        await Assert.That(containerDecision.Profile).IsEqualTo(ExecutionProfileKind.Performance);
    }

    [Test]
    public async Task Resolve_EnforcesStrictOverridePrecedence_CliBeatsTUnitEnvBeatsRepoEnvBeatsProfile()
    {
        var envVars = new Dictionary<string, string?>
        {
            [TestExecutionProfile.ProfileEnvironmentVariable] = "constrained",
            [TestExecutionProfile.RepoParallelLimitEnvironmentVariable] = "4",
            [TestExecutionProfile.TUnitMaxParallelTestsEnvironmentVariable] = "6"
        };

        var cliWins = TestExecutionProfile.Resolve(
            TestSuiteKind.ContainerBacked,
            CreateEnvironment(16, 12 * GiB, ["--maximum-parallel-tests", "10"], envVars));
        await Assert.That(cliWins.EffectiveLimit).IsEqualTo(10);
        await Assert.That(cliWins.Source).IsEqualTo(ParallelLimitSource.CommandLine);
        await Assert.That(cliWins.ShouldConfigureTUnitSettings).IsFalse();

        var cliEqualsWins = TestExecutionProfile.Resolve(
            TestSuiteKind.ContainerBacked,
            CreateEnvironment(16, 12 * GiB, ["--maximum-parallel-tests=9"], envVars));
        await Assert.That(cliEqualsWins.EffectiveLimit).IsEqualTo(9);
        await Assert.That(cliEqualsWins.Source).IsEqualTo(ParallelLimitSource.CommandLine);
        await Assert.That(cliEqualsWins.ShouldConfigureTUnitSettings).IsFalse();

        var tunitEnvWins = TestExecutionProfile.Resolve(
            TestSuiteKind.ContainerBacked,
            CreateEnvironment(16, 12 * GiB, [], envVars));
        await Assert.That(tunitEnvWins.EffectiveLimit).IsEqualTo(6);
        await Assert.That(tunitEnvWins.Source).IsEqualTo(ParallelLimitSource.TUnitEnv);
        await Assert.That(tunitEnvWins.ShouldConfigureTUnitSettings).IsFalse();

        envVars.Remove(TestExecutionProfile.TUnitMaxParallelTestsEnvironmentVariable);
        var repoEnvWins = TestExecutionProfile.Resolve(
            TestSuiteKind.ContainerBacked,
            CreateEnvironment(16, 12 * GiB, [], envVars));
        await Assert.That(repoEnvWins.EffectiveLimit).IsEqualTo(4);
        await Assert.That(repoEnvWins.Source).IsEqualTo(ParallelLimitSource.RepoEnv);
        await Assert.That(repoEnvWins.ShouldConfigureTUnitSettings).IsTrue();
    }

    [Test]
    public async Task Resolve_InvalidEnvironmentValues_FailSafeToBalancedAdaptiveDefaults()
    {
        var env = CreateEnvironment(
            processorCount: 16,
            availableMemoryBytes: 12 * GiB,
            args: ["--maximum-parallel-tests", "0"],
            envVars: new Dictionary<string, string?>
            {
                [TestExecutionProfile.ProfileEnvironmentVariable] = "invalid-profile",
                [TestExecutionProfile.RepoParallelLimitEnvironmentVariable] = "-3",
                [TestExecutionProfile.TUnitMaxParallelTestsEnvironmentVariable] = "not-a-number"
            });

        var decision = TestExecutionProfile.Resolve(TestSuiteKind.ContainerBacked, env);

        await Assert.That(decision.Profile).IsEqualTo(ExecutionProfileKind.Balanced);
        await Assert.That(decision.EffectiveLimit).IsEqualTo(1);
        await Assert.That(decision.Source).IsEqualTo(ParallelLimitSource.Adaptive);
    }

    [Test]
    public async Task Resolve_ContainerBudgetEnv_CapsContainerAndHostMemoryDetection()
    {
        var env = CreateEnvironment(
            processorCount: 16,
            availableMemoryBytes: 64 * GiB,
            envVars: new Dictionary<string, string?>
            {
                [TestExecutionProfile.ContainerBudgetGiBEnvironmentVariable] = "8"
            });

        var containerDecision = TestExecutionProfile.Resolve(TestSuiteKind.ContainerBacked, env);
        var unitDecision = TestExecutionProfile.Resolve(TestSuiteKind.UnitPure, env);

        await Assert.That(containerDecision.EffectiveLimit).IsEqualTo(1);
        await Assert.That(containerDecision.EffectiveAvailableGiB).IsEqualTo(8.0);
        await Assert.That(unitDecision.EffectiveLimit).IsEqualTo(6);
    }

    [Test]
    public async Task ParseLinuxMemorySignals_ClampsToTighterCgroupV2RemainingBytesAndFallsBackCleanly()
    {
        const string memInfo = """
            MemTotal:       65874968 kB
            MemFree:         2048000 kB
            MemAvailable:   33554432 kB
            """;

        long? hostBytes = TestExecutionProfile.ParseLinuxMemInfoAvailableBytes(memInfo);
        await Assert.That(hostBytes).IsEqualTo(32L * GiB);

        long? cgroupBytes = TestExecutionProfile.ParseLinuxCgroupV2AvailableBytes(
            (16L * GiB).ToString(CultureInfo.InvariantCulture),
            (4L * GiB).ToString(CultureInfo.InvariantCulture));
        await Assert.That(cgroupBytes).IsEqualTo(12L * GiB);

        long? clamped = TestExecutionProfile.ComputeEffectiveAvailableMemoryBytes(
            hostBytes,
            cgroupBytes,
            64L * GiB);
        await Assert.That(clamped).IsEqualTo(12L * GiB);

        long? maxUnbounded = TestExecutionProfile.ParseLinuxCgroupV2AvailableBytes("max", "1024");
        await Assert.That(maxUnbounded).IsNull();

        long? nonLinuxFallback = TestExecutionProfile.ComputeEffectiveAvailableMemoryBytes(
            null,
            null,
            16L * GiB);
        await Assert.That(nonLinuxFallback).IsEqualTo(16L * GiB);
    }

    [Test]
    public async Task Resolve_UnknownAvailableMemory_ConservativeFallbackPreventsOom()
    {
        var env = CreateEnvironment(processorCount: 16, availableMemoryBytes: null);

        var containerDecision = TestExecutionProfile.Resolve(TestSuiteKind.ContainerBacked, env);
        var hostDecision = TestExecutionProfile.Resolve(TestSuiteKind.HostInProcess, env);
        var unitDecision = TestExecutionProfile.Resolve(TestSuiteKind.UnitPure, env);

        await Assert.That(containerDecision.EffectiveLimit).IsEqualTo(1);
        await Assert.That(hostDecision.EffectiveLimit).IsEqualTo(1);
        await Assert.That(unitDecision.EffectiveLimit).IsEqualTo(2);
    }

    private static TestExecutionEnvironment CreateEnvironment(
        int processorCount,
        long? availableMemoryBytes,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string?>? envVars = null)
    {
        return new TestExecutionEnvironment(
            ProcessorCount: processorCount,
            EffectiveAvailableMemoryBytes: availableMemoryBytes,
            CommandLineArgs: args ?? [],
            GetEnvironmentVariable: key => envVars is not null && envVars.TryGetValue(key, out var val) ? val : null);
    }
}
