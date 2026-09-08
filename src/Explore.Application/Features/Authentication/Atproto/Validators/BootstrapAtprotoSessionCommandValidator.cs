using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Domain.ValueObjects;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Atproto.Validators;

public sealed class BootstrapAtprotoSessionCommandValidator : AbstractValidator<BootstrapAtprotoSessionCommand>
{
    public const int MaximumSessionPayloadBytes = 128 * 1024;

    public BootstrapAtprotoSessionCommandValidator()
    {
        RuleFor(command => command.ExpectedDid)
            .NotEqual(default(AtprotoDid));
        RuleFor(command => command.ExpectedPdsUri)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(BeCanonicalHttpsOrigin);
        RuleFor(command => command.OAuthClientKeyId)
            .NotEmpty()
            .MaximumLength(128)
            .Matches("^[A-Za-z0-9._-]+$");
        RuleFor(command => command.Classification).IsInEnum();
        RuleFor(command => command.OAuthSessionPayload)
            .Must(payload => payload.Length is > 0 and <= MaximumSessionPayloadBytes);
        RuleFor(command => command)
            .Must(command => command.CanonicalActorId.HasValue == command.ExpectedCanonicalActorConcurrencyStamp.HasValue
                             && command.CanonicalActorId != Guid.Empty
                             && command.ExpectedCanonicalActorConcurrencyStamp != Guid.Empty)
            .WithMessage("Canonical Actor target must be supplied as a complete non-empty pair.");
    }

    private static bool BeCanonicalHttpsOrigin(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps
               && !string.IsNullOrWhiteSpace(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo)
               && string.IsNullOrEmpty(uri.Query)
               && string.IsNullOrEmpty(uri.Fragment)
               && uri.AbsolutePath == "/";
    }
}
