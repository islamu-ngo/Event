using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.DTOs.StorageObject.Validators;

public class UpdateStorageObjectDtoValidator : AbstractValidator<UpdateStorageObjectDto>
{
    private readonly IActorRepository _actorRepository;

    public UpdateStorageObjectDtoValidator(IActorRepository actorRepository)
    {
        _actorRepository = actorRepository;

        RuleFor(x => x)
            .Must(x => x.Metadata is not null || x.Access is not null || x.Ownership is not null)
            .WithMessage("At least one update group is required");

        RuleFor(x => x.Metadata!.FullName)
            .NotEmpty().WithMessage("{PropertyName} is required")
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters")
            .Must(StorageObjectMetadataValidation.NotContainControlCharacters)
            .WithMessage("{PropertyName} must not contain control characters")
            .Must(StorageObjectMetadataValidation.NotContainPathSeparators)
            .WithMessage("{PropertyName} must not contain path separators")
            .Must(StorageObjectMetadataValidation.NotBeDotSegment)
            .WithMessage("{PropertyName} must be a simple file name")
            .Must(StorageObjectMetadataValidation.NotBeReservedFileName)
            .WithMessage("{PropertyName} must not be a reserved file name")
            .When(x => x.Metadata is not null);

        RuleFor(x => x.Metadata!.SafeDisplayName)
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters")
            .Must(StorageObjectMetadataValidation.NotContainControlCharacters)
            .WithMessage("{PropertyName} must not contain control characters")
            .Must(StorageObjectMetadataValidation.NotContainPathSeparators)
            .WithMessage("{PropertyName} must not contain path separators")
            .Must(StorageObjectMetadataValidation.NotBeDotSegment)
            .WithMessage("{PropertyName} must be a simple file name")
            .Must(StorageObjectMetadataValidation.NotBeReservedFileName)
            .WithMessage("{PropertyName} must not be a reserved file name")
            .When(x => x.Metadata is not null);

        RuleFor(x => x.Access!.Visibility)
            .NotEmpty().WithMessage("{PropertyName} is required")
            .Must(value => StorageObjectVisibilities.All.Contains(value))
            .WithMessage("{PropertyName} must be a supported storage visibility")
            .When(x => x.Access is not null);

        RuleFor(x => x.Access!.Purpose)
            .NotEmpty().WithMessage("{PropertyName} is required")
            .Must(value => StorageObjectPurposes.All.Contains(value))
            .WithMessage("{PropertyName} must be a supported storage purpose")
            .When(x => x.Access is not null);

        RuleFor(x => x.Ownership!.OwningResourceKind)
            .MaximumLength(100).WithMessage("{PropertyName} must not exceed 100 characters")
            .Must(StorageObjectMetadataValidation.NotContainControlCharacters)
            .WithMessage("{PropertyName} must not contain control characters")
            .Matches("^[A-Za-z0-9._:-]+$")
            .When(x => !string.IsNullOrWhiteSpace(x.Ownership?.OwningResourceKind))
            .WithMessage("{PropertyName} contains unsupported characters")
            .NotEmpty().When(x => x.Ownership?.OwningResourceId.HasValue == true)
            .WithMessage("{PropertyName} is required when OwningResourceId is provided");

        RuleFor(x => x.Ownership!.OwningResourceId)
            .NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.Ownership?.OwningResourceKind))
            .WithMessage("{PropertyName} is required when OwningResourceKind is provided");

        RuleFor(x => x.Ownership!.ActorId)
            .MustAsync(ActorExists)
            .When(x => x.Ownership?.ActorId.HasValue == true)
            .WithMessage("{PropertyName} not found");
    }

    private async Task<bool> ActorExists(Guid? actorId, CancellationToken cancellationToken)
    {
        if (!actorId.HasValue) return true;
        return await _actorRepository.Exists(actorId.Value);
    }
}
