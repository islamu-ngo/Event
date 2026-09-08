namespace Explore.Application.Contracts.Services;

public interface ICustomPropertyGovernancePolicy
{
    CustomPropertyGovernanceEvaluation EvaluateDefinition(string namespaceValue, string key, bool canManageReservedNamespaces = false);
}
