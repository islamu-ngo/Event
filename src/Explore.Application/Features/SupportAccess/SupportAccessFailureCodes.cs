namespace Explore.Application.Features.SupportAccess;

public static class SupportAccessFailureCodes
{
    public const string ValidationFailed = "support_access_validation_failed";
    public const string Disabled = "support_access_disabled";
    public const string WriteModeDisabled = "support_access_write_mode_disabled";
    public const string DurationExceedsPolicy = "support_access_duration_exceeds_policy";
    public const string TicketReferenceRequired = "support_access_ticket_reference_required";
    public const string ActorNotResolved = "support_access_actor_not_resolved";
    public const string TargetTenantNotFound = "support_access_target_tenant_not_found";
    public const string TargetTenantUserMismatch = "support_access_target_tenant_user_mismatch";
    public const string ActiveSessionExists = "support_access_active_session_exists";
    public const string SessionNotFound = "support_access_session_not_found";
    public const string SessionNotActive = "support_access_session_not_active";
    public const string ConcurrencyConflict = "support_access_concurrency_conflict";
}
