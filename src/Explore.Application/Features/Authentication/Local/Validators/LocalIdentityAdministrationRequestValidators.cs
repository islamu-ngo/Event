// ABOUTME: Validates bounded administrative Local creation and supervised reset request intent.
// ABOUTME: Uses manually constructed FluentValidation validators without treating request metadata as authority.

using Explore.Application.Contracts.Identity;
using System.Net.Mail;
using Explore.Application.Features.Authentication.Local.Models;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Local.Validators;

public sealed class CreateLocalIdentityRequestDtoValidator : AbstractValidator<CreateLocalIdentityRequestDto>
{
    public CreateLocalIdentityRequestDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(request => request.OperationId).NotEmpty();
        RuleFor(request => request.Email).NotEmpty().MaximumLength(256).EmailAddress()
            .Must(email => MailAddress.TryCreate(email.Trim(), out MailAddress? address)
                && string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
            .WithMessage("A valid email address is required.");
        RuleFor(request => request.FirstName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.LastName).NotNull().MaximumLength(200);
    }
}

public sealed class ResetLocalCredentialRequestDtoValidator : AbstractValidator<ResetLocalCredentialRequestDto>
{
    public ResetLocalCredentialRequestDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(request => request.OperationId).NotEmpty()
            .NotEqual(request => request.ExpectedCurrentOperationId);
        RuleFor(request => request.ExpectedCurrentOperationId).NotEmpty();
        RuleFor(request => request.ExpectedCurrentOperationConcurrencyStamp).NotEmpty();
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(LocalCredentialResetRequest.MaximumReasonLength);
    }
}
