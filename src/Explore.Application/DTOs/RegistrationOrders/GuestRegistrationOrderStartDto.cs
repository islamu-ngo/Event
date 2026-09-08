using System.Text.Json.Serialization;
using Explore.Application.Responses;

namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record GuestRegistrationOrderStartDto : BaseCommandResponse<Guid>
{
    private GuestRegistrationOrderStartDto(
        BaseCommandResponse<Guid> state,
        string? guestCapabilityToken) : base(state, true)
    {
        GuestCapabilityToken = guestCapabilityToken;
    }

    [JsonConstructor]
    internal GuestRegistrationOrderStartDto(
        Guid id,
        bool isSuccess,
        string? message,
        IReadOnlyList<string>? errors,
        string? failureCode,
        QuotaExceededDetails? quotaExceeded,
        string? guestCapabilityToken)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), guestCapabilityToken)
    {
    }

    [JsonIgnore]
    public string? GuestCapabilityToken { get; }

    public static GuestRegistrationOrderStartDto Success(
        Guid id,
        string? message,
        string? guestCapabilityToken) =>
        new(BaseCommandResponse.Success(id, message), guestCapabilityToken);

    public static GuestRegistrationOrderStartDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);
}
