using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.CustomProperties;

internal static class CustomPropertyPurgeResponseFactory
{
    public const string PurgeAction = "CustomPropertyDefinitionPurged";

    public static CustomPropertyPurgeResultDto ToResult(
        CustomPropertyPurgeDependencySummary summary,
        bool purged,
        Guid? auditLogId,
        string reason)
        => new(
            summary.DefinitionId,
            summary.TenantId,
            summary.Scope,
            purged,
            auditLogId,
            reason,
            summary.OptionCount,
            summary.ValueCount,
            summary.ProjectionCount,
            summary.AuditLogCount,
            summary.SyncProvenanceCount);

    public static AuditLog CreateAudit(
        CustomPropertyPurgeDependencySummary summary,
        CustomPropertyPurgeResultDto result,
        Guid? actorId)
        => new()
        {
            Id = result.AuditLogId ?? Guid.CreateVersion7(),
            TenantId = summary.TenantId,
            Tenant = null!,
            EntityType = summary.Scope,
            EntityId = summary.DefinitionId.ToString(),
            Action = PurgeAction,
            OldValues = JsonSerializer.Serialize(new
            {
                summary.OptionCount,
                summary.ValueCount,
                summary.ProjectionCount,
                summary.AuditLogCount,
                summary.SyncProvenanceCount
            }),
            NewValues = JsonSerializer.Serialize(new
            {
                result.Purged,
                result.Reason,
                result.AuditLogId
            }),
            AffectedColumns = JsonSerializer.Serialize(new[] { "definition", "options" }),
            ActorId = actorId,
            Timestamp = DateTime.UtcNow
        };

    public static BaseCommandResponse<CustomPropertyPurgeResultDto> ToBlockedResponse(
        CustomPropertyPurgeDependencySummary summary,
        string reason,
        string message)
        => BaseCommandResponse.Validation(
            ToBlockingErrors(summary),
            message,
            ToResult(summary, false, null, reason));

    public static IReadOnlyList<string> ToBlockingErrors(CustomPropertyPurgeDependencySummary summary)
    {
        var errors = new List<string>();

        if (summary.ValueCount > 0)
        {
            errors.Add($"Purge blocked: {summary.ValueCount} historical custom-property value(s) reference this definition.");
        }

        if (summary.ProjectionCount > 0)
        {
            errors.Add($"Purge blocked: {summary.ProjectionCount} projection row(s) reference this definition.");
        }

        if (summary.AuditLogCount > 0)
        {
            errors.Add($"Purge blocked: {summary.AuditLogCount} audit log row(s) reference this definition.");
        }

        if (summary.SyncProvenanceCount > 0)
        {
            errors.Add($"Purge blocked: {summary.SyncProvenanceCount} template sync provenance reference(s) exist for this definition.");
        }

        return errors;
    }

    public static string GetPrimaryBlockerCategory(CustomPropertyPurgeDependencySummary summary)
    {
        if (summary.ValueCount > 0)
            return "value";

        if (summary.ProjectionCount > 0)
            return "projection";

        if (summary.AuditLogCount > 0)
            return "audit_log";

        if (summary.SyncProvenanceCount > 0)
            return "sync_provenance";

        return "none";
    }
}
