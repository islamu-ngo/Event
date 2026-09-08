using System.ComponentModel.DataAnnotations;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Responses;
using Explore.Application.Specifications.Events;

namespace Explore.API.Models;

internal static class QueryValidationRules
{
    private const string PageNumberMemberName = "PageNumber";
    private const string PageSizeMemberName = "PageSize";
    private const string SortByMemberName = "SortBy";
    private const string DateFromMemberName = "DateFrom";
    private const string DateToMemberName = "DateTo";

    public const int MaxSearchTermLength = 200;
    public const int MaxShortTextLength = 100;
    public const int MaxCustomPropertyValueLength = 500;
    public const int MaxFilterListCount = 50;
    public const int MaxCustomPropertyFilterCount = 10;

    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "date",
        "title",
        "views",
        "createdAt"
    };

    private static readonly HashSet<string> AllowedFilterModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "and",
        "or"
    };

    private static readonly HashSet<string> AllowedContactShareExportFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "csv",
        "tsv"
    };

    public static IEnumerable<ValidationResult> ValidatePagination(int pageNumber, int pageSize)
    {
        if (pageNumber < 1)
        {
            yield return new ValidationResult(
                "PageNumber must be greater than or equal to 1.",
                [PageNumberMemberName]);
        }

        if (pageSize is < 1 or > PaginatedResult<object>.MaxPageSize)
        {
            yield return new ValidationResult(
                $"PageSize must be between 1 and {PaginatedResult<object>.MaxPageSize}.",
                [PageSizeMemberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateBoundedText(
        string? value,
        string memberName,
        int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield break;
        }

        if (value.Length > maxLength)
        {
            yield return new ValidationResult(
                $"{memberName} must not exceed {maxLength} characters.",
                [memberName]);
        }

        if (ContainsControlCharacter(value))
        {
            yield return new ValidationResult(
                $"{memberName} contains unsupported control characters.",
                [memberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateSortBy(string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            yield break;
        }

        if (!AllowedSortFields.Contains(sortBy.Trim()))
        {
            yield return new ValidationResult(
                "SortBy must be one of: date, title, views, createdAt.",
                [SortByMemberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateTemporalView(string? value, string memberName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        if (!Enum.TryParse<TemporalView>(value.Trim(), ignoreCase: true, out var view)
            || !Enum.IsDefined(view))
        {
            yield return new ValidationResult(
                $"{memberName} must be one of: upcoming, ongoing, past, upcomingAndOngoing, all.",
                [memberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateFilterMode(string? value, string memberName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        if (!AllowedFilterModes.Contains(value.Trim()))
        {
            yield return new ValidationResult(
                $"{memberName} must be either 'and' or 'or'.",
                [memberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateContactShareExportFormat(string? value, string memberName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return new ValidationResult(
                $"{memberName} must be one of: csv, tsv.",
                [memberName]);
            yield break;
        }

        if (ContainsControlCharacter(value))
        {
            yield return new ValidationResult(
                $"{memberName} contains unsupported control characters.",
                [memberName]);
            yield break;
        }

        if (!AllowedContactShareExportFormats.Contains(value.Trim()))
        {
            yield return new ValidationResult(
                $"{memberName} must be one of: csv, tsv.",
                [memberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateDateRange(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            yield return new ValidationResult(
                "DateFrom must be earlier than or equal to DateTo.",
                [DateFromMemberName, DateToMemberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateRequiredDateRange(
        DateOnly from,
        DateOnly to,
        string fromMemberName,
        string toMemberName,
        int maxRangeDays)
    {
        var hasFrom = from != default;
        var hasTo = to != default;

        if (!hasFrom)
        {
            yield return new ValidationResult(
                $"{fromMemberName} is required.",
                [fromMemberName]);
        }

        if (!hasTo)
        {
            yield return new ValidationResult(
                $"{toMemberName} is required.",
                [toMemberName]);
        }

        if (!hasFrom || !hasTo)
        {
            yield break;
        }

        if (from > to)
        {
            yield return new ValidationResult(
                $"{fromMemberName} must be earlier than or equal to {toMemberName}.",
                [fromMemberName, toMemberName]);
            yield break;
        }

        var rangeDays = to.DayNumber - from.DayNumber + 1;
        if (rangeDays > maxRangeDays)
        {
            yield return new ValidationResult(
                $"{fromMemberName} and {toMemberName} must span at most {maxRangeDays} days.",
                [fromMemberName, toMemberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateOptionalGuid(Guid? value, string memberName)
    {
        if (value == Guid.Empty)
        {
            yield return new ValidationResult(
                $"{memberName} must not be an empty GUID.",
                [memberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateRequiredGuid(Guid value, string memberName)
    {
        if (value == Guid.Empty)
        {
            yield return new ValidationResult(
                $"{memberName} is required.",
                [memberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateOptionalPositiveInt(int? value, string memberName)
    {
        if (value is <= 0)
        {
            yield return new ValidationResult(
                $"{memberName} must be greater than 0.",
                [memberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateGuidList(
        IReadOnlyCollection<Guid>? values,
        string memberName)
    {
        return ValidateList(
            values,
            memberName,
            value => value != Guid.Empty,
            $"{memberName} values must not be empty GUIDs.");
    }

    public static IEnumerable<ValidationResult> ValidatePositiveIntList(
        IReadOnlyCollection<int>? values,
        string memberName)
    {
        return ValidateList(
            values,
            memberName,
            value => value > 0,
            $"{memberName} values must be greater than 0.");
    }

    public static IEnumerable<ValidationResult> ValidateCustomPropertyFilters(
        IReadOnlyCollection<CustomPropertyFilterCriterion>? filters,
        string memberName)
    {
        if (filters is null)
        {
            yield break;
        }

        if (filters.Count > MaxCustomPropertyFilterCount)
        {
            yield return new ValidationResult(
                $"{memberName} must contain at most {MaxCustomPropertyFilterCount} filters.",
                [memberName]);
        }

        var index = 0;
        foreach (var filter in filters)
        {
            foreach (var result in ValidateCustomPropertyFilter(filter, $"{memberName}[{index}]"))
            {
                yield return result;
            }

            index++;
        }
    }

    private static IEnumerable<ValidationResult> ValidateCustomPropertyFilter(
        CustomPropertyFilterCriterion filter,
        string memberName)
    {
        foreach (var result in ValidateRequiredIdentifier(filter.Namespace, $"{memberName}.Namespace"))
        {
            yield return result;
        }

        foreach (var result in ValidateRequiredIdentifier(filter.Key, $"{memberName}.Key"))
        {
            yield return result;
        }

        foreach (var result in ValidateBoundedText(filter.Value, $"{memberName}.Value", MaxCustomPropertyValueLength))
        {
            yield return result;
        }

        if (filter.OptionIds is { Count: > MaxFilterListCount })
        {
            yield return new ValidationResult(
                $"{memberName}.OptionIds must contain at most {MaxFilterListCount} values.",
                [$"{memberName}.OptionIds"]);
        }

        if (filter.OptionId == Guid.Empty)
        {
            yield return new ValidationResult(
                $"{memberName}.OptionId must not be an empty GUID.",
                [$"{memberName}.OptionId"]);
        }

        if (filter.OptionIds?.Any(optionId => optionId == Guid.Empty) == true)
        {
            yield return new ValidationResult(
                $"{memberName}.OptionIds values must not be empty GUIDs.",
                [$"{memberName}.OptionIds"]);
        }

        switch (filter.Operator)
        {
            case CustomPropertyFilterOperator.Equals:
            case CustomPropertyFilterOperator.Contains:
                if (string.IsNullOrWhiteSpace(filter.Value))
                {
                    yield return new ValidationResult(
                        $"{memberName}.Value is required for {filter.Operator}.",
                        [$"{memberName}.Value"]);
                }
                break;
            case CustomPropertyFilterOperator.OptionEquals:
                if (!filter.OptionId.HasValue)
                {
                    yield return new ValidationResult(
                        $"{memberName}.OptionId is required for OptionEquals.",
                        [$"{memberName}.OptionId"]);
                }
                break;
            case CustomPropertyFilterOperator.OptionIn:
                if (filter.OptionIds is not { Count: > 0 })
                {
                    yield return new ValidationResult(
                        $"{memberName}.OptionIds is required for OptionIn.",
                        [$"{memberName}.OptionIds"]);
                }
                break;
            case CustomPropertyFilterOperator.NumberRange:
                if (!filter.MinNumber.HasValue && !filter.MaxNumber.HasValue)
                {
                    yield return new ValidationResult(
                        $"{memberName}.MinNumber or {memberName}.MaxNumber is required for NumberRange.",
                        [$"{memberName}.MinNumber", $"{memberName}.MaxNumber"]);
                }

                if (filter.MinNumber.HasValue && filter.MaxNumber.HasValue && filter.MinNumber > filter.MaxNumber)
                {
                    yield return new ValidationResult(
                        $"{memberName}.MinNumber must be less than or equal to {memberName}.MaxNumber.",
                        [$"{memberName}.MinNumber", $"{memberName}.MaxNumber"]);
                }
                break;
            case CustomPropertyFilterOperator.DateRange:
                if (!filter.DateFrom.HasValue && !filter.DateTo.HasValue)
                {
                    yield return new ValidationResult(
                        $"{memberName}.DateFrom or {memberName}.DateTo is required for DateRange.",
                        [$"{memberName}.DateFrom", $"{memberName}.DateTo"]);
                }

                if (filter.DateFrom.HasValue && filter.DateTo.HasValue && filter.DateFrom > filter.DateTo)
                {
                    yield return new ValidationResult(
                        $"{memberName}.DateFrom must be earlier than or equal to {memberName}.DateTo.",
                        [$"{memberName}.DateFrom", $"{memberName}.DateTo"]);
                }
                break;
            case CustomPropertyFilterOperator.Exists:
            case CustomPropertyFilterOperator.BooleanTrue:
                break;
            default:
                yield return new ValidationResult(
                    $"{memberName}.Operator is not supported.",
                    [$"{memberName}.Operator"]);
                break;
        }
    }

    private static IEnumerable<ValidationResult> ValidateRequiredIdentifier(string? value, string memberName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return new ValidationResult($"{memberName} is required.", [memberName]);
            yield break;
        }

        foreach (var result in ValidateBoundedText(value, memberName, MaxShortTextLength))
        {
            yield return result;
        }
    }

    private static IEnumerable<ValidationResult> ValidateList<T>(
        IReadOnlyCollection<T>? values,
        string memberName,
        Func<T, bool> isValidItem,
        string? invalidItemMessage)
    {
        if (values is null)
        {
            yield break;
        }

        if (values.Count > MaxFilterListCount)
        {
            yield return new ValidationResult(
                $"{memberName} must contain at most {MaxFilterListCount} values.",
                [memberName]);
        }

        if (invalidItemMessage is not null && values.Any(value => !isValidItem(value)))
        {
            yield return new ValidationResult(invalidItemMessage, [memberName]);
        }
    }

    private static bool ContainsControlCharacter(string value)
        => value.Any(char.IsControl);
}
