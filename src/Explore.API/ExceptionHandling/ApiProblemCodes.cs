namespace Explore.API.ExceptionHandling;

internal static class ApiProblemCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string TenantRequired = "tenant_required";
    public const string AuthenticationRequired = "authentication_required";
    public const string Forbidden = "forbidden";
    public const string ResourceNotFound = "resource_not_found";
    public const string ResourceConflict = "resource_conflict";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string DuplicateRequest = "duplicate_request";
    public const string RateLimited = "rate_limited";
    public const string AnalyticsRelayRejected = "analytics_relay_rejected";
    public const string AuthorizationPolicyPackageUnavailable = "authorization_policy_package_unavailable";
    public const string AdmissionCheckInUnavailable = "admission_check_in_unavailable";
    public const string AuthorizationProviderUnavailable = "authorization_provider_unavailable";
    public const string SetupAlreadyCompleted = "setup_already_completed";
    public const string ProviderGateway = "provider_gateway";
    public const string UnexpectedError = "unexpected_error";
}
