using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.DTOs.EventTicketing.Validators;

public sealed class ManageEventCapacityPoolDtoValidator : AbstractValidator<ManageEventCapacityPoolDto>
{
    public ManageEventCapacityPoolDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.HoldDurationSeconds).GreaterThan(0);
        RuleFor(x => x.CapacityHoldPolicyId).InclusiveBetween(
            (int)CapacityHoldPolicyEnum.NoHoldUntilReady,
            (int)CapacityHoldPolicyEnum.WaitlistWhenFull);
    }
}
