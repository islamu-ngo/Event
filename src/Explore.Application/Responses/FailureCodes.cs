namespace Explore.Application.Responses;

/// <summary>
/// Canonical failure codes for <see cref="BaseCommandResponse{TKey}.FailureCode"/>.
/// Null means success or a non-specific failure; these constants identify actionable failure conditions.
/// </summary>
public static class FailureCodes
{
    /// <summary>Requested resource does not exist.</summary>
    public const string NotFound = "not_found";

    /// <summary>Operation requires instance administrator authority.</summary>
    public const string AdminRequired = "admin_required";

    /// <summary>Operation requires an authenticated user context.</summary>
    public const string AuthenticationRequired = "authentication_required";

    /// <summary>Operation conflicts with a concurrent update.</summary>
    public const string ConcurrencyConflict = "concurrency_conflict";

    /// <summary>Tenant reporting provider overrides are locked by instance policy.</summary>
    public const string ReportingTenantOverridesLocked = "ReportingTenantOverridesLocked";

    public const string QuotaExceeded = "quota_exceeded";
    public const string StorageUploadTooLarge = "storage_upload_too_large";
    public const string StorageUploadSessionNotFound = "storage_upload_session_not_found";
    public const string StorageUploadSessionFinalized = "storage_upload_session_finalized";
    public const string StorageUploadSessionExpired = "storage_upload_session_expired";
    public const string StorageUploadSessionInvalidState = "storage_upload_session_invalid_state";
    public const string StorageUploadSizeMismatch = "storage_upload_size_mismatch";
    public const string StorageUploadContentTypeMismatch = "storage_upload_content_type_mismatch";
    public const string StorageUploadContentSignatureMismatch = "storage_upload_content_signature_mismatch";
    public const string StorageUploadWriteFailed = "storage_upload_write_failed";
    public const string AddressSelectionInvalid = "address_selection_invalid";

    /// <summary>
    /// Multi-Tenant → Single-Tenant mode switch blocked because more than one active tenant exists.
    /// The UI should direct the user to archive or suspend the extra tenants first.
    /// </summary>
    public const string DeploymentModeChangeBlockedByActiveTenants =
        "DeploymentModeChangeBlockedByActiveTenants";

    /// <summary>Scheduler control was requested on a host that runs with background scheduling disabled.</summary>
    public const string SchedulerUnavailable = "scheduler_unavailable";

    /// <summary>Scheduler control was requested while the host is configured as a read-only operator surface.</summary>
    public const string SchedulerReadOnly = "scheduler_read_only";

    /// <summary>A scheduler recovery action no longer applies: nothing was executing, or no trigger was in error.</summary>
    public const string SchedulerActionNotApplicable = "scheduler_action_not_applicable";

    /// <summary>An instance-wide scheduler action was attempted without its required typed confirmation.</summary>
    public const string SchedulerConfirmationRequired = "scheduler_confirmation_required";
}
