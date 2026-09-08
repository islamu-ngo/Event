using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class CustomPropertyAutomationConditionPolicy : ICustomPropertyAutomationConditionPolicy
{
    private static readonly HashSet<string> WorkflowCriticalKeys = new(StringComparer.Ordinal)
    {
        "dispatch_status",
        "email_dispatch_status",
        "automation_execution_status",
        "registration_lifecycle_state",
        "registration_status",
        "delivery_attempt_state",
        "tenant_pause_state",
        "email_dispatch_replay_state",
        "email_dispatch_parking_state",
        "idempotency_key",
        "dedup_key",
    };

    private static readonly HashSet<PropertyType> SupportedConditionTypes =
    [
        PropertyType.Text,
        PropertyType.Number,
        PropertyType.Option,
        PropertyType.Boolean,
        PropertyType.DateTime,
    ];

    public CustomPropertyAutomationConditionEvaluation Evaluate(EventCustomPropertyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var normalizedNamespace = CustomPropertyIdentity.NormalizeNamespace(definition.Namespace);
        var normalizedKey = CustomPropertyIdentity.NormalizeKey(definition.Key);
        var errors = new List<string>();

        if (!CustomPropertyNamespaces.IsTenantOwned(normalizedNamespace))
        {
            errors.Add("Automation condition custom properties must use tenant-owned namespaces.");
        }

        if (definition.IsSystemOwned)
        {
            errors.Add("System-owned custom properties cannot drive tenant automation conditions.");
        }

        if (!definition.IsActive)
        {
            errors.Add("Inactive custom properties cannot drive automation conditions.");
        }

        if (!definition.IsFilterable)
        {
            errors.Add("Automation condition custom properties must be filterable so projection rows can back evaluation.");
        }

        if (!SupportedConditionTypes.Contains(definition.PropertyType))
        {
            errors.Add("Custom property type is not supported for automation conditions.");
        }

        if (WorkflowCriticalKeys.Contains(normalizedKey)
            || CustomPropertySemanticReservations.IsReservedLayer2Semantic(normalizedNamespace, normalizedKey))
        {
            errors.Add("Workflow-critical state must use explicit entities or aspects, not custom properties.");
        }

        return new CustomPropertyAutomationConditionEvaluation(
            IsEligible: errors.Count == 0,
            NormalizedNamespace: normalizedNamespace,
            NormalizedKey: normalizedKey,
            RequiresProjection: true,
            Errors: errors);
    }
}
