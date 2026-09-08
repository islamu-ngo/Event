using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.DTOs.ActorSubscription.Validators;

public class UpdateActorSubscriptionNotificationLevelDtoValidator : AbstractValidator<UpdateActorSubscriptionNotificationLevelDto>
{
    private static readonly int[] AllowedNotificationLevelIds =
    [
        (int)ActorSubscriptionNotificationLevelEnum.None,
        (int)ActorSubscriptionNotificationLevelEnum.All,
        (int)ActorSubscriptionNotificationLevelEnum.Personalized
    ];

    public UpdateActorSubscriptionNotificationLevelDtoValidator()
    {
        RuleFor(dto => dto.NotificationLevel)
            .NotNull().WithMessage("Notification level is required.");

        RuleFor(dto => dto.NotificationLevel!.Id)
            .Must(id => AllowedNotificationLevelIds.Contains(id))
            .WithMessage("Notification level is not supported.")
            .When(dto => dto.NotificationLevel is not null);

        RuleFor(dto => dto.ExpectedConcurrencyStamp)
            .NotEmpty().WithMessage("Expected concurrency stamp is required.");
    }
}
