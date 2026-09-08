using System.Text.Json;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Validators;
using Explore.Application.Services.Federation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Scheduling;
using FluentValidation;

namespace Explore.Application.Features.Federation.Atproto.Services;

public static class AtprotoFederatedEventImportPlanFactory
{
    public static async Task<IReadOnlyList<AtprotoFederatedEventImportPlan>> CreateAsync(
        AtprotoRecord record,
        AtprotoEventProjection projection,
        IEnumerable<Guid> tenantIds,
        CancellationToken cancellationToken)
    {
        if (record.Id == Guid.Empty
            || projection.AtprotoRecordId != record.Id
            || string.IsNullOrWhiteSpace(record.Did)
            || string.IsNullOrWhiteSpace(record.Collection)
            || string.IsNullOrWhiteSpace(record.RecordKey))
        {
            throw new ValidationException("The canonical ATProto event identity is invalid.");
        }

        var importInput = new AtprotoFederatedEventImportInput(
            projection.Name,
            projection.CreatedAt)
        {
            Description = NormalizeOptional(projection.Description),
            SourceUrl = projection.SourceUrl,
            StartsAt = projection.StartsAt,
            EndsAt = projection.EndsAt,
            Mode = NormalizeToken(projection.Mode),
            Status = NormalizeToken(projection.Status),
            RsvpExpected = projection.RsvpExpected
        };
        var validator = new AtprotoFederatedEventImportInputValidator();
        await validator.ValidateAndThrowAsync(importInput, cancellationToken);
        (string timeZoneId, AtprotoThumbnailBlobCandidate? thumbnail) =
            ReadOptionalExtensions(record.RecordJson, record.Did);

        string atUri = string.IsNullOrWhiteSpace(record.Uri)
            ? $"at://{record.Did}/{record.Collection}/{record.RecordKey}"
            : record.Uri;
        return tenantIds
            .Distinct()
            .Select(tenantId => new AtprotoFederatedEventImportPlan(
                tenantId,
                record.Id,
                record.Did,
                atUri,
                importInput.Name.Trim(),
                importInput.CreatedAt!.Value,
                importInput.Description,
                AtprotoExternalUriPolicy.Normalize(importInput.SourceUrl),
                importInput.StartsAt,
                importInput.EndsAt,
                importInput.Mode,
                importInput.Status,
                importInput.RsvpExpected)
            {
                TimeZoneId = timeZoneId,
                ParticipationConfiguration = new ConfigureEventParticipationDto
                {
                    ParticipationHandlingModeId = importInput.RsvpExpected == true
                        ? (int)ParticipationHandlingModeEnum.ExternalManaged
                        : (int)ParticipationHandlingModeEnum.InformationOnly,
                    AdvanceRegistrationObligationId = importInput.RsvpExpected == true
                        ? (int)AdvanceRegistrationObligationEnum.Required
                        : (int)AdvanceRegistrationObligationEnum.NotApplicable
                },
                Thumbnail = thumbnail
            })
            .ToArray();
    }

    private static (string TimeZoneId, AtprotoThumbnailBlobCandidate? Thumbnail) ReadOptionalExtensions(
        string? recordJson,
        string did)
    {
        if (string.IsNullOrWhiteSpace(recordJson))
        {
            return (ScheduleTimeZoneResolver.UtcId, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                recordJson,
                new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (ScheduleTimeZoneResolver.UtcId, null);
            }

            string timeZoneId = ReadTimeZoneId(root);
            return (timeZoneId, ReadThumbnail(root, did));
        }
        catch (JsonException)
        {
            return (ScheduleTimeZoneResolver.UtcId, null);
        }
    }

    private static string ReadTimeZoneId(JsonElement root)
    {
        if (!root.TryGetProperty("timezone", out JsonElement timezone)
            || timezone.ValueKind != JsonValueKind.String)
        {
            return ScheduleTimeZoneResolver.UtcId;
        }

        string? value = timezone.GetString();
        if (string.IsNullOrWhiteSpace(value)
            || !TimeZoneInfo.TryConvertIanaIdToWindowsId(value.Trim(), out _))
        {
            return ScheduleTimeZoneResolver.UtcId;
        }

        try
        {
            return ScheduleTimeZoneResolver.NormalizeOrUtc(value);
        }
        catch (ArgumentException)
        {
            return ScheduleTimeZoneResolver.UtcId;
        }
    }

    private static AtprotoThumbnailBlobCandidate? ReadThumbnail(JsonElement root, string did)
    {
        if (!root.TryGetProperty("media", out JsonElement media)
            || media.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement entry in media.EnumerateArray())
        {
            if (TryReadThumbnail(entry, did, out AtprotoThumbnailBlobCandidate? candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryReadThumbnail(
        JsonElement entry,
        string did,
        out AtprotoThumbnailBlobCandidate? candidate)
    {
        candidate = null;
        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty("role", out JsonElement role)
            || role.ValueKind != JsonValueKind.String
            || !string.Equals(role.GetString(), "thumbnail", StringComparison.Ordinal)
            || !(entry.TryGetProperty("content", out JsonElement blob)
                || entry.TryGetProperty("blob", out blob))
            || blob.ValueKind != JsonValueKind.Object
            || !blob.TryGetProperty("$type", out JsonElement type)
            || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), "blob", StringComparison.Ordinal)
            || !blob.TryGetProperty("ref", out JsonElement reference)
            || reference.ValueKind != JsonValueKind.Object
            || !reference.TryGetProperty("$link", out JsonElement link)
            || link.ValueKind != JsonValueKind.String
            || !IsStructurallyValidCid(link.GetString())
            || !blob.TryGetProperty("mimeType", out JsonElement mime)
            || mime.ValueKind != JsonValueKind.String
            || !TryReadImageMime(mime.GetString(), out string? mimeType)
            || !blob.TryGetProperty("size", out JsonElement size)
            || !size.TryGetInt64(out long byteCount)
            || byteCount <= 0)
        {
            return false;
        }

        candidate = new AtprotoThumbnailBlobCandidate(did, link.GetString()!, mimeType, byteCount);
        return true;
    }

    private static bool IsStructurallyValidCid(string? value)
    {
        if (value is not { Length: >= 10 and <= 255 } || value[0] != 'b')
        {
            return false;
        }

        foreach (char character in value.AsSpan(1))
        {
            if (character is not (>= 'a' and <= 'z') and not (>= '2' and <= '7'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadImageMime(string? value, out string mimeType)
    {
        mimeType = value?.Trim() ?? string.Empty;
        if (mimeType.Length is <= 6 or > 255
            || !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (char character in mimeType.AsSpan(6))
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not '!' and not '#' and not '$' and not '%' and not '&'
                    and not '\'' and not '*' and not '+' and not '-' and not '.'
                    and not '^' and not '_' and not '`' and not '|' and not '~')
            {
                return false;
            }
        }

        return true;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        int fragmentIndex = normalized.IndexOf('#', StringComparison.Ordinal);
        return fragmentIndex >= 0 ? normalized[fragmentIndex..] : $"#{normalized}";
    }
}
