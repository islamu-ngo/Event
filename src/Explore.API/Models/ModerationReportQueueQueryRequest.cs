using System.ComponentModel.DataAnnotations;
using Explore.Domain.Enums;

namespace Explore.API.Models;

public sealed class ModerationReportQueueQueryRequest : PaginationQueryRequest
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "created_at",
        "createdAt",
        "updated_at",
        "updatedAt",
        "priority",
        "status",
        "reason_code",
        "reasonCode"
    };

    public List<string>? Statuses { get; set; }
    public List<string>? CaseStatuses { get; set; }
    public string? Priority { get; set; }
    public string? QueueCode { get; set; }
    public Guid? AssignedModeratorUserId { get; set; }
    public bool UnassignedOnly { get; set; }
    public bool OpenOnly { get; set; } = true;
    public string? ReasonCode { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;

    public IReadOnlyCollection<EventReportStatus> ToStatuses()
        => ParseMany<EventReportStatus>(Statuses);

    public IReadOnlyCollection<EventReportCaseStatus> ToCaseStatuses()
        => ParseMany<EventReportCaseStatus>(CaseStatuses);

    public EventReportPriority? ToPriority()
        => TryParseCode<EventReportPriority>(Priority, out var priority) ? priority : null;

    public string? ToSortBy()
        => SortBy?.Trim() switch
        {
            "createdAt" => "created_at",
            "updatedAt" => "updated_at",
            "reasonCode" => "reason_code",
            { Length: > 0 } value => value,
            _ => null
        };

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in ValidateCodeList<EventReportStatus>(Statuses, nameof(Statuses)))
        {
            yield return result;
        }

        foreach (var result in ValidateCodeList<EventReportCaseStatus>(CaseStatuses, nameof(CaseStatuses)))
        {
            yield return result;
        }

        foreach (var result in ValidateOptionalCode<EventReportPriority>(Priority, nameof(Priority)))
        {
            yield return result;
        }

        foreach (var result in QueryValidationRules.ValidateBoundedText(
                     QueueCode,
                     nameof(QueueCode),
                     QueryValidationRules.MaxShortTextLength))
        {
            yield return result;
        }

        foreach (var result in QueryValidationRules.ValidateOptionalGuid(
                     AssignedModeratorUserId,
                     nameof(AssignedModeratorUserId)))
        {
            yield return result;
        }

        foreach (var result in QueryValidationRules.ValidateBoundedText(
                     ReasonCode,
                     nameof(ReasonCode),
                     QueryValidationRules.MaxShortTextLength))
        {
            yield return result;
        }

        foreach (var result in ValidateSortBy())
        {
            yield return result;
        }
    }

    private static IReadOnlyCollection<TEnum> ParseMany<TEnum>(IReadOnlyCollection<string>? values)
        where TEnum : struct, Enum
        => ExpandTokens(values)
            .Select(value => TryParseCode<TEnum>(value, out var parsed) ? parsed : (TEnum?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();

    private static IEnumerable<ValidationResult> ValidateCodeList<TEnum>(
        IReadOnlyCollection<string>? values,
        string memberName)
        where TEnum : struct, Enum
    {
        var tokens = ExpandTokens(values).ToArray();
        if (tokens.Length > QueryValidationRules.MaxFilterListCount)
        {
            yield return new ValidationResult(
                $"{memberName} must contain at most {QueryValidationRules.MaxFilterListCount} values.",
                [memberName]);
        }

        foreach (var value in tokens)
        {
            if (!TryParseCode<TEnum>(value, out _))
            {
                yield return new ValidationResult(
                    $"{memberName} contains unsupported value '{value}'.",
                    [memberName]);
            }
        }
    }

    private static IEnumerable<ValidationResult> ValidateOptionalCode<TEnum>(
        string? value,
        string memberName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        if (!TryParseCode<TEnum>(value, out _))
        {
            yield return new ValidationResult(
                $"{memberName} contains unsupported value '{value}'.",
                [memberName]);
        }
    }

    private IEnumerable<ValidationResult> ValidateSortBy()
    {
        if (string.IsNullOrWhiteSpace(SortBy))
        {
            yield break;
        }

        var normalized = SortBy.Trim();
        if (!AllowedSortFields.Contains(normalized))
        {
            yield return new ValidationResult(
                "SortBy must be one of: created_at, updated_at, priority, status, reason_code.",
                [nameof(SortBy)]);
        }
    }

    private static IEnumerable<string> ExpandTokens(IReadOnlyCollection<string>? values)
    {
        if (values is null)
        {
            yield break;
        }

        foreach (var value in values)
        {
            foreach (var token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                yield return token;
            }
        }
    }

    private static bool TryParseCode<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Normalize(value);
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (Normalize(candidate.ToString()) == normalized)
            {
                parsed = candidate;
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
