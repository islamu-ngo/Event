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
using Explore.Application.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using FluentValidation;
using MediatR;

public sealed class PatchTenantBrandingSettingsDocumentCommandHandler(
    ITenantSettingsDocumentRepository tenantSettingsDocumentRepository,
    IUnitOfWork unitOfWork,
    ITypedSettingsDocumentResolver typedSettingsDocumentResolver,
    ITenantBrandingSettingsDocumentLockService lockService)
    : IRequestHandler<
        PatchTenantBrandingSettingsDocumentCommand,
        BaseCommandResponse<TenantBrandingSettingsDocumentDto>>
{
    private const string PayloadDeserializationError = "Document 'tenant.branding' payload could not be deserialized.";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<BaseCommandResponse<TenantBrandingSettingsDocumentDto>> Handle(
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

        var document = await tenantSettingsDocumentRepository.GetTrackedByTenantAndDocumentKey(
            request.TenantId,
            SettingsDocumentKeys.Tenant.Branding,
            cancellationToken);

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

        var currentPayload = DeserializePayload(document.PayloadJson);
        var mergedPayload = MergePayload(currentPayload, request.Patch);
        var mergedPayloadDto = MapPayload(mergedPayload);
        var payloadValidator = new TenantBrandingSettingsPayloadDtoValidator();
        validationResult = await payloadValidator.ValidateAsync(mergedPayloadDto, cancellationToken);

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

        var payloadJson = JsonSerializer.Serialize(mergedPayload, SerializerOptions);
        await unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            document.UpdatePayload(document.SchemaVersion, document.DefaultsVersion, payloadJson);
            await tenantSettingsDocumentRepository.Update(document);
        }, cancellationToken);
        typedSettingsDocumentResolver.InvalidateTenantDocumentCache(request.TenantId, SettingsDocumentKeys.Tenant.Branding);

        return BaseCommandResponse.Success(
            MapDocument(document, mergedPayload, lockState),
            "Tenant branding settings patched successfully.");
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
