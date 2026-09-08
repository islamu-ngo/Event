using System.Net;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public interface ITenantDirectoryOperatorIdentityAdminService
{
    Task<TenantDirectoryOperatorIdentityAdminModel> GetAsync(
        CancellationToken cancellationToken = default);

    Task<TenantDirectoryOperatorIdentitySaveResult> SaveAsync(
        TenantDirectoryOperatorIdentityAdminModel model,
        CancellationToken cancellationToken = default);
}

public sealed class TenantDirectoryOperatorIdentityAdminService(
    ITenantSettingsDocumentsClient api,
    ILogger<TenantDirectoryOperatorIdentityAdminService> logger)
    : ITenantDirectoryOperatorIdentityAdminService
{
    private const string EditLinkRelation = "edit";
    private const string PatchMethod = "PATCH";
    private const string CanonicalEditHref =
        "/api/tenant/settings/documents/directory-operator-identity";

    public async Task<TenantDirectoryOperatorIdentityAdminModel> GetAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            HalResourceOfTenantDirectoryOperatorIdentityDocumentDto document =
                await api.GetTenantDirectoryOperatorIdentityDocumentAsync(
                    cancellationToken: cancellationToken);
            return Map(document);
        }
        catch (Exception exception) when (IsStatus(exception, HttpStatusCode.NotFound))
        {
            return TenantDirectoryOperatorIdentityAdminModel.Missing();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load the tenant directory-operator identity document.");
            return TenantDirectoryOperatorIdentityAdminModel.Failed();
        }
    }

    public async Task<TenantDirectoryOperatorIdentitySaveResult> SaveAsync(
        TenantDirectoryOperatorIdentityAdminModel model,
        CancellationToken cancellationToken = default)
    {
        if (!model.Exists)
        {
            return TenantDirectoryOperatorIdentitySaveResult.Failed(
                TenantDirectoryOperatorIdentityAdminMessageCode.NotInitialized);
        }

        if (!model.CanEdit)
        {
            return TenantDirectoryOperatorIdentitySaveResult.Failed(
                TenantDirectoryOperatorIdentityAdminMessageCode.EditUnavailable);
        }

        try
        {
            HalResourceOfTenantDirectoryOperatorIdentityDocumentDto updated =
                await api.PatchTenantDirectoryOperatorIdentityDocumentAsync(
                    BuildPatch(model),
                    cancellationToken: cancellationToken);
            return TenantDirectoryOperatorIdentitySaveResult.Successful(Map(updated));
        }
        catch (Exception exception) when (IsStatus(exception, HttpStatusCode.Conflict))
        {
            logger.LogWarning(
                exception,
                "Tenant directory-operator identity PATCH encountered a concurrency conflict.");
            TenantDirectoryOperatorIdentityAdminModel authoritative =
                await GetAsync(cancellationToken);
            return TenantDirectoryOperatorIdentitySaveResult.Conflict(authoritative);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to patch the tenant directory-operator identity document.");
            return TenantDirectoryOperatorIdentitySaveResult.Failed(
                TenantDirectoryOperatorIdentityAdminMessageCode.SaveFailed);
        }
    }

    private static PatchTenantDirectoryOperatorIdentityDocumentDto BuildPatch(
        TenantDirectoryOperatorIdentityAdminModel model) => new()
        {
            ExpectedConcurrencyStamp = model.ConcurrencyStamp,
            LegalEntity = new PatchTenantDirectoryOperatorLegalEntityDto
            {
                PublicName = Update(model.PublicName),
                LegalName = Update(model.LegalName),
                OperatorKindCode = Update(model.OperatorKindCode),
                JurisdictionCountryCode = Update(model.JurisdictionCountryCode),
                RegistrationIdentifier = Update(model.RegistrationIdentifier)
            },
            Contacts = new PatchTenantDirectoryOperatorContactsDto
            {
                PublicContactEmail = Update(model.PublicContactEmail)
            },
            LegalLinks = new PatchTenantDirectoryOperatorLegalLinksDto
            {
                LegalNoticeUrl = Update(model.LegalNoticeUrl),
                TermsUrl = Update(model.TermsUrl),
                PrivacyUrl = Update(model.PrivacyUrl)
            }
        };

    private static TenantDirectoryOperatorIdentityAdminModel Map(
        HalResourceOfTenantDirectoryOperatorIdentityDocumentDto document)
    {
        Payload2? payload = document.Payload;
        return new TenantDirectoryOperatorIdentityAdminModel
        {
            Exists = true,
            CanEdit = HasExactEditAffordance(document._links),
            ConcurrencyStamp = document.ConcurrencyStamp,
            PublicName = payload?.PublicName,
            LegalName = payload?.LegalName,
            OperatorKindCode = payload?.OperatorKindCode,
            JurisdictionCountryCode = payload?.JurisdictionCountryCode,
            RegistrationIdentifier = payload?.RegistrationIdentifier,
            PublicContactEmail = payload?.PublicContactEmail,
            LegalNoticeUrl = payload?.LegalNoticeUrl,
            TermsUrl = payload?.TermsUrl,
            PrivacyUrl = payload?.PrivacyUrl,
            IsActivationReady = document.IsActivationReady == true,
            IsPublicDisclosureReady = document.IsPublicDisclosureReady == true,
            IsPaidCommerceReady = document.IsPaidCommerceReady == true
        };
    }

    private static bool HasExactEditAffordance(
        IDictionary<string, HalLink>? links) =>
        links is not null
        && links.TryGetValue(EditLinkRelation, out HalLink? edit)
        && string.Equals(edit.Method, PatchMethod, StringComparison.Ordinal)
        && string.Equals(edit.Href, CanonicalEditHref, StringComparison.Ordinal);

    private static OptionalUpdateOfstring Update(string? value) => new()
    {
        HasValue = true,
        Value = Normalize(value)
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsStatus(Exception exception, HttpStatusCode status) =>
        exception is ApiException apiException
            && apiException.StatusCode == (int)status
        || exception.InnerException is not null
            && IsStatus(exception.InnerException, status);
}

