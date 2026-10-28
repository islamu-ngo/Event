using System.Reflection;
using Explore.Infrastructure;
using Explore.Infrastructure.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
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
    [Arguments("TolgeeClient")]
    [Arguments("WeblateClient")]
    public async Task TmsClientsHaveOneResiliencePipelineAndOneResponseBoundary(string clientName)
    {
        var services = new ServiceCollection();
        services.ConfigureInfrastructureServices(new ConfigurationBuilder().Build(), registerBackgroundWorkers: false);
        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        var policyHandlers = new List<Type>();
        while (handler is DelegatingHandler delegating)
        {
            if (handler is ResilienceHandler || handler.GetType().Assembly == InfrastructureAssembly)
                policyHandlers.Add(handler.GetType());
            handler = delegating.InnerHandler!;
        }

        await Assert.That(policyHandlers).IsEquivalentTo([typeof(ResilienceHandler), typeof(TmsResponseBoundaryHandler)])
            .Because("Each TMS client needs exactly one retry pipeline and one bounded response reader, not layered retries");
        await Assert.That(policyHandlers[0]).IsEqualTo(typeof(ResilienceHandler))
            .Because("The response boundary must run before retry metadata readers consume the response");
    }
}
