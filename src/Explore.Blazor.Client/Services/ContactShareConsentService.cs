using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public class ContactShareConsentService : IContactShareConsentService
{
    private static readonly HashSet<string> SupportedExportFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "csv",
        "tsv"
    };

    private readonly IContactShareConsentClient _apiClient;
    private readonly ILogger<ContactShareConsentService> _logger;

    public ContactShareConsentService(IContactShareConsentClient apiClient, ILogger<ContactShareConsentService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> CheckConsentForOrganizerAsync(Guid organizerActorId, CancellationToken ct = default)
    {
        try
        {
            return await _apiClient.CheckConsentForOrganizerAsync(organizerActorId, cancellationToken: ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CONSENT SERVICE] Error checking consent for organizer {ActorId}", organizerActorId);
            return false;
        }
    }

    public async Task<List<UserConsentViewModel>> GetMyConsentsAsync(CancellationToken ct = default)
    {
        try
        {
            var consents = await _apiClient.GetUserContactShareConsentsAsync(cancellationToken: ct);
            return consents?.Select(c => new UserConsentViewModel
            {
                Id = c.Id ?? Guid.Empty,
                OrganizationName = c.OrganizationName ?? "Unknown Organization",
                OrganizationActorId = c.RecipientActorId ?? Guid.Empty,
                OrganizationProfilePictureUri = null,
                PurposeCode = c.PurposeCode ?? string.Empty,
                Status = MapConsentStatus(c.Status),
                EmailSnapshot = c.EmailSnapshot ?? string.Empty,
                GrantedAt = c.GrantedAt ?? DateTimeOffset.MinValue,
                WithdrawnAt = c.WithdrawnAt,
                SourceEventTitle = c.SourceEventTitle
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CONSENT SERVICE] Error fetching user consents");
            return [];
        }
    }

    public async Task<bool> WithdrawConsentAsync(Guid consentId, CancellationToken ct = default)
    {
        try
        {
            var result = await _apiClient.WithdrawContactShareConsentAsync(consentId, cancellationToken: ct);
            return result?.Success ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CONSENT SERVICE] Error withdrawing consent {ConsentId}", consentId);
            return false;
        }
    }

    public async Task<List<SharedContactViewModel>> GetOrganizationSharedContactsAsync(
        Guid organizationActorId,
        Guid? eventId = null,
        string? searchEmail = null,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _apiClient.GetOrganizationSharedContactsAsync(organizationActorId, eventId, searchEmail, pageNumber, pageSize, cancellationToken: ct);
            return result?.Items?.Select(c => new SharedContactViewModel
            {
                ConsentId = c.ConsentId ?? Guid.Empty,
                Email = c.Email ?? string.Empty,
                GrantedAt = c.GrantedAt ?? DateTimeOffset.MinValue,
                SourceEventId = c.SourceEventId,
                SourceEventTitle = c.SourceEventTitle,
                PurposeCode = c.PurposeCode ?? string.Empty
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CONSENT SERVICE] Error fetching shared contacts for org {ActorId}", organizationActorId);
            return [];
        }
    }

    public async Task<(byte[] FileBytes, string FileName)?> ExportSharedContactsAsync(
        Guid organizationActorId,
        string format = "csv",
        Guid? eventId = null,
        CancellationToken ct = default)
    {
        try
        {
            var normalizedFormat = NormalizeExportFormat(format);
            if (normalizedFormat is null)
            {
                _logger.LogWarning("[CONSENT SERVICE] Unsupported shared contact export format requested");
                return null;
            }

            var result = await _apiClient.ExportOrganizationSharedContactsAsync(organizationActorId, normalizedFormat, eventId, cancellationToken: ct);
            if (result?.FileContents == null || result.FileContents.Length == 0) return null;

            var extension = normalizedFormat == "tsv" ? "tsv" : "csv";
            var fileName = result.FileDownloadName
                ?? $"shared-contacts-{organizationActorId:N}-{DateTimeOffset.UtcNow:yyyyMMdd}.{extension}";
            return (result.FileContents, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CONSENT SERVICE] Error exporting contacts for org {ActorId}", organizationActorId);
            return null;
        }
    }

    private static string MapConsentStatus(int? status) => status switch
    {
        1 => "Granted",
        2 => "Withdrawn",
        _ => "Unknown"
    };

    private static string? NormalizeExportFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Any(char.IsControl))
        {
            return null;
        }

        var normalizedFormat = format.Trim().ToLowerInvariant();
        return SupportedExportFormats.Contains(normalizedFormat) ? normalizedFormat : null;
    }
}
