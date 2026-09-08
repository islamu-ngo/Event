using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.DTOs.EventSessionTemplateSync.Validators;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using FluentValidation.Results;

namespace Explore.Application.Services;

public class EventSessionTemplateSyncService : IEventSessionTemplateSyncService
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventSessionTemplateRepository _eventSessionTemplateRepository;
    private readonly IEventSessionCustomPropertyRepository _eventSessionCustomPropertyRepository;
    private readonly IEventSessionTemplateDiffService _eventSessionTemplateDiffService;
    private readonly IEventSessionCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public EventSessionTemplateSyncService(
        IEventSessionRepository eventSessionRepository,
        IEventSessionTemplateRepository eventSessionTemplateRepository,
        IEventSessionCustomPropertyRepository eventSessionCustomPropertyRepository,
        IEventSessionTemplateDiffService eventSessionTemplateDiffService,
        IEventSessionCustomPropertyProjectionUpdater projectionUpdater,
        IAuditLogRepository auditLogRepository,
        ICustomPropertyQuotaResolver quotaResolver,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
    {
        _eventSessionRepository = eventSessionRepository;
        _eventSessionTemplateRepository = eventSessionTemplateRepository;
        _eventSessionCustomPropertyRepository = eventSessionCustomPropertyRepository;
        _eventSessionTemplateDiffService = eventSessionTemplateDiffService;
        _projectionUpdater = projectionUpdater;
        _auditLogRepository = auditLogRepository;
        _quotaResolver = quotaResolver;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
    }

    public async Task<TemplateSyncOutcomeDto> ApplySyncAsync(
        Guid eventSessionId,
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

        var eventSession = await _eventSessionRepository.GetById(eventSessionId)
            ?? throw new NotFoundException(nameof(EventSession), eventSessionId);

        var requestedKeys = GetRequestedKeys(plan);
        if ((eventSession.SourceTemplateVersion ?? 0) != baseProvenanceVersion)
        {
            return BuildConflictOutcome(requestedKeys, ConcurrencyConflictException.StaleSyncBase, plan.TargetTemplateVersion, baseProvenanceVersion);
        }

        var diff = await _eventSessionTemplateDiffService.ComputeDiffAsync(eventSessionId, plan.TargetTemplateVersion, cancellationToken);
        var quota = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.SyncApplyMaxChangeCount.Key,
            eventSession.TenantId,
            cancellationToken);

        var totalChangeCount = plan.GetTotalChangeCount();
        if (totalChangeCount > quota)
        {
            throw new QuotaExceededException(
                "Session template sync plan exceeds the tenant change-count quota.",
                CustomPropertyQuotaSettingDefinitions.SyncApplyMaxChangeCount.Key,
                quota,
                totalChangeCount,
                totalChangeCount,
                "event_session_template_sync",
                eventSession.TenantId);
        }

        var maxPayloadBytes = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.SyncApplyMaxPayloadBytes.Key,
            eventSession.TenantId,
            cancellationToken);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(plan).Length;
        if (payloadBytes > maxPayloadBytes)
        {
            throw new QuotaExceededException(
                "Session template sync plan payload exceeds the tenant payload-size quota.",
                CustomPropertyQuotaSettingDefinitions.SyncApplyMaxPayloadBytes.Key,
                maxPayloadBytes,
                payloadBytes,
                payloadBytes,
                "event_session_template_sync_payload",
                eventSession.TenantId);
        }

        var templateKey = eventSession.SourceTemplateKey;
        if (string.IsNullOrWhiteSpace(templateKey) || !eventSession.SourceTemplateId.HasValue)
        {
            return BuildConflictOutcome(requestedKeys, "missing_template_provenance", plan.TargetTemplateVersion, eventSession.SourceTemplateVersion ?? 0);
        }

        var targetTemplate = await _eventSessionTemplateRepository.GetPublishedSessionTemplateVersion(
            eventSession.SourceTemplateId.Value,
            templateKey,
            plan.TargetTemplateVersion,
            cancellationToken)
            ?? throw new NotFoundException(nameof(EventSessionTemplate), $"{templateKey}:{plan.TargetTemplateVersion}");

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var trackedSession = await _eventSessionRepository.GetById(eventSessionId)
                    ?? throw new NotFoundException(nameof(EventSession), eventSessionId);

                if ((trackedSession.SourceTemplateVersion ?? 0) != baseProvenanceVersion)
                {
                    return BuildConflictOutcome(requestedKeys, ConcurrencyConflictException.StaleSyncBase, plan.TargetTemplateVersion, trackedSession.SourceTemplateVersion ?? 0);
                }

                var trackedDefinitions = await _eventSessionCustomPropertyRepository.GetTrackedDefinitionsForSession(eventSessionId, ct);
                var applied = new List<string>();
                var skipped = new List<string>();
                var conflicts = new List<SyncConflictDto>();
                var now = DateTimeOffset.UtcNow;

                await EnforceResultingQuotaAsync(plan, diff, targetTemplate, trackedDefinitions, trackedSession.TenantId, ct);

                await ApplyAddedDefinitions(plan, diff, targetTemplate, trackedSession, applied, skipped, trackedDefinitions, now, ct);
                await ApplyModifiedDefinitions(plan, diff, targetTemplate, trackedDefinitions, applied, skipped, conflicts, now, ct);
                await ApplyRetiredDefinitions(plan, diff, trackedDefinitions, applied, skipped, conflicts, now, ct);
                await ApplyAddedOptions(plan, diff, targetTemplate, trackedDefinitions, applied, skipped, now, ct);
                await ApplyModifiedOptions(plan, diff, targetTemplate, trackedDefinitions, applied, skipped, conflicts, now, ct);
                await ApplyRetiredOptions(plan, diff, trackedDefinitions, applied, skipped, conflicts, now, ct);

                if (applied.Count > 0)
                {
                    trackedSession.SourceTemplateId = targetTemplate.Id;
                    trackedSession.SourceTemplateKey = targetTemplate.SessionTemplateKey;
                    trackedSession.SourceTemplateVersion = targetTemplate.Version;
                    trackedSession.LastSyncedFromTemplateAt = now;
                    await _eventSessionRepository.Update(trackedSession);

                    await _projectionUpdater.RefreshForEventSessionAsync(eventSessionId, ct);
                    await WriteAuditAsync(trackedSession, plan, applied, skipped, conflicts, now, ct);
                }

                return new TemplateSyncOutcomeDto(applied, skipped, conflicts, applied.Count > 0 ? targetTemplate.Version : trackedSession.SourceTemplateVersion ?? 0, now);
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return BuildConflictOutcome(requestedKeys, ConcurrencyConflictException.ConcurrentUpdate, plan.TargetTemplateVersion, eventSession.SourceTemplateVersion ?? 0);
        }
        catch (QuotaExceededException)
        {
            throw;
        }
        catch
        {
            return BuildConflictOutcome(requestedKeys, "apply_failed", plan.TargetTemplateVersion, eventSession.SourceTemplateVersion ?? 0);
        }
    }

    private async Task EnforceResultingQuotaAsync(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventSessionTemplate targetTemplate,
        List<EventSessionCustomPropertyDefinition> trackedDefinitions,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var addedDefinitionKeys = GetApplicableAddedDefinitionKeys(plan, diff, targetTemplate).ToList();
        var maxDefinitions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEventSession.Key,
            tenantId,
            cancellationToken);
        var attemptedDefinitionCount = trackedDefinitions.Count + addedDefinitionKeys.Count;
        if (attemptedDefinitionCount > maxDefinitions)
        {
            throw new QuotaExceededException(
                "Session template sync would exceed the event-session custom-property definition quota.",
                CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEventSession.Key,
                maxDefinitions,
                trackedDefinitions.Count,
                attemptedDefinitionCount,
                "event_session_custom_property_definitions",
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
                    "Session template sync would exceed the event-session custom-property option quota.",
                    CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                    maxOptions,
                    null,
                    optionCount,
                    "event_session_custom_property_options",
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
                    "Session template sync would exceed the event-session custom-property option quota.",
                    CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                    maxOptions,
                    currentOptionCount,
                    attemptedOptionCount,
                    "event_session_custom_property_options",
                    tenantId);
            }
        }
    }

    private static IEnumerable<string> GetApplicableAddedDefinitionKeys(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventSessionTemplate targetTemplate)
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

    private static IEnumerable<(EventSessionCustomPropertyDefinition Definition, int AddedOptionCount)> GetApplicableAddedOptionCounts(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventSessionTemplate targetTemplate,
        List<EventSessionCustomPropertyDefinition> trackedDefinitions)
    {
        var allowed = new HashSet<string>(diff.AddedOptions.Select(x => ComposeKey(x.Namespace, x.Key)), StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<Guid, (EventSessionCustomPropertyDefinition Definition, int Count)>();

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
        EventSessionTemplate targetTemplate,
        EventSession trackedSession,
        List<string> applied,
        List<string> skipped,
        List<EventSessionCustomPropertyDefinition> trackedDefinitions,
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
            var options = new List<EventSessionCustomPropertyOption>();

            foreach (var templateOption in templateDefinition.Options.OrderBy(x => x.SortOrder))
            {
                var optionId = Guid.CreateVersion7();
                optionIdMap[templateOption.Id] = optionId;
                options.Add(new EventSessionCustomPropertyOption
                {
                    Id = optionId,
                    EventSessionCustomPropertyDefinitionId = definitionId,
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

            var runtimeDefinition = new EventSessionCustomPropertyDefinition
            {
                Id = definitionId,
                EventSessionId = trackedSession.Id,
                TenantId = trackedSession.TenantId,
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
                SourceTemplateKey = targetTemplate.SessionTemplateKey,
                SourceTemplateVersion = targetTemplate.Version,
                SourceTemplateDefinitionId = templateDefinition.Id,
                InstantiatedAt = now,
                LastSyncedFromTemplateAt = now,
                CreatedAt = now.UtcDateTime,
                CreatedBy = _currentUserService.UserId,
                UpdatedAt = now.UtcDateTime,
                UpdatedBy = _currentUserService.UserId
            };

            await _eventSessionCustomPropertyRepository.CreateWithOptions(runtimeDefinition, options, defaultOptionId, cancellationToken);
            trackedDefinitions.Add(runtimeDefinition);
            applied.Add(key);
        }
    }

    private async Task ApplyModifiedDefinitions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventSessionTemplate targetTemplate,
        List<EventSessionCustomPropertyDefinition> trackedDefinitions,
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
            trackedDefinition.SourceTemplateKey = targetTemplate.SessionTemplateKey;
            trackedDefinition.SourceTemplateVersion = targetTemplate.Version;
            trackedDefinition.SourceTemplateDefinitionId = templateDefinition.Id;
            trackedDefinition.LastSyncedFromTemplateAt = now;
            trackedDefinition.UpdatedAt = now.UtcDateTime;
            trackedDefinition.UpdatedBy = _currentUserService.UserId;
            trackedDefinition.ConcurrencyStamp = Guid.CreateVersion7();

            await _eventSessionCustomPropertyRepository.Update(trackedDefinition);
            applied.Add(key);
        }
    }

    private async Task ApplyRetiredDefinitions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        List<EventSessionCustomPropertyDefinition> trackedDefinitions,
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
            await _eventSessionCustomPropertyRepository.Update(trackedDefinition);
            applied.Add(key);
        }
    }

    private async Task ApplyAddedOptions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventSessionTemplate targetTemplate,
        List<EventSessionCustomPropertyDefinition> trackedDefinitions,
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

            var option = new EventSessionCustomPropertyOption
            {
                Id = Guid.CreateVersion7(),
                EventSessionCustomPropertyDefinitionId = trackedDefinition.Id,
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

            await _eventSessionCustomPropertyRepository.CreateOption(option, cancellationToken);
            if (templateOption.IsDefault)
            {
                trackedDefinition.DefaultOptionId = option.Id;
                trackedDefinition.UpdatedAt = now.UtcDateTime;
                trackedDefinition.UpdatedBy = _currentUserService.UserId;
                await _eventSessionCustomPropertyRepository.Update(trackedDefinition);
            }

            applied.Add(key);
        }
    }

    private async Task ApplyModifiedOptions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        EventSessionTemplate targetTemplate,
        List<EventSessionCustomPropertyDefinition> trackedDefinitions,
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
            await _eventSessionCustomPropertyRepository.UpdateOption(trackedOption, cancellationToken);

            if (templateOption.IsDefault)
            {
                trackedDefinition.DefaultOptionId = trackedOption.Id;
                trackedDefinition.UpdatedAt = now.UtcDateTime;
                trackedDefinition.UpdatedBy = _currentUserService.UserId;
                await _eventSessionCustomPropertyRepository.Update(trackedDefinition);
            }

            applied.Add(key);
        }
    }

    private async Task ApplyRetiredOptions(
        TemplateSyncPlanDto plan,
        TemplateDiffDto diff,
        List<EventSessionCustomPropertyDefinition> trackedDefinitions,
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
            await _eventSessionCustomPropertyRepository.UpdateOption(trackedOption, cancellationToken);

            if (trackedDefinition.DefaultOptionId == trackedOption.Id)
            {
                trackedDefinition.DefaultOptionId = null;
                trackedDefinition.UpdatedAt = now.UtcDateTime;
                trackedDefinition.UpdatedBy = _currentUserService.UserId;
                await _eventSessionCustomPropertyRepository.Update(trackedDefinition);
            }

            applied.Add(key);
        }
    }

    private async Task WriteAuditAsync(
        EventSession eventSession,
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
            TenantId = eventSession.TenantId,
            Tenant = null!,
            EntityType = nameof(EventSession),
            EntityId = eventSession.Id.ToString(),
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
        EventSessionCustomPropertyDefinition trackedDefinition,
        EventSessionTemplateCustomPropertyDefinition templateDefinition,
        EventSessionTemplateCustomPropertyOption templateOption)
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
