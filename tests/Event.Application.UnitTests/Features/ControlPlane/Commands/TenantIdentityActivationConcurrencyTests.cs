using System.Collections.Concurrent;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Exceptions;
using Explore.Application.Features.ControlPlane.Handlers.Commands;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.Management;
using Explore.Application.Features.TenantSettingsDocuments.Handlers.Commands;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Management;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests.Features.ControlPlane.Commands;

public sealed class TenantIdentityActivationConcurrencyTests
{
    [Test]
    public async Task ActivationWinningSharedLease_ForcesIncompletePatchConflictWithoutLostRevision()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        int tenantStatus = (int)TenantStatusEnum.Provisioning;
        TenantSettingsDocument document = ReadyDocument(tenantId);
        Guid originalRevision = document.ConcurrencyStamp;
        var activationAtCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowActivationCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var mutationLock = new EventControlledMutationLock();
        var tenantRepository = Substitute.For<ITenantRepository>();
        tenantRepository.GetByIdAsNoTrackingAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(_ => TenantWithStatus(tenantId, (TenantStatusEnum)Volatile.Read(ref tenantStatus)));
        tenantRepository.TryTransitionStatusAsync(
                tenantId,
                (int)TenantStatusEnum.Provisioning,
                (int)TenantStatusEnum.Active,
                Arg.Any<DateTime>(),
                userId,
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                activationAtCommit.TrySetResult();
                await allowActivationCommit.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return Interlocked.CompareExchange(
                    ref tenantStatus,
                    (int)TenantStatusEnum.Active,
                    (int)TenantStatusEnum.Provisioning) == (int)TenantStatusEnum.Provisioning;
            });
        var identityRepository = Substitute.For<ITenantSettingsDocumentRepository>();
        identityRepository.GetTrackedByTenantAndDocumentKey(
                tenantId,
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                Arg.Any<CancellationToken>())
            .Returns(document);
        var readiness = Substitute.For<ITenantDirectoryOperatorReadinessEvaluator>();
        TenantDirectoryOperatorIdentity readyIdentity = TenantDirectoryOperatorIdentity.Evaluate(
            Deserialize(document),
            TenantDirectoryOperatorIdentityCapability.Activation).Identity!;
        readiness.EvaluateAsync(
                tenantId,
                TenantDirectoryOperatorIdentityCapability.Activation,
                Arg.Any<CancellationToken>())
            .Returns(TenantDirectoryOperatorReadinessAssessment.Ready(
                readyIdentity,
                document.ConcurrencyStamp,
                document.Id));
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(userId);
        var lifecycleLog = Substitute.For<ITenantLifecycleLogRepository>();
        lifecycleLog.CreateAsync(Arg.Any<TenantLifecycleLog>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<TenantLifecycleLog>()));
        var activationHandler = new TransitionControlPlaneTenantLifecycleCommandHandler(
            tenantRepository,
            lifecycleLog,
            Substitute.For<IEmailDispatchOutboxRepository>(),
            currentUser,
            mutationLock,
            new TenantActivationCapacityPolicy(
                Substitute.For<IInstanceBootstrapStateRepository>(),
                tenantRepository,
                Substitute.For<IManagedTenantProvisioningOperationRepository>(),
                Microsoft.Extensions.Options.Options.Create(new ManagedControlPlaneOptions())),
            readiness);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var patchHandler = new PatchTenantDirectoryOperatorIdentityDocumentCommandHandler(
            tenantContext,
            currentUser,
            identityRepository,
            tenantRepository,
            mutationLock,
            Substitute.For<ITypedSettingsDocumentResolver>());

        Task<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>> activationTask =
            activationHandler.Handle(
                new TransitionControlPlaneTenantLifecycleCommand(
                    tenantId,
                    TenantStatusEnum.Active,
                    reason: null),
                CancellationToken.None);
        await activationAtCommit.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Task<BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>> patchTask =
            patchHandler.Handle(
                new PatchTenantDirectoryOperatorIdentityDocumentCommand
                {
                    TenantId = tenantId,
                    Patch = new PatchTenantDirectoryOperatorIdentityDocumentDto
                    {
                        ExpectedConcurrencyStamp = originalRevision,
                        LegalEntity = new PatchTenantDirectoryOperatorLegalEntityDto
                        {
                            LegalName = OptionalUpdate<string?>.Set(null)
                        }
                    }
                },
                CancellationToken.None);
        await mutationLock.ContenderQueued.WaitAsync(TimeSpan.FromSeconds(10));
        allowActivationCommit.TrySetResult();

        BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto> activation =
            await activationTask;
        ConcurrencyConflictException conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => patchTask);

        await Assert.That(activation.IsSuccess).IsTrue();
        await Assert.That(conflict.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(conflict.Message).DoesNotContain("Community Events ASBL");
        await Assert.That((TenantStatusEnum)Volatile.Read(ref tenantStatus))
            .IsEqualTo(TenantStatusEnum.Active);
        await Assert.That(TenantDirectoryOperatorIdentity.Evaluate(
            Deserialize(document),
            TenantDirectoryOperatorIdentityCapability.Activation).IsReady).IsTrue();
        await Assert.That(document.ConcurrencyStamp).IsEqualTo(originalRevision);
        await identityRepository.DidNotReceiveWithAnyArgs().Update(default!);
        await Assert.That(mutationLock.ObservedKeys.Distinct(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo([TenantDirectoryOperatorIdentityMutationLockKeys.ForTenant(tenantId)]);
    }

    private static TenantSettingsDocument ReadyDocument(Guid tenantId)
    {
        TenantSettingsDocument document = TenantDirectoryOperatorIdentityDocumentDefaults.Create(
            tenantId,
            new TenantDirectoryOperatorIdentitySettings
            {
                PublicName = "Community Events",
                LegalName = "Community Events ASBL",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE",
                PublicContactEmail = "contact@example.test",
                LegalNoticeUrl = "https://example.test/legal",
                PrivacyUrl = "https://example.test/privacy"
            });
        document.Id = Guid.CreateVersion7();
        document.ConcurrencyStamp = Guid.CreateVersion7();
        return document;
    }

    private static TenantDirectoryOperatorIdentitySettings Deserialize(TenantSettingsDocument document) =>
        JsonSerializer.Deserialize<TenantDirectoryOperatorIdentitySettings>(
            document.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static Tenant TenantWithStatus(Guid tenantId, TenantStatusEnum status) => new()
    {
        Id = tenantId,
        FullName = "Concurrent tenant",
        Slug = "concurrent-tenant",
        TenantStatusId = (int)status,
        TenantStatus = new TenantStatus
        {
            Id = (int)status,
            MasterCode = status.ToString(),
            FullName = status.ToString(),
            IsActiveState = status == TenantStatusEnum.Active
        }
    };

    private sealed class EventControlledMutationLock : ISettingMutationLock, IDisposable
    {
        private readonly SemaphoreSlim _lease = new(1, 1);
        public ConcurrentQueue<string> ObservedKeys { get; } = new();
        public Task ContenderQueued => _contenderQueued.Task;
        private readonly TaskCompletionSource _contenderQueued =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> ExecuteAsync<T>(
            string canonicalSettingKey,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            ExecuteManyAsync([canonicalSettingKey], operation, cancellationToken);

        public async Task<T> ExecuteManyAsync<T>(
            IEnumerable<string> canonicalSettingKeys,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            string[] keys = canonicalSettingKeys.Order(StringComparer.Ordinal).ToArray();
            foreach (string key in keys)
                ObservedKeys.Enqueue(key);

            if (!await _lease.WaitAsync(0, cancellationToken))
            {
                _contenderQueued.TrySetResult();
                await _lease.WaitAsync(cancellationToken);
            }

            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                _lease.Release();
            }
        }

        public void Dispose() => _lease.Dispose();
    }
}
