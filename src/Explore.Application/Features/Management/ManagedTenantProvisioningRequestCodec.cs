// ABOUTME: Canonicalizes, hashes, serializes, and maps managed tenant provisioning request snapshots.
// ABOUTME: Makes idempotency comparisons deterministic while keeping terminal operation projections data-minimized.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Domain;

namespace Explore.Application.Features.Management;

public static class ManagedTenantProvisioningRequestCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };

    public static ManagementTenantProvisioningRequestDto Normalize(ManagementTenantProvisioningRequestDto request)
    {
        ManagementTenantExternalIdentityDto? externalIdentity = request.Administrator.ExternalIdentity;

        return new ManagementTenantProvisioningRequestDto
        {
            SchemaVersion = request.SchemaVersion,
            ExternalRequestId = request.ExternalRequestId.Trim(),
            ExternalCustomerReference = request.ExternalCustomerReference.Trim(),
            TenantName = request.TenantName.Trim(),
            TenantSlug = request.TenantSlug.Trim().ToLowerInvariant(),
            Administrator = new ManagementTenantAdministratorDto
            {
                ExternalIdentity = externalIdentity is null
                    ? null
                    : new ManagementTenantExternalIdentityDto
                    {
                        IdentityProvider = externalIdentity.IdentityProvider.Trim(),
                        Subject = externalIdentity.Subject.Trim(),
                        Email = externalIdentity.Email.Trim().ToLowerInvariant(),
                        FirstName = externalIdentity.FirstName.Trim(),
                        LastName = externalIdentity.LastName.Trim(),
                        DisplayName = NormalizeOptional(externalIdentity.DisplayName),
                        EmailVerified = externalIdentity.EmailVerified
                    },
                LocalIdentity = request.Administrator.LocalIdentity
            },
            DirectoryOperatorIdentity = request.DirectoryOperatorIdentity is null ? null : request.DirectoryOperatorIdentity with
            {
                PublicName = NormalizeOptional(request.DirectoryOperatorIdentity.PublicName),
                LegalName = NormalizeOptional(request.DirectoryOperatorIdentity.LegalName),
                OperatorKindCode = NormalizeOptional(request.DirectoryOperatorIdentity.OperatorKindCode),
                JurisdictionCountryCode = NormalizeOptional(request.DirectoryOperatorIdentity.JurisdictionCountryCode),
                RegistrationIdentifier = NormalizeOptional(request.DirectoryOperatorIdentity.RegistrationIdentifier),
                PublicContactEmail = NormalizeOptional(request.DirectoryOperatorIdentity.PublicContactEmail),
                LegalNoticeUrl = NormalizeOptional(request.DirectoryOperatorIdentity.LegalNoticeUrl),
                TermsUrl = NormalizeOptional(request.DirectoryOperatorIdentity.TermsUrl),
                PrivacyUrl = NormalizeOptional(request.DirectoryOperatorIdentity.PrivacyUrl)
            },
            Plan = new ManagementTenantPlanDto
            {
                Key = request.Plan.Key.Trim(),
                VersionId = request.Plan.VersionId,
                Quotas = request.Plan.Quotas
                    .Select(quota => new ManagementTenantQuotaDto(quota.Key.Trim(), quota.Limit))
                    .OrderBy(quota => quota.Key, StringComparer.Ordinal)
                    .ToArray()
            },
            ApprovedModules = request.ApprovedModules
                .Select(module => module.Trim())
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Domain = request.Domain is null
                ? null
                : new ManagementTenantDomainIntentDto
                {
                    Subdomain = NormalizeOptional(request.Domain.Subdomain)?.ToLowerInvariant(),
                    CustomDomain = NormalizeHost(request.Domain.CustomDomain)
                },
            Branding = request.Branding is null
                ? null
                : new ManagementTenantBrandingIntentDto
                {
                    DisplayName = NormalizeOptional(request.Branding.DisplayName),
                    LogoUrl = NormalizeOptional(request.Branding.LogoUrl),
                    FaviconUrl = NormalizeOptional(request.Branding.FaviconUrl),
                    CustomCssUrl = NormalizeOptional(request.Branding.CustomCssUrl)
                },
            InitialSettings = request.InitialSettings
                .Select(setting => new ManagementTenantInitialSettingDto(
                    setting.Key.Trim(),
                    NormalizeJson(setting.JsonValue)))
                .OrderBy(setting => setting.Key, StringComparer.Ordinal)
                .ToArray(),
            Callback = request.Callback is null
                ? null
                : new ManagementTenantCallbackMetadataDto
                {
                    CorrelationId = NormalizeOptional(request.Callback.CorrelationId),
                    CallbackReference = NormalizeOptional(request.Callback.CallbackReference)
                }
        };
    }

    public static ManagedProviderClientProvisioningDto ToProvisioningRequest(ManagementTenantProvisioningRequestDto request) => new()
    {
        ProviderKey = "islamu-event-control-plane",
        ExternalSystem = "control-plane",
        ExternalCustomerId = request.ExternalCustomerReference,
        TenantFullName = request.TenantName,
        TenantSlug = request.TenantSlug,
        ActivateTenant = true,
        DirectoryOperatorIdentity = request.DirectoryOperatorIdentity,
        LocalIdentity = request.Administrator.LocalIdentity,
        ExternalAdmin = request.Administrator.ExternalIdentity is { } identity ? new ManagedProviderExternalAdminDto
        {
            IdentityProvider = identity.IdentityProvider,
            Subject = identity.Subject,
            Email = identity.Email,
            FirstName = identity.FirstName,
            LastName = identity.LastName,
            DisplayName = identity.DisplayName,
            EmailVerified = identity.EmailVerified
        } : null
    };

    public static string Serialize(ManagementTenantProvisioningRequestDto request) =>
        JsonSerializer.Serialize(request, SerializerOptions);

    public static ManagementTenantProvisioningRequestDto Deserialize(string requestJson) =>
        JsonSerializer.Deserialize<ManagementTenantProvisioningRequestDto>(requestJson, SerializerOptions)
        ?? throw new JsonException("Managed tenant provisioning request snapshot is empty.");

    public static string ComputeHash(ManagementTenantProvisioningRequestDto request) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(request))));

    public static ManagementTenantProvisioningOperationDto ToDto(ManagedTenantProvisioningOperation operation) =>
        new(
            operation.Id,
            operation.ExternalRequestId,
            operation.ExternalCustomerReference,
            operation.TenantSlug,
            operation.Status.ToString(),
            operation.TenantId,
            operation.TenantAdministratorUserId,
            operation.FailureCode,
            operation.CorrelationId,
            operation.CanCancel,
            operation.CreatedAt,
            operation.StartedAt,
            operation.CompletedAt,
            operation.FailedAt,
            operation.CancelledAt);

    private static string NormalizeJson(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalJson(writer, document.RootElement);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject()
                         .OrderBy(property => property.Name, StringComparer.Ordinal)
                         .ThenBy(property => property.Value.GetRawText(), StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonicalJson(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray())
            {
                WriteCanonicalJson(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }

    private static string? NormalizeHost(string? value)
    {
        string? host = NormalizeOptional(value)?.TrimEnd('.').ToLowerInvariant();
        return host is not null
            && Uri.TryCreate($"https://{host}/", UriKind.Absolute, out Uri? uri)
                ? uri.IdnHost.ToLowerInvariant()
                : host;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
