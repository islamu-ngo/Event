using FluentValidation;

namespace Explore.Application.DTOs.Tag.Validators;

public class UpdateTagDtoValidator : AbstractValidator<UpdateTagDto>
{
    public UpdateTagDtoValidator()
    {
        RuleFor(dto => dto.MasterCode!)
            .SetValidator(new UpdateTagMasterCodeDtoValidator())
            .When(dto => dto.MasterCode is not null);

        RuleFor(dto => dto.FullName!)
            .SetValidator(new UpdateTagFullNameDtoValidator())
            .When(dto => dto.FullName is not null);

        RuleFor(dto => dto)
            .Must(dto => dto.MasterCode is not null || dto.FullName is not null)
            .WithMessage("At least one tag update group must be provided.");
    }
}

public class UpdateTagMasterCodeDtoValidator : AbstractValidator<UpdateTagMasterCodeDto>
{
    public UpdateTagMasterCodeDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Master code is required.")
            .MaximumLength(500).WithMessage("Master code must not exceed 500 characters.");
    }
}

public class UpdateTagFullNameDtoValidator : AbstractValidator<UpdateTagFullNameDto>
{
    public UpdateTagFullNameDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(500).WithMessage("Full name must not exceed 500 characters.");
    }
}
