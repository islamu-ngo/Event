using Explore.Application.DTOs.Event;

namespace Explore.API.Mcp;

internal static class EventManagementMcpReadinessMapper
{
    public static EventMcpPublishReadinessDescriptor Map(
        EventPublishReadinessDto dto,
        int maxErrors,
        int maxTextLength,
        ICollection<string> truncatedFields)
    {
        var errors = dto.Errors
            .Take(maxErrors)
            .Select(error => new EventMcpPublishReadinessIssueDescriptor(
                TrimToEmpty(error.Code, maxTextLength, truncatedFields, nameof(error.Code)),
                TrimToEmpty(error.FieldPath, maxTextLength, truncatedFields, nameof(error.FieldPath)),
                TrimToEmpty(error.Severity, maxTextLength, truncatedFields, nameof(error.Severity)),
                TrimToEmpty(error.Message, maxTextLength, truncatedFields, nameof(error.Message))))
            .ToArray();

        if (dto.Errors.Count > maxErrors)
        {
            truncatedFields.Add("PublishReadiness.Errors");
        }

        return new EventMcpPublishReadinessDescriptor(
            dto.EventId,
            dto.IsReady,
            dto.Errors.Count,
            dto.Errors.Count > maxErrors,
            errors);
    }

    private static string TrimToEmpty(
        string? value,
        int maxLength,
        ICollection<string> truncatedFields,
        string fieldName)
        => TrimToNull(value, maxLength, truncatedFields, fieldName) ?? string.Empty;

    private static string? TrimToNull(
        string? value,
        int maxLength,
        ICollection<string> truncatedFields,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        truncatedFields.Add(fieldName);
        return trimmed[..maxLength];
    }
}
