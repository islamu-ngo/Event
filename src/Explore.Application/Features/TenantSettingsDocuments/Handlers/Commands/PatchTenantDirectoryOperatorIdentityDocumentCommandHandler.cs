namespace Explore.Application.Features.TenantSettingsDocuments.Handlers.Commands;

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.DTOs.TenantSettingsDocuments.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using FluentValidation;
using MediatR;

public sealed class PatchTenantDirectoryOperatorIdentityDocumentCommandHandler(
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    ITenantSettingsDocumentRepository tenantSettingsDocumentRepository,
    ITenantRepository tenantRepository,
    ISettingMutationLock mutationLock,
    ITypedSettingsDocumentResolver typedSettingsDocumentResolver)
    : IRequestHandler<
        PatchTenantDirectoryOperatorIdentityDocumentCommand,
        BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>>
{
    private const string DocumentKey = SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity;
    private const string PayloadDeserializationError =
        "The tenant directory-operator identity payload could not be deserialized.";
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>> Handle(
        PatchTenantDirectoryOperatorIdentityDocumentCommand request,
        CancellationToken cancellationToken)
    {
        Guid currentTenantId = tenantContext.TenantId;
        Guid? currentUserId = currentUserService.UserId;
        if (currentTenantId == Guid.Empty
            || request.TenantId != currentTenantId
            || currentUserId is null)
        {
            return BaseCommandResponse.Authorization<TenantDirectoryOperatorIdentityDocumentDto>(
                "The directory-operator identity request is outside the current tenant context.");
        }

        var patchValidator = new PatchTenantDirectoryOperatorIdentityDocumentDtoValidator();
        var patchValidation = await patchValidator.ValidateAsync(request.Patch, cancellationToken);
        if (!patchValidation.IsValid)
        {
            return ValidationFailure(
                patchValidation.Errors.Select(error => error.ErrorMessage));
        }

        async Task<BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>> PatchAsync(
            CancellationToken ct)
        {
            TenantSettingsDocument? document =
                await tenantSettingsDocumentRepository.GetTrackedByTenantAndDocumentKey(
                    currentTenantId,
                    DocumentKey,
                    ct);
            if (document is null
                || document.TenantId != currentTenantId
                || !string.Equals(document.DocumentKey, DocumentKey, StringComparison.Ordinal)
                || document.SchemaVersion != TenantDirectoryOperatorIdentityDocumentDefaults.SchemaVersion)
            {
                return BaseCommandResponse.NotFound<TenantDirectoryOperatorIdentityDocumentDto>(
                    "Tenant directory-operator identity document not found.");
            }

            if (document.ConcurrencyStamp != request.Patch.ExpectedConcurrencyStamp)
            {
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "Tenant directory-operator identity changed since it was loaded.",
                    "tenant_settings_document",
                    document.Id.ToString());
            }

            TenantDirectoryOperatorIdentitySettings current =
                DeserializePayload(document.PayloadJson);
            TenantDirectoryOperatorIdentitySettings candidate = MergeRaw(current, request.Patch);
            TenantDirectoryOperatorIdentityDraftValidation draftValidation =
                TenantDirectoryOperatorIdentity.ValidateDraft(candidate);
            if (!draftValidation.IsValid)
            {
                return ValidationFailure(draftValidation.ReasonCodes);
            }

            TenantDirectoryOperatorIdentitySettings payload = ApplyNormalizedUpdates(
                current,
                request.Patch,
                draftValidation.NormalizedSettings);
            Tenant? tenant = await tenantRepository.GetByIdAsNoTrackingAsync(
                currentTenantId,
                ct);
            if (tenant is null)
            {
                return BaseCommandResponse.NotFound<TenantDirectoryOperatorIdentityDocumentDto>(
                    "Tenant not found.");
            }

            if (tenant.TenantStatusId == (int)TenantStatusEnum.Active)
            {
                TenantDirectoryOperatorIdentityReadiness activationReadiness =
                    TenantDirectoryOperatorIdentity.Evaluate(
                        payload,
                        TenantDirectoryOperatorIdentityCapability.Activation);
                if (!activationReadiness.IsReady)
                {
                    throw new ConcurrencyConflictException(
                        ConcurrencyConflictException.ConcurrentUpdate,
                        "Tenant activation state changed; reload identity before retrying.",
                        nameof(Tenant),
                        tenant.Id.ToString());
                }
            }

            document.UpdatePayload(
                document.SchemaVersion,
                document.DefaultsVersion,
                JsonSerializer.Serialize(payload, SerializerOptions));
            document.ConcurrencyStamp = Guid.CreateVersion7();
            document.UpdatedAt = DateTime.UtcNow;
            document.UpdatedBy = currentUserId.Value;
            await tenantSettingsDocumentRepository.Update(document);

            typedSettingsDocumentResolver.InvalidateTenantDocumentCache(
                currentTenantId,
                DocumentKey);

            return BaseCommandResponse.Success(
                TenantDirectoryOperatorIdentityDocumentMapper.Map(document, payload),
                "Tenant directory-operator identity patched successfully.");
        }

        return await mutationLock.ExecuteAsync(
            TenantDirectoryOperatorIdentityMutationLockKeys.ForTenant(currentTenantId),
            PatchAsync,
            cancellationToken);
    }

    private static TenantDirectoryOperatorIdentitySettings DeserializePayload(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<TenantDirectoryOperatorIdentitySettings>(
                    payloadJson,
                    SerializerOptions)
                ?? throw new InvalidOperationException(PayloadDeserializationError);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(PayloadDeserializationError, exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidOperationException(PayloadDeserializationError, exception);
        }
    }

    private static TenantDirectoryOperatorIdentitySettings MergeRaw(
        TenantDirectoryOperatorIdentitySettings current,
        PatchTenantDirectoryOperatorIdentityDocumentDto patch)
        => current with
        {
            PublicName = Requested(patch.LegalEntity?.PublicName, current.PublicName),
            LegalName = Requested(patch.LegalEntity?.LegalName, current.LegalName),
            OperatorKindCode = Requested(
                patch.LegalEntity?.OperatorKindCode,
                current.OperatorKindCode),
            JurisdictionCountryCode = Requested(
                patch.LegalEntity?.JurisdictionCountryCode,
                current.JurisdictionCountryCode),
            RegistrationIdentifier = Requested(
                patch.LegalEntity?.RegistrationIdentifier,
                current.RegistrationIdentifier),
            PublicContactEmail = Requested(
                patch.Contacts?.PublicContactEmail,
                current.PublicContactEmail),
            LegalNoticeUrl = Requested(
                patch.LegalLinks?.LegalNoticeUrl,
                current.LegalNoticeUrl),
            TermsUrl = Requested(patch.LegalLinks?.TermsUrl, current.TermsUrl),
            PrivacyUrl = Requested(patch.LegalLinks?.PrivacyUrl, current.PrivacyUrl)
        };

    private static TenantDirectoryOperatorIdentitySettings ApplyNormalizedUpdates(
        TenantDirectoryOperatorIdentitySettings current,
        PatchTenantDirectoryOperatorIdentityDocumentDto patch,
        TenantDirectoryOperatorIdentitySettings normalized)
        => current with
        {
            PublicName = Requested(patch.LegalEntity?.PublicName, current.PublicName, normalized.PublicName),
            LegalName = Requested(patch.LegalEntity?.LegalName, current.LegalName, normalized.LegalName),
            OperatorKindCode = Requested(
                patch.LegalEntity?.OperatorKindCode,
                current.OperatorKindCode,
                normalized.OperatorKindCode),
            JurisdictionCountryCode = Requested(
                patch.LegalEntity?.JurisdictionCountryCode,
                current.JurisdictionCountryCode,
                normalized.JurisdictionCountryCode),
            RegistrationIdentifier = Requested(
                patch.LegalEntity?.RegistrationIdentifier,
                current.RegistrationIdentifier,
                normalized.RegistrationIdentifier),
            PublicContactEmail = Requested(
                patch.Contacts?.PublicContactEmail,
                current.PublicContactEmail,
                normalized.PublicContactEmail),
            LegalNoticeUrl = Requested(
                patch.LegalLinks?.LegalNoticeUrl,
                current.LegalNoticeUrl,
                normalized.LegalNoticeUrl),
            TermsUrl = Requested(
                patch.LegalLinks?.TermsUrl,
                current.TermsUrl,
                normalized.TermsUrl),
            PrivacyUrl = Requested(
                patch.LegalLinks?.PrivacyUrl,
                current.PrivacyUrl,
                normalized.PrivacyUrl)
        };

    private static string? Requested(
        Explore.Application.Models.Common.OptionalUpdate<string?>? update,
        string? current)
        => update is { HasValue: true } specified ? specified.Value : current;

    private static string? Requested(
        Explore.Application.Models.Common.OptionalUpdate<string?>? update,
        string? current,
        string? normalized)
        => update is { HasValue: true } ? normalized : current;

    private static BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto> ValidationFailure(
        IEnumerable<string> errors)
        => BaseCommandResponse.Validation<TenantDirectoryOperatorIdentityDocumentDto>(
            errors,
            "Tenant directory-operator identity patch failed.");
}
