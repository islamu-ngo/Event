using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using FluentValidation;

namespace Explore.Application.DTOs.EventPublicAction.Validators;

public sealed class ManageEventPublicActionDtoValidator : AbstractValidator<ManageEventPublicActionDto>
{
    public ManageEventPublicActionDtoValidator(bool requireConcurrencyStamp)
    {
        RuleFor(dto => dto.KindId)
            .Must(kindId => Enum.IsDefined((EventPublicActionKindEnum)kindId))
            .WithMessage("A supported public action kind is required.");
        RuleFor(dto => dto.Url)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(BeSafeExternalActionUrl)
            .WithMessage("Public action URL must be an absolute HTTPS URL without userinfo or a fragment.");
        RuleFor(dto => dto.Label).MaximumLength(120);
        RuleFor(dto => dto.SortOrder).InclusiveBetween(0, 1000);

        if (requireConcurrencyStamp)
        {
            RuleFor(dto => dto.ExpectedConcurrencyStamp).NotEmpty();
        }
    }

    private static bool BeSafeExternalActionUrl(string value)
    {
        try
        {
            _ = ExternalActionUrl.Create(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
