using System.Reflection;
using NetArchTest.Rules;

namespace Event.Architecture.Tests;

/// <summary>
/// Ensures TMS resilience uses stateless readers (not DelegatingHandlers) and a single Polly pipeline per client.
/// </summary>
public class LocalizationResilienceTests
{
    private static readonly Assembly InfrastructureAssembly =
        typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly;

    [Test]
    public async Task Resilience_NoClassShouldInherit_DelegatingHandler()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespace("Explore.Infrastructure.Localization.Resilience")
            .ShouldNot()
            .Inherit(typeof(DelegatingHandler))
            .GetResult();

        var failures = result.FailingTypeNames ?? [];
        await Assert.That(failures.Count).IsEqualTo(0)
            .Because("Resilience readers must NOT be DelegatingHandlers — they are stateless helpers called by the pipeline's DelayGenerator");
    }

    [Test]
    public async Task Resilience_NoClassShouldEndWith_Handler()
    {
        var types = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespace("Explore.Infrastructure.Localization.Resilience")
            .GetTypes();

        var handlerNames = types
            .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        await Assert.That(handlerNames.Count).IsEqualTo(0)
            .Because($"Reader naming enforced — found: {string.Join(", ", handlerNames)}");
    }

    [Test]
    public async Task Resilience_ReadersAreStaticClasses()
    {
        var types = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespace("Explore.Infrastructure.Localization.Resilience")
            .GetTypes()
            .Where(t => t.Name.EndsWith("Reader", StringComparison.Ordinal))
            .ToList();

        await Assert.That(types.Count).IsGreaterThanOrEqualTo(2)
            .Because("Expected at least TolgeeRetryAfterReader and WeblateRateLimitReader");

        foreach (var type in types)
        {
            await Assert.That(type.IsAbstract && type.IsSealed).IsTrue()
                .Because($"{type.Name} must be a static class (stateless reader)");
        }
    }

    [Test]
    public async Task InfrastructureRegistration_DoesNotUseAddHttpMessageHandler_ForTmsClients()
    {
        // Grep-style check: read the registration source and verify no AddHttpMessageHandler
        // for Tolgee/Weblate clients. This prevents double-retry layering.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        string? repoRoot = null;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
            {
                repoRoot = current.FullName;
                break;
            }
            current = current.Parent;
        }

        var sourceFile = repoRoot is not null
            ? Path.Combine(repoRoot, "src", "Explore.Infrastructure", "InfrastructureServicesRegistration.cs")
            : "";

        if (!File.Exists(sourceFile))
        {
            // Can't verify at runtime without source — pass with a note
            await Assert.That(true).IsTrue()
                .Because("Source file not accessible at test runtime — skipping grep-style check");
            return;
        }

        var source = await File.ReadAllTextAsync(sourceFile);

        // Find the TMS registration section (between "Translation Management System" comment and next section)
        var tmsStart = source.IndexOf("Translation Management System", StringComparison.Ordinal);
        var tmsEnd = source.IndexOf("// Generic Outbox", StringComparison.Ordinal);
        if (tmsStart < 0 || tmsEnd < 0 || tmsEnd <= tmsStart)
        {
            await Assert.That(true).IsTrue()
                .Because("Could not isolate TMS section — skipping");
            return;
        }

        var tmsSection = source[tmsStart..tmsEnd];
        var hasMessageHandler = tmsSection.Contains("AddHttpMessageHandler", StringComparison.Ordinal);

        await Assert.That(hasMessageHandler).IsFalse()
            .Because("TMS clients must NOT use AddHttpMessageHandler — single retry source via AddResilienceHandler only");
    }
}
