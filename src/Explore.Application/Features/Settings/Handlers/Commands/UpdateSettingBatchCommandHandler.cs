// ABOUTME: Batch update handler supporting BestEffort (skip locked, apply rest) and Strict (reject all) modes.
// ABOUTME: Validates each key independently and commits SMTP batches under one ordered policy lock and transaction.

namespace Explore.Application.Features.Settings.Handlers.Commands;

using System.Collections.Immutable;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.Extensions.Logging;

public class UpdateSettingBatchCommandHandler
    : IRequestHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto>
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly IUserPreferenceRepository _userPreferenceRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly ICerbosConfigResolver? _cerbosConfigResolver;
    private readonly IMediator _mediator;
    private readonly ILogger<UpdateSettingBatchCommandHandler> _logger;
    private readonly ILocationPrivacyGovernanceMutationService? _locationPrivacyMutations;
    private readonly IPublicationPolicyMutationBoundary _publicationPolicyMutationBoundary;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IEmailDeliverySettingsWriter _emailSettingsWriter;

    public UpdateSettingBatchCommandHandler(
        IHierarchicalSettingsResolver resolver,
        IUserPreferenceRepository userPreferenceRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        IMediator mediator,
        ILogger<UpdateSettingBatchCommandHandler> logger,
        IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
        IUnitOfWork unitOfWork,
        ISettingMutationLock mutationLock,
        IEmailDeliverySettingsWriter emailSettingsWriter,
        ICerbosConfigResolver? cerbosConfigResolver = null,
        ILocationPrivacyGovernanceMutationService? locationPrivacyMutations = null)
    {
        _resolver = resolver;
        _userPreferenceRepository = userPreferenceRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _adminContext = adminContext;
        _cerbosConfigResolver = cerbosConfigResolver;
        _mediator = mediator;
        _logger = logger;
        _locationPrivacyMutations = locationPrivacyMutations;
        _publicationPolicyMutationBoundary = publicationPolicyMutationBoundary;
        _unitOfWork = unitOfWork;
        _mutationLock = mutationLock;
        _emailSettingsWriter = emailSettingsWriter;
    }

    public async Task<BatchUpdateResponseDto> Handle(
        UpdateSettingBatchCommand request, CancellationToken cancellationToken)
    {
        if (request.Values.Count == 0)
        {
            return new BatchUpdateResponseDto
            {
                Success = true,
                Results = [],
                Message = "No settings to update."
            };
        }

        // Authorization check (applies to all keys in batch)
        var (authorized, authError) = await SettingCommandHelper.CheckAuthorizationAsync(
            request.Scope, _adminContext, _tenantContext, _currentUserService, cancellationToken);
        if (!authorized)
        {
            return new BatchUpdateResponseDto
            {
                Success = false,
                Results = request.Values.Keys
                    .Select(k => new SettingUpdateResultDto { Key = k, Applied = false, SkipReason = authError })
                    .ToList(),
                Message = authError
            };
        }

        var categoryDefinitions = SettingRegistry.GetByCategory(request.Category);
        var categoryKeys = categoryDefinitions is not null
            ? new HashSet<string>(categoryDefinitions.Select(definition => definition.Key))
            : [];
        if (request.Scope is SettingScope.Instance or SettingScope.Tenant
            && request.Values.Keys.Any(key => categoryKeys.Contains(key)
                && EmailDeliverySettingKeys.Contains(key)
                && SettingRegistry.Get(key) is { } definition
                && IsScopeAllowed(definition, request.Scope)))
        {
            // Resolve governance and validate values while the complete SMTP policy is stable.
            // Allocate deferred effects per attempt so transaction retries cannot duplicate them.
            var mutation = await _mutationLock.ExecuteOrderedGroupsAsync(
                [EmailDeliverySettingKeys.All],
                token => _unitOfWork.ExecuteSerializableAsync(async transactionToken =>
                {
                    var notifications = new List<SettingChangedNotification>();
                    var response = await HandleAuthorizedAsync(request, categoryKeys, notifications, transactionToken);
                    return (Response: response, Notifications: notifications);
                }, token), cancellationToken);

            int appliedCount = mutation.Response.Results.Count(result => result.Applied);
            if (appliedCount > 0)
                _resolver.InvalidateCache(request.Scope,
                    request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : Guid.Empty);
            foreach (var notification in mutation.Notifications)
                await _mediator.Publish(notification, CancellationToken.None);
            LogCompletedBatch(request, appliedCount, mutation.Response.Results.Count - appliedCount);
            return mutation.Response;
        }

        return await HandleAuthorizedAsync(request, categoryKeys, deferredNotifications: null, cancellationToken);
    }

    private async Task<BatchUpdateResponseDto> HandleAuthorizedAsync(
        UpdateSettingBatchCommand request,
        IReadOnlySet<string> categoryKeys,
        List<SettingChangedNotification>? deferredNotifications,
        CancellationToken cancellationToken)
    {
        Guid? resolvedUserId = await SettingCommandHelper.ResolveCurrentUserIdAsync(
            _adminContext, _currentUserService, cancellationToken);

        if (request.Scope == SettingScope.User && resolvedUserId is null)
        {
            return new BatchUpdateResponseDto
            {
                Success = false,
                Results = request.Values.Keys
                    .Select(k => new SettingUpdateResultDto
                    {
                        Key = k,
                        Applied = false,
                        SkipReason = "Unable to resolve authenticated user."
                    })
                    .ToList(),
                Message = "Unable to resolve authenticated user."
            };
        }

        // Phase 1: Validate all keys
        var context = SettingCommandHelper.BuildSettingContext(
            request.Scope, _tenantContext, resolvedUserId);
        var allKeys = request.Values.Keys.ToList();
        var resolved = await _resolver.ResolveBatchAsync(allKeys, context, cancellationToken);

        var validationResults = new List<(string Key, string Value, SettingDefinition Definition,
            string? SerializedValue, string? SkipReason, string? OldValue)>();

        for (var i = 0; i < allKeys.Count; i++)
        {
            var key = allKeys[i];
            var value = request.Values[key];
            var currentResolved = resolved[i];

            // Validate key exists in registry
            var definition = SettingRegistry.Get(key);
            if (definition is null)
            {
                validationResults.Add((key, value, null!, null, $"Key '{key}' not found in registry.", null));
                continue;
            }

            // Validate key belongs to category
            if (!categoryKeys.Contains(key))
            {
                validationResults.Add((key, value, definition, null,
                    $"Key '{key}' does not belong to category '{request.Category}'.", null));
                continue;
            }

            // Validate scope range
            if (!IsScopeAllowed(definition, request.Scope))
            {
                validationResults.Add((key, value, definition, null,
                    $"Not overridable at {request.Scope} scope.", null));
                continue;
            }

            bool isGuardedPublicationPolicyMutation = request.Scope is SettingScope.Tenant or SettingScope.Instance
                && PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal);

            // Guarded publication-policy keys own lock evaluation at their mutation boundary.
            if (!isGuardedPublicationPolicyMutation)
            {
                var (isBlocked, lockReason) = SettingCommandHelper.CheckLockState(
                    currentResolved, request.Scope);
                if (isBlocked)
                {
                    _logger.LogInformation(
                        "Batch update: skipping locked key {SettingKey} at {Scope}. Reason: {Reason}",
                        key, request.Scope, lockReason);
                    validationResults.Add((key, value, definition, null, lockReason, null));
                    continue;
                }
            }

            // Validate and serialize value
            var (isValid, serialized, valError) =
                SettingCommandHelper.ValidateAndSerialize(value, definition);
            if (!isValid)
            {
                validationResults.Add((key, value, definition, null, valError, null));
                continue;
            }

            var authorityError = await SettingCommandHelper.ValidateTenantVerificationOverrideAsync(
                key, value, request.Scope, _resolver, cancellationToken);
            if (authorityError is not null)
            {
                validationResults.Add((key, value, definition, null, authorityError, null));
                continue;
            }

            if (request.Scope == SettingScope.Tenant
                && _locationPrivacyMutations?.Handles(key) == true)
            {
                string? governanceError = await _locationPrivacyMutations.ValidateTenantValueAsync(
                    key,
                    serialized!,
                    cancellationToken);
                if (governanceError is not null)
                {
                    validationResults.Add((key, value, definition, null, governanceError, null));
                    continue;
                }
            }

            validationResults.Add((key, value, definition, serialized,
                null, currentResolved.Value));
        }

        var authorityFailure = validationResults.FirstOrDefault(
            result => result.SkipReason == SettingCommandHelper.TenantVerificationAuthorityError);
        if (authorityFailure.SkipReason is not null)
        {
            return new BatchUpdateResponseDto
            {
                Success = false,
                Results = validationResults
                    .Select(result => new SettingUpdateResultDto
                    {
                        Key = result.Key,
                        Applied = false,
                        SkipReason = result.SkipReason ?? authorityFailure.SkipReason
                    })
                    .ToList(),
                Message = authorityFailure.SkipReason
            };
        }

        // Strict mode: reject all if any invalid
        if (request.Mode == BatchUpdateMode.Strict)
        {
            var blocked = validationResults.Where(r => r.SkipReason is not null).ToList();
            if (blocked.Count > 0)
            {
                var blockedKeys = string.Join(", ", blocked.Select(b => b.Key));
                return new BatchUpdateResponseDto
                {
                    Success = false,
                    Results = validationResults
                        .Select(r => new SettingUpdateResultDto
                        {
                            Key = r.Key,
                            Applied = false,
                            SkipReason = r.SkipReason ?? $"Rejected: other keys in batch are blocked ({blockedKeys})"
                        })
                        .ToList(),
                    Message = $"Batch rejected (strict mode): {blocked.Count} key(s) blocked."
                };
            }
        }

        var smtpEntries = validationResults.Where(result => result.SkipReason is null
            && EmailDeliverySettingKeys.Contains(result.Key)).ToArray();
        var appliedSmtpKeys = new HashSet<string>(StringComparer.Ordinal);
        if (smtpEntries.Length > 0)
        {
            var smtpResult = await _emailSettingsWriter.ApplyAsync(
                smtpEntries.Select(entry => new EmailDeliverySettingMutation(
                    TenantId: request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                    Key: entry.Definition.Key,
                    Kind: EmailDeliverySettingMutationKind.SetValue,
                    Value: entry.SerializedValue)).ToImmutableArray(),
                actorUserId: resolvedUserId, cancellationToken: cancellationToken);
            if (!smtpResult.IsAccepted())
            {
                string message = smtpResult.Status.FailureMessage();
                if (request.Mode == BatchUpdateMode.Strict)
                    return new BatchUpdateResponseDto
                    {
                        Success = false,
                        Results = validationResults.Select(entry => new SettingUpdateResultDto
                            { Key = entry.Key, Applied = false, SkipReason = entry.SkipReason ?? message }).ToList(),
                        Message = message
                    };
                for (int index = 0; index < validationResults.Count; index++)
                {
                    var entry = validationResults[index];
                    if (entry.SkipReason is null && EmailDeliverySettingKeys.Contains(entry.Key))
                        validationResults[index] = (Key: entry.Key, Value: entry.Value, Definition: entry.Definition,
                            SerializedValue: entry.SerializedValue, SkipReason: message, OldValue: entry.OldValue);
                }
            }
            else
            {
                appliedSmtpKeys.UnionWith(smtpEntries.Select(entry => entry.Key));
                foreach (var notification in smtpResult.ToNotifications(resolvedUserId))
                {
                    if (deferredNotifications is null)
                        await _mediator.Publish(notification, CancellationToken.None);
                    else
                        deferredNotifications.Add(notification);
                }
            }
        }

        bool hasValidGuardedEntries = request.Scope is SettingScope.Tenant or SettingScope.Instance
            && validationResults.Any(result => result.SkipReason is null
                && PublicationPolicySettingKeys.All.Contains(result.Key, StringComparer.Ordinal));
        if (hasValidGuardedEntries)
        {
            return await ApplyGuardedBatchAsync(
                request,
                validationResults,
                allKeys.Zip(resolved).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal),
                resolvedUserId,
                cancellationToken);
        }

        // Phase 2: Apply valid updates
        var (scopeId, actorId) = SettingCommandHelper.GetScopeAndActorIds(
            request.Scope, _tenantContext, resolvedUserId);
        var results = new List<SettingUpdateResultDto>(validationResults.Count);
        var appliedCount = 0;

        foreach (var (key, _, definition, serializedValue, skipReason, oldValue) in validationResults)
        {
            if (skipReason is not null)
            {
                results.Add(new SettingUpdateResultDto { Key = key, Applied = false, SkipReason = skipReason });
                continue;
            }

            if (appliedSmtpKeys.Contains(key))
            {
                results.Add(new SettingUpdateResultDto { Key = key, Applied = true });
                appliedCount++;
                continue;
            }

            if (request.Scope == SettingScope.User)
            {
                await WriteUserPreferenceAsync(key, serializedValue!, actorId, cancellationToken);
            }
            else
            {
                if (_locationPrivacyMutations?.Handles(key) == true)
                {
                    LocationPrivacyGovernanceMutationResult mutation = await _locationPrivacyMutations.ExecuteAsync(
                        key,
                        serializedValue!,
                        request.Scope,
                        request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                        actorId,
                        async token =>
                        {
                            await _resolver.SetValueAsync(
                                key,
                                serializedValue!,
                                request.Scope,
                                scopeId,
                                actorId,
                                token);
                            return oldValue;
                        },
                        cancellationToken);
                    if (!mutation.Accepted)
                    {
                        results.Add(new SettingUpdateResultDto
                        {
                            Key = key,
                            Applied = false,
                            SkipReason = mutation.Error
                        });
                        continue;
                    }

                    await _locationPrivacyMutations.InvalidateMutationAsync(
                        request.Scope,
                        request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                        mutation.CorrectedProjections,
                        CancellationToken.None);
                }
                else
                {
                    await _resolver.SetValueAsync(
                        key, serializedValue!, request.Scope, scopeId, actorId, cancellationToken);
                }
            }

            var notification = new SettingChangedNotification(
                key, oldValue, serializedValue,
                SettingCommandHelper.MapScopeToSource(request.Scope),
                _tenantContext.TenantId, actorId, DateTime.UtcNow);
            if (deferredNotifications is null)
                await _mediator.Publish(notification, CancellationToken.None);
            else
                deferredNotifications.Add(notification);

            results.Add(new SettingUpdateResultDto { Key = key, Applied = true });
            appliedCount++;
        }

        // Single cache invalidation after all writes
        if (appliedCount > 0 && deferredNotifications is null)
        {
            if (request.Scope == SettingScope.User)
                _resolver.InvalidateUserCache(_tenantContext.TenantId, actorId);
            else
            {
                _resolver.InvalidateCache(request.Scope, scopeId);
                CerbosSettingsCacheInvalidation.InvalidateIfAnyCerbosSettingChanged(
                    _cerbosConfigResolver,
                    results.Where(result => result.Applied).Select(result => result.Key),
                    request.Scope,
                    scopeId);
            }
        }

        var skippedCount = results.Count - appliedCount;
        if (deferredNotifications is null)
            LogCompletedBatch(request, appliedCount, skippedCount);

        return new BatchUpdateResponseDto
        {
            Success = appliedCount > 0 || skippedCount == 0,
            Results = results,
            Message = skippedCount > 0
                ? $"{appliedCount} setting(s) updated, {skippedCount} skipped."
                : $"{appliedCount} setting(s) updated successfully."
        };
    }

    private void LogCompletedBatch(UpdateSettingBatchCommand request, int appliedCount, int skippedCount) =>
        _logger.LogInformation(
            "Batch update complete for category '{Category}' at {Scope}: {Applied} applied, {Skipped} skipped",
            request.Category, request.Scope, appliedCount, skippedCount);

    private static bool IsScopeAllowed(SettingDefinition definition, SettingScope scope) =>
        scope >= definition.MinScope && scope <= definition.MaxScope;

    private async Task<BatchUpdateResponseDto> ApplyGuardedBatchAsync(
        UpdateSettingBatchCommand request,
        List<(string Key, string Value, SettingDefinition Definition, string? SerializedValue, string? SkipReason, string? OldValue)> validationResults,
        IReadOnlyDictionary<string, ResolvedSetting> resolvedByKey,
        Guid? resolvedUserId,
        CancellationToken cancellationToken)
    {
        var (scopeId, actorId) = SettingCommandHelper.GetScopeAndActorIds(
            request.Scope, _tenantContext, resolvedUserId);
        DateTime occurredAtUtc = DateTime.UtcNow;
        var guardedEntries = validationResults
            .Where(result => result.SkipReason is null
                && PublicationPolicySettingKeys.All.Contains(result.Key, StringComparer.Ordinal))
            .OrderBy(result => PublicationPolicyOrder(result.Key))
            .ToList();
        var guardedKeys = guardedEntries.Select(result => result.Key).ToHashSet(StringComparer.Ordinal);

        GuardedBatchTransactionOutcome outcome = await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var mutations = guardedEntries.Select(result => new PublicationPolicySettingMutation(
                    result.Key,
                    PublicationPolicyMutationKind.Set,
                    result.SerializedValue,
                    request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                    request.Scope == SettingScope.Instance
                        ? resolvedByKey[result.Key].IsLocked
                        : null)).ToImmutableArray();
                PublicationPolicyMutationResult boundaryResult = request.Scope == SettingScope.Tenant
                    ? await _publicationPolicyMutationBoundary.ApplyTenantAsync(
                        new PublicationPolicyTenantMutationRequest(
                            _tenantContext.TenantId,
                            actorId,
                            occurredAtUtc,
                            mutations,
                            PublicationPolicyLockedSystemBehavior.Reject),
                        token)
                    : await _publicationPolicyMutationBoundary.ApplyInstanceAsync(
                        new PublicationPolicyInstanceMutationRequest(actorId, occurredAtUtc, mutations), token);
                if (!boundaryResult.Success)
                {
                    return GuardedBatchTransactionOutcome.Rejected(
                        string.IsNullOrWhiteSpace(boundaryResult.FailureCode)
                            ? "event_reporting_intake_policy_invalid"
                            : boundaryResult.FailureCode,
                        boundaryResult.Message);
                }

                var results = new List<SettingUpdateResultDto>(validationResults.Count);
                var notifications = boundaryResult.DeferredNotifications.ToList();
                var correctedProjectionSets = new List<IReadOnlyList<LocationPrivacyProjectionIdentity>>();
                foreach (var (key, _, _, serializedValue, skipReason, oldValue) in validationResults)
                {
                    if (skipReason is not null)
                    {
                        results.Add(new SettingUpdateResultDto { Key = key, Applied = false, SkipReason = skipReason });
                        continue;
                    }

                    if (guardedKeys.Contains(key))
                    {
                        results.Add(new SettingUpdateResultDto { Key = key, Applied = true });
                        continue;
                    }

                    if (_locationPrivacyMutations?.Handles(key) == true)
                    {
                        LocationPrivacyGovernanceMutationResult locationMutation =
                            await _locationPrivacyMutations.ExecuteAsync(
                                key,
                                serializedValue!,
                                request.Scope,
                                request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                                actorId,
                                async persistToken =>
                                {
                                    await _resolver.SetValueAsync(
                                        key, serializedValue!, request.Scope, scopeId, actorId, persistToken);
                                    return oldValue;
                                },
                                token);
                        if (!locationMutation.Accepted)
                        {
                            results.Add(new SettingUpdateResultDto
                            {
                                Key = key,
                                Applied = false,
                                SkipReason = locationMutation.Error
                            });
                            continue;
                        }

                        correctedProjectionSets.Add(locationMutation.CorrectedProjections);
                    }
                    else
                    {
                        await _resolver.SetValueAsync(
                            key, serializedValue!, request.Scope, scopeId, actorId, token);
                    }

                    notifications.Add(new SettingChangedNotification(
                        key,
                        oldValue,
                        serializedValue,
                        SettingCommandHelper.MapScopeToSource(request.Scope),
                        _tenantContext.TenantId,
                        actorId,
                        occurredAtUtc));
                    results.Add(new SettingUpdateResultDto { Key = key, Applied = true });
                }

                return GuardedBatchTransactionOutcome.Accepted(results, notifications, correctedProjectionSets);
            },
            cancellationToken);

        if (!outcome.Success)
        {
            return new BatchUpdateResponseDto
            {
                Success = false,
                Results = validationResults.Select(result => new SettingUpdateResultDto
                {
                    Key = result.Key,
                    Applied = false,
                    SkipReason = result.SkipReason ?? outcome.FailureCode
                }).ToList(),
                Message = outcome.Message
            };
        }

        foreach (IReadOnlyList<LocationPrivacyProjectionIdentity> corrected in outcome.CorrectedProjectionSets)
        {
            await _locationPrivacyMutations!.InvalidateMutationAsync(
                request.Scope,
                request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                corrected,
                CancellationToken.None);
        }

        int appliedCount = outcome.Results.Count(result => result.Applied);
        if (appliedCount > 0)
        {
            _resolver.InvalidateCache(request.Scope, scopeId);
            CerbosSettingsCacheInvalidation.InvalidateIfAnyCerbosSettingChanged(
                _cerbosConfigResolver,
                outcome.Results.Where(result => result.Applied).Select(result => result.Key),
                request.Scope,
                scopeId);
        }

        foreach (SettingChangedNotification notification in outcome.Notifications)
        {
            await _mediator.Publish(notification, CancellationToken.None);
        }

        int skippedCount = outcome.Results.Count - appliedCount;
        _logger.LogInformation(
            "Batch update complete for category '{Category}' at {Scope}: {Applied} applied, {Skipped} skipped",
            request.Category, request.Scope, appliedCount, skippedCount);
        return new BatchUpdateResponseDto
        {
            Success = appliedCount > 0 || skippedCount == 0,
            Results = outcome.Results,
            Message = skippedCount > 0
                ? $"{appliedCount} setting(s) updated, {skippedCount} skipped."
                : $"{appliedCount} setting(s) updated successfully."
        };
    }

    private static int PublicationPolicyOrder(string key)
    {
        for (var index = 0; index < PublicationPolicySettingKeys.All.Count; index++)
        {
            if (string.Equals(PublicationPolicySettingKeys.All[index], key, StringComparison.Ordinal))
                return index;
        }

        return int.MaxValue;
    }

    private sealed record GuardedBatchTransactionOutcome(
        bool Success,
        string? FailureCode,
        string? Message,
        IReadOnlyList<SettingUpdateResultDto> Results,
        IReadOnlyList<SettingChangedNotification> Notifications,
        IReadOnlyList<IReadOnlyList<LocationPrivacyProjectionIdentity>> CorrectedProjectionSets)
    {
        public static GuardedBatchTransactionOutcome Rejected(string failureCode, string message) =>
            new(false, failureCode, message, [], [], []);

        public static GuardedBatchTransactionOutcome Accepted(
            IReadOnlyList<SettingUpdateResultDto> results,
            IReadOnlyList<SettingChangedNotification> notifications,
            IReadOnlyList<IReadOnlyList<LocationPrivacyProjectionIdentity>> correctedProjectionSets) =>
            new(true, null, null, results, notifications, correctedProjectionSets);
    }

    private async Task WriteUserPreferenceAsync(
        string key, string serializedValue, Guid userId, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var existing = await _userPreferenceRepository.GetByUserAndKey(tenantId, userId, key);

        if (existing is not null)
        {
            existing.Value = serializedValue;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = userId;
            await _userPreferenceRepository.Update(existing);
        }
        else
        {
            await _userPreferenceRepository.Create(new UserPreference
            {
                TenantId = tenantId,
                Tenant = null!,
                UserId = userId,
                SettingKey = key,
                Value = serializedValue,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            });
        }
    }
}
