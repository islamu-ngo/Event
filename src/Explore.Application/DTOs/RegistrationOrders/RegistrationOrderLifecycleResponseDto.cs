using System.Text.Json.Serialization;
using Explore.Application.Responses;

namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record RegistrationOrderLifecycleResponseDto : BaseCommandResponse<Guid>
{
    private RegistrationOrderLifecycleResponseDto(
        BaseCommandResponse<Guid> state,
        RegistrationOrderDto? order) : base(state, true)
    {
        Order = order;
    }

    [JsonConstructor]
    internal RegistrationOrderLifecycleResponseDto(
        Guid id,
        bool isSuccess,
        string? message,
        IReadOnlyList<string>? errors,
        string? failureCode,
        QuotaExceededDetails? quotaExceeded,
        RegistrationOrderDto? order)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), order)
    {
    }

    public RegistrationOrderDto? Order { get; }

    public static RegistrationOrderLifecycleResponseDto Success(
        Guid id,
        string? message,
        RegistrationOrderDto? order) =>
        new(BaseCommandResponse.Success(id, message), order);

    public static RegistrationOrderLifecycleResponseDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);
}
