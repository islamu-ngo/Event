namespace Explore.Application.Settings;

public readonly record struct ReportingIntakePolicyState(
    bool IntakeEnabled,
    bool RequireApproval,
    bool UserSubmissionEnabled,
    bool OrganizationSubmissionEnabled,
    bool GroupSubmissionEnabled);

public sealed record ReportingIntakePolicyEvaluation(bool Allowed, string ReasonCode, string Message);

public static class ReportingIntakePolicyReasonCodes
{
    public const string IntakeEnabled = "event_reporting_intake_enabled";
    public const string ProtectedByApproval = "event_reporting_intake_protected_by_approval";
    public const string ProtectedByClosedSubmissions = "event_reporting_intake_protected_by_closed_submissions";
    public const string UnsafePublicationPolicy = "event_reporting_intake_unsafe_publication_policy";
}

public static class ReportingIntakePolicyEvaluator
{
    public static ReportingIntakePolicyEvaluation Evaluate(ReportingIntakePolicyState state)
    {
        if (state.IntakeEnabled)
            return new(true, ReportingIntakePolicyReasonCodes.IntakeEnabled, "Reporting intake is enabled.");

        if (state.RequireApproval)
            return new(true, ReportingIntakePolicyReasonCodes.ProtectedByApproval, "Publication is protected by approval.");

        if (!state.UserSubmissionEnabled
            && !state.OrganizationSubmissionEnabled
            && !state.GroupSubmissionEnabled)
        {
            return new(true, ReportingIntakePolicyReasonCodes.ProtectedByClosedSubmissions, "All ordinary submission paths are closed.");
        }

        return new(false, ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy,
            "Reporting intake cannot be disabled while an ordinary submission path is open.");
    }
}
