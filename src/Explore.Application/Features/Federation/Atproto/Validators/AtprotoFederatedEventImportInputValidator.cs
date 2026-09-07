using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Services.Federation;
using FluentValidation;

namespace Explore.Application.Features.Federation.Atproto.Validators;

public sealed class AtprotoFederatedEventImportInputValidator
    : AbstractValidator<AtprotoFederatedEventImportInput>
{
    private static readonly HashSet<string> Modes =
    [
        "#hybrid",
        "#inperson",
        "#virtual"
    ];

    private static readonly HashSet<string> Statuses =
    [
        "#cancelled",
        "#planned",
        "#postponed",
        "#rescheduled",
        "#scheduled"
    ];

    public AtprotoFederatedEventImportInputValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(200);
        RuleFor(request => request.CreatedAt)
            .NotNull()
            .Must(value => value is not null && value.Value != default);
        RuleFor(request => request.Description)
            .MaximumLength(4000)
            .When(request => request.Description is not null);
        RuleFor(request => request.SourceUrl)
            .Must(value => AtprotoExternalUriPolicy.Normalize(value) is not null)
            .When(request => request.SourceUrl is not null);
        RuleFor(request => request.Mode)
            .Must(value => value is not null && Modes.Contains(value))
            .When(request => request.Mode is not null);
        RuleFor(request => request.Status)
            .Must(value => value is not null && Statuses.Contains(value))
            .When(request => request.Status is not null);
        RuleFor(request => request.EndsAt)
            .Must((request, endsAt) =>
                endsAt is null
                || request.StartsAt is not null && endsAt > request.StartsAt)
            .WithMessage("The event end must be after its start.");
    }
}
