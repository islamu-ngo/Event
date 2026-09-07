// ABOUTME: Manually validates bounded Local setup credentials and explicit directory-operator settings.
// ABOUTME: Rejects malformed transient inputs without including their values in validation messages.

using System.Net.Mail;
using Explore.Application.Configuration;
using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public sealed class CompleteLocalInstanceOnboardingRequestDtoValidator : AbstractValidator<CompleteLocalInstanceOnboardingRequestDto>
{
    public CompleteLocalInstanceOnboardingRequestDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(request => request.OperationId)
            .Must(id => id.Version == 7 && id.Variant is >= 8 and <= 11)
            .WithMessage("A UUIDv7 operation identifier is required.");
        RuleFor(request => request.Username).NotEmpty().MaximumLength(256)
            .Must(username => username.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '+'))
            .WithMessage("A bounded Local username without an email separator is required.");
        RuleFor(request => request.TemporaryPassword).NotEmpty()
            .MinimumLength(LocalIdentityOptions.MinimumPasswordLength)
            .MaximumLength(LocalIdentityOptions.MaximumPasswordLength);
        RuleFor(request => request.Email).MaximumLength(256)
            .Must(email => email is null || (MailAddress.TryCreate(email, out var address)
                && string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("A valid bounded email address is required when supplied.");
        RuleFor(request => request.FirstName).MaximumLength(200)
            .Must(name => name is null || !string.IsNullOrWhiteSpace(name))
            .WithMessage("First name must not be blank when supplied.");
        RuleFor(request => request.LastName).MaximumLength(200);
        RuleFor(request => request.Settings).NotNull().SetValidator(new CompleteInstanceOnboardingRequestValidator());
    }
}
