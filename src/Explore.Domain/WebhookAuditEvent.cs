using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Text.Json;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class WebhookAuditEvent
{
    public const int MaxPrincipalReferenceLength = 300;
    public const int MaxConfigurationVersionLength = 200;
    public const int MaxCorrelationIdLength = 100;
    public const int MaxReasonCodeLength = 200;
    public const int MaxSafeMetadataLength = 16 * 1024;

    public Guid Id { get; private set; }
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public int PrincipalKindId { get; private set; }
    public WebhookAuditPrincipalKindLookup PrincipalKindLookup { get; private set; } = null!;
    public string PrincipalReference { get; private set; } = string.Empty;
    public int EffectiveScopeKindId { get; private set; }
    public WebhookAuditScopeKindLookup EffectiveScopeKindLookup { get; private set; } = null!;
    public Guid? EffectiveScopeId { get; private set; }
    public int ActionId { get; private set; }
    public WebhookAuditActionLookup ActionLookup { get; private set; } = null!;
    public int TargetKindId { get; private set; }
    public WebhookAuditTargetKindLookup TargetKindLookup { get; private set; } = null!;
    public Guid TargetId { get; private set; }
    public string? SafeBeforeJson { get; private set; }
    public string? SafeAfterJson { get; private set; }
    public string? ConfigurationVersion { get; private set; }
    public string? CorrelationId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public int OutcomeId { get; private set; }
    public WebhookAuditOutcomeLookup OutcomeLookup { get; private set; } = null!;
    public DateTime OccurredAt { get; private set; }
    public string RetentionPolicyVersion { get; private set; } = string.Empty;
    public DateTime RetentionUntil { get; private set; }

    [NotMapped]
    public WebhookAuditPrincipalKind PrincipalKind => (WebhookAuditPrincipalKind)PrincipalKindId;

    [NotMapped]
    public WebhookAuditScopeKind EffectiveScopeKind => (WebhookAuditScopeKind)EffectiveScopeKindId;

    [NotMapped]
    public WebhookAuditAction Action => (WebhookAuditAction)ActionId;

    [NotMapped]
    public WebhookAuditTargetKind TargetKind => (WebhookAuditTargetKind)TargetKindId;

    [NotMapped]
    public WebhookAuditOutcome Outcome => (WebhookAuditOutcome)OutcomeId;

    public static WebhookAuditEvent Create(
        Guid? tenantId,
        WebhookAuditPrincipalKind principalKind,
        string principalReference,
        WebhookAuditScopeKind effectiveScopeKind,
        Guid? effectiveScopeId,
        WebhookAuditAction action,
        WebhookAuditTargetKind targetKind,
        Guid targetId,
        string? safeBeforeJson,
        string? safeAfterJson,
        string? configurationVersion,
        string? correlationId,
        string reasonCode,
        WebhookAuditOutcome outcome,
        string retentionPolicyVersion,
        DateTime retentionUntil)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        }
        ArgumentOutOfRangeException.ThrowIfEqual(targetId, Guid.Empty);
        if (!Enum.IsDefined(principalKind)) throw new ArgumentOutOfRangeException(nameof(principalKind));
        if (!Enum.IsDefined(effectiveScopeKind)) throw new ArgumentOutOfRangeException(nameof(effectiveScopeKind));
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (!Enum.IsDefined(targetKind)) throw new ArgumentOutOfRangeException(nameof(targetKind));
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (retentionUntil.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Retention timestamp must use UTC kind.", nameof(retentionUntil));
        }

        if (!effectiveScopeId.HasValue || effectiveScopeId.Value == Guid.Empty)
        {
            throw new ArgumentException("Webhook audit evidence requires an effective owner scope.", nameof(effectiveScopeId));
        }

        if (effectiveScopeKind == WebhookAuditScopeKind.Instance && tenantId is not null)
        {
            throw new ArgumentException("Instance audit scope cannot be tenant-bound.", nameof(tenantId));
        }

        if (effectiveScopeKind != WebhookAuditScopeKind.Instance && tenantId is null)
        {
            throw new ArgumentException("Non-instance audit scope requires a tenant.", nameof(tenantId));
        }

        if (effectiveScopeKind == WebhookAuditScopeKind.Tenant && effectiveScopeId != tenantId)
        {
            throw new ArgumentException("Tenant audit scope must match the event tenant.", nameof(effectiveScopeId));
        }

        return new WebhookAuditEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            PrincipalKindId = (int)principalKind,
            PrincipalReference = NormalizeRequired(
                principalReference,
                MaxPrincipalReferenceLength,
                nameof(principalReference)),
            EffectiveScopeKindId = (int)effectiveScopeKind,
            EffectiveScopeId = effectiveScopeId,
            ActionId = (int)action,
            TargetKindId = (int)targetKind,
            TargetId = targetId,
            SafeBeforeJson = NormalizeSafeJson(safeBeforeJson, nameof(safeBeforeJson)),
            SafeAfterJson = NormalizeSafeJson(safeAfterJson, nameof(safeAfterJson)),
            ConfigurationVersion = NormalizeOptional(
                configurationVersion,
                MaxConfigurationVersionLength,
                nameof(configurationVersion)),
            CorrelationId = NormalizeOptional(
                correlationId ?? Activity.Current?.TraceId.ToHexString(),
                MaxCorrelationIdLength,
                nameof(correlationId)),
            ReasonCode = NormalizeReasonCode(reasonCode),
            OutcomeId = (int)outcome,
            RetentionPolicyVersion = NormalizeRequired(
                retentionPolicyVersion,
                MaxConfigurationVersionLength,
                nameof(retentionPolicyVersion)),
            RetentionUntil = retentionUntil
        };
    }

    private static string? NormalizeSafeJson(string? json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        if (json.Length > MaxSafeMetadataLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Safe metadata cannot exceed {MaxSafeMetadataLength} characters.");
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Safe audit metadata must be a JSON object.", parameterName);
        }

        ValidateSafeElement(document.RootElement, parameterName);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static void ValidateSafeElement(JsonElement element, string parameterName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (IsForbiddenProperty(property.Name))
                {
                    throw new ArgumentException($"Audit metadata property '{property.Name}' is not safe.", parameterName);
                }

                ValidateSafeElement(property.Value, parameterName);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateSafeElement(item, parameterName);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (value is not null &&
                (value.Contains("whsec_", StringComparison.OrdinalIgnoreCase)
                 || value.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
                 || value.Contains("://", StringComparison.Ordinal)))
            {
                throw new ArgumentException("Audit metadata contains a forbidden credential or URL value.", parameterName);
            }
        }
    }

    private static bool IsForbiddenProperty(string propertyName)
    {
        var normalized = propertyName.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized is "payload" or "payloadjson" or "payloadbytes" or "rawpayload"
            or "secret" or "secretref" or "secretvalue"
            or "url" or "endpointurl" or "portalurl" or "portaltoken" or "accesstoken"
            || normalized.Contains("signature", StringComparison.Ordinal)
            || normalized.Contains("rawprovidererror", StringComparison.Ordinal)
            || normalized.Contains("providererror", StringComparison.Ordinal);
    }

    private static string NormalizeReasonCode(string value)
    {
        var normalized = NormalizeRequired(value, MaxReasonCodeLength, nameof(value)).ToLowerInvariant();
        if (normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-' and not '.' and not ':'))
        {
            throw new ArgumentException("Audit reason codes must use only ASCII letters, digits, underscore, dash, dot, or colon.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.");
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maximumLength, parameterName);
}
