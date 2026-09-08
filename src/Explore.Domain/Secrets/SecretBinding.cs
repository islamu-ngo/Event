using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain.Secrets;

/// <summary>
/// Represents a DB-control-plane record that declares, for a given <see cref="SettingKey"/> at a given
/// <see cref="Scope"/> (optionally scoped to <see cref="ScopeId"/>), which <see cref="SourceType"/> the
/// runtime must consult to fetch the active secret value.
/// </summary>
/// <remarks>
/// <para>
/// The settings table is metadata and resolution control, never the secret value itself. Bindings hold only references:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="SecretSourceType.Infisical"/>: <see cref="InfisicalEnvironment"/> + <see cref="InfisicalPath"/> + <see cref="InfisicalKey"/></description></item>
///   <item><description><see cref="SecretSourceType.EnvironmentVariable"/>: <see cref="EnvironmentVariableName"/></description></item>
/// </list>
/// <para>
/// Exactly one metadata group is populated per row. This is enforced by DB CHECK constraints and by the
/// <c>SecretBinding.Create</c> factory method (see the Secrets namespace factory extensions). There is no
/// fallback chain: runtime dispatches on <see cref="SourceType"/> and fetches from that single source.
/// </para>
/// </remarks>
public partial class SecretBinding : IAuditableEntity
{
    /// <summary>Primary key (UUIDv7, DB-generated).</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Canonical setting key, e.g. <c>smtp.password</c>, <c>postgresql.host</c>. Must correspond to a known
    /// key in <see cref="Explore.Domain.Secrets.SecretDefinitionRegistry"/>.
    /// </summary>
    public required string SettingKey { get; set; }

    public string Qualifier { get; set; } = string.Empty;

    /// <summary>
    /// Scope level: <see cref="SecretScope.Instance"/> for instance-wide bindings (<see cref="ScopeId"/> MUST be null),
    /// or <see cref="SecretScope.Tenant"/> for tenant-scoped bindings (<see cref="ScopeId"/> MUST be the tenant id).
    /// </summary>
    public int SettingScopeId { get; set; }
    public SettingScopeLookup SettingScope { get; set; } = null!;

    public SecretScope Scope
    {
        get => SettingScopeId switch
        {
            1 => SecretScope.Instance,
            2 => SecretScope.Tenant,
            _ => throw new InvalidOperationException($"Setting scope id '{SettingScopeId}' is not valid for secret bindings.")
        };
        set => SettingScopeId = value switch
        {
            SecretScope.Instance => 1,
            SecretScope.Tenant => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported secret scope.")
        };
    }

    /// <summary>
    /// Null when <see cref="Scope"/> = <see cref="SecretScope.Instance"/>; the tenant id when <see cref="Scope"/> = <see cref="SecretScope.Tenant"/>.
    /// </summary>
    public Guid? ScopeId { get; set; }

    /// <summary>
    /// Declares which data plane holds the active secret value. Dispatch on this, never chain fallbacks.
    /// </summary>
    public int SecretSourceTypeId { get; set; }
    public SecretSourceTypeLookup SecretSourceType { get; set; } = null!;

    public SecretSourceType SourceType
    {
        get => (SecretSourceType)SecretSourceTypeId;
        set => SecretSourceTypeId = (int)value;
    }

    /// <summary>Infisical environment slug (e.g. <c>prod</c>, <c>staging</c>). Populated only when <see cref="SourceType"/> = <see cref="SecretSourceType.Infisical"/>.</summary>
    public string? InfisicalEnvironment { get; set; }

    /// <summary>Infisical folder path (e.g. <c>/smtp</c>, <c>/postgresql</c>). Populated only when <see cref="SourceType"/> = <see cref="SecretSourceType.Infisical"/>.</summary>
    public string? InfisicalPath { get; set; }

    /// <summary>Infisical secret key within the folder (e.g. <c>MAIL_SMTP_PASSWORD</c>). Populated only when <see cref="SourceType"/> = <see cref="SecretSourceType.Infisical"/>.</summary>
    public string? InfisicalKey { get; set; }

    /// <summary>Name of the environment variable to read. Populated only when <see cref="SourceType"/> = <see cref="SecretSourceType.EnvironmentVariable"/>.</summary>
    public string? EnvironmentVariableName { get; set; }

    /// <summary>
    /// When true, tenant-scope rows cannot override this instance-scope binding. Enforced by command handlers
    /// in the Application layer; purely declarative here.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Result of the most recent validation attempt (resolve + fetch from the declared source).
    /// </summary>
    public int SecretValidationStatusId { get; set; }
    public SecretValidationStatus SecretValidationStatus { get; set; } = null!;

    public SecretValidationResult LastValidationResult
    {
        get => (SecretValidationResult)SecretValidationStatusId;
        set => SecretValidationStatusId = (int)value;
    }

    /// <summary>
    /// Sanitized error message from the most recent validation failure. Never contains secret values.
    /// Null when the last validation succeeded or has not been attempted.
    /// </summary>
    public string? LastValidationError { get; set; }

    /// <summary>UTC timestamp of the most recent validation attempt, or null if never validated.</summary>
    public DateTime? LastValidatedAt { get; set; }

    /// <summary>User id that triggered the most recent validation attempt, or null for system-initiated.</summary>
    public Guid? LastValidatedBy { get; set; }

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}
