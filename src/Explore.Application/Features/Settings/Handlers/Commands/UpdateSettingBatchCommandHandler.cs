namespace Explore.Application.Features.Settings.Handlers.Commands;

using System.Collections.Immutable;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

public class UpdateSettingBatchCommandHandler
    : ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto>
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly IUserPreferenceRepository _userPreferenceRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly ICerbosConfigResolver? _cerbosConfigResolver;
    private readonly IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> _notificationHandlers;
    private readonly ILogger<UpdateSettingBatchCommandHandler> _logger;
    private readonly ILocationPrivacyGovernanceMutationService? _locationPrivacyMutations;
    private readonly IPublicationPolicyMutationBoundary _publicationPolicyMutationBoundary;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IEmailDeliverySettingsWriter _emailSettingsWriter;
    private readonly IVisitorAccessSettingsWriter _visitorSettings;
    private readonly IEventResourceSettingsWriter _eventResourceSettingsWriter;
    private readonly IInstanceAdministratorCommitAuthority _instanceCommitAuthority;
    private readonly ITenantAdministratorCommitAuthority _tenantCommitAuthority;
    private readonly IHumanSubjectCommitAuthority _subjectCommitAuthority;
    private readonly IHumanCommitScopePreparation _humanCommitScope;
    private readonly IHumanSettingsMutationPreparation _settingsPreparation;
    private readonly IHumanEmailMutationPreparation _emailPreparation;
    private readonly IAtprotoFederationSettingsWriter _federationSettings;

    public UpdateSettingBatchCommandHandler(
        IHierarchicalSettingsResolver resolver,
        IUserPreferenceRepository userPreferenceRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> notificationHandlers,
        ILogger<UpdateSettingBatchCommandHandler> logger,
        IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
        IUnitOfWork unitOfWork,
        ISettingMutationLock mutationLock,
        IEmailDeliverySettingsWriter emailSettingsWriter,
        IVisitorAccessSettingsWriter visitorSettings,
        IEventResourceSettingsWriter eventResourceSettingsWriter,
        IInstanceAdministratorCommitAuthority instanceCommitAuthority,
        ITenantAdministratorCommitAuthority tenantCommitAuthority,
        IHumanSubjectCommitAuthority subjectCommitAuthority,
        IHumanCommitScopePreparation humanCommitScope,
        IHumanSettingsMutationPreparation settingsPreparation,
        IHumanEmailMutationPreparation emailPreparation,
        IAtprotoFederationSettingsWriter federationSettings,
        ICerbosConfigResolver? cerbosConfigResolver = null,
        ILocationPrivacyGovernanceMutationService? locationPrivacyMutations = null)
    {
        _resolver = resolver;
        _userPreferenceRepository = userPreferenceRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _adminContext = adminContext;
        _cerbosConfigResolver = cerbosConfigResolver;
        _notificationHandlers = notificationHandlers;
        _logger = logger;
        _locationPrivacyMutations = locationPrivacyMutations;
        _publicationPolicyMutationBoundary = publicationPolicyMutationBoundary;
        _unitOfWork = unitOfWork;
        _mutationLock = mutationLock;
        _emailSettingsWriter = emailSettingsWriter;
        _visitorSettings = visitorSettings;
        _eventResourceSettingsWriter = eventResourceSettingsWriter;
        _instanceCommitAuthority = instanceCommitAuthority;
        _tenantCommitAuthority = tenantCommitAuthority;
        _subjectCommitAuthority = subjectCommitAuthority;
        _humanCommitScope = humanCommitScope;
        _settingsPreparation = settingsPreparation;
        _emailPreparation = emailPreparation;
        _federationSettings = federationSettings;
    }

    public async Task<BatchUpdateResponseDto> ExecuteAsync(
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

        MixedSettingsBatchMutation? mixed = MixedSettingsBatchMutation.RequiresCombinedSource(request)
                ? await _emailPreparation.PrepareMixedAsync(request, cancellationToken) : null;
        IPreparedHumanSettingsMutation? prepared = null;
        Exception? primaryFailure = null;
        try
        {

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
            Guid? resolvedUserId = await SettingCommandHelper.ResolveCurrentUserIdAsync(
                _adminContext, _currentUserService, cancellationToken);
            bool hasLocationPrivacyMutation = request.Values.Keys.Any(key =>
                LocationPrivacySettingDefinitions.All.Any(item => item.Key == key));
            if (hasLocationPrivacyMutation && _locationPrivacyMutations is null)
                throw new InvalidOperationException("Location privacy settings require their governance mutation service.");
            bool requiresSpecializedOwner = request.Values.Keys.Any(EmailDeliverySettingKeys.Contains);
            bool emailOnly = requiresSpecializedOwner && request.Values.Keys.All(EmailDeliverySettingKeys.Contains);
            ImmutableDictionary<string, string>? storedValues = null;
            IPreparedHumanEmailMutation? email = null;
            if (mixed is null && (!requiresSpecializedOwner || emailOnly))
            {
                var values = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
                foreach (var (key, value) in request.Values)
                {
                    var definition = SettingRegistry.Get(key);
                    if (definition is null || !categoryKeys.Contains(key) || !IsScopeAllowed(definition, request.Scope))
                        continue;
                    var (valid, serialized, _) = SettingCommandHelper.ValidateAndSerialize(value, definition);
                    if (valid)
                        values.Add(key, serialized!);
                }
                storedValues = values.ToImmutable();
                if (emailOnly)
                    email = await _emailPreparation.PrepareApplyAsync(
                        storedValues.Select(entry => new EmailDeliverySettingMutation(
                            request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                            entry.Key, EmailDeliverySettingMutationKind.SetValue, entry.Value)).ToImmutableArray(),
                        resolvedUserId, cancellationToken);
                else
                    prepared = await _settingsPreparation.PrepareAsync(request, storedValues, cancellationToken);
                SettingCommandHelper.RequireHumanSubject(_humanCommitScope, resolvedUserId);
            }

            // Resolve governance under the original owner while the ordered policy locks are held.
            // Notifications and invalidations remain provisional until that owner settles.
            (BatchUpdateResponseDto Response, List<SettingChangedNotification> Notifications,
                List<Func<Task>> Invalidations) mutation;
            try
            {
                mutation = await _mutationLock.ExecuteOrderedGroupsAsync(
                    [request.Values.Keys.Any(VisitorAccessSettingMutationGuard.Handles)
                    ? Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys : [],
                 request.Values.Keys.Any(EventResourceSettingMutationGuard.Handles)
                    ? EventResourceSettingMutationGuard.Keys : [],
                 request.Values.Keys.Any(EmailDeliverySettingKeys.Contains) ? EmailDeliverySettingKeys.All : [],
                 request.Values.Keys.Any(key => PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
                    ? PublicationPolicySettingKeys.All : [],
                 request.Values.Keys],
                    token => mixed is not null
                        ? _emailPreparation.ExecuteMixedPreparedAsync(mixed, ApplyAsync, token)
                        : email is not null
                        ? _emailPreparation.ExecutePreparedAsync(email, ApplyAsync, token)
                        : prepared is null
                        ? ((ISerializableAttemptOwner)_unitOfWork).ExecuteAsync(ApplyAsync, token)
                        : _settingsPreparation.ExecutePreparedAsync(prepared, (_, ownerToken) => ApplyAsync(ownerToken), token),
                    cancellationToken);
            }
            catch (BatchRejectedException exception)
            {
                return exception.Response;
            }

            async Task<(BatchUpdateResponseDto Response, List<SettingChangedNotification> Notifications,
                List<Func<Task>> Invalidations)> ApplyAsync(CancellationToken transactionToken)
            {
                if (mixed is null)
                    await SettingCommandHelper.RequireCommitAuthorizationAsync(
                        request.Scope, resolvedUserId, _tenantContext.TenantId,
                        _instanceCommitAuthority, _tenantCommitAuthority, _subjectCommitAuthority, transactionToken);
                else if (resolvedUserId != mixed.Actor)
                    throw new UnauthorizedAccessException("The mixed batch cannot substitute its original administrator.");
                var notifications = new List<SettingChangedNotification>();
                var invalidations = new List<Func<Task>>();
                var response = await HandleAuthorizedAsync(
                    request, categoryKeys, resolvedUserId, notifications, invalidations, storedValues, mixed, transactionToken);
                if (!response.Success)
                    throw new BatchRejectedException(response);
                return (Response: response, Notifications: notifications, Invalidations: invalidations);
            }

            int appliedCount = mutation.Response.Results.Count(result => result.Applied);
            if (appliedCount > 0)
            {
                if (request.Scope == SettingScope.User)
                    _resolver.InvalidateUserCache(_tenantContext.TenantId, resolvedUserId!.Value);
                else
                {
                    _resolver.InvalidateCache(request.Scope,
                        request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : Guid.Empty);
                    CerbosSettingsCacheInvalidation.InvalidateIfAnyCerbosSettingChanged(
                        _cerbosConfigResolver, mutation.Response.Results.Where(result => result.Applied)
                            .Select(result => result.Key), request.Scope,
                        request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : Guid.Empty);
                }
            }
            foreach (var invalidate in mutation.Invalidations)
                await invalidate();
            foreach (var notification in mutation.Notifications)
                await _notificationHandlers.HandleAsync(notification, CancellationToken.None);
            LogCompletedBatch(request, appliedCount, mutation.Response.Results.Count - appliedCount);
            return mutation.Response;
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally
        {
            if (mixed is not null) await _emailPreparation.CloseMixedAsync(mixed, primaryFailure);
            else if (prepared is not null) await _settingsPreparation.CloseAsync(prepared, primaryFailure);
        }
    }

    private async Task<BatchUpdateResponseDto> HandleAuthorizedAsync(
        UpdateSettingBatchCommand request,
        IReadOnlySet<string> categoryKeys,
        Guid? resolvedUserId,
        List<SettingChangedNotification>? deferredNotifications,
        List<Func<Task>> deferredInvalidations,
        ImmutableDictionary<string, string>? storedValues,
        MixedSettingsBatchMutation? mixed,
        CancellationToken cancellationToken)
    {
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
        var resolved = mixed is null
            ? await _resolver.ResolveBatchAsync(allKeys, context, cancellationToken)
            : await mixed.Store.ResolveOriginalAsync(mixed, cancellationToken);

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
            if (!isGuardedPublicationPolicyMutation
                && !VisitorAccessSettingMutationGuard.Handles(key)
                && !EventResourceSettingMutationGuard.Handles(key))
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
            if (storedValues is not null)
                serialized = storedValues[key];

            string? authorityError = null;
            if (mixed is null)
                authorityError = await SettingCommandHelper.ValidateTenantVerificationOverrideAsync(
                    key, value, request.Scope, _resolver, cancellationToken);
            else if (request.Scope == SettingScope.Tenant && key == GovernanceSettingKeys.Organizations.VerificationRequired
                && bool.TryParse(value, out bool requiresVerification) && !requiresVerification)
            {
                var authority = await mixed.Store.ResolveOriginalInstanceAsync(mixed,
                    GovernanceSettingKeys.Organizations.TenantCanOmitVerification, cancellationToken);
                authorityError = authority is not null && bool.TryParse(authority.Value, out bool canOmit) && canOmit
                    ? null : SettingCommandHelper.TenantVerificationAuthorityError;
            }
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

        var resolvedByKey = allKeys.Zip(resolved).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        if (mixed is not null)
        {
            mixed.Compile(request, validationResults.Where(result => result.SkipReason is null)
                .ToImmutableDictionary(result => result.Key, result => result.SerializedValue!, StringComparer.Ordinal));
            await mixed.Store.CompileAsync(mixed, cancellationToken);
            return await _settingsPreparation.ExecuteMixedJoinedAsync(mixed, (_, token) =>
                ApplyValidatedAsync(request, validationResults, resolvedByKey, resolvedUserId,
                    deferredNotifications, deferredInvalidations, mixed, token), cancellationToken);
        }
        return await ApplyValidatedAsync(request, validationResults, resolvedByKey, resolvedUserId,
            deferredNotifications, deferredInvalidations, null, cancellationToken);
    }

    private async Task<BatchUpdateResponseDto> ApplyValidatedAsync(
        UpdateSettingBatchCommand request,
        List<(string Key, string Value, SettingDefinition Definition, string? SerializedValue, string? SkipReason, string? OldValue)> validationResults,
        IReadOnlyDictionary<string, ResolvedSetting> resolvedByKey,
        Guid? resolvedUserId,
        List<SettingChangedNotification>? deferredNotifications,
        List<Func<Task>> deferredInvalidations,
        MixedSettingsBatchMutation? mixed,
        CancellationToken cancellationToken)
    {
        var visitorEntries = validationResults.Where(result => result.SkipReason is null
            && VisitorAccessSettingMutationGuard.Handles(result.Key)).ToArray();
        var appliedVisitorKeys = new HashSet<string>(StringComparer.Ordinal);
        if (visitorEntries.Length > 0)
        {
            var visitorResult = await _visitorSettings.ApplyAsync(
                [.. visitorEntries.Select(entry => new VisitorAccessSettingMutation(
                    request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                    entry.Key, VisitorAccessSettingMutationKind.SetValue, entry.SerializedValue))],
                SettingCommandHelper.RequireHumanSubject(_humanCommitScope, resolvedUserId),
                request.Scope == SettingScope.Instance ? SettingsMutationOperation.InstanceSettings : SettingsMutationOperation.TenantSettings,
                resolvedUserId, cancellationToken);
            if (!visitorResult.Success)
                return new BatchUpdateResponseDto
                {
                    Success = false,
                    Results = validationResults.Select(entry => new SettingUpdateResultDto
                    { Key = entry.Key, Applied = false, SkipReason = entry.SkipReason ?? visitorResult.FailureCode }).ToList(),
                    Message = visitorResult.FailureCode
                };
            appliedVisitorKeys.UnionWith(visitorEntries.Select(entry => entry.Key));
            deferredNotifications!.AddRange(visitorResult.DeferredNotifications);
        }

        var resourceEntries = validationResults.Where(result => result.SkipReason is null
            && EventResourceSettingMutationGuard.Handles(result.Key)).ToArray();
        var appliedSpecializedKeys = new HashSet<string>(appliedVisitorKeys, StringComparer.Ordinal);
        if (resourceEntries.Length > 0)
        {
            EventResourceSettingsWriteResult resourceResult = await _eventResourceSettingsWriter.ApplyAsync(
                [.. resourceEntries.Select(entry => new EventResourceSettingMutation(
                    request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                    entry.Key, EventResourceSettingMutationKind.SetValue, entry.SerializedValue))],
                SettingCommandHelper.RequireHumanSubject(_humanCommitScope, resolvedUserId),
                request.Scope == SettingScope.Instance ? SettingsMutationOperation.InstanceSettings : SettingsMutationOperation.TenantSettings,
                resolvedUserId, cancellationToken);
            if (!resourceResult.Success)
            {
                if (request.Mode == BatchUpdateMode.Strict || mixed is not null)
                    return new BatchUpdateResponseDto
                    {
                        Success = false,
                        Results = validationResults.Select(entry => new SettingUpdateResultDto
                        { Key = entry.Key, Applied = false, SkipReason = entry.SkipReason ?? resourceResult.FailureCode }).ToList(),
                        Message = resourceResult.FailureCode
                    };
                for (int index = 0; index < validationResults.Count; index++)
                {
                    var entry = validationResults[index];
                    if (entry.SkipReason is null && EventResourceSettingMutationGuard.Handles(entry.Key))
                        validationResults[index] = (entry.Key, entry.Value, entry.Definition,
                            entry.SerializedValue, resourceResult.FailureCode, entry.OldValue);
                }
            }
            else
            {
                appliedSpecializedKeys.UnionWith(resourceEntries.Select(entry => entry.Key));
                deferredNotifications!.AddRange(resourceResult.DeferredNotifications);
            }
        }

        var smtpEntries = validationResults.Where(result => result.SkipReason is null
            && EmailDeliverySettingKeys.Contains(result.Key)).ToArray();
        var appliedSmtpKeys = new HashSet<string>(appliedSpecializedKeys, StringComparer.Ordinal);
        if (smtpEntries.Length > 0)
        {
            var smtpResult = mixed is not null
                ? await (_emailSettingsWriter as IMixedEmailDeliverySettingsWriter
                    ?? throw new UnauthorizedAccessException("The selected Email writer has no closed mixed seam."))
                    .ApplyMixedAsync(mixed, cancellationToken)
                : await _emailSettingsWriter.ApplyAsync(
                smtpEntries.Select(entry => new EmailDeliverySettingMutation(
                    TenantId: request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                    Key: entry.Definition.Key,
                    Kind: EmailDeliverySettingMutationKind.SetValue,
                    Value: entry.SerializedValue)).ToImmutableArray(),
                actorUserId: resolvedUserId, cancellationToken: cancellationToken);
            if (!smtpResult.IsAccepted())
            {
                string message = smtpResult.Status.FailureMessage();
                if (request.Mode == BatchUpdateMode.Strict || mixed is not null)
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
                        await _notificationHandlers.HandleAsync(notification, CancellationToken.None);
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
                resolvedByKey,
                resolvedUserId,
                appliedSmtpKeys,
                deferredNotifications!,
                deferredInvalidations,
                mixed,
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
                            if (mixed is not null)
                                await mixed.Store.ApplyLocationValueAsync(mixed, key, token);
                            else
                                await _resolver.SetValueAsync(
                                    key, serializedValue!, request.Scope, scopeId, actorId, token);
                            return oldValue;
                        },
                        cancellationToken);
                    if (!mutation.Accepted)
                    {
                        if (mixed is not null)
                            return new BatchUpdateResponseDto
                            {
                                Success = false,
                                Results = validationResults.Select(entry => new SettingUpdateResultDto
                                { Key = entry.Key, Applied = false, SkipReason = entry.SkipReason ?? mutation.Error }).ToList(),
                                Message = mutation.Error
                            };
                        results.Add(new SettingUpdateResultDto
                        {
                            Key = key,
                            Applied = false,
                            SkipReason = mutation.Error
                        });
                        continue;
                    }

                    deferredInvalidations.Add(() => _locationPrivacyMutations.InvalidateMutationAsync(
                        request.Scope,
                        request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                        mutation.CorrectedProjections,
                        CancellationToken.None));
                }
                else
                {
                    if (AtprotoFederationSettingPolicy.IsDedicatedKey(key))
                    {
                        var written = await _federationSettings.ApplyCurrentAsync(new(
                            request.Scope == SettingScope.Instance ? HumanSettingsOperation.InstanceSettings : HumanSettingsOperation.TenantSettings,
                            request.Scope, request.Scope == SettingScope.Tenant ? scopeId : null, null,
                            key, HumanSettingsMutationKind.SetValue, serializedValue), cancellationToken);
                        if (!written.Applied)
                        {
                            if (request.Mode == BatchUpdateMode.Strict || mixed is not null)
                                return new BatchUpdateResponseDto
                                {
                                    Success = false,
                                    Results = validationResults.Select(entry => new SettingUpdateResultDto
                                    { Key = entry.Key, Applied = false, SkipReason = entry.SkipReason ?? written.FailureCode }).ToList(),
                                    Message = written.FailureCode
                                };
                            results.Add(new SettingUpdateResultDto { Key = key, Applied = false, SkipReason = written.FailureCode });
                            continue;
                        }
                    }
                    else if (mixed is not null)
                        await mixed.Store.ApplyOrdinaryAsync(mixed, key, cancellationToken);
                    else
                        await _resolver.SetValueAsync(
                            key, serializedValue!, request.Scope, scopeId, actorId, cancellationToken);
                }
            }

            var notification = new SettingChangedNotification(
                key, oldValue, serializedValue,
                SettingCommandHelper.MapScopeToSource(request.Scope),
                _tenantContext.TenantId, actorId, DateTime.UtcNow);
            if (deferredNotifications is null)
                await _notificationHandlers.HandleAsync(notification, CancellationToken.None);
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
        IReadOnlySet<string> appliedSpecializedKeys,
        List<SettingChangedNotification> deferredNotifications,
        List<Func<Task>> deferredInvalidations,
        MixedSettingsBatchMutation? mixed,
        CancellationToken cancellationToken)
    {
        var (scopeId, actorId) = SettingCommandHelper.GetScopeAndActorIds(
            request.Scope, _tenantContext, resolvedUserId);
        DateTime occurredAtUtc = mixed?.Store.OccurredAt ?? DateTime.UtcNow;
        var guardedEntries = validationResults
            .Where(result => result.SkipReason is null
                && PublicationPolicySettingKeys.All.Contains(result.Key, StringComparer.Ordinal))
            .OrderBy(result => PublicationPolicyOrder(result.Key))
            .ToList();
        var guardedKeys = guardedEntries.Select(result => result.Key).ToHashSet(StringComparer.Ordinal);

        async Task<GuardedBatchTransactionOutcome> ApplyInsideTransactionAsync(CancellationToken token)
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

                if (guardedKeys.Contains(key) || appliedSpecializedKeys.Contains(key))
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
                                if (mixed is not null)
                                    await mixed.Store.ApplyLocationValueAsync(mixed, key, persistToken);
                                else
                                    await _resolver.SetValueAsync(
                                        key, serializedValue!, request.Scope, scopeId, actorId, persistToken);
                                return oldValue;
                            },
                            token);
                    if (!locationMutation.Accepted)
                    {
                        if (mixed is not null)
                            return GuardedBatchTransactionOutcome.Rejected(
                                locationMutation.Error ?? "location_privacy_mutation_rejected",
                                locationMutation.Error ?? "The accepted location slice was rejected.");
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
                    if (AtprotoFederationSettingPolicy.IsDedicatedKey(key))
                    {
                        var written = await _federationSettings.ApplyCurrentAsync(new(
                            request.Scope == SettingScope.Instance ? HumanSettingsOperation.InstanceSettings : HumanSettingsOperation.TenantSettings,
                            request.Scope, request.Scope == SettingScope.Tenant ? scopeId : null, null,
                            key, HumanSettingsMutationKind.SetValue, serializedValue), token);
                        if (!written.Applied)
                            return GuardedBatchTransactionOutcome.Rejected(
                                written.FailureCode ?? "federation_setting_not_applied",
                                "The federation setting change was not applied.");
                    }
                    else if (mixed is not null)
                        await mixed.Store.ApplyOrdinaryAsync(mixed, key, token);
                    else
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
        }
        GuardedBatchTransactionOutcome outcome = await ApplyInsideTransactionAsync(cancellationToken);

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
            deferredInvalidations.Add(() => _locationPrivacyMutations!.InvalidateMutationAsync(
                request.Scope,
                request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                corrected,
                CancellationToken.None));
        }

        int appliedCount = outcome.Results.Count(result => result.Applied);
        deferredNotifications.AddRange(outcome.Notifications);

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

    private sealed class BatchRejectedException(BatchUpdateResponseDto response) : Exception
    {
        internal BatchUpdateResponseDto Response { get; } = response;
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
