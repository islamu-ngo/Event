namespace Explore.Application.Features.EmailDispatch;

public static class EmailDispatchFailureCodes
{
    public const string ValidationFailed = "email_dispatch_validation_failed";
    public const string NotFound = "email_dispatch_not_found";
    public const string InvalidTransition = "email_dispatch_invalid_transition";
    public const string ConcurrentTransition = "email_dispatch_concurrent_transition";
    public const string Misconfigured = "email_dispatch_misconfigured";
}
