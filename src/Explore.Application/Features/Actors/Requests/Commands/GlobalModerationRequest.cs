using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.Features.Actors.Requests.Commands;

public sealed class GlobalModerationRequest
{
    public GlobalModerationAction Action { get; init; }
    public required string ReasonCode { get; init; }
}

internal sealed class GlobalModerationRequestValidator : AbstractValidator<GlobalModerationRequest>
{
    public GlobalModerationRequestValidator()
    {
        RuleFor(request => request.Action).IsInEnum();
        RuleFor(request => request.ReasonCode).NotEmpty().MaximumLength(128);
    }
}
