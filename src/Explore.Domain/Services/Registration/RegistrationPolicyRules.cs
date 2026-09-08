using Explore.Domain.Enums;

namespace Explore.Domain.Services.Registration;

public static class RegistrationPolicyRules
{
    /// <summary>
    /// Returns true when the supplied registration scope is permitted by the supplied organizer policy.
    /// A null policy id is treated as <see cref="EventRegistrationPolicyEnum.Flexible"/> so events that were
    /// created before the policy field landed still accept every scope during rollout.
    /// </summary>
    public static bool IsScopeAllowed(int? policyId, int scopeId)
    {
        var policy = policyId.HasValue
            ? (EventRegistrationPolicyEnum)policyId.Value
            : EventRegistrationPolicyEnum.Flexible;

        var scope = (RegistrationScopeEnum)scopeId;

        return policy switch
        {
            EventRegistrationPolicyEnum.WholeEventOnly => scope == RegistrationScopeEnum.Event,
            EventRegistrationPolicyEnum.WholeDayOnly => scope == RegistrationScopeEnum.Day,
            EventRegistrationPolicyEnum.SessionSelectionOnly => scope == RegistrationScopeEnum.SessionSelection,
            EventRegistrationPolicyEnum.WholeEventOrDay => scope is RegistrationScopeEnum.Event or RegistrationScopeEnum.Day,
            EventRegistrationPolicyEnum.WholeEventOrSession => scope is RegistrationScopeEnum.Event or RegistrationScopeEnum.SessionSelection,
            EventRegistrationPolicyEnum.Flexible => true,
            _ => false
        };
    }

    public static ApprovalStatusEnum? ResolveInitialApprovalStatus(IEnumerable<int?> registrationModeIds)
    {
        var resolved = ApprovalStatusEnum.Approved;
        var hasSession = false;

        foreach (var registrationModeId in registrationModeIds)
        {
            hasSession = true;
            switch (registrationModeId)
            {
                case (int)RegistrationModeEnum.Open:
                    break;
                case (int)RegistrationModeEnum.ApprovalRequired:
                    resolved = ApprovalStatusEnum.Pending;
                    break;
                default:
                    return null;
            }
        }

        return hasSession ? resolved : null;
    }
}
