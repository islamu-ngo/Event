using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Application.Features.ContactShareConsents.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ContactShareConsents.Handlers.Commands;

public class ExportSharedContactsCommandHandler : IRequestHandler<ExportSharedContactsCommand, BaseCommandResponse<SharedContactExportResultDto>>
{
    private readonly IEventContactShareConsentRepository _consentRepository;
    private readonly IEventContactShareExportRepository _exportRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<ExportSharedContactsCommandHandler> _logger;

    public ExportSharedContactsCommandHandler(
        IEventContactShareConsentRepository consentRepository,
        IEventContactShareExportRepository exportRepository,
        IActorRepository actorRepository,
        IOrganizationRepository organizationRepository,
        ILogger<ExportSharedContactsCommandHandler> logger)
    {
        _consentRepository = consentRepository;
        _exportRepository = exportRepository;
        _actorRepository = actorRepository;
        _organizationRepository = organizationRepository;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<SharedContactExportResultDto>> Handle(
        ExportSharedContactsCommand request, CancellationToken cancellationToken)
    {
        var format = NormalizeFormat(request.Format);
        if (format is null)
        {
            return BaseCommandResponse.Validation<SharedContactExportResultDto>(
                ["Invalid export format."],
                "Invalid export format. Supported formats: csv, tsv.");
        }

        // Validate actor is an approved organisation
        var actor = await _actorRepository.GetById(request.RecipientActorId);
        if (actor?.OrganizationId == null || actor.OrganizationId != request.OrganizationId)
        {
            return BaseCommandResponse.Validation<SharedContactExportResultDto>(
                ["Actor is not an organisation."],
                "Recipient actor is not an organisation.");
        }

        var org = await _organizationRepository.GetById(actor.OrganizationId.Value);
        if (org is null || !org.TenantParticipations.Any(
                participation => participation.TenantId == request.TenantId &&
                    participation.ApprovalStatusId == (int)ApprovalStatusEnum.Approved))
        {
            return BaseCommandResponse.Validation<SharedContactExportResultDto>(
                ["Organisation is not approved."],
                "Organisation is not approved.");
        }

        // Fetch granted consents
        var consents = await _consentRepository.GetGrantedForExport(
            request.TenantId, request.RecipientActorId, request.EventId, request.ConsentPurposeCode);

        var separator = format == "tsv" ? '\t' : ',';
        var contentType = format == "tsv" ? "text/tab-separated-values" : "text/csv";
        var extension = format == "tsv" ? "tsv" : "csv";
        var orgName = org.FullName;
        var fileName = $"shared-contacts-{SanitizeFileNameSegment(orgName)}-{DateTime.UtcNow:yyyyMMdd}.{extension}";

        // Build file content
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(separator, [
            "Email", "GrantedAtUtc", "EventId", "EventTitle",
            "OrganizationId", "OrganizationName", "PurposeCode"
        ]));

        foreach (var consent in consents)
        {
            sb.AppendLine(string.Join(separator, [
                EscapeField(consent.EmailSnapshot, separator),
                consent.GrantedAt.ToString("o", CultureInfo.InvariantCulture),
                "",
                "",
                actor.OrganizationId.Value.ToString(),
                EscapeField(orgName, separator),
                consent.PurposeCode
            ]));
        }

        var fileBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

        var exportedAt = DateTime.UtcNow;
        var export = EventContactShareExport.Request(request.TenantId, request.RecipientActorId, request.EventId,
            request.ExportedByUserId, format, request.PurposeCode, "[\"Email\",\"GrantedAtUtc\",\"EventId\",\"EventTitle\",\"OrganizationId\",\"OrganizationName\",\"PurposeCode\"]",
            request.PolicyVersion, exportedAt);
        string includedFields = "[\"Email\",\"GrantedAtUtc\",\"EventId\",\"EventTitle\",\"OrganizationId\",\"OrganizationName\",\"PurposeCode\"]";
        export.Complete(includedFields, Convert.ToHexStringLower(SHA256.HashData(fileBytes)), consents.Count, exportedAt);
        foreach (EventContactShareConsent consent in consents)
        {
            export.AddItem(EventContactShareExportItem.Create(export.Id, consent.Id, JsonSerializer.Serialize(new
            {
                Email = consent.EmailSnapshot,
                consent.GrantedAt,
                consent.PurposeCode
            })));
        }

        export = await _exportRepository.Create(export);

        _logger.LogInformation(
            "Exported {RowCount} shared contacts for actor {ActorId} by user {UserId} in {Format} format (export {ExportId})",
            consents.Count, request.RecipientActorId, request.ExportedByUserId, format, export.Id);

        return BaseCommandResponse.Success(
            new SharedContactExportResultDto
            {
                ExportId = export.Id,
                RowCount = consents.Count,
                Format = format,
                FileContent = fileBytes,
                FileName = fileName,
                ContentType = contentType
            },
            $"Exported {consents.Count} contacts.");
    }

    private static string? NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Any(char.IsControl))
        {
            return null;
        }

        var normalizedFormat = format.Trim().ToLowerInvariant();
        return normalizedFormat is "csv" or "tsv" ? normalizedFormat : null;
    }

    private static string EscapeField(string value, char separator)
    {
        value = SanitizeSpreadsheetCell(value);

        if (separator == '\t')
            return value.Replace("\t", " ").Replace("\n", " ").Replace("\r", " ");

        if (value.Contains(separator) || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    private static string SanitizeSpreadsheetCell(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmedStart = value.TrimStart();
        return trimmedStart.Length > 0 && trimmedStart[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
    }

    private static string SanitizeFileNameSegment(string value)
    {
        const int maxSegmentLength = 60;

        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var character in value.ToLowerInvariant())
        {
            if (builder.Length >= maxSegmentLength)
            {
                break;
            }

            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var segment = builder.ToString().Trim('-');
        return segment.Length == 0 ? "organization" : segment;
    }
}
