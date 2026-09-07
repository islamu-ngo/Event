using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using AppAccountAuthorityKind = Explore.Application.Notifications.AccountAuthorityKind;
using AppExternalWorkflowProviderKind = Explore.Application.Notifications.ExternalWorkflowProviderKind;
using AppNotificationCategory = Explore.Application.Notifications.NotificationCategory;
using AppNotificationOwnership = Explore.Application.Notifications.NotificationOwnership;

namespace Event.Application.UnitTests.Notifications;

public sealed class DefaultNotificationOrchestratorTests
{
    [Test]
    public async Task EnqueueAsync_CreatesPendingLocalDeliveryForIslamuOwnedNotification()
    {
        var repository = new CapturingNotificationIntentRepository();
        var orchestrator = CreateOrchestrator(
            repository,
            new NotificationOwnershipDecision(AppNotificationCategory.RegistrationLifecycle, AppNotificationOwnership.IslamuEvent));

        var result = await orchestrator.EnqueueAsync(CreateDraft(AppNotificationCategory.RegistrationLifecycle));

        await Assert.That(result.Intent.StatusId).IsEqualTo((int)NotificationIntentStatusEnum.Pending);
        await Assert.That(result.Intent.OwnershipTypeId).IsEqualTo((int)NotificationOwnershipTypeEnum.IslamuEvent);
        await Assert.That(result.Delivery).IsNotNull();
        await Assert.That(result.Delivery!.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Pending);
        await Assert.That(result.ExternalDelegation).IsNull();
        await Assert.That(repository.CreatedIntent).IsSameReferenceAs(result.Intent);
    }

    [Test]
    public async Task EnqueueAsync_SkipsFencedRecipientBeforeOwnershipResolution()
    {
        var repository = new CapturingNotificationIntentRepository();
        var privacyState = new FencedPrivacyErasureStateRepository();
        var resolver = new FixedNotificationOwnershipResolver(
            new NotificationOwnershipDecision(AppNotificationCategory.RegistrationLifecycle, AppNotificationOwnership.IslamuEvent));
        var unitOfWork = new TrackingUnitOfWork();
        var draft = CreateDraft(AppNotificationCategory.RegistrationLifecycle);
        privacyState.Fence(draft.UserId!.Value);
        var orchestrator = new DefaultNotificationOrchestrator(resolver, repository, privacyState, unitOfWork);

        var result = await orchestrator.EnqueueAsync(draft);

        await Assert.That(result.IsFenced).IsTrue();
        await Assert.That(result.Intent).IsNull();
        await Assert.That(result.Decision).IsNull();
        await Assert.That(result.Delivery).IsNull();
        await Assert.That(result.ExternalDelegation).IsNull();
        await Assert.That(resolver.ResolveCallCount).IsEqualTo(0);
        await Assert.That(repository.CreateAttempts).IsEqualTo(0);
        await Assert.That(unitOfWork.SerializableExecutionCount).IsEqualTo(0);
    }

    [Test]
    public async Task EnqueueAsync_SkipsRacedFenceInsideSerializableSaveBoundaryWithoutDelegation()
    {
        var repository = new CapturingNotificationIntentRepository();
        var unitOfWork = new TrackingUnitOfWork();
        var privacyState = new FencedPrivacyErasureStateRepository(() => unitOfWork.IsExecutingSerializable);
        var resolver = new FixedNotificationOwnershipResolver(
            new NotificationOwnershipDecision(
                AppNotificationCategory.TrustSafetyModeration,
                AppNotificationOwnership.ExternalWorkflowProvider,
                ExternalWorkflowProviderKind: AppExternalWorkflowProviderKind.Coop,
                RequiresLocalAudit: true),
            draft => privacyState.Fence(draft.UserId!.Value));
        var orchestrator = new DefaultNotificationOrchestrator(resolver, repository, privacyState, unitOfWork);

        var result = await orchestrator.EnqueueAsync(CreateDraft(AppNotificationCategory.TrustSafetyModeration));

        await Assert.That(result.IsFenced).IsTrue();
        await Assert.That(result.Intent).IsNull();
        await Assert.That(result.Decision).IsNull();
        await Assert.That(result.Delivery).IsNull();
        await Assert.That(result.ExternalDelegation).IsNull();
        await Assert.That(resolver.ResolveCallCount).IsEqualTo(1);
        await Assert.That(repository.CreateAttempts).IsEqualTo(0);
        await Assert.That(unitOfWork.SerializableExecutionCount).IsEqualTo(1);
        await Assert.That(privacyState.ChecksInsideSerializableBoundary).IsEquivalentTo([false, true]);
    }

