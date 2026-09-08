using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.Common;
using FluentValidation;

namespace Explore.Application.DTOs.Actor.Validators;

public class UpdateActorDtoValidator : AbstractValidator<UpdateActorDto>
{
    public UpdateActorDtoValidator(
        IActorTypeRepository actorTypeRepository,
        IStorageObjectRepository storageObjectRepository)
    {
        RuleFor(dto => dto.Profile!)
            .SetValidator(new UpdateActorProfileDtoValidator(actorTypeRepository))
            .When(dto => dto.Profile is not null);

        RuleFor(dto => dto.ProfileImage!)
            .SetValidator(new UpdateActorProfileImageDtoValidator(storageObjectRepository))
            .When(dto => dto.ProfileImage is not null);

        RuleFor(dto => dto.Appearance!)
            .SetValidator(new UpdateActorAppearanceDtoValidator())
            .When(dto => dto.Appearance is not null);

        RuleFor(dto => dto)
            .Must(HasAnyGroup)
            .WithMessage("At least one actor update group must be provided.");
    }

    private static bool HasAnyGroup(UpdateActorDto dto) =>
        dto.Profile is not null ||
        dto.ProfileImage is not null ||
        dto.Appearance is not null;
}

public class UpdateActorProfileDtoValidator : AbstractValidator<UpdateActorProfileDto>
{
    public UpdateActorProfileDtoValidator(IActorTypeRepository actorTypeRepository)
    {
        RuleFor(dto => dto)
            .Must(dto => dto.ActorTypeId.HasValue || dto.DisplayName is not null || dto.Description.HasValue)
            .WithMessage("Profile group must include at least one field.");

        RuleFor(dto => dto.ActorTypeId!.Value)
            .MustAsync(async (actorTypeId, cancellation) => await actorTypeRepository.Exists(actorTypeId))
            .When(dto => dto.ActorTypeId.HasValue)
            .WithMessage("Invalid actor type.");

        RuleFor(dto => dto.DisplayName)
            .NotEmpty().WithMessage("Display name is required when provided.")
            .MaximumLength(500).WithMessage("Display name cannot exceed 500 characters.")
            .When(dto => dto.DisplayName is not null);

        RuleFor(dto => dto.Description.Value)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.")
            .When(dto => dto.Description.HasValue);
    }
}

public class UpdateActorProfileImageDtoValidator : AbstractValidator<UpdateActorProfileImageDto>
{
    public UpdateActorProfileImageDtoValidator(IStorageObjectRepository storageObjectRepository)
    {
        RuleFor(dto => dto)
            .Must(dto => dto.ProfilePictureId.HasValue)
            .WithMessage("ProfileImage group must include ProfilePictureId.");

        RuleFor(dto => dto.ProfilePictureId.Value!.Value)
            .MustAsync(async (profilePictureId, cancellation) => await storageObjectRepository.Exists(profilePictureId))
            .When(dto => dto.ProfilePictureId.HasValue && dto.ProfilePictureId.Value.HasValue)
            .WithMessage("Invalid profile picture.");
    }
}

public class UpdateActorAppearanceDtoValidator : AbstractValidator<UpdateActorAppearanceDto>
{
    private static readonly string[] ValidEffects = ["SoftOverlay", "StrongOverlay", "Blur", "None"];

    public UpdateActorAppearanceDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(HasAnyOperation)
            .WithMessage("Appearance group must include at least one field operation.");

        RuleFor(dto => dto.BackgroundColor.Value)
            .MaximumLength(50).WithMessage("Background color cannot exceed 50 characters.")
            .Matches(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")
            .When(dto => dto.BackgroundColor.HasValue && !string.IsNullOrEmpty(dto.BackgroundColor.Value))
            .WithMessage("Background color must be a valid hex color (e.g., #1a2b3c).");

        RuleFor(dto => dto.BackgroundEffect.Value)
            .MaximumLength(50).WithMessage("Background effect cannot exceed 50 characters.")
            .Must(effect => effect is null || ValidEffects.Contains(effect))
            .When(dto => dto.BackgroundEffect.HasValue)
            .WithMessage($"Background effect must be one of: {string.Join(", ", ValidEffects)}.");

        RuleFor(dto => dto.BannerColor.Value)
            .MaximumLength(50).WithMessage("Banner color cannot exceed 50 characters.")
            .Matches(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")
            .When(dto => dto.BannerColor.HasValue && !string.IsNullOrEmpty(dto.BannerColor.Value))
            .WithMessage("Banner color must be a valid hex color (e.g., #1a2b3c).");

    }

    private static bool HasAnyOperation(UpdateActorAppearanceDto dto) =>
        dto.BackgroundColor.HasValue ||
        dto.BackgroundEffect.HasValue ||
        dto.BannerColor.HasValue;
}
