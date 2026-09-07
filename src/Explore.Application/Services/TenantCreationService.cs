namespace Explore.Application.Services;

using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;

public sealed class TenantCreationService(
    ITenantRepository tenantRepository,
    ITenantSettingsDocumentRepository tenantSettingsDocumentRepository)
    : ITenantCreationService
{
    public async Task<TenantCreationOutcome> CreateInCurrentTransactionAsync(
        TenantCreationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        TenantSettingsDocument branding = CreateBranding(request);
        TenantSettingsDocument identity = CreateIdentity(request);

        var tenant = new Tenant
        {
            Id = request.TenantId,
            FullName = request.FullName.Trim(),
            Slug = request.Slug.Trim(),
            TenantStatusId = request.TenantStatusId,
            TenantStatus = null!,
            CreatedAt = request.OccurredAt,
            CreatedBy = request.ActorUserId
        };
        Tenant created = await tenantRepository.Create(tenant);
        if (created.Id != request.TenantId)
        {
            throw new InvalidOperationException("Tenant persistence changed the planned aggregate identity.");
        }

        TenantSettingsDocument createdBranding =
            await tenantSettingsDocumentRepository.Create(branding);
        TenantSettingsDocument createdIdentity =
            await tenantSettingsDocumentRepository.Create(identity);
        return new TenantCreationOutcome(created, createdBranding, createdIdentity);
    }

    private static void Validate(TenantCreationRequest request)
    {
        if (request.TenantId == Guid.Empty || request.TenantId.Version != 7
            || request.Branding.DocumentId == Guid.Empty
            || request.Branding.DocumentId.Version != 7
            || request.DirectoryOperatorIdentity.DocumentId == Guid.Empty
            || request.DirectoryOperatorIdentity.DocumentId.Version != 7)
        {
            throw new ArgumentException("Tenant and mandatory document identities must be UUIDv7.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.FullName)
            || string.IsNullOrWhiteSpace(request.Slug)
            || request.TenantStatusId <= 0)
        {
            throw new ArgumentException("Tenant creation identity and status are required.", nameof(request));
        }

        if (request.OccurredAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Tenant creation timestamp must use UTC kind.", nameof(request));
        }

        if (request.Branding.SchemaVersion != TenantBrandingSettingsDocumentDefaults.SchemaVersion
            || !string.Equals(
                request.Branding.DefaultsVersion,
                TenantBrandingSettingsDocumentDefaults.DefaultsVersion,
                StringComparison.Ordinal)
            || request.DirectoryOperatorIdentity.SchemaVersion
                != TenantDirectoryOperatorIdentityDocumentDefaults.SchemaVersion
            || !string.Equals(
                request.DirectoryOperatorIdentity.DefaultsVersion,
                TenantDirectoryOperatorIdentityDocumentDefaults.DefaultsVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Tenant creation requires canonical mandatory document metadata.", nameof(request));
        }
    }

    private static TenantSettingsDocument CreateBranding(TenantCreationRequest request)
    {
        TenantSettingsDocument branding = TenantSettingsDocument.Create(
            request.TenantId,
            SettingsDocumentKeys.Tenant.Branding,
            request.Branding.SchemaVersion,
            request.Branding.DefaultsVersion,
            request.Branding.PayloadJson);
        ApplyPlannedIdentity(branding, request.Branding.DocumentId, request);
        return branding;
    }

    private static TenantSettingsDocument CreateIdentity(TenantCreationRequest request)
    {
        TenantDirectoryOperatorIdentitySettings payload;
        try
        {
            payload = JsonSerializer.Deserialize<TenantDirectoryOperatorIdentitySettings>(
                request.DirectoryOperatorIdentity.PayloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new JsonException("Identity payload was null.");
        }
        catch (JsonException)
        {
            throw new TenantDirectoryOperatorIdentityReadinessException(
                "tenant_directory_operator_identity_integrity_error",
                ["tenant_directory_operator_identity_payload_invalid"]);
        }

        if (request.TenantStatusId == (int)TenantStatusEnum.Active)
        {
            TenantDirectoryOperatorIdentityReadiness readiness =
                TenantDirectoryOperatorIdentity.Evaluate(
                    payload,
                    TenantDirectoryOperatorIdentityCapability.Activation);
            if (!readiness.IsReady)
            {
                throw new TenantDirectoryOperatorIdentityReadinessException(
                    "tenant_directory_operator_identity_incomplete",
                    readiness.ReasonCodes);
            }

            payload = readiness.Identity!.ToSettings();
        }

        TenantSettingsDocument identity =
            TenantDirectoryOperatorIdentityDocumentDefaults.Create(request.TenantId, payload);
        ApplyPlannedIdentity(
            identity,
            request.DirectoryOperatorIdentity.DocumentId,
            request);
        return identity;
    }

    private static void ApplyPlannedIdentity(
        TenantSettingsDocument document,
        Guid documentId,
        TenantCreationRequest request)
    {
        document.Id = documentId;
        document.CreatedAt = request.OccurredAt;
        document.CreatedBy = request.ActorUserId;
    }
}
