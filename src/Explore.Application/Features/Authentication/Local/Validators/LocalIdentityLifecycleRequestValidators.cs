
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Local.Validators;

public sealed class LocalEmailVerificationRequestDtoValidator : AbstractValidator<LocalEmailVerificationRequestDto>
{
    public LocalEmailVerificationRequestDtoValidator()
    {
        RuleFor(request => request.Identifier).NotEmpty().MaximumLength(256)
            .Must(value => value is null || !value.Any(char.IsControl))
            .When(request => request.Identifier is not null);
        RuleFor(request => request.ProposedEmail).NotEmpty().MaximumLength(256).EmailAddress()
            .When(request => request.ProposedEmail is not null);
        RuleFor(request => request.Identifier).Null().When(request => request.ProposedEmail is not null);
    }
}

public sealed class LocalPasswordRecoveryRequestDtoValidator : AbstractValidator<LocalPasswordRecoveryRequestDto>
{
    public LocalPasswordRecoveryRequestDtoValidator()
    {
        RuleFor(request => request.Identifier).NotEmpty().MaximumLength(256)
            .Must(value => value is not null && !value.Any(char.IsControl));
    }
}

public sealed class LocalEmailConfirmationRequestDtoValidator : AbstractValidator<LocalEmailConfirmationRequestDto>
{
    public LocalEmailConfirmationRequestDtoValidator()
    {
        RuleFor(request => request.OperationId).NotEmpty();
        RuleFor(request => request.LocalSubjectId).NotEmpty();
        RuleFor(request => request.PersonalActorId).NotEmpty();
        RuleFor(request => request.ExternalLoginId).NotEmpty();
        RuleFor(request => request.Generation).NotEmpty();
        RuleFor(request => request.Purpose).Must(purpose => purpose is
            LocalIdentityLifecyclePurpose.EmailVerification or LocalIdentityLifecyclePurpose.EmailChange);
        RuleFor(request => request.Token).NotEmpty().MaximumLength(8192);
    }
}

public sealed class LocalPasswordRecoveryCompletionRequestDtoValidator : AbstractValidator<LocalPasswordRecoveryCompletionRequestDto>
{
    public LocalPasswordRecoveryCompletionRequestDtoValidator()
    {
        RuleFor(request => request.OperationId).NotEmpty();
        RuleFor(request => request.LocalSubjectId).NotEmpty();
        RuleFor(request => request.PersonalActorId).NotEmpty();
        RuleFor(request => request.ExternalLoginId).NotEmpty();
        RuleFor(request => request.Generation).NotEmpty();
        RuleFor(request => request.Purpose).Equal(LocalIdentityLifecyclePurpose.PasswordRecovery);
        RuleFor(request => request.Token).NotEmpty().MaximumLength(8192);
        RuleFor(request => request.NewPassword).NotEmpty()
            .MinimumLength(LocalIdentityOptions.MinimumPasswordLength)
            .MaximumLength(LocalIdentityOptions.MaximumPasswordLength);
    }
}
