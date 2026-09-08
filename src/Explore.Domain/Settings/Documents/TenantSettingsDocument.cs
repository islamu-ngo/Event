namespace Explore.Domain.Settings.Documents;

using System.Text.Json;
using Explore.Domain.Interfaces;

public class TenantSettingsDocument : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required Tenant Tenant { get; set; }

    public required string DocumentKey { get; set; }

    public int SchemaVersion { get; set; }

    public required string DefaultsVersion { get; set; }

    public required string PayloadJson { get; set; }

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static TenantSettingsDocument Create(
        Guid tenantId,
        string documentKey,
        int schemaVersion,
        string defaultsVersion,
        string payloadJson)
    {
        Validate(documentKey, schemaVersion, defaultsVersion, payloadJson);

        return new TenantSettingsDocument
        {
            TenantId = tenantId,
            Tenant = null!,
            DocumentKey = documentKey,
            SchemaVersion = schemaVersion,
            DefaultsVersion = defaultsVersion,
            PayloadJson = payloadJson,
        };
    }

    public void UpdatePayload(int schemaVersion, string defaultsVersion, string payloadJson)
    {
        Validate(DocumentKey, schemaVersion, defaultsVersion, payloadJson);

        SchemaVersion = schemaVersion;
        DefaultsVersion = defaultsVersion;
        PayloadJson = payloadJson;
    }

    private static void Validate(
        string documentKey,
        int schemaVersion,
        string defaultsVersion,
        string payloadJson)
    {
        if (!SettingsDocumentTaxonomy.IsNonSecretTenantDocument(documentKey))
        {
            throw new ArgumentException("Document key is not an approved non-secret tenant settings document.", nameof(documentKey));
        }

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(defaultsVersion))
        {
            throw new ArgumentException("Defaults version is required.", nameof(defaultsVersion));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("Payload JSON is required.", nameof(payloadJson));
        }

        using var document = JsonDocument.Parse(payloadJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Payload JSON must be an object.", nameof(payloadJson));
        }
    }
}
