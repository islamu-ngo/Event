using Explore.Domain;

namespace Explore.Application.Contracts.Services;

public interface ICustomPropertyAutomationConditionPolicy
{
    CustomPropertyAutomationConditionEvaluation Evaluate(EventCustomPropertyDefinition definition);
}

public sealed record CustomPropertyAutomationConditionEvaluation(
    bool IsEligible,
    string NormalizedNamespace,
    string NormalizedKey,
    bool RequiresProjection,
    IReadOnlyList<string> Errors);
