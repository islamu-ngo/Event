// ABOUTME: Validates local sign-in credentials before any Identity store access.
// ABOUTME: Applies bounded username-or-email and password rules without revealing account existence.

using System.Net.Mail;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Configuration;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Local.Validators;

public sealed class LocalAuthRequestDtoValidator : AbstractValidator<LocalAuthRequestDto>
{
    public LocalAuthRequestDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(request => request.Identifier)
            .NotEmpty()
            .MaximumLength(256)
            .Must(identifier => !identifier.Any(char.IsControl)
                && (identifier.Contains('@', StringComparison.Ordinal)
                    ? MailAddress.TryCreate(identifier.Trim(), out var address)
                        && string.Equals(address.Address, identifier.Trim(), StringComparison.OrdinalIgnoreCase)
                    : identifier.Trim().All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '+')))
            .WithMessage("A bounded username or valid email identifier is required.");

        RuleFor(request => request.Password)
            .NotEmpty()
            .MinimumLength(LocalIdentityOptions.MinimumPasswordLength)
            .MaximumLength(LocalIdentityOptions.MaximumPasswordLength);
    }
}
