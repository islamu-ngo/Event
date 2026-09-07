// ABOUTME: Pure delivery-state and credential-ownership rules for instance and tenant SMTP.
// ABOUTME: Prevents SMTP capability from granting authentication or cross-scope secret authority.

using Explore.Domain.Enums;

namespace Explore.Domain.Services;

public static class EmailDeliveryPolicy
{
    public static bool ShouldSuppressOptional(EmailDeliveryState state, bool honorsPreference,
        long occurrenceRevision, long? suppressedThroughRevision) =>
        honorsPreference && (state != EmailDeliveryState.Available
            || suppressedThroughRevision is { } cutoff && occurrenceRevision <= cutoff);

    public static EmailDeliveryState Evaluate(
        bool enabled,
        bool hasHost,
        bool hasSender,
        bool configurationValid,
        bool authorityAvailable)
    {
        if (!enabled)
            return EmailDeliveryState.Disabled;
        if (!hasHost && !hasSender)
            return EmailDeliveryState.Unconfigured;
        if (!hasHost || !hasSender || !configurationValid)
            return EmailDeliveryState.Misconfigured;
        return authorityAvailable ? EmailDeliveryState.Available : EmailDeliveryState.Degraded;
    }

    public static bool CanUseCredential(Guid? transportTenantId, SecretScope credentialScope, Guid? credentialScopeId) =>
        transportTenantId is { } tenantId
            ? tenantId != Guid.Empty && credentialScope == SecretScope.Tenant && credentialScopeId == tenantId
            : credentialScope == SecretScope.Instance && credentialScopeId is null;
}
