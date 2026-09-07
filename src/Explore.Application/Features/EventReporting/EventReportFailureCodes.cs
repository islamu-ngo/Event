namespace Explore.Application.Features.EventReporting;

public static class EventReportFailureCodes
{
    public const string ValidationFailed = "event_report_validation_failed";
    public const string UserUnresolved = "event_report_user_unresolved";
    public const string ReporterActorUnresolved = "event_report_actor_unresolved";
    public const string TenantUnresolved = "event_report_tenant_unresolved";
    public const string IntakeDisabled = "event_reporting_intake_disabled";
    public const string EventNotFound = "event_report_event_not_found";
    public const string EventInvalidStatus = "event_report_event_invalid_status";
    public const string Duplicate = "event_report_duplicate";
    public const string ReportNotFound = "event_report_not_found";
    public const string CaseNotFound = "event_report_case_not_found";
    public const string EventMismatch = "event_report_event_mismatch";
    public const string CaseConcurrencyConflict = "event_report_case_concurrency_conflict";
    public const string CaseInvalidStatus = "event_report_case_invalid_status";
    public const string ReportInvalidStatus = "event_report_invalid_status";
    public const string ModeratorUnavailable = "event_report_moderator_unavailable";
    public const string AssigneeUnavailable = "event_report_assignee_unavailable";
    public const string AssignmentMismatch = "event_report_assignment_mismatch";
    public const string DuplicateGroupRequired = "event_report_duplicate_group_required";
    public const string DecisionNotFound = "event_report_decision_not_found";
    public const string DecisionInvalid = "event_report_decision_invalid";
    public const string DecisionExecutionFailed = "event_report_decision_execution_failed";
    public const string DecisionExecutionInProgress = "event_report_decision_execution_in_progress";
    public const string DecisionExecutionMissing = "event_report_decision_execution_missing";
    public const string DecisionExecutionInvalidState = "event_report_decision_execution_invalid_state";
    public const string DecisionEnforcementReceiptMissing = "event_report_decision_enforcement_receipt_missing";
    public const string DecisionEnforcementReceiptMismatch = "event_report_decision_enforcement_receipt_mismatch";
    public const string DecisionCompletionFailed = "event_report_decision_completion_failed";
    public const string DecisionOrganizerUnavailable = "event_report_decision_organizer_unavailable";
    public const string DecisionRecipientAuthorityChanged = "event_report_decision_recipient_authority_changed";
}
