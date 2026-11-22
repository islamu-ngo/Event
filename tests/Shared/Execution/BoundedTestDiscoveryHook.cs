using System.Reflection;
using TUnit.Core;

namespace Event.Testing;

/// <summary>
/// Applies memory-aware default test concurrency before TUnit discovery begins,
/// yielding automatically when CLI or TUNIT_MAX_PARALLEL_TESTS overrides are active.
/// </summary>
internal static class BoundedTestDiscoveryHook
{
    [Before(HookType.TestDiscovery)]
    public static void ConfigureParallelism(BeforeTestDiscoveryContext context)
    {
        var assembly = typeof(BoundedTestDiscoveryHook).Assembly;
        var suiteKindAttribute = assembly.GetCustomAttribute<TestSuiteKindAttribute>()
            ?? throw new InvalidOperationException(
                $"Test assembly '{assembly.GetName().Name}' must declare [assembly: TestSuiteKindAttribute(...)].");

        var decision = TestExecutionProfile.ResolveFromCurrentEnvironment(suiteKindAttribute.SuiteKind);
        if (decision.ShouldConfigureTUnitSettings)
        {
            context.Settings.Parallelism.MaximumParallelTests = decision.EffectiveLimit;
        }

        Console.Error.WriteLine(TestExecutionProfile.FormatStartupDiagnostic(
            assembly.GetName().Name ?? "UnknownTestAssembly",
            suiteKindAttribute.SuiteKind,
            decision));
    }
}
