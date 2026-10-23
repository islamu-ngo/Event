namespace Explore.Application.Features.TenantSettingsDocuments.Handlers.Commands;

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.DTOs.TenantSettingsDocuments.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Application.Contracts.Operations;

public sealed class PatchTenantBrandingSettingsDocumentCommandHandler(
    ITenantSettingsDocumentRepository tenantSettingsDocumentRepository,
    ISerializableAttemptOwner attemptOwner,
    ITypedSettingsDocumentResolver typedSettingsDocumentResolver,
    ITenantBrandingSettingsDocumentLockService lockService,
    IHumanCommitScopePreparation humanScope,
    ITenantContext tenantContext,
    ITenantAdministratorCommitAuthority commitAuthority,
    IHumanTenantBrandingMutationPreparation preparation,
    IHumanActionCommitFinalizer finalizer)
    : ICommandHandler<
        PatchTenantBrandingSettingsDocumentCommand,
        BaseCommandResponse<TenantBrandingSettingsDocumentDto>>
{
    private const string PayloadDeserializationError = "Document 'tenant.branding' payload could not be deserialized.";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<BaseCommandResponse<TenantBrandingSettingsDocumentDto>> ExecuteAsync(
        PatchTenantBrandingSettingsDocumentCommand request,
        CancellationToken cancellationToken)
    {
        var patchValidator = new PatchTenantBrandingSettingsDocumentDtoValidator();
        var validationResult = await patchValidator.ValidateAsync(request.Patch, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<TenantBrandingSettingsDocumentDto>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Tenant branding settings patch failed.");
        }

        var lockState = await lockService.GetLockStateAsync(cancellationToken);
        var errors = ValidateRequestedFieldLocks(request.Patch, lockState);
        if (errors.Count > 0)
        {
            return BaseCommandResponse.Validation<TenantBrandingSettingsDocumentDto>(
                errors,
                "Tenant branding settings patch failed.");
        }

        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("Original authenticated branding authority is required.");
        BaseCommandResponse<TenantBrandingSettingsDocumentDto> response;
        for (int attempt = 1; ; attempt++)
        {
            IPreparedHumanTenantBrandingMutation prepared;
            try
            {
                prepared = await preparation.PrepareAsync(request, cancellationToken);
            }
            catch (AuthorizationException)
            {
                return BaseCommandResponse.Authorization<TenantBrandingSettingsDocumentDto>(
                    "Current tenant branding authority is required.");
            }
            try
            {
                response = await ExecuteAttemptAsync(prepared);
                break;
            }
            catch (HumanMutationAttemptRolledBackException) when (attempt < 4)
            {
                // Only a whole acknowledged rollback allows a fresh action decision.
                // The original capability is never replayed by an execution strategy.
            }
            catch (AuthorizationException)
            {
                // The actual owner has finished its failed attempt. Unknown rollback
                // outcomes propagate rather than being normalized to a business response.
                return BaseCommandResponse.Authorization<TenantBrandingSettingsDocumentDto>(
                    "Current tenant branding authority is required.");
            }
        }

        if (response.IsSuccess)
        {
            typedSettingsDocumentResolver.InvalidateTenantDocumentCache(request.TenantId, SettingsDocumentKeys.Tenant.Branding);
        }

        return response;

        // These session leases wrap the one selected nonreplaying physical owner, survive
        // its final admission and commit, and do not introduce another transaction owner.
        Task<BaseCommandResponse<TenantBrandingSettingsDocumentDto>> ExecuteAttemptAsync(
            IPreparedHumanTenantBrandingMutation prepared) => lockService.ExecuteWithGovernanceLocksAsync(
            token => attemptOwner.ExecuteAsync(async transactionToken =>
        {
            await prepared.AcquireAsync(request, transactionToken);
            if (request.TenantId != tenantContext.TenantId
                || !ReferenceEquals(humanScope.Subject, subject)
                || !await commitAuthority.IsTenantAdministratorForCommitAsync(
                    subject.PrincipalId, request.TenantId, transactionToken))
            {
                return BaseCommandResponse.Authorization<TenantBrandingSettingsDocumentDto>(
                    "Current tenant administrator authority is required.");
            }

            var document = await tenantSettingsDocumentRepository.GetTrackedByTenantAndDocumentKey(
                request.TenantId,
                SettingsDocumentKeys.Tenant.Branding,
                transactionToken);

            if (document is null)
            {
                return BaseCommandResponse.NotFound<TenantBrandingSettingsDocumentDto>(
                    "Tenant branding settings document not found.");
            }

            if (document.ConcurrencyStamp != request.Patch.ExpectedConcurrencyStamp)
            {
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "Tenant branding settings changed since they were loaded. Refresh the settings and try again.",
                    "tenant_settings_document",
                    document.Id.ToString());
            }

            // Preflight and request flags are advisory; these leases survive this transaction's commit.
            var commitLockState = await lockService.GetLockStateAsync(transactionToken);
            var commitErrors = ValidateRequestedFieldLocks(request.Patch, commitLockState);
            if (commitErrors.Count > 0)
            {
                return BaseCommandResponse.Validation<TenantBrandingSettingsDocumentDto>(
                    commitErrors,
                    "Tenant branding settings patch failed.");
            }

            var mergedPayload = MergePayload(DeserializePayload(document.PayloadJson), request.Patch);
            var payloadValidator = new TenantBrandingSettingsPayloadDtoValidator();
            var payloadValidation = await payloadValidator.ValidateAsync(MapPayload(mergedPayload), transactionToken);
            if (!payloadValidation.IsValid)
            {
                return BaseCommandResponse.Validation<TenantBrandingSettingsDocumentDto>(
                    payloadValidation.Errors.Select(error => error.ErrorMessage),
                    "Tenant branding settings patch failed.");
            }

            var payloadJson = JsonSerializer.Serialize(mergedPayload, SerializerOptions);
            document.UpdatePayload(document.SchemaVersion, document.DefaultsVersion, payloadJson);
            await tenantSettingsDocumentRepository.Update(document);
            finalizer.Register(async finalToken =>
            {
                var finalLocks = await lockService.GetLockStateAsync(finalToken);
                if (request.TenantId != tenantContext.TenantId
                    || !ReferenceEquals(humanScope.Subject, subject)
                    || !await commitAuthority.IsTenantAdministratorForCommitAsync(
                        subject.PrincipalId, request.TenantId, finalToken)
                    || ValidateRequestedFieldLocks(request.Patch, finalLocks).Count != 0)
                    throw new UnauthorizedAccessException("Current branding governance and original tenant authority are required.");
            }, _ => { });
            return BaseCommandResponse.Success(
                MapDocument(document, mergedPayload, commitLockState),
                "Tenant branding settings patched successfully.");
        }, token), cancellationToken);

    }

    private static BrandingSettings DeserializePayload(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<BrandingSettings>(payloadJson, SerializerOptions)
                ?? throw new InvalidOperationException(PayloadDeserializationError);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(PayloadDeserializationError);
        }
    }

    private static BrandingSettings MergePayload(
        BrandingSettings current,
        PatchTenantBrandingSettingsDocumentDto patch)
        => current with
        {
            DisplayName = patch.DisplayName?.Value is { HasValue: true } displayName
                ? Normalize(displayName.Value)
                : current.DisplayName,
            LogoUrl = patch.Assets?.LogoUrl is { HasValue: true } logoUrl
                ? Normalize(logoUrl.Value)
                : current.LogoUrl,
            FaviconUrl = patch.Assets?.FaviconUrl is { HasValue: true } faviconUrl
                ? Normalize(faviconUrl.Value)
                : current.FaviconUrl,
            CustomCssUrl = patch.Assets?.CustomCssUrl is { HasValue: true } customCssUrl
                ? Normalize(customCssUrl.Value)
                : current.CustomCssUrl
        };

    private static IReadOnlyList<string> ValidateRequestedFieldLocks(
        PatchTenantBrandingSettingsDocumentDto patch,
        TenantBrandingSettingsDocumentLockState lockState)
    {
        List<string> errors = [];
        AddIfRequestedAndLocked(errors, "Display name", patch.DisplayName?.Value.HasValue == true, lockState.CanChangeDisplayName);
        AddIfRequestedAndLocked(errors, "Logo URL", patch.Assets?.LogoUrl.HasValue == true, lockState.CanChangeLogoUrl);
        AddIfRequestedAndLocked(errors, "Favicon URL", patch.Assets?.FaviconUrl.HasValue == true, lockState.CanChangeFaviconUrl);
        AddIfRequestedAndLocked(errors, "Custom CSS URL", patch.Assets?.CustomCssUrl.HasValue == true, lockState.CanChangeCustomCssUrl);
        return errors;
    }

    private static void AddIfRequestedAndLocked(
        List<string> errors,
        string fieldName,
        bool isRequested,
        bool canChange)
    {
        if (isRequested && !canChange)
        {
            errors.Add($"{fieldName} cannot be changed because instance branding governance currently locks this tenant override.");
        }
    }

    private static TenantBrandingSettingsDocumentDto MapDocument(
        TenantSettingsDocument document,
        BrandingSettings payload,
        TenantBrandingSettingsDocumentLockState lockState)
        => new()
        {
            DocumentKey = document.DocumentKey,
            SchemaVersion = document.SchemaVersion,
            DefaultsVersion = document.DefaultsVersion,
            Payload = MapPayload(payload),
            Source = SettingsDocumentSource.Tenant.ToString(),
            SourceScopeId = document.TenantId,
            ConcurrencyStamp = document.ConcurrencyStamp,
            IsLockedByInstance = lockState.IsLockedByInstance,
            CanChangeDisplayName = lockState.CanChangeDisplayName,
            CanChangeLogoUrl = lockState.CanChangeLogoUrl,
            CanChangeFaviconUrl = lockState.CanChangeFaviconUrl,
            CanChangeCustomCssUrl = lockState.CanChangeCustomCssUrl,
            UpdatedAt = document.UpdatedAt
        };

    private static TenantBrandingSettingsPayloadDto MapPayload(BrandingSettings payload)
        => new()
        {
            DisplayName = payload.DisplayName,
            LogoUrl = payload.LogoUrl,
            FaviconUrl = payload.FaviconUrl,
            CustomCssUrl = payload.CustomCssUrl
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
