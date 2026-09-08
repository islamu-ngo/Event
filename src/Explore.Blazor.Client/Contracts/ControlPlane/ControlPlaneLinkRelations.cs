namespace Explore.Blazor.Client.Contracts.ControlPlane;

public static class ControlPlaneLinkRelations
{
    public const string LocalIdentities = "local-identities";
    public const string CreateLocalIdentity = "create-local-identity";
    public const string IssueTemporaryCredential = "issue-temporary-credential";
    public const string Reconcile = "reconcile";
    public const string Self = "self";
    public const string Plans = "plans";
    public const string Settings = "settings";
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string Activate = "activate";
    public const string Suspend = "suspend";
    public const string Archive = "archive";
    public const string Reactivate = "reactivate";
    public const string SchedulePurge = "schedule-purge";
    public const string Verify = "verify";
    public const string Retry = "retry";
    public const string Test = "test";
    public const string DeploymentModeRunbook = "deployment-mode-runbook";
    public const string TransitionToMultiTenant = "transition-to-multi-tenant";
    public const string TransitionToSingleTenant = "transition-to-single-tenant";
    public const string Collection = "collection";
    public const string Publish = "publish";
    public const string CreateVersionDraft = "create-version-draft";
    public const string UpdateVersionDraft = "update-version-draft";
    public const string Validate = "validate";
    public const string PreviewDiff = "preview-diff";
    public const string Clone = "clone";
    public const string PlanAssignment = "plan-assignment";
    public const string Configuration = "configuration";
    public const string SwitchPlan = "switch-plan";
    public const string Apply = "apply";
    public const string Rollback = "rollback";
    public const string Override = "override";
    public const string Lock = "lock";
    public const string Unlock = "unlock";
    public const string ExportConfigurationOverrides = "export-configuration-overrides";
    public const string ExportConfigurationPortable = "export-configuration-portable";
    public const string CreateConfigurationImportSession =
        "create-configuration-import-session";
    public const string ExportTenantConfigurationPackage =
        "export-tenant-configuration-package";
    public const string ConfigurationImportHistory = "configuration-import-history";
    public const string ApplyConfigurationImport = "apply-configuration-import";
    public const string CreateConfigurationImportRollback =
        "create-configuration-import-rollback";
}
