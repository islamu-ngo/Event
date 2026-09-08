// Part of the Explore.Secrets system for database-backed dynamic configuration.
// Different from SystemSetting/TenantSetting which handle cascading application settings.

namespace Explore.Domain;

using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

/// <summary>
/// Encrypted operational configuration setting stored in the database.
/// Used for settings that need to be admin-configurable with encryption at rest.
/// </summary>
/// <remarks>
/// This entity is part of the secret management infrastructure:
/// - Values are encrypted using AES-256-GCM before storage
/// - Supports key versioning for encryption key rotation
/// - Protected by check constraint: cannot store high-value secrets (Database:*, Security:MasterKey*)
/// - For high-value secrets, use the secret manager (Infisical, Vault, etc.)
/// </remarks>
public class AppSetting : IAuditableEntity
{
    /// <summary>
    /// Unique configuration key in hierarchical format (e.g., "Smtp:Host", "Email:ApiKey").
    /// Uses colon notation for namespacing to match IConfiguration conventions.
    /// </summary>
    /// <remarks>
    /// Check constraint prevents storing:
    /// - Database:* keys (connection strings must use secret manager)
    /// - Security:MasterKey* keys (master keys must use secret manager)
    /// </remarks>
    public required string ConfigKey { get; set; }

    /// <summary>
    /// AES-256-GCM encrypted value as base64 string.
    /// Format: base64(nonce[12] + tag[16] + ciphertext)
    /// </summary>
    public required string EncryptedValue { get; set; }

    /// <summary>
    /// Version of the encryption key used to encrypt this value.
    /// Used during key rotation to identify values that need re-encryption.
    /// </summary>
    public int KeyVersion { get; set; }

    /// <summary>
    /// When this value was last encrypted.
    /// Updated during key rotation when value is re-encrypted.
    /// </summary>
    public DateTime EncryptedAt { get; set; }

    /// <summary>
    /// User who last encrypted this value.
    /// Null for system-initiated encryption (e.g., key rotation).
    /// </summary>
    public Guid? EncryptedBy { get; set; }

    /// <summary>
    /// Whether this setting contains sensitive data.
    /// When true, value is never logged and is masked in UI.
    /// </summary>
    public bool IsSensitive { get; set; }

    /// <summary>
    /// Human-readable description of the setting purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Category for grouping settings (e.g., "Email", "Integration", "FeatureFlags").
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Data type hint for the decrypted value.
    /// Used for validation and UI rendering.
    /// </summary>
    public AppSettingValueTypeEnum ValueType { get; set; }

    // IAuditableEntity implementation

    /// <summary>
    /// When this setting was first created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// User who created this setting.
    /// </summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>
    /// When this setting was last modified (key, metadata, or re-encrypted).
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// User who last modified this setting.
    /// </summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Concurrency token for optimistic concurrency control.
    /// Prevents lost updates during concurrent modifications.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}