    [Test]
    public async Task EnqueueAsync_CreatesAccountAuthorityDelegationWhenIslamuInitiated()
    {
        var repository = new CapturingNotificationIntentRepository();
        var orchestrator = CreateOrchestrator(
            repository,
            new NotificationOwnershipDecision(
                AppNotificationCategory.IdentityLifecycle,
                AppNotificationOwnership.AccountAuthority,
                AppAccountAuthorityKind.Keycloak,
                RequiresLocalAudit: true));

        var result = await orchestrator.EnqueueAsync(CreateDraft(AppNotificationCategory.IdentityLifecycle));

        await Assert.That(result.Intent.StatusId).IsEqualTo((int)NotificationIntentStatusEnum.Delegated);
        await Assert.That(result.Delivery).IsNull();
        await Assert.That(result.ExternalDelegation).IsNotNull();
        await Assert.That(result.ExternalDelegation!.ProviderKindId).IsEqualTo((int)ExternalWorkflowProviderKindEnum.None);
        await Assert.That(result.ExternalDelegation.AccountAuthorityKindId).IsEqualTo((int)AccountAuthorityKindEnum.Keycloak);
        await Assert.That(result.ExternalDelegation.StatusId).IsEqualTo((int)NotificationExternalDelegationStatusEnum.Requested);
    }

    [Test]
    public async Task EnqueueAsync_ResolvesAccountAuthorityWithoutDelegationWhenNotIslamuInitiated()
    {
        var repository = new CapturingNotificationIntentRepository();
        var orchestrator = CreateOrchestrator(
            repository,
            new NotificationOwnershipDecision(
                AppNotificationCategory.IdentityLifecycle,
                AppNotificationOwnership.AccountAuthority,
                AppAccountAuthorityKind.AtprotoPds,
                RequiresLocalAudit: false));

        var result = await orchestrator.EnqueueAsync(CreateDraft(
            AppNotificationCategory.IdentityLifecycle,
            isIslamuInitiated: false));

        await Assert.That(result.Intent.StatusId).IsEqualTo((int)NotificationIntentStatusEnum.Resolved);
        await Assert.That(result.Delivery).IsNull();
        await Assert.That(result.ExternalDelegation).IsNull();
    }

    [Test]
    public async Task EnqueueAsync_CreatesExternalWorkflowDelegationForAuditedProviderNotification()
    {
        var repository = new CapturingNotificationIntentRepository();
        var orchestrator = CreateOrchestrator(
            repository,
            new NotificationOwnershipDecision(
                AppNotificationCategory.TrustSafetyModeration,
                AppNotificationOwnership.ExternalWorkflowProvider,
                ExternalWorkflowProviderKind: AppExternalWorkflowProviderKind.Coop,
                RequiresLocalAudit: true));

        var result = await orchestrator.EnqueueAsync(CreateDraft(AppNotificationCategory.TrustSafetyModeration));

        await Assert.That(result.Intent.StatusId).IsEqualTo((int)NotificationIntentStatusEnum.Delegated);
        await Assert.That(result.ExternalDelegation).IsNotNull();
        await Assert.That(result.ExternalDelegation!.ProviderKindId).IsEqualTo((int)ExternalWorkflowProviderKindEnum.Coop);
        await Assert.That(result.ExternalDelegation.AccountAuthorityKindId).IsNull();
        await Assert.That(result.ExternalDelegation.SafePayloadHash).IsEqualTo("sha256:test-safe-payload");
    }

