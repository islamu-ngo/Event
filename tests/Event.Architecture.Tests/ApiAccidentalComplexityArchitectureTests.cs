using System.Reflection;
using Explore.Application;
using Explore.Application.Operations.Decorators;
using NetArchTest.Rules;

namespace Event.Architecture.Tests;

public sealed class ApiAccidentalComplexityArchitectureTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(ApplicationServicesRegistration).Assembly;

    [Test]
    [DisplayName("Authorization decorators must not depend on feature namespaces")]
    public async Task AuthorizationDecoratorsMustNotDependOnFeatureNamespaces()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespace("Explore.Application.Operations.Decorators")
            .And()
            .HaveNameStartingWith("Authorization")
            .ShouldNot()
            .HaveDependencyOn("Explore.Application.Features")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue()
            .Because("request-specific authorization belongs in closed generic enrichers");
    }
}
