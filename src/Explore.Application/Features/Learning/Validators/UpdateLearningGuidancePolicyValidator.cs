using Explore.Application.Features.Learning.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Learning.Validators;

public sealed class UpdateLearningGuidancePolicyValidator : AbstractValidator<UpdateLearningGuidancePolicyCommand>
{
    public UpdateLearningGuidancePolicyValidator()
    {
        RuleFor(command => command.Input).NotNull();
        When(command => command.Input is not null, () =>
        {
            RuleFor(command => command.Input.ExpectedConcurrencyStamp)
                .Must(stamp => stamp != Guid.Empty).WithMessage("Use null for observed absence or the actual document stamp.");
            RuleFor(command => command.Input.Policy).Custom((policy, context) =>
            {
                try { LearningGuidancePolicyCodec.FromPayload(policy); }
                catch (ArgumentException)
                {
                    context.AddFailure("Use a valid policy with known audiences and at most four distinct families; disabled learning cannot be required.");
                }
            });
        });
    }
}