    [Test]
    public async Task EnqueueAsync_CreatesSkippedIntentForDisabledNotification()
    {
        var repository = new CapturingNotificationIntentRepository();
        var orchestrator = CreateOrchestrator(
            repository,
            new NotificationOwnershipDecision(AppNotificationCategory.Marketing, AppNotificationOwnership.Disabled, RequiresLocalAudit: false));

        var result = await orchestrator.EnqueueAsync(CreateDraft(AppNotificationCategory.Marketing));

        await Assert.That(result.Intent.StatusId).IsEqualTo((int)NotificationIntentStatusEnum.Skipped);
        await Assert.That(result.Delivery).IsNull();
        await Assert.That(result.ExternalDelegation).IsNull();
    }

    [Test]
    public async Task EnqueueAsync_RequiresTenantTemplateAndDeduplicationKey()
    {
        var orchestrator = CreateOrchestrator(
            new CapturingNotificationIntentRepository(),
            new NotificationOwnershipDecision(AppNotificationCategory.RegistrationLifecycle, AppNotificationOwnership.IslamuEvent));

        await Assert.That(async () => await orchestrator.EnqueueAsync(CreateDraft(AppNotificationCategory.RegistrationLifecycle) with
        {
            TenantId = null
        })).Throws<InvalidOperationException>();
        await Assert.That(async () => await orchestrator.EnqueueAsync(CreateDraft(AppNotificationCategory.RegistrationLifecycle) with
        {
            TemplateKey = " "
        })).Throws<InvalidOperationException>();
        await Assert.That(async () => await orchestrator.EnqueueAsync(CreateDraft(AppNotificationCategory.RegistrationLifecycle) with
        {
            DeduplicationKey = null
        })).Throws<InvalidOperationException>();
    }

    private static DefaultNotificationOrchestrator CreateOrchestrator(
        CapturingNotificationIntentRepository repository,
        NotificationOwnershipDecision decision)
    {
        return new DefaultNotificationOrchestrator(
            new FixedNotificationOwnershipResolver(decision),
            repository,
            new FencedPrivacyErasureStateRepository(),
            new TrackingUnitOfWork());
    }

    private static NotificationIntentDraft CreateDraft(
        AppNotificationCategory category,
        bool isIslamuInitiated = true)
    {
        return new NotificationIntentDraft(
            category,
            TenantId: Guid.CreateVersion7(),
            RecipientKind: "User",
            TemplateKey: "registration.approved",
            SafePayloadReference: "notification-intents/test-safe-payload",
            SafePayloadHash: "sha256:test-safe-payload",
            IsUserFacing: true,
            IsIslamuInitiated: isIslamuInitiated,
            DeduplicationKey: $"{category}:test-deduplication",
            CorrelationId: Guid.NewGuid().ToString("N"),
            UserId: Guid.CreateVersion7(),
            ExternalProviderId: "external-provider-safe-id",
            ExternalCorrelationId: "external-correlation-safe-id");
    }

    private sealed class FixedNotificationOwnershipResolver(
        NotificationOwnershipDecision decision,
        Action<NotificationIntentDraft>? onResolve = null)
        : INotificationOwnershipResolver
    {
        public int ResolveCallCount { get; private set; }

        public Task<NotificationOwnershipDecision> ResolveAsync(
            NotificationIntentDraft draft,
            CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            onResolve?.Invoke(draft);
            return Task.FromResult(decision);
        }
    }

    private sealed class CapturingNotificationIntentRepository : INotificationIntentRepository
    {
        public NotificationIntent? CreatedIntent { get; private set; }
        public int CreateAttempts { get; private set; }

        public Task<NotificationIntent?> GetById(Guid id) => Task.FromResult<NotificationIntent?>(CreatedIntent?.Id == id ? CreatedIntent : null);

        public Task<IReadOnlyList<NotificationIntent>> GetAll() => Task.FromResult<IReadOnlyList<NotificationIntent>>(Array.Empty<NotificationIntent>());

