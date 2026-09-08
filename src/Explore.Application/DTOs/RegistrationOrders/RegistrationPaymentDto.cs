namespace Explore.Application.DTOs.RegistrationOrders;

using Explore.Application.Responses;
using System.Text.Json.Serialization;

public sealed record RegistrationPaymentDto
{
    public Guid Id { get; init; }
    public Guid RegistrationOrderId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public bool HostedRedirectAvailable { get; init; }
    public bool RetryAvailable { get; init; }
    public string? FailureCode { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastUpdatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public long RefundedAmountMinor { get; init; }
    public long RefundPendingAmountMinor { get; init; }
    public IReadOnlyList<RegistrationRefundDto> Refunds { get; init; } = [];
    public IReadOnlyList<RegistrationPaymentDisputeDto> Disputes { get; init; } = [];
    public IReadOnlyList<RegistrationMaterialChangeChoiceDto> MaterialChangeChoices { get; init; } = [];
    public bool BuyerRefundRequestAvailable { get; init; }
    public bool OrganizerRefundAvailable { get; init; }
    public long CapturedAmountMinor { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public int CurrencyMinorUnitDigits { get; init; }
}

public sealed record RegistrationMaterialChangeChoiceDto
{
    public Guid Id { get; init; }
    public Guid CampaignId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? DecidedAt { get; init; }
}

public sealed record RegistrationMaterialChangeChoiceRequestDto
{
    public Guid CampaignId { get; init; }
    public string ChoiceCode { get; init; } = string.Empty;
}

public sealed record RegistrationMaterialChangeChoiceCommandResultDto : BaseCommandResponse<Guid>
{
    private RegistrationMaterialChangeChoiceCommandResultDto(BaseCommandResponse<Guid> state, RegistrationMaterialChangeChoiceDto? choice, RegistrationRefundDto? refund) : base(state, true)
    {
        Choice = choice;
        Refund = refund;
    }

    [JsonConstructor]
    internal RegistrationMaterialChangeChoiceCommandResultDto(Guid id, bool isSuccess, string? message, IReadOnlyList<string>? errors, string? failureCode, QuotaExceededDetails? quotaExceeded, RegistrationMaterialChangeChoiceDto? choice, RegistrationRefundDto? refund)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), choice, refund)
    {
    }

    public RegistrationMaterialChangeChoiceDto? Choice { get; }
    public RegistrationRefundDto? Refund { get; }

    public static RegistrationMaterialChangeChoiceCommandResultDto Success(Guid id, string? message, RegistrationMaterialChangeChoiceDto? choice, RegistrationRefundDto? refund) =>
        new(BaseCommandResponse.Success(id, message), choice, refund);

    public static RegistrationMaterialChangeChoiceCommandResultDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null, null);
}

public sealed record RegistrationRefundDto
{
    internal bool SettlementRetryAvailable { get; init; }
    public bool ShouldAdvertiseSettlementRetry() => SettlementRetryAvailable;
    public Guid Id { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public string? FailureCode { get; init; }
    public long AmountMinor { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public int AcceptedRefundPolicyVersion { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastObservedAt { get; init; }
    public DateTime? SucceededAt { get; init; }
}

public sealed record RegistrationPaymentDisputeDto
{
    public Guid Id { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public long AmountMinor { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public DateTime LastObservedAt { get; init; }
    public DateTime? ResponseDueAt { get; init; }
}

public sealed record RegistrationRefundRequestDto
{
    public long? AmountMinor { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed record RegistrationRefundCommandResultDto : BaseCommandResponse<Guid>
{
    private RegistrationRefundCommandResultDto(BaseCommandResponse<Guid> state, RegistrationRefundDto? refund) : base(state, true)
    {
        Refund = refund;
    }

    [JsonConstructor]
    internal RegistrationRefundCommandResultDto(Guid id, bool isSuccess, string? message, IReadOnlyList<string>? errors, string? failureCode, QuotaExceededDetails? quotaExceeded, RegistrationRefundDto? refund)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), refund)
    {
    }

    public RegistrationRefundDto? Refund { get; }

    public static RegistrationRefundCommandResultDto Success(Guid id, string? message, RegistrationRefundDto? refund) =>
        new(BaseCommandResponse.Success(id, message), refund);

    public static RegistrationRefundCommandResultDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);
}

public sealed record RegistrationPaymentCommandResultDto : BaseCommandResponse<Guid>
{
    private RegistrationPaymentCommandResultDto(BaseCommandResponse<Guid> state, RegistrationPaymentDto? payment) : base(state, true)
    {
        Payment = payment;
    }

    [JsonConstructor]
    internal RegistrationPaymentCommandResultDto(Guid id, bool isSuccess, string? message, IReadOnlyList<string>? errors, string? failureCode, QuotaExceededDetails? quotaExceeded, RegistrationPaymentDto? payment)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), payment)
    {
    }

    public RegistrationPaymentDto? Payment { get; }

    public static RegistrationPaymentCommandResultDto Success(Guid id, string? message, RegistrationPaymentDto? payment) =>
        new(BaseCommandResponse.Success(id, message), payment);

    public static RegistrationPaymentCommandResultDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);
}

public sealed record RegistrationPaymentCheckoutTargetDto
{
    public required string Url { get; init; }
}
