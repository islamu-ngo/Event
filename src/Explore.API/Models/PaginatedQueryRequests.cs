using System.ComponentModel.DataAnnotations;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Models;

public class PaginationQueryRequest : IValidatableObject
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => QueryValidationRules.ValidatePagination(PageNumber, PageSize);
}

public sealed class EventSeriesListQueryRequest : PaginationQueryRequest
{
    public EventSeriesListQueryRequest()
    {
        PageSize = 10;
    }

    public Guid? ActorId { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => base.Validate(validationContext)
            .Concat(QueryValidationRules.ValidateOptionalGuid(ActorId, nameof(ActorId)));
}

public sealed class EventTemplateListQueryRequest : PaginationQueryRequest
{
    public int? EventTypeId { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => base.Validate(validationContext)
            .Concat(QueryValidationRules.ValidateOptionalPositiveInt(EventTypeId, nameof(EventTypeId)));
}

public sealed class EventSessionTemplateListQueryRequest : PaginationQueryRequest
{
    public Guid EventTemplateId { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => base.Validate(validationContext)
            .Concat(QueryValidationRules.ValidateRequiredGuid(EventTemplateId, nameof(EventTemplateId)));
}

public sealed class CustomPropertyDefinitionListQueryRequest : PaginationQueryRequest
{
    public EntityTypeName EntityTypeName { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        if (!Enum.IsDefined(EntityTypeName))
        {
            yield return new ValidationResult(
                $"{nameof(EntityTypeName)} is not supported.",
                [nameof(EntityTypeName)]);
        }
    }
}

public sealed class EventCustomPropertyDefinitionListQueryRequest : PaginationQueryRequest
{
    public Guid EventId { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => base.Validate(validationContext)
            .Concat(QueryValidationRules.ValidateRequiredGuid(EventId, nameof(EventId)));
}

public sealed class EventSessionCustomPropertyDefinitionListQueryRequest : PaginationQueryRequest
{
    public Guid EventSessionId { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => base.Validate(validationContext)
            .Concat(QueryValidationRules.ValidateRequiredGuid(EventSessionId, nameof(EventSessionId)));
}

public sealed class NotificationListQueryRequest : PaginationQueryRequest
{
    public bool? IsRead { get; set; }

    public int? NotificationTypeId { get; set; }

    public int? NotificationScopeId { get; set; }

    public int? NotificationReasonId { get; set; }

    public bool? IsArchived { get; set; }

    public bool? IsSnoozed { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => base.Validate(validationContext)
            .Concat(QueryValidationRules.ValidateOptionalPositiveInt(
                NotificationTypeId,
                nameof(NotificationTypeId)))
            .Concat(QueryValidationRules.ValidateOptionalPositiveInt(
                NotificationScopeId,
                nameof(NotificationScopeId)))
            .Concat(QueryValidationRules.ValidateOptionalPositiveInt(
                NotificationReasonId,
                nameof(NotificationReasonId)));
}

public sealed class ContactShareConsentListQueryRequest : PaginationQueryRequest
{
    public Guid? EventId { get; set; }

    public string? EmailSearch { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => base.Validate(validationContext)
            .Concat(QueryValidationRules.ValidateOptionalGuid(EventId, nameof(EventId)))
            .Concat(QueryValidationRules.ValidateBoundedText(
                EmailSearch,
                nameof(EmailSearch),
                QueryValidationRules.MaxSearchTermLength));
}

public sealed class ContactShareConsentExportQueryRequest : IValidatableObject
{
    public string? Format { get; set; } = "csv";

    public Guid? EventId { get; set; }

    public string GetNormalizedFormat() => Format?.Trim().ToLowerInvariant() ?? "csv";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => QueryValidationRules.ValidateContactShareExportFormat(Format, nameof(Format))
            .Concat(QueryValidationRules.ValidateOptionalGuid(EventId, nameof(EventId)));
}

public sealed class ExternalApiKeyUsageReportQueryRequest : IValidatableObject
{
    public const int MaxRangeDays = 366;

    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public Guid? TenantId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => QueryValidationRules.ValidateRequiredDateRange(
                From,
                To,
                nameof(From),
                nameof(To),
                MaxRangeDays)
            .Concat(QueryValidationRules.ValidateOptionalGuid(TenantId, nameof(TenantId)));
}

public sealed class EmailDispatchStatusQueryRequest : IValidatableObject
{
    public const int MaxLimit = 200;

    public Guid TenantId { get; set; }

    public int Limit { get; set; } = 50;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in QueryValidationRules.ValidateRequiredGuid(TenantId, nameof(TenantId)))
        {
            yield return result;
        }

        if (Limit is < 1 or > MaxLimit)
        {
            yield return new ValidationResult(
                $"{nameof(Limit)} must be between 1 and {MaxLimit}.",
                [nameof(Limit)]);
        }
    }
}

public sealed class EmailDispatchPauseTenantQueryRequest : IValidatableObject
{
    public const int MaxReasonLength = 500;

    public string? Reason { get; set; }

