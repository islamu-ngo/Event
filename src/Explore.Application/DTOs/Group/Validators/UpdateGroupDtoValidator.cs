using FluentValidation;

namespace Explore.Application.DTOs.Group.Validators;

public class UpdateGroupDtoValidator : AbstractValidator<UpdateGroupDto>
{
    public UpdateGroupDtoValidator()
    {
        RuleFor(dto => dto.FullName!)
            .SetValidator(new UpdateGroupFullNameDtoValidator())
            .When(dto => dto.FullName is not null);

        RuleFor(dto => dto.Description!)
            .SetValidator(new UpdateGroupDescriptionDtoValidator())
            .When(dto => dto.Description is not null);

        RuleFor(dto => dto.ParentOrganization!)
            .SetValidator(new UpdateGroupParentOrganizationDtoValidator())
            .When(dto => dto.ParentOrganization is not null);

        RuleFor(dto => dto.ParentGroup!)
            .SetValidator(new UpdateGroupParentGroupDtoValidator())
            .When(dto => dto.ParentGroup is not null);

        RuleFor(dto => dto)
            .Must(HasAnyGroup)
            .WithMessage("At least one group update group must be provided.");

        RuleFor(dto => dto)
            .Must(dto => !HasNonNullParentOrganization(dto) || !HasNonNullParentGroup(dto))
            .WithMessage("A group can have either a parent organization or a parent group, not both.");
    }

    private static bool HasAnyGroup(UpdateGroupDto dto) =>
        dto.FullName is not null ||
        dto.Description is not null ||
        dto.ParentOrganization is not null ||
        dto.ParentGroup is not null;

    private static bool HasNonNullParentOrganization(UpdateGroupDto dto) =>
        dto.ParentOrganization?.Value is { HasValue: true, Value: not null };

    private static bool HasNonNullParentGroup(UpdateGroupDto dto) =>
        dto.ParentGroup?.Value is { HasValue: true, Value: not null };
}

public class UpdateGroupFullNameDtoValidator : AbstractValidator<UpdateGroupFullNameDto>
{
    public UpdateGroupFullNameDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Group name is required.")
            .MaximumLength(500).WithMessage("Group name must not exceed 500 characters.");
    }
}

public class UpdateGroupDescriptionDtoValidator : AbstractValidator<UpdateGroupDescriptionDto>
{
    public UpdateGroupDescriptionDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Description group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MaximumLength(5000)
            .WithMessage("Description must not exceed 5000 characters.")
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null);
    }
}

public class UpdateGroupParentOrganizationDtoValidator : AbstractValidator<UpdateGroupParentOrganizationDto>
{
    public UpdateGroupParentOrganizationDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("ParentOrganization group must include Value.");
    }
}

public class UpdateGroupParentGroupDtoValidator : AbstractValidator<UpdateGroupParentGroupDto>
{
    public UpdateGroupParentGroupDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("ParentGroup group must include Value.");
    }
}
