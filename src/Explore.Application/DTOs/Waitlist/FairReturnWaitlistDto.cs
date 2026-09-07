using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Waitlist;

public sealed record FairReturnWaitlistDto
{
    public const int PositionUnavailable = 0;
    public const int MaximumPublishedPosition = 999;

    public required Guid Id { get; init; }
    public required string StatusCode { get; init; }
    public required int Position { get; init; }
    public required string ReasonCode { get; init; }
    public DateTime? OfferExpiresAt { get; init; }

    [JsonIgnore]
    public bool CanJoin { get; init; }
    [JsonIgnore]
    public bool CanLeave { get; init; }
    [JsonIgnore]
    public bool CanAcceptOffer { get; init; }
    [JsonIgnore]
    public bool CanWithdrawSupply { get; init; }
    [JsonIgnore]
    public bool AllocationOpen { get; init; }
    [JsonIgnore]
    public bool WithdrawalOpen { get; init; }
    [JsonIgnore]
    public Guid EventId { get; init; }
    [JsonIgnore]
    public Guid RegistrationOrderId { get; init; }
    [JsonIgnore]
    public Guid RegistrationOrderLineId { get; init; }
    [JsonIgnore]
    public Guid? OfferId { get; init; }
    [JsonIgnore]
    public Guid? SupplyId { get; init; }
}
