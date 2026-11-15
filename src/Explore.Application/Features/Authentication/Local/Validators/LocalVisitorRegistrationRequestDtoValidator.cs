using System.Net.Mail;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain.Authentication;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Local.Validators;

public sealed class LocalVisitorRegistrationRequestDtoValidator : AbstractValidator<LocalVisitorRegistrationRequestDto>
{
    public LocalVisitorRegistrationRequestDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(request => request.OperationId)
            .Must(id => id.Version == 7 && id.Variant is >= 8 and <= 11)
            .WithMessage("A UUIDv7 enrollment operation is required.");
        RuleFor(request => request.Username)
            .NotEmpty()
            .MaximumLength(256)
            .Must(username => username.Trim().All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '+'))
            .WithMessage("A bounded Local username is required.");
        RuleFor(request => request.Password)
            .Must(password => LocalPasswordPolicy.TryNormalizeNewCredential(password, out _))
            .WithMessage("A bounded new Local credential is required.");
        RuleFor(request => request.Email)
            .Must(email => string.IsNullOrEmpty(email)
                || email.Length <= 256 && MailAddress.TryCreate(email.Trim(), out var address)
                    && string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
            .WithMessage("A bounded contact email address is required when supplied.");
        RuleFor(request => request.EnrollmentIntent)
            .NotEmpty()
            .MaximumLength(4096)
            .Must(intent => !intent.Any(char.IsControl))
            .WithMessage("A protected enrollment intent is required.");
    }
}
