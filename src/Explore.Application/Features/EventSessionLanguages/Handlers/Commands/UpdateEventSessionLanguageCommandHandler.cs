using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionLanguage.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Commands;

public class UpdateEventSessionLanguageCommandHandler : IRequestHandler<UpdateEventSessionLanguageCommand, BaseCommandResponse<int>>
{
    private readonly IEventSessionLanguageRepository _repository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly ILanguageRepository _languageRepository;
    private readonly HybridCache _cache;

    public UpdateEventSessionLanguageCommandHandler(
        IEventSessionLanguageRepository repository,
        IEventSessionRepository eventSessionRepository,
        ILanguageRepository languageRepository,
        HybridCache cache)
    {
        _repository = repository;
        _eventSessionRepository = eventSessionRepository;
        _languageRepository = languageRepository;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<int>> Handle(UpdateEventSessionLanguageCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventSessionLanguageDtoValidator();
        var validationResult = await validator.ValidateAsync(request.EventSessionLanguageDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return ValidationFailure(validationResult.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var eventSessionLanguage = await _repository.GetById(request.EventSessionLanguageId);
        if (eventSessionLanguage == null)
        {
            return BaseCommandResponse.NotFound<int>("Event Session Language not found.");
        }

        request = request with { EventSessionId = eventSessionLanguage.EventSessionId };

        if (eventSessionLanguage.EventSessionId != request.EventSessionId)
        {
            return ValidationFailure("Event session language does not belong to the authorized event session.");
        }

        if (eventSessionLanguage.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                $"Event session language {request.EventSessionLanguageId} was modified by another request.");
        }

        var sourceSession = await _eventSessionRepository.GetById(eventSessionLanguage.EventSessionId);
        if (sourceSession is null)
        {
            return ValidationFailure("Event session not found.");
        }

        var previousEventId = sourceSession.EventId;
        var targetSessionId = request.EventSessionLanguageDto.Session?.EventSessionId ?? eventSessionLanguage.EventSessionId;
        var targetLanguageId = request.EventSessionLanguageDto.Language?.LanguageId ?? eventSessionLanguage.LanguageId;

        var targetSession = await _eventSessionRepository.GetById(targetSessionId);
        if (targetSession is null)
        {
            return ValidationFailure("Event session not found.");
        }

        if (targetSession.TenantId != eventSessionLanguage.TenantId || targetSession.EventId != sourceSession.EventId)
        {
            return ValidationFailure("Event session must belong to the same event as the language assignment.");
        }

        if (!await _languageRepository.Exists(targetLanguageId))
        {
            return ValidationFailure("Language not found.");
        }

        var duplicate = await _repository.GetBySessionAndLanguage(
            targetSessionId,
            targetLanguageId,
            request.EventSessionLanguageId,
            cancellationToken);
        if (duplicate is not null)
        {
            return ValidationFailure("Language is already assigned to this event session.");
        }

        ApplySession(eventSessionLanguage, request.EventSessionLanguageDto.Session, targetSession);
        ApplyLanguage(eventSessionLanguage, request.EventSessionLanguageDto.Language);

        await _repository.Update(eventSessionLanguage);
        await InvalidateCachesAsync(previousEventId, targetSession.EventId, targetSession.TenantId, cancellationToken);

        return BaseCommandResponse.Success(
            eventSessionLanguage.Id,
            "Event Session Language updated successfully.");
    }

    private static void ApplySession(EventSessionLanguage entity, DTOs.EventSessionLanguage.UpdateEventSessionLanguageSessionDto? dto, EventSession targetSession)
    {
        if (dto is null)
        {
            return;
        }

        entity.EventSessionId = dto.EventSessionId;
        entity.TenantId = targetSession.TenantId;
    }

    private static void ApplyLanguage(EventSessionLanguage entity, DTOs.EventSessionLanguage.UpdateEventSessionLanguageLanguageDto? dto)
    {
        if (dto is null)
        {
            return;
        }

        entity.LanguageId = dto.LanguageId;
    }

    private async Task InvalidateCachesAsync(Guid? previousEventId, Guid currentEventId, Guid tenantId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync($"event:detail:{currentEventId}", cancellationToken);

        if (previousEventId.HasValue && previousEventId.Value != currentEventId)
        {
            await _cache.RemoveAsync($"event:detail:{previousEventId.Value}", cancellationToken);
        }

        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
    }

    private static BaseCommandResponse<int> ValidationFailure(string error) =>
        ValidationFailure(new List<string> { error });

    private static BaseCommandResponse<int> ValidationFailure(List<string> errors) =>
        BaseCommandResponse.Validation<int>(errors, "Event Session Language update failed.");
}
