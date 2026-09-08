namespace Event.Architecture.Tests;

using System.Reflection;
using Explore.Application.Specifications.Events;
using Explore.Application.Specifications.EventSessions;
using NetArchTest.Rules;

public class ProjectionLayerBoundaryTests
{
    private static readonly Assembly DomainAssembly = typeof(Explore.Domain.Event).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Explore.Application.ApplicationServicesRegistration).Assembly;

    [Test]
    public async Task Domain_ShouldNotReference_ProjectionFilterSpecifications()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Explore.Application.Specifications.Events")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Domain_ShouldNotReference_ProjectionFilterDtos()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Explore.Application.DTOs.CustomPropertyProjection")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Domain_ShouldNotReference_SessionProjectionFilterSpecifications()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Explore.Application.Specifications.EventSessions")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task ProjectionFilterType_ShouldExist_InApplicationLayer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameStartingWith("EventCustomPropertyProjectionFilter")
            .Should()
            .ResideInNamespace("Explore.Application.Specifications.Events")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task SessionProjectionFilterType_ShouldExist_InApplicationLayer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameStartingWith("EventSessionCustomPropertyProjectionFilter")
            .Should()
            .ResideInNamespace("Explore.Application.Specifications.EventSessions")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task CustomPropertyFilterCriterion_ShouldExist_InApplicationLayer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameStartingWith("CustomPropertyFilter")
            .Should()
            .ResideInNamespace("Explore.Application.DTOs.CustomPropertyProjection")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task EventQuerySpecification_ShouldNotLeak_ToInfrastructure()
    {
        var infrastructureAssembly = typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly;

        var result = Types.InAssembly(infrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn("Explore.Application.Specifications.Events")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task EventQuerySpecification_ShouldCompose_Layer2Filters_SeparatelyFromLayer3ProjectionFilters()
    {
        var andParameterTypes = typeof(EventQuerySpecification)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == nameof(EventQuerySpecification.And))
            .Select(method => method.GetParameters())
            .Where(parameters => parameters.Length == 1)
            .Select(parameters => parameters[0].ParameterType)
            .ToHashSet();

        var missingOverloads = new List<string>();

        AddMissingOverload<IslamicAspectFilter>(andParameterTypes, missingOverloads);
        AddMissingOverload<TechAspectFilter>(andParameterTypes, missingOverloads);
        AddMissingOverload<AspectPresenceFilter>(andParameterTypes, missingOverloads);
        AddMissingOverload<EventCustomPropertyProjectionFilter>(andParameterTypes, missingOverloads);

        await Assert.That(missingOverloads).IsEmpty()
            .Because("Layer 2 typed aspect filters must compose directly instead of being routed through Layer 3 projection filters.");
    }

    [Test]
    public async Task Layer3ProjectionFilters_ShouldNotExpose_Layer2SemanticFactories()
    {
        string[] forbiddenLayer2Terms = ["Islamic", "Madhab", "Gender", "Prayer", "Tech", "Skill", "Aspect"];
        var violations = new List<string>();

        AddLayer2SemanticFactoryViolations<EventCustomPropertyProjectionFilter>(forbiddenLayer2Terms, violations);
        AddLayer2SemanticFactoryViolations<EventSessionCustomPropertyProjectionFilter>(forbiddenLayer2Terms, violations);

        await Assert.That(violations).IsEmpty()
            .Because("Layer 3 projection filters must stay generic; sector-standard semantics belong in typed Layer 2 filters or schema.");
    }

    private static void AddMissingOverload<TFilter>(HashSet<Type> parameterTypes, List<string> missingOverloads)
    {
        if (!parameterTypes.Contains(typeof(TFilter)))
        {
            missingOverloads.Add($"Missing EventQuerySpecification.And({typeof(TFilter).Name}) overload");
        }
    }

    private static void AddLayer2SemanticFactoryViolations<TProjectionFilter>(
        IReadOnlyList<string> forbiddenLayer2Terms,
        List<string> violations)
    {
        var factoryNames = typeof(TProjectionFilter)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name);

        foreach (var factoryName in factoryNames)
        {
            foreach (var term in forbiddenLayer2Terms)
            {
                if (factoryName.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{typeof(TProjectionFilter).Name}.{factoryName} contains Layer 2 semantic term '{term}'");
                }
            }
        }
    }
}