    public string? GetNormalizedReason() => string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => QueryValidationRules.ValidateBoundedText(Reason, nameof(Reason), MaxReasonLength);
}

public sealed class EmailDispatchParkQueryRequest : IValidatableObject
{
    public const int MaxReasonLength = 500;

    public string? Reason { get; set; }

    public string GetNormalizedReason() => Reason?.Trim() ?? string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Reason))
        {
            yield return new ValidationResult(
                $"{nameof(Reason)} is required.",
                [nameof(Reason)]);
            yield break;
        }

        foreach (var result in QueryValidationRules.ValidateBoundedText(Reason, nameof(Reason), MaxReasonLength))
        {
            yield return result;
        }
    }
}

public sealed class EmailDispatchResolveQueryRequest : IValidatableObject
{
    public const int MaxReasonLength = 500;

    public string? Reason { get; set; }

    public string GetNormalizedReason() => Reason?.Trim() ?? string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Reason))
        {
            yield return new ValidationResult(
                $"{nameof(Reason)} is required.",
                [nameof(Reason)]);
            yield break;
        }

        foreach (var result in QueryValidationRules.ValidateBoundedText(Reason, nameof(Reason), MaxReasonLength))
        {
            yield return result;
        }
    }
}

public sealed class EmailDispatchProcessorPauseQueryRequest : IValidatableObject
{
    public string? Reason { get; set; }

    public string? GetNormalizedReason() => string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => QueryValidationRules.ValidateBoundedText(Reason, nameof(Reason), 500);
}

public sealed class EmailDispatchGlobalRateLimitQueryRequest : IValidatableObject
{
    public int RateLimitPerMinute { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RateLimitPerMinute is < 1 or > 100000)
        {
            yield return new ValidationResult(
                $"{nameof(RateLimitPerMinute)} must be between 1 and 100000.",
                [nameof(RateLimitPerMinute)]);
        }
    }
}

public sealed class EmailDispatchReconciliationQueryRequest : IValidatableObject
{
    public Explore.Application.Contracts.Persistence.EmailDispatchUnknownReconciliationOutcome Outcome { get; set; }
    public string? Reason { get; set; }
    public string? ProviderMessageId { get; set; }

    public string GetNormalizedReason() => Reason?.Trim() ?? string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Outcome is not Explore.Application.Contracts.Persistence.EmailDispatchUnknownReconciliationOutcome.Delivered
            and not Explore.Application.Contracts.Persistence.EmailDispatchUnknownReconciliationOutcome.NotDelivered)
        {
            yield return new ValidationResult(
                $"{nameof(Outcome)} must be Delivered or NotDelivered.",
                [nameof(Outcome)]);
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            yield return new ValidationResult($"{nameof(Reason)} is required.", [nameof(Reason)]);
        }

        foreach (var result in QueryValidationRules.ValidateBoundedText(Reason, nameof(Reason), 500)
            .Concat(QueryValidationRules.ValidateBoundedText(ProviderMessageId, nameof(ProviderMessageId), 500)))
        {
            yield return result;
        }
    }
}

public sealed class CustomPropertyGovernanceReportQueryRequest : PaginationQueryRequest
{
    public Guid TenantId { get; set; }

    public string? Scope { get; set; }

    public PromotionRecommendation? Recommendation { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in QueryValidationRules.ValidateRequiredGuid(TenantId, nameof(TenantId)))
        {
            yield return result;
        }

        foreach (var result in QueryValidationRules.ValidateBoundedText(
                     Scope,
                     nameof(Scope),
                     QueryValidationRules.MaxShortTextLength))
        {
            yield return result;
        }

        if (Recommendation.HasValue && !Enum.IsDefined(Recommendation.Value))
        {
            yield return new ValidationResult(
                $"{nameof(Recommendation)} is not supported.",
                [nameof(Recommendation)]);
        }
    }
}

public sealed class CustomPropertyProjectionDirtyScopesQueryRequest : PaginationQueryRequest
{
    public Guid TenantId { get; set; }

    public string? ProjectionName { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in QueryValidationRules.ValidateRequiredGuid(TenantId, nameof(TenantId)))
        {
            yield return result;
        }

        if (string.IsNullOrWhiteSpace(ProjectionName))
        {
            yield return new ValidationResult(
                $"{nameof(ProjectionName)} is required.",
                [nameof(ProjectionName)]);
            yield break;
        }

        foreach (var result in QueryValidationRules.ValidateBoundedText(
                     ProjectionName,
                     nameof(ProjectionName),
                     QueryValidationRules.MaxShortTextLength))
        {
            yield return result;
        }
    }
}

public sealed class TemplateSyncHistoryQueryRequest : IValidatableObject
{
    [FromQuery(Name = "page")]
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Page < 1)
        {
            yield return new ValidationResult(
                "Page must be greater than or equal to 1.",
                [nameof(Page)]);
        }

        foreach (var result in QueryValidationRules.ValidatePagination(1, PageSize))
        {
            yield return result;
        }
    }
}