public sealed class TenantDirectoryOperatorIdentityAdminModel
{
    public bool Exists { get; set; }
    public bool CanEdit { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public string? PublicName { get; set; }
    public string? LegalName { get; set; }
    public string? OperatorKindCode { get; set; }
    public string? JurisdictionCountryCode { get; set; }
    public string? RegistrationIdentifier { get; set; }
    public string? PublicContactEmail { get; set; }
    public string? LegalNoticeUrl { get; set; }
    public string? TermsUrl { get; set; }
    public string? PrivacyUrl { get; set; }
    public bool IsActivationReady { get; set; }
    public bool IsPublicDisclosureReady { get; set; }
    public bool IsPaidCommerceReady { get; set; }
    public TenantDirectoryOperatorIdentityAdminMessageCode MessageCode { get; set; }

    public static TenantDirectoryOperatorIdentityAdminModel Missing() => new()
    {
        MessageCode = TenantDirectoryOperatorIdentityAdminMessageCode.NotInitialized
    };

    public static TenantDirectoryOperatorIdentityAdminModel Failed() => new()
    {
        MessageCode = TenantDirectoryOperatorIdentityAdminMessageCode.LoadFailed
    };

    public void Apply(TenantDirectoryOperatorIdentityAdminModel source)
    {
        Exists = source.Exists;
        CanEdit = source.CanEdit;
        ConcurrencyStamp = source.ConcurrencyStamp;
        PublicName = source.PublicName;
        LegalName = source.LegalName;
        OperatorKindCode = source.OperatorKindCode;
        JurisdictionCountryCode = source.JurisdictionCountryCode;
        RegistrationIdentifier = source.RegistrationIdentifier;
        PublicContactEmail = source.PublicContactEmail;
        LegalNoticeUrl = source.LegalNoticeUrl;
        TermsUrl = source.TermsUrl;
        PrivacyUrl = source.PrivacyUrl;
        IsActivationReady = source.IsActivationReady;
        IsPublicDisclosureReady = source.IsPublicDisclosureReady;
        IsPaidCommerceReady = source.IsPaidCommerceReady;
        MessageCode = source.MessageCode;
    }
}

public sealed record TenantDirectoryOperatorIdentitySaveResult
{
    public bool Success { get; init; }
    public bool IsConcurrencyConflict { get; init; }
    public TenantDirectoryOperatorIdentityAdminMessageCode MessageCode { get; init; }
    public TenantDirectoryOperatorIdentityAdminModel? Model { get; init; }

    public static TenantDirectoryOperatorIdentitySaveResult Successful(
        TenantDirectoryOperatorIdentityAdminModel model) => new()
        {
            Success = true,
            MessageCode = TenantDirectoryOperatorIdentityAdminMessageCode.Saved,
            Model = model
        };

    public static TenantDirectoryOperatorIdentitySaveResult Failed(
        TenantDirectoryOperatorIdentityAdminMessageCode messageCode) => new()
    {
        MessageCode = messageCode
    };

    public static TenantDirectoryOperatorIdentitySaveResult Conflict(
        TenantDirectoryOperatorIdentityAdminModel authoritative) => new()
        {
            IsConcurrencyConflict = true,
            MessageCode = TenantDirectoryOperatorIdentityAdminMessageCode.Conflict,
            Model = authoritative
        };
}

public enum TenantDirectoryOperatorIdentityAdminMessageCode
{
    None,
    NotInitialized,
    LoadFailed,
    EditUnavailable,
    SaveFailed,
    Saved,
    Conflict
}
