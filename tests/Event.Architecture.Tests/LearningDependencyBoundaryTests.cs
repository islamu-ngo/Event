using System.Reflection;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Learning;
using Explore.Domain.Learning;
using ISLAMU.Wire.Contracts.Learning;

namespace Event.Architecture.Tests;

public sealed class LearningDependencyBoundaryTests
{
    [Test]
    public async Task LearningDomainAndWireRetainTheirDependencyDirection()
    {
        var domain = typeof(UserLearningState).Assembly.GetReferencedAssemblies().Select(value => value.Name).ToArray();
        var wire = typeof(LearningContextDto).Assembly.GetReferencedAssemblies().Select(value => value.Name).ToArray();
        await Assert.That(domain.Any(value => value is not null && (value.Contains("Infrastructure", StringComparison.Ordinal)
            || value.Contains("Persistence", StringComparison.Ordinal) || value.Contains("Application", StringComparison.Ordinal))))
            .IsFalse();
        await Assert.That(wire.Any(value => value is not null && value.StartsWith("Explore.", StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    public async Task ClosedLearningPreparationAcceptsOnlyItsFiveNativeIntents()
    {
        var methods = typeof(IHumanLearningMutationPreparation).GetMethods()
            .Where(method => method.Name == nameof(IHumanLearningMutationPreparation.ExecuteAsync)).ToArray();
        await Assert.That(methods.Length).IsEqualTo(5);
        await Assert.That(methods.SelectMany(method => method.GetParameters())
            .Any(parameter => typeof(Delegate).IsAssignableFrom(parameter.ParameterType))).IsFalse();
        await Assert.That(methods.Select(method => method.GetParameters()[0].ParameterType.Name))
            .IsEquivalentTo(new[]
            {
                "ClaimLearningTourCommand", "RecordLearningTourOutcomeCommand", "UpdateLearningPreferencesCommand",
                "ApplyLearningPracticeActionCommand", "ManageTenantLearningSpaceCommand"
            });
    }

    [Test]
    public async Task AuthoredCatalogueCannotCallProductionServicesOrEffects()
    {
        var calls = ApiCompiledBoundaryTests.EnumerateImplementationBodies(typeof(LearningLessonCatalog))
            .SelectMany(ApiCompiledBoundaryTests.ResolveCalls).Select(method => method.DeclaringType)
            .Where(type => type is not null).Cast<Type>().ToArray();
        var forbidden = calls.Where(type =>
            type.Namespace?.StartsWith("Explore.", StringComparison.Ordinal) == true
            && type.Namespace != typeof(LearningLessonCatalog).Namespace).Select(type => type.FullName).ToArray();
        await Assert.That(forbidden).IsEmpty();
    }

    [Test]
    public async Task LearningHandlersHaveNoProductionRepositoryOrProviderConstructor()
    {
        var handlers = typeof(LearningLessonCatalog).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.Namespace?.StartsWith(
                "Explore.Application.Features.Learning.Handlers.", StringComparison.Ordinal) == true).ToArray();
        await Assert.That(handlers.Length).IsEqualTo(7);
        var allowed = new[]
        {
            typeof(IHumanLearningMutationPreparation), typeof(IHumanLearningContextReader),
            typeof(Explore.Application.Contracts.Operations.IHumanCommitScopePreparation)
        };
        var forbidden = handlers.SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => !allowed.Contains(parameter.ParameterType))
            .Select(parameter => parameter.ParameterType.FullName).ToArray();
        await Assert.That(forbidden).IsEmpty();
    }

    [Test]
    public async Task PracticeInputCannotCarryProductionIdentityOrArbitraryPayload()
    {
        PropertyInfo[] fields = typeof(LearningApplyPracticeActionRequest).GetProperties();
        await Assert.That(fields.Select(field => field.Name)).IsEquivalentTo(new[]
        {
            "ExpectedScopeStamp", "ExpectedRevision", "IntentId", "ExpectedPracticeRevision", "PracticeId",
            "ContextKey", "RecipeKey", "MaterialVersion", "Action", "ChoiceKey"
        });
        await Assert.That(fields.Any(field => typeof(Delegate).IsAssignableFrom(field.PropertyType)
            || field.PropertyType == typeof(object))).IsFalse();
    }
}
