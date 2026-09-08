// ABOUTME: Maps Local lifecycle command failures to the native RFC 7807 HTTP conventions.
// ABOUTME: Keeps invalid operation, stale synchronization, authentication, and global availability outcomes explicit.

using Explore.Application.Responses;

namespace Explore.API.ExceptionHandling;

internal static class LocalIdentityLifecycleFailurePolicy
{
    internal static readonly CommandFailurePolicy Instance = CommandFailurePolicy
        .ValidatedBy(new ApiValidationProblemDescriptor(
            ErrorKey: "request", Title: "Local identity operation failed",
            FallbackDetail: "The Local identity operation could not be completed."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Conflict(title: "Local identity operation conflict", fallbackDetail: "The Local identity operation changed.",
            failureCodes: [FailureCodes.ConcurrencyConflict])
        .Unavailable(title: "Local identity operation unavailable", fallbackDetail: "Local email delivery is unavailable.",
            failureCodes: ["local_lifecycle_unavailable"]);
}
