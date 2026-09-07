using System.Reflection;
using Explore.Application.Behaviors;
using NetArchTest.Rules;

namespace Event.Architecture.Tests;

public sealed class ApiAccidentalComplexityArchitectureTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(AuthorizationBehavior<,>).Assembly;

    [Test]
    [DisplayName("AuthorizationBehavior must not depend on feature namespaces")]
    public async Task AuthorizationBehaviorMustNotDependOnFeatureNamespaces()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveName("AuthorizationBehavior`2")
            .ShouldNot()
            .HaveDependencyOn("Explore.Application.Features")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue()
            .Because("request-specific authorization belongs in closed generic enrichers");
    }
}
