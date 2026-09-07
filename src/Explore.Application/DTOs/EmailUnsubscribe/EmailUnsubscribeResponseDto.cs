namespace Explore.Application.DTOs.EmailUnsubscribe;

public sealed record EmailUnsubscribeResponseDto(
    string Status,
    string Message,
    string? Category = null,
    bool? IsSubscribed = null,
    bool RequiresConfirmation = false);
