using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.StorageObject.Validators;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Handlers.Commands;

public class UpdateStorageObjectCommandHandler : IRequestHandler<UpdateStorageObjectCommand, BaseCommandResponse<Guid>>
{
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IActorRepository _actorRepository;
    private readonly ITenantContext _tenantContext;

    public UpdateStorageObjectCommandHandler(
        IStorageObjectRepository storageObjectRepository,
        IActorRepository actorRepository,
        ITenantContext tenantContext)
    {
        _storageObjectRepository = storageObjectRepository;
        _actorRepository = actorRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateStorageObjectCommand request, CancellationToken cancellationToken)
    {
        var entity = await _storageObjectRepository.GetById(request.StorageObjectId);
        if (entity is null || entity.TenantId != _tenantContext.TenantId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Storage object not found."],
                "Storage object update failed.");
        }

        if (await _storageObjectRepository.IsRetainedEvidenceAsync(entity.Id, cancellationToken))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Retained legitimacy evidence cannot be modified."],
                "Storage object update failed.");
        }

        var validator = new UpdateStorageObjectDtoValidator(_actorRepository);
        var validationResult = await validator.ValidateAsync(request.StorageObjectDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Storage object update failed.");
        }

        if (!HasValidMergedAccess(entity, request.StorageObjectDto))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["The resulting storage visibility and purpose are not eligible for the stored content."],
                "Storage object update failed.");
        }

        if (request.StorageObjectDto.Ownership is { } ownership &&
            (ownership.OwningResourceKind != entity.OwningResourceKind ||
             ownership.OwningResourceId != entity.OwningResourceId ||
             ownership.ActorId != entity.ActorId) &&
            StorageObjectContentReader.IsRegistrationOwned(entity,
                await _storageObjectRepository.GetRegistrationAnswerFileAsync(entity.Id, entity.TenantId, cancellationToken)))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Registration artifact ownership cannot be modified."],
                "Storage object update failed.");
        }

        ApplyUpdate(entity, request.StorageObjectDto);
        await _storageObjectRepository.Update(entity);

        return BaseCommandResponse.Success(entity.Id, "Storage object updated successfully.");
    }

    private static void ApplyUpdate(Domain.StorageObject entity, UpdateStorageObjectDto dto)
    {
        if (dto.Metadata is { } metadata)
        {
            entity.FullName = metadata.FullName;
            entity.SafeDisplayName = string.IsNullOrWhiteSpace(metadata.SafeDisplayName)
                ? metadata.FullName
                : metadata.SafeDisplayName;
        }

        if (dto.Access is { } access)
        {
            entity.Visibility = access.Visibility;
            entity.Purpose = access.Purpose;
        }

        if (dto.Ownership is { } ownership)
        {
            entity.OwningResourceKind = ownership.OwningResourceKind;
            entity.OwningResourceId = ownership.OwningResourceId;
            entity.ActorId = ownership.ActorId;
        }
    }

    private static bool HasValidMergedAccess(Domain.StorageObject entity, UpdateStorageObjectDto dto) =>
        SafeRasterContentPolicy.IsValidAccessMetadata(
            entity.ContentType,
            entity.Extension,
            dto.Access?.Purpose ?? entity.Purpose,
            dto.Access?.Visibility ?? entity.Visibility);
}
