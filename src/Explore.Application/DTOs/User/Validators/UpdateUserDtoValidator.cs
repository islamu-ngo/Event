using FluentValidation;

namespace Explore.Application.DTOs.User.Validators;

public class UpdateUserDtoValidator : AbstractValidator<UpdateUserDto>
{
    public UpdateUserDtoValidator()
    {
        RuleFor(p => p.Names!)
            .SetValidator(new UpdateUserNamesDtoValidator())
            .When(p => p.Names is not null);

        RuleFor(p => p.ProfileImage!)
            .SetValidator(new UpdateUserProfileImageDtoValidator())
            .When(p => p.ProfileImage is not null);

        RuleFor(p => p)
            .Must(p => p.Names is not null || p.ProfileImage is not null)
            .WithMessage("At least one of Names or ProfileImage must be provided.");
    }
}

public class UpdateUserNamesDtoValidator : AbstractValidator<UpdateUserNamesDto>
{
    public UpdateUserNamesDtoValidator()
    {
        RuleFor(p => p.FirstName)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(100).WithMessage("{PropertyName} must not exceed 100 characters.");

        RuleFor(p => p.LastName)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(100).WithMessage("{PropertyName} must not exceed 100 characters.");
    }
}

public class UpdateUserProfileImageDtoValidator : AbstractValidator<UpdateUserProfileImageDto>
{
    public UpdateUserProfileImageDtoValidator()
    {
        RuleFor(p => p.ProfilePictureId)
            .NotEmpty().WithMessage("{PropertyName} is required.");
    }
}
