namespace Explore.Application.Features.ConfigurationManifest.Handlers.Commands;

using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class ApplyConfigurationManifestCommandHandler(
    IConfigurationManifestPreflight preflight,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    ITenantCreationService tenantCreationService,
    IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
    IPaidEventPolicyMutationBoundary paidEventPolicyMutationBoundary,
    IConfigurationManifestInstanceSettingMutationBoundary
        instanceSettingMutations,
    IConfigurationManifestTenantSettingMutationBoundary tenantSettingMutations,
    IConfigurationManifestOperationRepository operationRepository,
    IConfigurationManifestFailureRecorder failureRecorder,
    IConfigurationManifestEffectDeliveryStrategy effectDelivery,
    ILogger<ApplyConfigurationManifestCommandHandler> logger)
    : IRequestHandler<ApplyConfigurationManifestCommand, BaseCommandResponse<Guid>>,
        IConfigurationManifestApplier
{
    public Task<BaseCommandResponse<Guid>> ApplyAsync(
        ConfigurationManifestReadResult source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Handle(
            new ApplyConfigurationManifestCommand(source),
            cancellationToken);
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        ApplyConfigurationManifestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guid operationId = Guid.CreateVersion7();
        DateTime startedAt = DateTime.UtcNow;
        ConfigurationManifestApplyPlan plan;
        try
        {
            plan = ConfigurationManifestCompiler.Compile(
                request.Source,
                operationId,
                startedAt);
        }
        catch (ConfigurationManifestCompilationException exception)
        {
            logger.LogWarning(
                "Configuration manifest compilation failed for operation {OperationId} with code {FailureCode}.",
                operationId,
                exception.FailureCode);
            return Failure(operationId, exception.FailureCode, exception.Message);
        }

        if (plan.Mode != ConfigurationManifestMode.ValidateOnly)
        {
            await effectDelivery.DrainPendingAsync(cancellationToken);
        }

        plan = await BindBootstrapStateAsync(plan, cancellationToken);
        ConfigurationManifestPreflightResult initial =
            await preflight.EvaluateAsync(plan, cancellationToken);
        if (!initial.IsValid)
        {
            if (plan.Mode == ConfigurationManifestMode.ValidateOnly)
            {
                ConfigurationManifestPreflightError first = initial.Errors[0];
                return Failure(
                    plan.OperationId,
                    first.Code,
                    first.Message,
                    initial.Errors.Select(error => $"{error.Code}:{error.Key}").ToList());
            }

            return await RecordPreflightFailureAsync(plan, initial.Errors, cancellationToken);
        }
        plan = initial.BoundPlan;

        if (plan.Mode == ConfigurationManifestMode.ValidateOnly)
        {
            return Success(CreateOperation(
                plan,
                ConfigurationManifestOperationStatus.Validated,
                createdCount: 0,
                skippedCount: 0,
                failedCount: 0,
                reasonCode: null,
                reason: null,
                DateTime.UtcNow));
        }

        ManifestCommitOutcome outcome;
        try
        {
            outcome = await mutationLock.ExecuteOrderedGroupsAsync(
                ConfigurationManifestLockKeys
                    .CompileOrderedGroups(plan)
                    .Select(group => group.AsEnumerable()),
                token => unitOfWork.ExecuteSerializableAsync(
                    lockedToken => ApplyInsideLockAsync(plan, lockedToken),
                    token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ConfigurationManifestApplyRejectedException exception)
        {
            logger.LogWarning(
                "Configuration manifest operation {OperationId} was rejected with code {FailureCode}.",
                operationId,
                exception.FailureCode);
            return await RecordFailureAsync(
                plan,
                exception.FailureCode,
                exception.SafeMessage,
                exception.FailedTenantCount,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Configuration manifest operation {OperationId} failed with exception type {ExceptionType}.",
                operationId,
                exception.GetType().Name);
            return await RecordFailureAsync(
                plan,
                ConfigurationManifestApplicationFailureCodes.ApplyFailed,
                "The configuration manifest transaction failed and no configuration was applied.",
                plan.Tenants.Length,
                cancellationToken);
        }

        try
        {
            if (outcome.EffectOutboxId.HasValue)
            {
                await effectDelivery.DeliverAsync(
                    outcome.EffectOutboxId.Value,
                    cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Post-commit effects failed for applied configuration manifest operation {OperationId} with exception type {ExceptionType}.",
                outcome.Operation.Id,
                exception.GetType().Name);
            throw;
        }

        return Success(outcome.Operation);
    }

    private async Task<ManifestCommitOutcome> ApplyInsideLockAsync(
        ConfigurationManifestApplyPlan plan,
        CancellationToken cancellationToken)
    {
        ConfigurationManifestOperation? committed =
            await operationRepository.GetByIdAsync(plan.OperationId, cancellationToken);
        if (committed is not null)
        {
            return new ManifestCommitOutcome(
                committed,
                committed.CreatedTenantCount > 0
                || committed.InstanceChangedSettingKeyNames.Count > 0
                || committed.InstanceChangedDocumentKeyNames.Count > 0
                    ? plan.EffectOutboxId
                    : null);
        }

        plan = await BindBootstrapStateAsync(plan, cancellationToken);
        ConfigurationManifestPreflightResult fresh =
            await preflight.EvaluateAsync(plan, cancellationToken);
        if (!fresh.IsValid)
        {
            throw ConfigurationManifestApplyRejectedException.Preflight(
                fresh.Errors,
                plan.Tenants.Length);
        }
        plan = fresh.BoundPlan;

        await ApplyInstanceSettingsAsync(plan, cancellationToken);
        await ApplyInstancePaidEventPolicyAsync(plan, cancellationToken);

        var tenantResults = new List<ConfigurationManifestTenantResult>(fresh.Tenants.Length);
        foreach (ConfigurationManifestPreflightTenant tenant in fresh.Tenants)
        {
            if (tenant.Disposition == ConfigurationManifestTenantDisposition.SkippedExisting)
            {
                tenantResults.Add(ConfigurationManifestTenantResult.Create(
                    plan.OperationId,
                    tenant.TenantId,
                    ConfigurationManifestTenantResultStatus.SkippedExisting,
                    [],
                    [],
                    DateTime.UtcNow));
                continue;
            }

            await CreateTenantConfigurationAsync(
                plan,
                tenant.Plan,
                cancellationToken);
            tenantResults.Add(ConfigurationManifestTenantResult.Create(
                plan.OperationId,
                tenant.TenantId,
                ConfigurationManifestTenantResultStatus.Created,
                tenant.Plan.ChangedSettingKeyNames,
                tenant.Plan.ChangedDocumentKeyNames,
                DateTime.UtcNow));
        }

        int createdCount = tenantResults.Count(result =>
            result.Status == ConfigurationManifestTenantResultStatus.Created);
        int skippedCount = tenantResults.Count - createdCount;
        ConfigurationManifestOperation applied = CreateOperation(
            plan,
            ConfigurationManifestOperationStatus.Applied,
            createdCount,
            skippedCount,
            failedCount: 0,
            reasonCode: null,
            reason: null,
            DateTime.UtcNow);
        await operationRepository.CreateAsync(applied, tenantResults, cancellationToken);
        Guid? effectOutboxId = null;
        if (createdCount > 0
            || !plan.Instance.ChangedSettingKeyNames.IsEmpty
            || !plan.Instance.ChangedDocumentKeyNames.IsEmpty)
        {
            await effectDelivery.CreatePendingAsync(
                plan.EffectOutboxId,
                applied.Id,
                applied.CompletedAt);
            effectOutboxId = plan.EffectOutboxId;
        }

        return new ManifestCommitOutcome(applied, effectOutboxId);
    }

    private async Task CreateTenantConfigurationAsync(
        ConfigurationManifestApplyPlan plan,
        ConfigurationManifestTenantPlan tenant,
        CancellationToken cancellationToken)
    {
        await tenantCreationService.CreateInCurrentTransactionAsync(
            new TenantCreationRequest(
                tenant.PlannedTenantId,
                tenant.DisplayName,
                tenant.Slug,
                (int)TenantStatusEnum.Provisioning,
                ActorUserId: null,
                plan.OccurredAt,
                new TenantBrandingDocumentSeed(
                    tenant.BrandingDocument.DocumentId,
                    tenant.BrandingDocument.SchemaVersion,
                    tenant.BrandingDocument.DefaultsVersion,
                    tenant.BrandingDocument.PayloadJson),
                CreateDirectoryOperatorSeed(tenant.PlannedTenantId, tenant.DisplayName)),
            cancellationToken);

        if (tenant.PaidEventPolicy is not null)
        {
            PaidEventPolicyMutationResult result =
                await paidEventPolicyMutationBoundary
                    .ReviseTenantInCurrentTransactionAsync(
                        new TenantPaidEventPolicyMutationInput(
                            tenant.PlannedTenantId,
                            ConfigurationManifestPaidEventPolicyMapper
                                .ToRevisionDto(tenant.PaidEventPolicy),
                            plan.Instance.PaidEventPolicy?.EffectivePolicyVersion,
                            RequireAbsentTenantPolicy: true),
                        cancellationToken);
            if (!result.Success)
            {
                throw new ConfigurationManifestApplyRejectedException(
                    ConfigurationManifestApplicationFailureCodes.WriteConflict,
                    result.Message,
                    failedTenantCount: 1);
            }
        }

        if (!tenant.GuardedSettings.IsEmpty)
        {
            PublicationPolicyMutationResult result =
                await publicationPolicyMutationBoundary
                    .ApplyTenantInCurrentTransactionAsync(
                    new PublicationPolicyTenantMutationRequest(
                        tenant.PlannedTenantId,
                        ActorUserId: null,
                        plan.OccurredAt,
                        [.. tenant.GuardedSettings.Select(setting =>
                            new PublicationPolicySettingMutation(
                                setting.Key,
                                PublicationPolicyMutationKind.Set,
                                setting.JsonValue,
                                tenant.PlannedTenantId,
                                IsLocked: null))],
                        PublicationPolicyLockedSystemBehavior.Reject),
                    cancellationToken);
            if (!result.Success)
            {
                throw new ConfigurationManifestApplyRejectedException(
                    result.FailureCode
                        ?? ConfigurationManifestApplicationFailureCodes.WriteConflict,
                    result.Message,
                    failedTenantCount: 1);
            }
        }

        if (!tenant.UnguardedSettings.IsEmpty)
        {
            await tenantSettingMutations.CreateInCurrentTransactionAsync(
                new ConfigurationManifestTenantSettingMutationInput(
                    tenant.PlannedTenantId,
                    [.. tenant.UnguardedSettings.Select(setting =>
                        new ConfigurationManifestTenantSettingMutation(
                            setting.Key,
                            setting.JsonValue))],
                    ActorUserId: null,
                    plan.OccurredAt),
                cancellationToken);
        }

    }

    private static TenantDirectoryOperatorIdentityDocumentSeed CreateDirectoryOperatorSeed(
        Guid tenantId,
        string displayName)
    {
        TenantSettingsDocument document =
            TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenantId, displayName);
        return new(
            Guid.CreateVersion7(),
            document.SchemaVersion,
            document.DefaultsVersion,
            document.PayloadJson);
    }

    private async Task ApplyInstanceSettingsAsync(
        ConfigurationManifestApplyPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.Instance.GuardedSettings.IsEmpty
            && plan.Instance.UnguardedSettings.IsEmpty)
        {
            return;
        }

        ConfigurationManifestInstanceSettingMutationResult result =
            await instanceSettingMutations.ApplyInCurrentTransactionAsync(
                new ConfigurationManifestInstanceSettingMutationInput(
                    [
                        .. plan.Instance.GuardedSettings.Select(setting =>
                            new ConfigurationManifestInstanceSettingMutation(
                                setting.Key,
                                setting.JsonValue)),
                        .. plan.Instance.UnguardedSettings.Select(setting =>
                            new ConfigurationManifestInstanceSettingMutation(
                                setting.Key,
                                setting.JsonValue))
                    ],
                    ActorUserId: null,
                    plan.OccurredAt),
                cancellationToken);
        if (!result.Success)
        {
            throw new ConfigurationManifestApplyRejectedException(
                result.FailureCode
                    ?? ConfigurationManifestApplicationFailureCodes
                        .WriteConflict,
                result.Message,
                failedTenantCount: plan.Tenants.Length);
        }
    }

    private async Task ApplyInstancePaidEventPolicyAsync(
        ConfigurationManifestApplyPlan plan,
        CancellationToken cancellationToken)
    {
        ConfigurationManifestInstancePaidEventPolicyPlan? authority =
            plan.Instance.PaidEventPolicy;
        if (authority?.ProposedRevision is null)
        {
            return;
        }

        if (authority.ExpectedActivePolicyVersion is not { } expectedVersion)
        {
            throw new ConfigurationManifestApplyRejectedException(
                ConfigurationManifestApplicationFailureCodes.PaidPolicyStale,
                "The instance paid-event policy authority was not bound.",
                failedTenantCount: plan.Tenants.Length);
        }

        PaidEventPolicyMutationResult result =
            await paidEventPolicyMutationBoundary
                .ReviseInstanceInCurrentTransactionAsync(
                    new InstancePaidEventPolicyMutationInput(
                        ConfigurationManifestPaidEventPolicyMapper
                            .ToRevisionDto(authority.ProposedRevision),
                        expectedVersion),
                    cancellationToken);
        if (!result.Success)
        {
            throw new ConfigurationManifestApplyRejectedException(
                result.FailureCode
                    == PaidEventPolicyMutationFailureCodes.ConcurrencyConflict
                        ? ConfigurationManifestApplicationFailureCodes
                            .PaidPolicyStale
                        : ConfigurationManifestApplicationFailureCodes
                            .WriteConflict,
                result.Message,
                failedTenantCount: plan.Tenants.Length);
        }
    }

    private Task<BaseCommandResponse<Guid>> RecordPreflightFailureAsync(
        ConfigurationManifestApplyPlan plan,
        ImmutableArray<ConfigurationManifestPreflightError> errors,
        CancellationToken cancellationToken)
    {
        ConfigurationManifestPreflightError first = errors[0];
        int failedCount = CountFailedTenants(errors, plan.Tenants.Length);
        return RecordFailureAsync(
            plan,
            first.Code,
            first.Message,
            failedCount,
            cancellationToken,
            errors.Select(error => $"{error.Code}:{error.Key}").ToList());
    }

    private async Task<BaseCommandResponse<Guid>> RecordFailureAsync(
        ConfigurationManifestApplyPlan plan,
        string failureCode,
        string safeMessage,
        int failedTenantCount,
        CancellationToken cancellationToken,
        List<string>? errors = null)
    {
        ConfigurationManifestOperation failed = CreateOperation(
            plan,
            ConfigurationManifestOperationStatus.Failed,
            createdCount: 0,
            skippedCount: 0,
            failedTenantCount,
            failureCode,
            safeMessage,
            DateTime.UtcNow);
        await failureRecorder.RecordAsync(failed, cancellationToken);
        return Failure(plan.OperationId, failureCode, safeMessage, errors);
    }

    private static ConfigurationManifestOperation CreateOperation(
        ConfigurationManifestApplyPlan plan,
        ConfigurationManifestOperationStatus status,
        int createdCount,
        int skippedCount,
        int failedCount,
        string? reasonCode,
        string? reason,
        DateTime completedAt) =>
        ConfigurationManifestOperation.Create(
            plan.OperationId,
            plan.Mode == ConfigurationManifestMode.Bootstrap
                ? ConfigurationManifestAuditMode.Bootstrap
                : ConfigurationManifestAuditMode.ValidateOnly,
            plan.ApiVersion,
            plan.Kind,
            plan.ManifestName,
            plan.Digest,
            status,
            plan.Tenants.Length,
            createdCount,
            skippedCount,
            failedCount,
            reasonCode,
            reason,
            plan.OccurredAt,
            completedAt,
            instanceSectionDigest:
                plan.Mode == ConfigurationManifestMode.Bootstrap
                    ? plan.InstanceSectionDigest
                    : null,
            bootstrapGeneration:
                plan.Mode == ConfigurationManifestMode.Bootstrap
                    ? plan.BootstrapState?.Generation ?? 1
                    : null,
            instanceChangedSettingKeyNames:
                status == ConfigurationManifestOperationStatus.Applied
                    ? plan.Instance.ChangedSettingKeyNames
                    : [],
            instanceChangedDocumentKeyNames:
                status == ConfigurationManifestOperationStatus.Applied
                    ? plan.Instance.ChangedDocumentKeyNames
                    : []);

    private async Task<ConfigurationManifestApplyPlan>
        BindBootstrapStateAsync(
            ConfigurationManifestApplyPlan plan,
            CancellationToken cancellationToken)
    {
        if (plan.Mode != ConfigurationManifestMode.Bootstrap)
        {
            return plan with { BootstrapState = null };
        }

        ConfigurationManifestOperation? bootstrap =
            await operationRepository.GetLatestAppliedBootstrapAsync(
                cancellationToken);
        return plan with
        {
            BootstrapState = bootstrap is null
                ? null
                : new ConfigurationManifestBootstrapState(
                    bootstrap.InstanceSectionDigest
                    ?? throw new InvalidOperationException(
                        "Applied manifest bootstrap state has no instance-section digest."),
                    bootstrap.BootstrapGeneration
                    ?? throw new InvalidOperationException(
                        "Applied manifest bootstrap state has no generation."))
        };
    }

    private static BaseCommandResponse<Guid> Success(
        ConfigurationManifestOperation operation) => BaseCommandResponse.Success(
        operation.Id,
        operation.Status == ConfigurationManifestOperationStatus.Validated
            ? "Configuration manifest validated."
            : "Configuration manifest bootstrap completed.");

    private static BaseCommandResponse<Guid> Failure(
        Guid operationId,
        string failureCode,
        string message,
        List<string>? errors = null) => BaseCommandResponse.Failure<Guid>(
        failureCode,
        message,
        errors ?? [failureCode],
        operationId);

    private sealed record ManifestCommitOutcome(
        ConfigurationManifestOperation Operation,
        Guid? EffectOutboxId);

    private sealed class ConfigurationManifestApplyRejectedException(
        string failureCode,
        string safeMessage,
        int failedTenantCount)
        : Exception(safeMessage)
    {
        public string FailureCode { get; } = failureCode;
        public string SafeMessage { get; } = safeMessage;
        public int FailedTenantCount { get; } = failedTenantCount;

        public static ConfigurationManifestApplyRejectedException Preflight(
            ImmutableArray<ConfigurationManifestPreflightError> errors,
            int requestedTenantCount)
        {
            ConfigurationManifestPreflightError first = errors[0];
            return new ConfigurationManifestApplyRejectedException(
                first.Code,
                first.Message,
                CountFailedTenants(errors, requestedTenantCount));
        }
    }

    private static int CountFailedTenants(
        IEnumerable<ConfigurationManifestPreflightError> errors,
        int requestedTenantCount) =>
        errors
            .Where(error => error.ManifestIndex >= 0
                && error.ManifestIndex < requestedTenantCount)
            .Select(error => error.ManifestIndex)
            .Distinct()
            .Count();
}
