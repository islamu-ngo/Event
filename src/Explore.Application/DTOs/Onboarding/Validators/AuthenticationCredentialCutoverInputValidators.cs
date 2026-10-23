using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public sealed class AuthenticationCredentialReplacementValidator
    : AbstractValidator<AuthenticationCredentialReplacementDto>
{
    public AuthenticationCredentialReplacementValidator()
    {
        RuleFor(input => input.ReplicaId).NotEmpty();
        RuleFor(input => input.PreviousProcessBootId).NotEmpty();
        RuleFor(input => input.ProcessBootId).NotEmpty()
            .NotEqual(input => input.PreviousProcessBootId);
        RuleFor(input => input.Consumer).IsInEnum();
        RuleFor(input => input.Generation).NotEmpty();
    }
}

public sealed class PrepareAuthenticationCredentialCutoverInputValidator
    : AbstractValidator<PrepareAuthenticationCredentialCutoverInputDto>
{
    public PrepareAuthenticationCredentialCutoverInputValidator()
    {
        RuleFor(input => input.DeploymentRevision).NotEmpty();
        RuleFor(input => input.RequiredReplacements).NotEmpty()
            .Must(members => members.Count <= 1024);
        RuleForEach(input => input.RequiredReplacements).NotNull()
            .SetValidator(new AuthenticationCredentialReplacementValidator());
        RuleFor(input => input.ExpectedCurrentOperationId)
            .Must(value => value is null || value != Guid.Empty);
        RuleFor(input => input.ExpectedHeadStamp)
            .Must(value => value is null || value != Guid.Empty);
        RuleFor(input => input.ExpectedPredecessorStamp)
            .Must(value => value is null || value != Guid.Empty);
        RuleFor(input => input).Must(input =>
                input.ExpectedCurrentOperationId.HasValue == input.ExpectedHeadStamp.HasValue
                && input.ExpectedHeadStamp.HasValue == input.ExpectedPredecessorStamp.HasValue)
            .WithMessage("Expected operation, head and predecessor stamps must be supplied together.");
    }
}

public sealed class AuthenticationCredentialActivationInputValidator
    : AbstractValidator<AuthenticationCredentialActivationInputDto>
{
    public AuthenticationCredentialActivationInputValidator()
    {
        RuleFor(input => input.OperationId).NotEmpty();
        RuleFor(input => input.DeploymentRevision).NotEmpty();
        RuleFor(input => input.ReplicaId).NotEmpty();
        RuleFor(input => input.ProcessBootId).NotEmpty();
        RuleFor(input => input.Consumer).IsInEnum();
        RuleFor(input => input.Generation).NotEmpty();
        RuleFor(input => input.ExpectedHeadStamp).NotEmpty();
        RuleFor(input => input.ExpectedOperationStamp).NotEmpty();
    }
}

public sealed class AuthenticationCredentialFenceInputValidator
    : AbstractValidator<AuthenticationCredentialFenceInputDto>
{
    public AuthenticationCredentialFenceInputValidator()
    {
        RuleFor(input => input.OperationId).NotEmpty();
        RuleFor(input => input.DeploymentRevision).NotEmpty();
        RuleFor(input => input.ExpectedHeadStamp).NotEmpty();
        RuleFor(input => input.ExpectedOperationStamp).NotEmpty();
        RuleFor(input => input.OldProcessesTerminated).Equal(true);
        RuleFor(input => input.OldWritersFenced).Equal(true);
        RuleFor(input => input.OldSessionsFenced).Equal(true);
    }
}

public sealed class AuthenticationCredentialCleanupInputValidator
    : AbstractValidator<AuthenticationCredentialCleanupInputDto>
{
    public AuthenticationCredentialCleanupInputValidator()
    {
        RuleFor(input => input.OperationId).NotEmpty();
        RuleFor(input => input.DeploymentRevision).NotEmpty();
        RuleFor(input => input.ExpectedHeadStamp).NotEmpty();
        RuleFor(input => input.ExpectedOperationStamp).NotEmpty();
        RuleFor(input => input.ApprovedSettingIds).NotNull().Must(ids =>
            ids is not null && ids.Count <= 2 && ids.All(id => id != Guid.Empty)
            && ids.Distinct().Count() == ids.Count);
        RuleFor(input => input.ApprovedBindingIds).NotNull().Must(ids =>
            ids is not null && ids.Count <= 2 && ids.All(id => id != Guid.Empty)
            && ids.Distinct().Count() == ids.Count);
    }
}
