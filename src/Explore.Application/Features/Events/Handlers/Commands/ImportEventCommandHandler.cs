using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Event.Validators;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Lifecycle;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class ImportEventCommandHandler(
    IEventRepository eventRepository,
    IStorageObjectRepository storageObjectRepository,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    IEventLifecyclePolicyProvider policyProvider,
    IEventLifecycleReadinessEvaluator readinessEvaluator,
    TimeProvider timeProvider) : IRequestHandler<ImportEventCommand, BaseCommandResponse<Guid>>
{
    private const string ValidationFailedCode = "event_import_validation_failed";
    private const string ReadinessFailedCode = "event_import_readiness_failed";

    public async Task<BaseCommandResponse<Guid>> Handle(ImportEventCommand command, CancellationToken cancellationToken)
    {
        var validator = new ImportEventRequestDtoValidator();
        var validation = await validator.ValidateAsync(command.Request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(Guid.Empty, "Event import failed validation.", validation.Errors.Select(e => e.ErrorMessage), ValidationFailedCode);
        }

        ImportEventRequestDto request = command.Request;

        if (!await ImageReferenceEligibility.AreEligibleAsync(
                storageObjectRepository,
                command.TenantId,
                request.FeaturedImageId))
        {
            return Failure(
                Guid.Empty,
                "Event import failed validation.",
                ["Every image must be an active public safe-raster object in the current tenant."],
                ValidationFailedCode);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTime utcNow = now.UtcDateTime;
        Guid eventId = Guid.CreateVersion7(now);
        Guid? importedTenantId = null;
        BaseCommandResponse<Guid> response = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            Event? existing = await eventRepository.GetById(eventId);
            if (existing is not null)
            {
                if (existing.TenantId != command.TenantId
                    || existing.EventProvenanceTypeId != (int)EventProvenanceTypeEnum.Imported
                    || existing.ProvenanceSource != request.ProvenanceSource
                    || existing.ProvenanceExternalId != request.ProvenanceExternalId)
                {
                    return Failure(
                        eventId,
                        "Event import identity verification failed.",
                        ["The deterministic event identity belongs to a different import."],
                        ValidationFailedCode);
                }

                importedTenantId = existing.TenantId;
                return Success(existing.Id, "Event imported successfully.");
            }

            var eventEntity = new Event
            {
                Id = eventId,
                Title = request.Title,
                Description = request.Description,
                TenantId = command.TenantId,
                ActorId = request.OwnerActorId,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Imported,
                ProvenanceSource = request.ProvenanceSource,
                ProvenanceExternalId = request.ProvenanceExternalId,
                EventTypeId = request.EventTypeId,
                AudienceGenderId = request.AudienceGenderId,
                AudienceAgeId = request.AudienceAgeId,
                VisibilityTypeId = request.VisibilityTypeId ?? (int)VisibilityTypeEnum.Private,
                VisibilityType = null!,
                EventFormatId = request.EventFormatId ?? (int)EventFormatEnum.Local,
                EventFormat = null!,
                Timezone = request.Timezone,
                FeaturedImageId = request.FeaturedImageId,
                EventStatus = null!,
                Tenant = null!,
                Actor = null!,
                CreatedAt = utcNow,
                UpdatedAt = utcNow,
                TotalViews = 0
            };

            eventEntity.ParticipationConfiguration = EventParticipationConfiguration.Create(
                eventEntity.Id,
                command.TenantId,
                request.ParticipationConfiguration.ParticipationHandlingModeId,
                request.ParticipationConfiguration.AdvanceRegistrationObligationId,
                request.ParticipationConfiguration.IdentityAccessModeId,
                request.ParticipationConfiguration.GuestRecoveryPolicy,
                utcNow);

            EventLifecyclePolicy policy = await policyProvider.GetEffectivePolicyAsync(command.TenantId, ValidationProfile.EventImportCreate, token);
            LifecycleReadinessResult readiness = readinessEvaluator.Evaluate(eventEntity, ValidationProfile.EventImportCreate, policy);
            if (!readiness.IsReady)
            {
                return Failure(Guid.Empty, "Event import failed readiness checks.", readiness.Errors.Select(e => e.Message), ReadinessFailedCode);
            }

            Event created = await eventRepository.Create(eventEntity);
            importedTenantId = created.TenantId;

            return Success(created.Id, "Event imported successfully.");
        }, cancellationToken);

        if (response.IsSuccess && importedTenantId.HasValue)
        {
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(importedTenantId.Value), cancellationToken);
        }

        return response;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors, string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation(errors, message, id)
            : BaseCommandResponse.Failure(failureCode, message, errors, id);
}
