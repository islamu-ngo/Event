using System.Net;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public interface IInstanceOperatorIdentityAdminService
{
    Task<InstanceOperatorIdentityAdminModel> GetAsync(
        CancellationToken cancellationToken = default);

    Task<InstanceOperatorIdentitySaveResult> SaveAsync(
        InstanceOperatorIdentityAdminModel model,
        CancellationToken cancellationToken = default);
}

public sealed class InstanceOperatorIdentityAdminService(
    IInstanceOperatorIdentityClient api,
    ILogger<InstanceOperatorIdentityAdminService> logger)
    : IInstanceOperatorIdentityAdminService
{
    private const string UpdateLinkRelation = "update";

    public async Task<InstanceOperatorIdentityAdminModel> GetAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            HalResourceOfInstanceOperatorIdentityDocumentDto document =
                await api.GetInstanceOperatorIdentityAsync(cancellationToken: cancellationToken);
            return Map(document);
        }
        catch (Exception exception) when (IsStatus(exception, HttpStatusCode.NotFound))
        {
            return InstanceOperatorIdentityAdminModel.Missing();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load the instance operator identity document.");
            return InstanceOperatorIdentityAdminModel.Failed();
        }
    }

    public async Task<InstanceOperatorIdentitySaveResult> SaveAsync(
        InstanceOperatorIdentityAdminModel model,
        CancellationToken cancellationToken = default)
    {
        if (!model.Exists)
        {
            return InstanceOperatorIdentitySaveResult.Failed(
                InstanceOperatorIdentityAdminMessageCode.NotInitialized);
        }

        if (!model.CanEdit)
        {
            return InstanceOperatorIdentitySaveResult.Failed(
                InstanceOperatorIdentityAdminMessageCode.EditUnavailable);
        }

        try
        {
            SaveInstanceOperatorIdentityRequestDto request = BuildRequest(model);
            BaseCommandResponseOfInstanceOperatorIdentitySavedDocumentDto response =
                await api.SaveInstanceOperatorIdentityAsync(request, cancellationToken: cancellationToken);

            if (response.Success == true && response.Id is not null)
            {
                model.Revision = response.Id.Revision;
                model.PaidCommerceIsReady = response.Id.PaidCommerce?.IsReady == true;
                model.FailureCode = response.Id.PaidCommerce?.FailureCode;
                model.ReasonCodes = response.Id.PaidCommerce?.ReasonCodes ?? new List<string>();
                model.PublicDisclosure = response.Id.PublicDisclosure;
                if (response.Id.OperatorId.HasValue)
                {
                    model.OperatorId = response.Id.OperatorId.Value;
                }

                return InstanceOperatorIdentitySaveResult.Successful(model);
            }

            var validationErrors = new Dictionary<string, string>();
            if (response.Errors != null)
            {
                int index = 0;
                foreach (var err in response.Errors)
                {
                    validationErrors[$"error_{index++}"] = err;
                }
            }

            return InstanceOperatorIdentitySaveResult.Failed(
                InstanceOperatorIdentityAdminMessageCode.SaveFailed,
                response.Message,
                validationErrors);
        }
        catch (Exception exception) when (IsStatus(exception, HttpStatusCode.Conflict))
        {
            logger.LogWarning(
                exception,
                "Instance operator identity save encountered a concurrency conflict.");
            InstanceOperatorIdentityAdminModel authoritative =
                await GetAsync(cancellationToken);
            return InstanceOperatorIdentitySaveResult.Conflict(authoritative);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to save the instance operator identity document.");
            return InstanceOperatorIdentitySaveResult.Failed(
                InstanceOperatorIdentityAdminMessageCode.SaveFailed);
        }
    }

    private static InstanceOperatorIdentityAdminModel Map(
        HalResourceOfInstanceOperatorIdentityDocumentDto document)
    {
        bool canEdit = document._links is not null
            && document._links.ContainsKey(UpdateLinkRelation);

        return new InstanceOperatorIdentityAdminModel
        {
            Exists = true,
            CanEdit = canEdit,
            Revision = document.Revision,
            OperatorId = document.OperatorId,
            PublicName = document.PublicName,
            LegalName = document.LegalName,
            OperatorKindCode = document.OperatorKindCode,
            JurisdictionCountryCode = document.JurisdictionCountryCode,
            RegistrationIdentifier = document.RegistrationIdentifier,
            PublicContactEmail = document.PublicContactEmail,
            WebsiteUrl = document.WebsiteUrl,
            LegalNoticeUrl = document.LegalNoticeUrl,
            TermsUrl = document.TermsUrl,
            PrivacyUrl = document.PrivacyUrl,
            IsOfficialInstance = document.IsOfficialInstance == true,
            OfficialOrigin = document.OfficialOrigin,
            PaidCommerceIsReady = document.PaidCommerce?.IsReady == true,
            FailureCode = document.PaidCommerce?.FailureCode,
            ReasonCodes = document.PaidCommerce?.ReasonCodes ?? new List<string>(),
            PublicDisclosure = document.PublicDisclosure is { } disclosure
                ? new InstanceOperatorIdentityCapabilityReadinessDto
                {
                    IsReady = disclosure.IsReady,
                    FailureCode = disclosure.FailureCode,
                    ReasonCodes = disclosure.ReasonCodes
                }
                : null,
            MessageCode = InstanceOperatorIdentityAdminMessageCode.None
        };
    }

    private static SaveInstanceOperatorIdentityRequestDto BuildRequest(
        InstanceOperatorIdentityAdminModel model) => new()
        {
            ExpectedRevision = model.Revision,
            PublicName = Normalize(model.PublicName),
            LegalName = Normalize(model.LegalName),
            OperatorKindCode = Normalize(model.OperatorKindCode),
            JurisdictionCountryCode = Normalize(model.JurisdictionCountryCode),
            RegistrationIdentifier = Normalize(model.RegistrationIdentifier),
            PublicContactEmail = Normalize(model.PublicContactEmail),
            WebsiteUrl = Normalize(model.WebsiteUrl),
            LegalNoticeUrl = Normalize(model.LegalNoticeUrl),
            TermsUrl = Normalize(model.TermsUrl),
            PrivacyUrl = Normalize(model.PrivacyUrl),
            OfficialOrigin = Normalize(model.OfficialOrigin)
        };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsStatus(Exception exception, HttpStatusCode status) =>
        exception is ApiException apiException
            && apiException.StatusCode == (int)status
        || exception.InnerException is not null
            && IsStatus(exception.InnerException, status);
}

