using Explore.Domain.Services.Registration;
using FluentValidation;

namespace Explore.Application.DTOs.Event.Validators;

public sealed class ConfigureEventParticipationDtoValidator
    : AbstractValidator<ConfigureEventParticipationDto>
{
    public ConfigureEventParticipationDtoValidator()
    {
        RuleFor(configuration => configuration).Custom((configuration, context) =>
        {
            foreach (var error in EventAuthorityRules.ValidateParticipationConfiguration(
                         configuration.ParticipationHandlingModeId,
                         configuration.AdvanceRegistrationObligationId,
                         configuration.IdentityAccessModeId,
                         configuration.GuestRecoveryPolicy))
            {
                context.AddFailure(error.Code.ToString(), error.Message);
            }
        });
    }
}
