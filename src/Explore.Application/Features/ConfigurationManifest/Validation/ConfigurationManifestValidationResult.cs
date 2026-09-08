namespace Explore.Application.Features.ConfigurationManifest.Validation;

public static class ConfigurationManifestFailureCodes
{
    public const string ContractInvalid = "configuration_manifest_contract_invalid";
    public const string TenantDuplicate = "configuration_manifest_tenant_duplicate";
    public const string KeyNotAllowed = "configuration_manifest_key_not_allowed";
    public const string SensitiveKeyForbidden =
        "configuration_manifest_sensitive_key_forbidden";
    public const string ValueInvalid = "configuration_manifest_value_invalid";
    public const string DocumentInvalid = "configuration_manifest_document_invalid";
    public const string LegalDocumentInvalid =
        "configuration_manifest_legal_document_invalid";
    public const string CrossReferenceInvalid =
        "configuration_manifest_cross_reference_invalid";
}

public static class ConfigurationManifestValidationReasonCodes
{
    public const string SettingScopeInvalid =
        "configuration_manifest_setting_scope_invalid";
}

public sealed record ConfigurationManifestValidationError(
    string Code,
    string Path,
    string Message,
    string? ReasonCode = null);

public sealed record ConfigurationManifestValidationResult(
    IReadOnlyList<ConfigurationManifestValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
