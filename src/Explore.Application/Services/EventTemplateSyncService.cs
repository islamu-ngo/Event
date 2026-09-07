using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.DTOs.EventTemplateSync.Validators;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using FluentValidation.Results;

namespace Explore.Application.Services;

public class EventTemplateSyncService : IEventTemplateSyncService
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IEventTemplateDiffService _eventTemplateDiffService;
    private readonly IEventCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public EventTemplateSyncService(
        IEventRepository eventRepository,
        IEventTemplateRepository eventTemplateRepository,
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IEventTemplateDiffService eventTemplateDiffService,
        IEventCustomPropertyProjectionUpdater projectionUpdater,
        IAuditLogRepository auditLogRepository,
        ICustomPropertyQuotaResolver quotaResolver,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
    {
        _eventRepository = eventRepository;
        _eventTemplateRepository = eventTemplateRepository;
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _eventTemplateDiffService = eventTemplateDiffService;
        _projectionUpdater = projectionUpdater;
        _auditLogRepository = auditLogRepository;
        _quotaResolver = quotaResolver;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
    }

    public async Task<TemplateSyncOutcomeDto> ApplySyncAsync(
        Guid eventId,
        TemplateSyncPlanDto plan,
        int baseProvenanceVersion,
        CancellationToken cancellationToken)
    {
        var validator = new TemplateSyncPlanDtoValidator();
        var validationResult = await validator.ValidateAsync(plan, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult);

        if (plan.BaseProvenanceVersion != baseProvenanceVersion)
        {
            throw new ValidationException(new ValidationResult([
                new ValidationFailure(nameof(plan.BaseProvenanceVersion), "Plan BaseProvenanceVersion must match the command baseProvenanceVersion.")
            ]));
        }

        var @event = await _eventRepository.GetById(eventId)
            ?? throw new NotFoundException(nameof(Event), eventId);

        var requestedKeys = GetRequestedKeys(plan);
        if ((@event.SourceTemplateVersion ?? 0) != baseProvenanceVersion)
        {
            return BuildConflictOutcome(requestedKeys, ConcurrencyConflictException.StaleSyncBase, plan.TargetTemplateVersion, baseProvenanceVersion);
        }

        var diff = await _eventTemplateDiffService.ComputeDiffAsync(eventId, plan.TargetTemplateVersion, cancellationToken);
        var quota = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.SyncApplyMaxChangeCount.Key,
            @event.TenantId,
            cancellationToken);

        var totalChangeCount = plan.GetTotalChangeCount();
        if (totalChangeCount > quota)
        {
            throw new QuotaExceededException(
                "Template sync plan exceeds the tenant change-count quota.",
                CustomPropertyQuotaSettingDefinitions.SyncApplyMaxChangeCount.Key,
                quota,
                totalChangeCount,
                totalChangeCount,
                "event_template_sync",
                @event.TenantId);
        }

        var maxPayloadBytes = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.SyncApplyMaxPayloadBytes.Key,
            @event.TenantId,
            cancellationToken);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(plan).Length;
        if (payloadBytes > maxPayloadBytes)
        {
            throw new QuotaExceededException(
                "Template sync plan payload exceeds the tenant payload-size quota.",
                CustomPropertyQuotaSettingDefinitions.SyncApplyMaxPayloadBytes.Key,
                maxPayloadBytes,
                payloadBytes,
                payloadBytes,
                "event_template_sync_payload",
                @event.TenantId);
        }

        var templateKey = @event.SourceTemplateKey;
        if (string.IsNullOrWhiteSpace(templateKey))
        {
            return BuildConflictOutcome(requestedKeys, "missing_template_provenance", plan.TargetTemplateVersion, @event.SourceTemplateVersion ?? 0);
        }

        var targetTemplate = await _eventTemplateRepository.GetPublishedTemplateVersion(
            @event.TenantId,
            templateKey,
            plan.TargetTemplateVersion,
            cancellationToken)
            ?? throw new NotFoundException(nameof(EventTemplate), $"{templateKey}:{plan.TargetTemplateVersion}");

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var trackedEvent = await _eventRepository.GetById(eventId)
                    ?? throw new NotFoundException(nameof(Event), eventId);

                if ((trackedEvent.SourceTemplateVersion ?? 0) != baseProvenanceVersion)
                {
                    return BuildConflictOutcome(requestedKeys, ConcurrencyConflictException.StaleSyncBase, plan.TargetTemplateVersion, trackedEvent.SourceTemplateVersion ?? 0);
                }

                var trackedDefinitions = await _eventCustomPropertyRepository.GetTrackedDefinitionsForEvent(eventId, ct);
                var applied = new List<string>();
                var skipped = new List<string>();
                var conflicts = new List<SyncConflictDto>();
                var now = DateTimeOffset.UtcNow;

                await EnforceResultingQuotaAsync(plan, diff, targetTemplate, trackedDefinitions, trackedEvent.TenantId, ct);

                await ApplyAddedDefinitions(plan, diff, targetTemplate, trackedEvent, applied, skipped, trackedDefinitions, now, ct);
                await ApplyModifiedDefinitions(plan, diff, targetTemplate, trackedDefinitions, applied, skipped, conflicts, now, ct);
                await ApplyRetiredDefinitions(plan, diff, trackedDefinitions, applied, skipped, conflicts, now, ct);
                await ApplyAddedOptions(plan, diff, targetTemplate, trackedDefinitions, applied, skipped, now, ct);
                await ApplyModifiedOptions(plan, diff, targetTemplate, trackedDefinitions, applied, skipped, conflicts, now, ct);
                await ApplyRetiredOptions(plan, diff, trackedDefinitions, applied, skipped, conflicts, now, ct);

                if (applied.Count > 0)
                {
                    trackedEvent.SourceTemplateId = targetTemplate.Id;
                    trackedEvent.SourceTemplateKey = targetTemplate.TemplateKey;
                    trackedEvent.SourceTemplateVersion = targetTemplate.Version;
                    trackedEvent.LastSyncedFromTemplateAt = now;
                    await _eventRepository.Update(trackedEvent);

                    await _projectionUpdater.RefreshForEventAsync(eventId, ct);
                    await WriteAuditAsync(trackedEvent, plan, applied, skipped, conflicts, now, ct);
                }

                return new TemplateSyncOutcomeDto(applied, skipped, conflicts, applied.Count > 0 ? targetTemplate.Version : trackedEvent.SourceTemplateVersion ?? 0, now);
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return BuildConflictOutcome(requestedKeys, ConcurrencyConflictException.ConcurrentUpdate, plan.TargetTemplateVersion, @event.SourceTemplateVersion ?? 0);
        }
        catch (QuotaExceededException)
        {
            throw;
        }
        catch
        {
            return BuildConflictOutcome(requestedKeys, "apply_failed", plan.TargetTemplateVersion, @event.SourceTemplateVersion ?? 0);
        }
    }

    private async Task EnforceResultingQuotaAsync(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventTemplate targetTemplate,
        List<EventCustomPropertyDefinition> trackedDefinitions,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var addedDefinitionKeys = GetApplicableAddedDefinitionKeys(plan, diff, targetTemplate).ToList();
        var maxDefinitions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key,
            tenantId,
            cancellationToken);
        var attemptedDefinitionCount = trackedDefinitions.Count + addedDefinitionKeys.Count;
        if (attemptedDefinitionCount > maxDefinitions)
        {
            throw new QuotaExceededException(
                "Template sync would exceed the event custom-property definition quota.",
                CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key,
                maxDefinitions,
                trackedDefinitions.Count,
                attemptedDefinitionCount,
                "event_custom_property_definitions",
                tenantId);
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            tenantId,
            cancellationToken);
        var templateDefinitions = targetTemplate.Definitions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var key in addedDefinitionKeys)
        {
            var optionCount = templateDefinitions[key].Options.Count;
            if (optionCount > maxOptions)
            {
                throw new QuotaExceededException(
                    "Template sync would exceed the event custom-property option quota.",
                    CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                    maxOptions,
                    null,
                    optionCount,
                    "event_custom_property_options",
                    tenantId);
            }
        }

        foreach (var (trackedDefinition, addedOptionCount) in GetApplicableAddedOptionCounts(plan, diff, targetTemplate, trackedDefinitions))
        {
            var currentOptionCount = trackedDefinition.Options.Count;
            var attemptedOptionCount = currentOptionCount + addedOptionCount;
            if (attemptedOptionCount > maxOptions)
            {
                throw new QuotaExceededException(
                    "Template sync would exceed the event custom-property option quota.",
                    CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                    maxOptions,
                    currentOptionCount,
                    attemptedOptionCount,
                    "event_custom_property_options",
                    tenantId);
            }
        }
    }

    private static IEnumerable<string> GetApplicableAddedDefinitionKeys(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventTemplate targetTemplate)
    {
        var allowed = new HashSet<string>(diff.AddedDefinitions.Select(x => ComposeKey(x.Namespace, x.Key)), StringComparer.OrdinalIgnoreCase);
        var templateDefinitions = targetTemplate.Definitions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var key in plan.AddedDefinitionKeys)
        {
            if (allowed.Contains(key) && templateDefinitions.ContainsKey(key))
            {
                yield return key;
            }
        }
    }

    private static IEnumerable<(EventCustomPropertyDefinition Definition, int AddedOptionCount)> GetApplicableAddedOptionCounts(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventTemplate targetTemplate,
        List<EventCustomPropertyDefinition> trackedDefinitions)
    {
        var allowed = new HashSet<string>(diff.AddedOptions.Select(x => ComposeKey(x.Namespace, x.Key)), StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<Guid, (EventCustomPropertyDefinition Definition, int Count)>();

        foreach (var key in plan.AddedOptionKeys)
        {
            if (!allowed.Contains(key))
            {
                continue;
            }

            var parentDefinition = targetTemplate.Definitions.FirstOrDefault(x => x.Options.Any(o => ComposeKey(o.Namespace, o.Key).Equals(key, StringComparison.OrdinalIgnoreCase)));
            if (parentDefinition is null)
            {
                continue;
            }

            var trackedDefinition = trackedDefinitions.FirstOrDefault(x => ComposeKey(x.Namespace, x.Key).Equals(ComposeKey(parentDefinition.Namespace, parentDefinition.Key), StringComparison.OrdinalIgnoreCase));
            if (trackedDefinition is null)
            {
                continue;
            }

            counts[trackedDefinition.Id] = counts.TryGetValue(trackedDefinition.Id, out var existing)
                ? (trackedDefinition, existing.Count + 1)
                : (trackedDefinition, 1);
        }

        return counts.Values.Select(x => (x.Definition, x.Count));
    }

    private async Task ApplyAddedDefinitions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventTemplate targetTemplate,
        Event trackedEvent,
        List<string> applied,
        List<string> skipped,
        List<EventCustomPropertyDefinition> trackedDefinitions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var allowed = new HashSet<string>(diff.AddedDefinitions.Select(x => ComposeKey(x.Namespace, x.Key)), StringComparer.OrdinalIgnoreCase);
        var templateDefinitions = targetTemplate.Definitions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var key in plan.AddedDefinitionKeys)
        {
            if (!allowed.Contains(key) || !templateDefinitions.TryGetValue(key, out var templateDefinition))
            {
                skipped.Add(key);
                continue;
            }

            var definitionId = Guid.CreateVersion7();
            var optionIdMap = new Dictionary<Guid, Guid>();
            var options = new List<EventCustomPropertyOption>();

            foreach (var templateOption in templateDefinition.Options.OrderBy(x => x.SortOrder))
            {
                var optionId = Guid.CreateVersion7();
                optionIdMap[templateOption.Id] = optionId;
                options.Add(new EventCustomPropertyOption
                {
                    Id = optionId,
                    EventCustomPropertyDefinitionId = definitionId,
                    Namespace = templateOption.Namespace,
                    Key = templateOption.Key,
                    DisplayName = templateOption.DisplayName,
                    Description = templateOption.Description,
                    Value = templateOption.Value,
                    IsDefault = templateOption.IsDefault,
                    IsActive = templateOption.IsActive,
                    SortOrder = templateOption.SortOrder,
                    ParentOptionId = templateOption.ParentOptionId.HasValue && optionIdMap.TryGetValue(templateOption.ParentOptionId.Value, out var parentOptionId)
                        ? parentOptionId
                        : null,
                    SourceTemplateOptionId = templateOption.Id,
                    SourceTemplateVersion = targetTemplate.Version,
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = _currentUserService.UserId,
                    UpdatedAt = now.UtcDateTime,
                    UpdatedBy = _currentUserService.UserId
                });
            }

            Guid? defaultOptionId = templateDefinition.DefaultOptionId.HasValue && optionIdMap.TryGetValue(templateDefinition.DefaultOptionId.Value, out var mappedDefault)
                ? mappedDefault
                : null;

            var runtimeDefinition = new EventCustomPropertyDefinition
            {
                Id = definitionId,
                EventId = trackedEvent.Id,
                TenantId = trackedEvent.TenantId,
                Namespace = templateDefinition.Namespace,
                Key = templateDefinition.Key,
                DisplayName = templateDefinition.DisplayName,
                Description = templateDefinition.Description,
                PropertyType = templateDefinition.PropertyType,
                IsRequired = templateDefinition.IsRequired,
                IsMulti = templateDefinition.IsMulti,
                IsActive = templateDefinition.IsActive,
                SortOrder = templateDefinition.SortOrder,
                ExposureLevel = templateDefinition.ExposureLevel,
                IsSearchable = templateDefinition.IsSearchable,
                IsFilterable = templateDefinition.IsFilterable,
                IsExportable = templateDefinition.IsExportable,
                IsModerationRelevant = templateDefinition.IsModerationRelevant,
                IsAnalyticsRelevant = templateDefinition.IsAnalyticsRelevant,
                IsSystemOwned = templateDefinition.IsSystemOwned,
                DefaultTextValue = templateDefinition.DefaultTextValue,
                DefaultNumberValue = templateDefinition.DefaultNumberValue,
                DefaultBooleanValue = templateDefinition.DefaultBooleanValue,
                DefaultDateTimeValue = templateDefinition.DefaultDateTimeValue,
                DefaultOptionId = defaultOptionId,
                MinLength = templateDefinition.MinLength,
                MaxLength = templateDefinition.MaxLength,
                RegexPattern = templateDefinition.RegexPattern,
                MinNumber = templateDefinition.MinNumber,
                MaxNumber = templateDefinition.MaxNumber,
                MinDateTime = templateDefinition.MinDateTime,
                MaxDateTime = templateDefinition.MaxDateTime,
                AllowedUrlSchemes = templateDefinition.AllowedUrlSchemes,
                SourceTemplateId = targetTemplate.Id,
                SourceTemplateKey = targetTemplate.TemplateKey,
                SourceTemplateVersion = targetTemplate.Version,
                SourceTemplateDefinitionId = templateDefinition.Id,
                InstantiatedAt = now,
                LastSyncedFromTemplateAt = now,
                CreatedAt = now.UtcDateTime,
                CreatedBy = _currentUserService.UserId,
                UpdatedAt = now.UtcDateTime,
                UpdatedBy = _currentUserService.UserId
            };

            await _eventCustomPropertyRepository.CreateWithOptions(runtimeDefinition, options, defaultOptionId, cancellationToken);
            trackedDefinitions.Add(runtimeDefinition);
            applied.Add(key);
        }
    }

    private async Task ApplyModifiedDefinitions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventTemplate targetTemplate,
        List<EventCustomPropertyDefinition> trackedDefinitions,
        List<string> applied,
        List<string> skipped,
        List<SyncConflictDto> conflicts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var diffMap = diff.ModifiedDefinitions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);
        var trackedMap = trackedDefinitions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);
        var templateMap = targetTemplate.Definitions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var key in plan.ModifiedDefinitionKeys)
        {
            if (!diffMap.TryGetValue(key, out var diffItem) || !trackedMap.TryGetValue(key, out var trackedDefinition) || !templateMap.TryGetValue(key, out var templateDefinition))
            {
                skipped.Add(key);
                continue;
            }

            if (trackedDefinition.ConcurrencyStamp != diffItem.CurrentConcurrencyStamp)
            {
                conflicts.Add(new SyncConflictDto(key, ConcurrencyConflictException.ConcurrentUpdate));
                skipped.Add(key);
                continue;
            }

            trackedDefinition.DisplayName = templateDefinition.DisplayName;
            trackedDefinition.Description = templateDefinition.Description;
            trackedDefinition.PropertyType = templateDefinition.PropertyType;
            trackedDefinition.IsRequired = templateDefinition.IsRequired;
            trackedDefinition.IsMulti = templateDefinition.IsMulti;
            trackedDefinition.IsActive = templateDefinition.IsActive;
            trackedDefinition.SortOrder = templateDefinition.SortOrder;
            trackedDefinition.ExposureLevel = templateDefinition.ExposureLevel;
            trackedDefinition.IsSearchable = templateDefinition.IsSearchable;
            trackedDefinition.IsFilterable = templateDefinition.IsFilterable;
            trackedDefinition.IsExportable = templateDefinition.IsExportable;
            trackedDefinition.IsModerationRelevant = templateDefinition.IsModerationRelevant;
            trackedDefinition.IsAnalyticsRelevant = templateDefinition.IsAnalyticsRelevant;
            trackedDefinition.IsSystemOwned = templateDefinition.IsSystemOwned;
            trackedDefinition.DefaultTextValue = templateDefinition.DefaultTextValue;
            trackedDefinition.DefaultNumberValue = templateDefinition.DefaultNumberValue;
            trackedDefinition.DefaultBooleanValue = templateDefinition.DefaultBooleanValue;
            trackedDefinition.DefaultDateTimeValue = templateDefinition.DefaultDateTimeValue;
            trackedDefinition.MinLength = templateDefinition.MinLength;
            trackedDefinition.MaxLength = templateDefinition.MaxLength;
            trackedDefinition.RegexPattern = templateDefinition.RegexPattern;
            trackedDefinition.MinNumber = templateDefinition.MinNumber;
            trackedDefinition.MaxNumber = templateDefinition.MaxNumber;
            trackedDefinition.MinDateTime = templateDefinition.MinDateTime;
            trackedDefinition.MaxDateTime = templateDefinition.MaxDateTime;
            trackedDefinition.AllowedUrlSchemes = templateDefinition.AllowedUrlSchemes;
            trackedDefinition.SourceTemplateId = targetTemplate.Id;
            trackedDefinition.SourceTemplateKey = targetTemplate.TemplateKey;
            trackedDefinition.SourceTemplateVersion = targetTemplate.Version;
            trackedDefinition.SourceTemplateDefinitionId = templateDefinition.Id;
            trackedDefinition.LastSyncedFromTemplateAt = now;
            trackedDefinition.UpdatedAt = now.UtcDateTime;
            trackedDefinition.UpdatedBy = _currentUserService.UserId;
            trackedDefinition.ConcurrencyStamp = Guid.CreateVersion7();

            await _eventCustomPropertyRepository.Update(trackedDefinition);
            applied.Add(key);
        }
    }

    private async Task ApplyRetiredDefinitions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        List<EventCustomPropertyDefinition> trackedDefinitions,
        List<string> applied,
        List<string> skipped,
        List<SyncConflictDto> conflicts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var diffMap = diff.RetiredDefinitions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);
        var trackedMap = trackedDefinitions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var key in plan.RetiredDefinitionKeys)
        {
            if (!diffMap.TryGetValue(key, out var diffItem) || !trackedMap.TryGetValue(key, out var trackedDefinition))
            {
                skipped.Add(key);
                continue;
            }

            if (trackedDefinition.ConcurrencyStamp != diffItem.CurrentConcurrencyStamp)
            {
                conflicts.Add(new SyncConflictDto(key, ConcurrencyConflictException.ConcurrentUpdate));
                skipped.Add(key);
                continue;
            }

            trackedDefinition.IsActive = false;
            trackedDefinition.LastSyncedFromTemplateAt = now;
            trackedDefinition.UpdatedAt = now.UtcDateTime;
            trackedDefinition.UpdatedBy = _currentUserService.UserId;
            trackedDefinition.ConcurrencyStamp = Guid.CreateVersion7();
            await _eventCustomPropertyRepository.Update(trackedDefinition);
            applied.Add(key);
        }
    }

    private async Task ApplyAddedOptions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventTemplate targetTemplate,
        List<EventCustomPropertyDefinition> trackedDefinitions,
        List<string> applied,
        List<string> skipped,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var allowed = new HashSet<string>(diff.AddedOptions.Select(x => ComposeKey(x.Namespace, x.Key)), StringComparer.OrdinalIgnoreCase);
        foreach (var key in plan.AddedOptionKeys)
        {
            if (!allowed.Contains(key))
            {
                skipped.Add(key);
                continue;
            }

            var parentDefinition = targetTemplate.Definitions.FirstOrDefault(x => x.Options.Any(o => ComposeKey(o.Namespace, o.Key).Equals(key, StringComparison.OrdinalIgnoreCase)));
            var templateOption = parentDefinition?.Options.FirstOrDefault(x => ComposeKey(x.Namespace, x.Key).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (parentDefinition is null || templateOption is null)
            {
                skipped.Add(key);
                continue;
            }

            var trackedDefinition = trackedDefinitions.FirstOrDefault(x => ComposeKey(x.Namespace, x.Key).Equals(ComposeKey(parentDefinition.Namespace, parentDefinition.Key), StringComparison.OrdinalIgnoreCase));
            if (trackedDefinition is null)
            {
                skipped.Add(key);
                continue;
            }

            var option = new EventCustomPropertyOption
            {
                Id = Guid.CreateVersion7(),
                EventCustomPropertyDefinitionId = trackedDefinition.Id,
                Namespace = templateOption.Namespace,
                Key = templateOption.Key,
                DisplayName = templateOption.DisplayName,
                Description = templateOption.Description,
                Value = templateOption.Value,
                IsDefault = templateOption.IsDefault,
                IsActive = templateOption.IsActive,
                SortOrder = templateOption.SortOrder,
                ParentOptionId = ResolveTrackedParentOptionId(trackedDefinition, parentDefinition, templateOption),
                SourceTemplateOptionId = templateOption.Id,
                SourceTemplateVersion = targetTemplate.Version,
                CreatedAt = now.UtcDateTime,
                CreatedBy = _currentUserService.UserId,
                UpdatedAt = now.UtcDateTime,
                UpdatedBy = _currentUserService.UserId
            };

            await _eventCustomPropertyRepository.CreateOption(option, cancellationToken);
            if (templateOption.IsDefault)
            {
                trackedDefinition.DefaultOptionId = option.Id;
                trackedDefinition.UpdatedAt = now.UtcDateTime;
                trackedDefinition.UpdatedBy = _currentUserService.UserId;
                await _eventCustomPropertyRepository.Update(trackedDefinition);
            }

            applied.Add(key);
        }
    }

    private async Task ApplyModifiedOptions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventTemplate targetTemplate,
        List<EventCustomPropertyDefinition> trackedDefinitions,
        List<string> applied,
        List<string> skipped,
        List<SyncConflictDto> conflicts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var diffMap = diff.ModifiedOptions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var key in plan.ModifiedOptionKeys)
        {
            if (!diffMap.TryGetValue(key, out var diffItem))
            {
                skipped.Add(key);
                continue;
            }

            var trackedDefinition = trackedDefinitions.FirstOrDefault(x => x.Options.Any(o => ComposeKey(o.Namespace, o.Key).Equals(key, StringComparison.OrdinalIgnoreCase)));
            var trackedOption = trackedDefinition?.Options.FirstOrDefault(o => ComposeKey(o.Namespace, o.Key).Equals(key, StringComparison.OrdinalIgnoreCase));
            var templateDefinition = targetTemplate.Definitions.FirstOrDefault(x => x.Options.Any(o => ComposeKey(o.Namespace, o.Key).Equals(key, StringComparison.OrdinalIgnoreCase)));
            var templateOption = templateDefinition?.Options.FirstOrDefault(o => ComposeKey(o.Namespace, o.Key).Equals(key, StringComparison.OrdinalIgnoreCase));

            if (trackedDefinition is null || trackedOption is null || templateDefinition is null || templateOption is null)
            {
                skipped.Add(key);
                continue;
            }

            if (trackedOption.ConcurrencyStamp != diffItem.CurrentConcurrencyStamp)
            {
                conflicts.Add(new SyncConflictDto(key, ConcurrencyConflictException.ConcurrentUpdate));
                skipped.Add(key);
                continue;
            }

            trackedOption.DisplayName = templateOption.DisplayName;
            trackedOption.Description = templateOption.Description;
            trackedOption.Value = templateOption.Value;
            trackedOption.IsDefault = templateOption.IsDefault;
            trackedOption.IsActive = templateOption.IsActive;
            trackedOption.SortOrder = templateOption.SortOrder;
            trackedOption.ParentOptionId = ResolveTrackedParentOptionId(trackedDefinition, templateDefinition, templateOption);
            trackedOption.SourceTemplateOptionId = templateOption.Id;
            trackedOption.SourceTemplateVersion = targetTemplate.Version;
            trackedOption.UpdatedAt = now.UtcDateTime;
            trackedOption.UpdatedBy = _currentUserService.UserId;
            trackedOption.ConcurrencyStamp = Guid.CreateVersion7();
            await _eventCustomPropertyRepository.UpdateOption(trackedOption, cancellationToken);

            if (templateOption.IsDefault)
            {
                trackedDefinition.DefaultOptionId = trackedOption.Id;
                trackedDefinition.UpdatedAt = now.UtcDateTime;
                trackedDefinition.UpdatedBy = _currentUserService.UserId;
                await _eventCustomPropertyRepository.Update(trackedDefinition);
            }

            applied.Add(key);
        }
    }

    private async Task ApplyRetiredOptions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        List<EventCustomPropertyDefinition> trackedDefinitions,
        List<string> applied,
        List<string> skipped,
        List<SyncConflictDto> conflicts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var diffMap = diff.RetiredOptions.ToDictionary(x => ComposeKey(x.Namespace, x.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var key in plan.RetiredOptionKeys)
        {
            if (!diffMap.TryGetValue(key, out var diffItem))
            {
                skipped.Add(key);
                continue;
            }

            var trackedDefinition = trackedDefinitions.FirstOrDefault(x => x.Options.Any(o => ComposeKey(o.Namespace, o.Key).Equals(key, StringComparison.OrdinalIgnoreCase)));
            var trackedOption = trackedDefinition?.Options.FirstOrDefault(o => ComposeKey(o.Namespace, o.Key).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (trackedDefinition is null || trackedOption is null)
            {
                skipped.Add(key);
                continue;
            }

            if (trackedOption.ConcurrencyStamp != diffItem.CurrentConcurrencyStamp)
            {
                conflicts.Add(new SyncConflictDto(key, ConcurrencyConflictException.ConcurrentUpdate));
                skipped.Add(key);
                continue;
            }

            trackedOption.IsActive = false;
            trackedOption.UpdatedAt = now.UtcDateTime;
            trackedOption.UpdatedBy = _currentUserService.UserId;
            trackedOption.ConcurrencyStamp = Guid.CreateVersion7();
            await _eventCustomPropertyRepository.UpdateOption(trackedOption, cancellationToken);

            if (trackedDefinition.DefaultOptionId == trackedOption.Id)
            {
                trackedDefinition.DefaultOptionId = null;
                trackedDefinition.UpdatedAt = now.UtcDateTime;
                trackedDefinition.UpdatedBy = _currentUserService.UserId;
                await _eventCustomPropertyRepository.Update(trackedDefinition);
            }

            applied.Add(key);
        }
    }

    private async Task WriteAuditAsync(
        Event @event,
        TemplateSyncPlanDto plan,
        IReadOnlyList<string> applied,
        IReadOnlyList<string> skipped,
        IReadOnlyList<SyncConflictDto> conflicts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var audit = new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = @event.TenantId,
            Tenant = null!,
            EntityType = nameof(Event),
            EntityId = @event.Id.ToString(),
            Action = "TemplateSyncApplied",
            OldValues = JsonSerializer.Serialize(new { BaseVersion = plan.BaseProvenanceVersion }),
            NewValues = JsonSerializer.Serialize(new
            {
                TargetVersion = plan.TargetTemplateVersion,
                Applied = applied,
                Skipped = skipped,
                Conflicts = conflicts
            }),
            AffectedColumns = JsonSerializer.Serialize(applied),
            ActorId = _currentUserService.UserId,
            Timestamp = now.UtcDateTime
        };

        await _auditLogRepository.Create(audit);
    }

    private static TemplateSyncOutcomeDto BuildConflictOutcome(
        IReadOnlyList<string> requestedKeys,
        string reason,
        int targetTemplateVersion,
        int currentBaseVersion)
        => new(
            [],
            requestedKeys,
            requestedKeys.Select(x => new SyncConflictDto(x, reason)).ToList(),
            currentBaseVersion,
            DateTimeOffset.UtcNow);

    private static List<string> GetRequestedKeys(TemplateSyncPlanDto plan)
        =>
        [
            .. plan.AddedDefinitionKeys,
            .. plan.ModifiedDefinitionKeys,
            .. plan.RetiredDefinitionKeys,
            .. plan.AddedOptionKeys,
            .. plan.ModifiedOptionKeys,
            .. plan.RetiredOptionKeys,
        ];

    private static string ComposeKey(string namespaceValue, string key)
        => $"{CustomPropertyIdentity.NormalizeNamespace(namespaceValue)}/{CustomPropertyIdentity.NormalizeKey(key)}";

    private static Guid? ResolveTrackedParentOptionId(
        EventCustomPropertyDefinition trackedDefinition,
        EventTemplateCustomPropertyDefinition templateDefinition,
        EventTemplateCustomPropertyOption templateOption)
    {
        if (!templateOption.ParentOptionId.HasValue)
            return null;

        var parentTemplateOption = templateDefinition.Options.FirstOrDefault(x => x.Id == templateOption.ParentOptionId.Value);
        if (parentTemplateOption is null)
            return null;

        return trackedDefinition.Options
            .FirstOrDefault(x => ComposeKey(x.Namespace, x.Key).Equals(ComposeKey(parentTemplateOption.Namespace, parentTemplateOption.Key), StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }
}
