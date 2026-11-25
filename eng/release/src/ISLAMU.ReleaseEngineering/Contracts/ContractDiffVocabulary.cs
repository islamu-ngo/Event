namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Canonical contract family identifiers emitted into <c>contract-diff.v1.json</c>.
/// </summary>
internal static class ContractFamilies
{
    public const string OpenApi = "openapi";
    public const string Configuration = "configuration";
    public const string Migrations = "migrations";
}

/// <summary>
/// Strongly named change kind identifiers shared across all conservative contract differs.
/// </summary>
internal static class ContractChangeKinds
{
    public const string ContractFileAdded = "contract_file_added";
    public const string ContractFileRemoved = "contract_file_removed";

    public const string OperationAdded = "operation_added";
    public const string OperationRemoved = "operation_removed";
    public const string SecurityNarrowed = "security_narrowed";
    public const string SecurityWidened = "security_widened";
    public const string SecurityChanged = "security_changed";

    public const string ParameterRemoved = "parameter_removed";
    public const string RequiredParameterAdded = "required_parameter_added";
    public const string OptionalParameterAdded = "optional_parameter_added";
    public const string ParameterNarrowedRequired = "parameter_narrowed_required";
    public const string ParameterWidenedOptional = "parameter_widened_optional";

    public const string RequestBodyRemoved = "request_body_removed";
    public const string RequiredRequestBodyAdded = "required_request_body_added";
    public const string OptionalRequestBodyAdded = "optional_request_body_added";
    public const string RequestBodyNarrowedRequired = "request_body_narrowed_required";
    public const string RequestBodyWidenedOptional = "request_body_widened_optional";

    public const string ResponseRemoved = "response_removed";
    public const string ResponseAdded = "response_added";
    public const string MediaTypeRemoved = "media_type_removed";
    public const string MediaTypeAdded = "media_type_added";

    public const string SchemaRemoved = "schema_removed";
    public const string SchemaAdded = "schema_added";
    public const string SchemaTypeNarrowed = "schema_type_narrowed";
    public const string SchemaTypeChanged = "schema_type_changed";
    public const string SchemaConstChanged = "schema_const_changed";
    public const string SchemaEnumNarrowed = "schema_enum_narrowed";
    public const string SchemaEnumExpanded = "schema_enum_expanded";
    public const string SchemaReviewRequired = "schema_review_required";

    public const string PropertyRemoved = "property_removed";
    public const string RequiredPropertyAdded = "required_property_added";
    public const string OptionalPropertyAdded = "optional_property_added";
    public const string RequiredPropertyRemoved = "required_property_removed";
    public const string HalLinkRemoved = "hal_link_removed";
    public const string HalLinkAdded = "hal_link_added";

    public const string UnresolvedRef = "unresolved_ref";
    public const string ExtensionChanged = "extension_changed";
    public const string OpenApiComponentChanged = "openapi_component_changed";
    public const string OpenApiReviewRequired = "openapi_review_required";

    public const string EnvVarRemoved = "env_var_removed";
    public const string EnvVarAdded = "env_var_added";
    public const string EnvVarAddedRequired = "env_var_added_required";
    public const string EnvVarActivationChanged = "env_var_activation_changed";
    public const string EnvVarDefaultChanged = "env_var_default_changed";

    public const string MigrationAdded = "migration_added";
    public const string MigrationRemoved = "migration_removed";
    public const string MigrationSnapshotAdded = "migration_snapshot_added";
    public const string MigrationSnapshotRemoved = "migration_snapshot_removed";
    public const string MigrationSnapshotModifiedWithoutMigration = "migration_snapshot_modified_without_migration";
    public const string MigrationProviderAsymmetry = "migration_provider_asymmetry";
}

/// <summary>
/// Scoped collector binding a contract family and artifact path to eliminate multi-argument change emission.
/// </summary>
internal sealed class ContractChangeCollector(string family, string artifactPath, List<ContractDiffChange> changes)
{
    public string Family { get; } = family;

    public string ArtifactPath { get; } = artifactPath;

    public void Add(string severity, string kind, string target, string summary) =>
        changes.Add(new ContractDiffChange(Family, severity, kind, target, summary, ArtifactPath));

    public void Breaking(string kind, string target, string summary) =>
        Add(ContractChangeSeverity.Breaking, kind, target, summary);

    public void Additive(string kind, string target, string summary) =>
        Add(ContractChangeSeverity.Additive, kind, target, summary);

    public void ReviewRequired(string kind, string target, string summary) =>
        Add(ContractChangeSeverity.ReviewRequired, kind, target, summary);
}
