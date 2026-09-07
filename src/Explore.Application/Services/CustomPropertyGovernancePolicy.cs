using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public sealed class CustomPropertyGovernancePolicy : ICustomPropertyGovernancePolicy
{
    public CustomPropertyGovernanceEvaluation EvaluateDefinition(string namespaceValue, string key, bool canManageReservedNamespaces = false)
    {
        var normalizedNamespace = CustomPropertyIdentity.NormalizeNamespace(namespaceValue);
        var normalizedKey = CustomPropertyIdentity.NormalizeKey(key);
        var errors = new List<string>();

        if (!CustomPropertyNamespaces.IsTenantOwned(normalizedNamespace) && !CustomPropertyNamespaces.IsReserved(normalizedNamespace))
        {
            errors.Add("Namespace must use a supported root: tenant, platform, sector, or pack.");
        }

        if (CustomPropertyNamespaces.IsReserved(normalizedNamespace) && !canManageReservedNamespaces)
        {
            errors.Add("Reserved namespaces require a privileged governance workflow.");
        }

        if (CustomPropertySemanticReservations.IsReservedLayer2Semantic(normalizedNamespace, normalizedKey))
        {
            errors.Add("Layer 3 custom properties cannot redefine reserved Layer 2 semantics.");
        }

        return new CustomPropertyGovernanceEvaluation
        {
            NormalizedNamespace = normalizedNamespace,
            NormalizedKey = normalizedKey,
            Errors = errors,
        };
    }
}
