using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using FluentValidation;

namespace Explore.Application.Features.Events.Discovery;

public sealed record ReviewEventDiscoveryAliasDto(
    Guid PrimaryEventId, long ExpectedRevision, string Decision, string ReasonCode);

public sealed record ReviewEventDiscoveryAliasCommand(Guid EventId, ReviewEventDiscoveryAliasDto Review)
    : ICommand<BaseCommandResponse<Guid>>;

public sealed class ReviewEventDiscoveryAliasCommandValidator : AbstractValidator<ReviewEventDiscoveryAliasCommand>
{
    public ReviewEventDiscoveryAliasCommandValidator()
    {
        RuleFor(request => request.EventId).NotEmpty();
        RuleFor(request => request.Review).NotNull();
        When(request => request.Review is not null, () =>
        {
            RuleFor(request => request.Review.PrimaryEventId).NotEmpty()
                .NotEqual(request => request.EventId);
            RuleFor(request => request.Review.ExpectedRevision).GreaterThanOrEqualTo(0);
            RuleFor(request => request.Review.Decision)
                .Must(value => value is "same-offering" or "different-offering" or "reverse");
            RuleFor(request => request.Review.ReasonCode).NotEmpty().MaximumLength(80)
                .Matches("\\A[a-z0-9_]+\\z");
        });
    }
}

/// <summary>Purpose-limited durable receipt; recipients and disclosure are resolved again at delivery.</summary>
public sealed record EventDiscoveryIdentityCorrectionRequested(
    Guid TenantId, Guid EventId, Guid PrimaryEventId, long Revision,
    string Decision, string ReasonCode)
{
    public const string EventType = "EventDiscoveryIdentityCorrectionRequested";
}