public sealed class InstanceOperatorIdentityAdminModel
{
    public bool Exists { get; set; } = true;
    public bool CanEdit { get; set; } = true;
    public Guid? Revision { get; set; }
    public Guid? OperatorId { get; set; }
    public string? PublicName { get; set; }
    public string? LegalName { get; set; }
    public string? OperatorKindCode { get; set; }
    public string? JurisdictionCountryCode { get; set; }
    public string? RegistrationIdentifier { get; set; }
    public string? PublicContactEmail { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? LegalNoticeUrl { get; set; }
    public string? TermsUrl { get; set; }
    public string? PrivacyUrl { get; set; }
    public bool IsOfficialInstance { get; set; }
    public string? OfficialOrigin { get; set; }
    public bool PaidCommerceIsReady { get; set; }
    public InstanceOperatorIdentityCapabilityReadinessDto? PublicDisclosure { get; set; }
    public string? FailureCode { get; set; }
    public ICollection<string> ReasonCodes { get; set; } = new List<string>();
    public InstanceOperatorIdentityAdminMessageCode MessageCode { get; set; }

    public static InstanceOperatorIdentityAdminModel Missing() => new()
    {
        Exists = false,
        CanEdit = false,
        MessageCode = InstanceOperatorIdentityAdminMessageCode.NotInitialized
    };

    public static InstanceOperatorIdentityAdminModel Failed() => new()
    {
        Exists = false,
        CanEdit = false,
        MessageCode = InstanceOperatorIdentityAdminMessageCode.LoadFailed
    };

    public void Apply(InstanceOperatorIdentityAdminModel source)
    {
        Exists = source.Exists;
        CanEdit = source.CanEdit;
        Revision = source.Revision;
        OperatorId = source.OperatorId;
        PublicName = source.PublicName;
        LegalName = source.LegalName;
        OperatorKindCode = source.OperatorKindCode;
        JurisdictionCountryCode = source.JurisdictionCountryCode;
        RegistrationIdentifier = source.RegistrationIdentifier;
        PublicContactEmail = source.PublicContactEmail;
        WebsiteUrl = source.WebsiteUrl;
        LegalNoticeUrl = source.LegalNoticeUrl;
        TermsUrl = source.TermsUrl;
        PrivacyUrl = source.PrivacyUrl;
        IsOfficialInstance = source.IsOfficialInstance;
        OfficialOrigin = source.OfficialOrigin;
        PaidCommerceIsReady = source.PaidCommerceIsReady;
        PublicDisclosure = source.PublicDisclosure;
        FailureCode = source.FailureCode;
        ReasonCodes = source.ReasonCodes;
        MessageCode = source.MessageCode;
    }
}

public sealed record InstanceOperatorIdentitySaveResult
{
    public bool Success { get; init; }
    public bool IsConcurrencyConflict { get; init; }
    public InstanceOperatorIdentityAdminMessageCode MessageCode { get; init; }
    public InstanceOperatorIdentityAdminModel? Model { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyDictionary<string, string> ValidationErrors { get; init; } = new Dictionary<string, string>();

    public static InstanceOperatorIdentitySaveResult Successful(
        InstanceOperatorIdentityAdminModel model) => new()
        {
            Success = true,
            MessageCode = InstanceOperatorIdentityAdminMessageCode.Saved,
            Model = model
        };

    public static InstanceOperatorIdentitySaveResult Failed(
        InstanceOperatorIdentityAdminMessageCode messageCode,
        string? errorMessage = null,
        IReadOnlyDictionary<string, string>? validationErrors = null) => new()
        {
            MessageCode = messageCode,
            ErrorMessage = errorMessage,
            ValidationErrors = validationErrors ?? new Dictionary<string, string>()
        };

    public static InstanceOperatorIdentitySaveResult Conflict(
        InstanceOperatorIdentityAdminModel authoritative) => new()
        {
            IsConcurrencyConflict = true,
            MessageCode = InstanceOperatorIdentityAdminMessageCode.Conflict,
            Model = authoritative
        };
}

public enum InstanceOperatorIdentityAdminMessageCode
{
    None,
    NotInitialized,
    LoadFailed,
    EditUnavailable,
    SaveFailed,
    Saved,
    Conflict
}