        public Task<(IReadOnlyList<NotificationIntent> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize)
        {
            return Task.FromResult<(IReadOnlyList<NotificationIntent>, int)>((Array.Empty<NotificationIntent>(), 0));
        }

        public Task<bool> Exists(Guid id) => Task.FromResult(CreatedIntent?.Id == id);

        public Task<NotificationIntent> Create(NotificationIntent entity) => CreateIntentAsync(entity);

        public Task Update(NotificationIntent entity) => Task.CompletedTask;

        public Task Delete(NotificationIntent entity) => Task.CompletedTask;

        public Task<NotificationIntent> CreateIntentAsync(
            NotificationIntent intent,
            CancellationToken cancellationToken = default)
        {
            CreateAttempts++;
            CreatedIntent = intent;
            return Task.FromResult(intent);
        }

        public Task<NotificationIntent?> GetByTenantAndIdAsync(
            Guid tenantId,
            Guid intentId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreatedIntent?.TenantId == tenantId && CreatedIntent.Id == intentId ? CreatedIntent : null);
        }

        public Task<bool> ExistsByDeduplicationKeyAsync(
            Guid tenantId,
            string deduplicationKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreatedIntent?.TenantId == tenantId && CreatedIntent.DeduplicationKey == deduplicationKey);
        }

        public Task<NotificationDelivery> AddDeliveryAsync(
            NotificationDelivery delivery,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(delivery);
        }

        public Task<NotificationExternalDelegation> AddExternalDelegationAsync(
            NotificationExternalDelegation delegation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(delegation);
        }
    }

    private sealed class FencedPrivacyErasureStateRepository(Func<bool>? isExecutingSerializable = null)
        : IPrivacyErasureStateRepository
    {
        private readonly HashSet<Guid> _fencedSubjectIds = [];

        public List<bool> ChecksInsideSerializableBoundary { get; } = [];

        public void Fence(Guid subjectId) => _fencedSubjectIds.Add(subjectId);

        public Task<PrivacyErasureSaga?> GetBySubjectAsync(Guid subjectId, CancellationToken cancellationToken)
        {
            ChecksInsideSerializableBoundary.Add(isExecutingSerializable?.Invoke() ?? false);
            return Task.FromResult<PrivacyErasureSaga?>(
                _fencedSubjectIds.Contains(subjectId) ? CreateFencedSaga(subjectId) : null);
        }

        public Task<PrivacyErasureSaga?> GetByIntentAsync(Guid intentId, CancellationToken cancellationToken) =>
            Task.FromResult<PrivacyErasureSaga?>(null);

        public Task<PrivacyErasureSaga?> FindByReceiptHashAsync(byte[] receiptHash, CancellationToken cancellationToken) =>
            Task.FromResult<PrivacyErasureSaga?>(null);

        public Task<int> ClearExpiredReceiptHashesAsync(
            DateTime utcNow,
            int batchSize,
            bool dryRun,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<bool> HasCoverageAsync(Guid intentId, int policyVersion, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task AddSagaAsync(PrivacyErasureSaga saga, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddCoverageAsync(PrivacyErasurePolicyCoverage coverage, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TrackingUnitOfWork : IUnitOfWork
    {
        public int SerializableExecutionCount { get; private set; }
        public bool IsExecutingSerializable { get; private set; }

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);

        public async Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            SerializableExecutionCount++;
            IsExecutingSerializable = true;
            try
            {
                return await operation(ct);
            }
            finally
            {
                IsExecutingSerializable = false;
            }
        }
    }

    private static PrivacyErasureSaga CreateFencedSaga(Guid userId)
    {
        DateTime nowUtc = DateTime.UtcNow;
        PrivacyErasureIntent intent = PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            1,
            PrivacyErasureSubjectKind.User,
            userId,
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            nowUtc,
            nowUtc);
        return PrivacyErasureSaga.Start(intent, 1, new byte[32], nowUtc.AddMinutes(5), nowUtc);
    }
}
