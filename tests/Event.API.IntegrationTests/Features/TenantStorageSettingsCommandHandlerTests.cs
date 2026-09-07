using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.TenantStorageSettings.Handlers.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Settings;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Fast)]
[Category("TenantStorageSettings")]
public sealed class TenantStorageSettingsCommandHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("018e4e5c-7f00-7000-8000-000000000099");
    private static readonly Guid UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000123");

    [Test]
    public async Task Handle_WhenTenantStorageIsLocked_ReturnsFailureWithoutApplyingSettings()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsTenantAdminAsync(TenantA, Arg.Any<CancellationToken>()).Returns(true);
        var storageService = Substitute.For<ITenantStorageSettingService>();
        storageService.ReadSettingsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(CreateCurrentSettings(isReadOnly: true));
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var s3ConfigResolver = Substitute.For<IS3ConfigResolver>();
        var unitOfWork = new RecordingUnitOfWork([]);
        var handler = CreateHandler(
            tenantContext,
            adminContext,
            storageService,
            unitOfWork,
            settingsResolver,
            s3ConfigResolver);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("StorageTenantOverridesLocked");
        await storageService.DidNotReceive().ApplyPatchAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<PatchTenantStorageSettingsDto>(),
            Arg.Any<CancellationToken>());
        await Assert.That(unitOfWork.ExecutionCount).IsEqualTo(0);
        settingsResolver.DidNotReceive().InvalidateCache(Arg.Any<SettingScope?>(), Arg.Any<Guid?>());
        s3ConfigResolver.DidNotReceive().InvalidateCache(Arg.Any<Guid?>());
    }

    [Test]
    public async Task Handle_WhenMaxUploadExceedsInstanceCeiling_ReturnsValidationFailure()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsTenantAdminAsync(TenantA, Arg.Any<CancellationToken>()).Returns(true);
        var storageService = Substitute.For<ITenantStorageSettingService>();
        storageService.ReadSettingsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(CreateCurrentSettings(isReadOnly: false, instanceMaxUploadBytes: 100));
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var s3ConfigResolver = Substitute.For<IS3ConfigResolver>();
        var unitOfWork = new RecordingUnitOfWork([]);
        var handler = CreateHandler(
            tenantContext,
            adminContext,
            storageService,
            unitOfWork,
            settingsResolver,
            s3ConfigResolver);

        var result = await handler.Handle(CreateCommand(maxUploadBytes: 101), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).IsEqualTo("Tenant storage settings validation failed.");
        await Assert.That(result.Errors!.Any(error => error.Contains("instance ceiling", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await storageService.DidNotReceive().ApplyPatchAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<PatchTenantStorageSettingsDto>(),
            Arg.Any<CancellationToken>());
        await Assert.That(unitOfWork.ExecutionCount).IsEqualTo(0);
        settingsResolver.DidNotReceive().InvalidateCache(Arg.Any<SettingScope?>(), Arg.Any<Guid?>());
        s3ConfigResolver.DidNotReceive().InvalidateCache(Arg.Any<Guid?>());
    }

    [Test]
    public async Task Handle_WhenTenantAdminTargetsDifferentTenant_ReturnsForbiddenFailure()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantB);
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsTenantAdminAsync(TenantB, Arg.Any<CancellationToken>()).Returns(false);
        adminContext.GetAdminTenantIdsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { TenantA });
        adminContext.IsInstanceAdminAsync(UserId, Arg.Any<CancellationToken>()).Returns(false);
        var storageService = Substitute.For<ITenantStorageSettingService>();
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var s3ConfigResolver = Substitute.For<IS3ConfigResolver>();
        var unitOfWork = new RecordingUnitOfWork([]);
        var handler = CreateHandler(
            tenantContext,
            adminContext,
            storageService,
            unitOfWork,
            settingsResolver,
            s3ConfigResolver);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("Only tenant administrators");
        await storageService.DidNotReceive().ReadSettingsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await Assert.That(unitOfWork.ExecutionCount).IsEqualTo(0);
        settingsResolver.DidNotReceive().InvalidateCache(Arg.Any<SettingScope?>(), Arg.Any<Guid?>());
        s3ConfigResolver.DidNotReceive().InvalidateCache(Arg.Any<Guid?>());
    }

    [Test]
    public async Task Handle_WhenPatchHasNoSuppliedLeaves_ReturnsValidationFailureWithoutMutation()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsTenantAdminAsync(TenantA, Arg.Any<CancellationToken>()).Returns(true);
        var storageService = Substitute.For<ITenantStorageSettingService>();
        storageService.ReadSettingsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(CreateCurrentSettings(isReadOnly: false));
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var s3ConfigResolver = Substitute.For<IS3ConfigResolver>();
        var unitOfWork = new RecordingUnitOfWork([]);
        var handler = CreateHandler(
            tenantContext,
            adminContext,
            storageService,
            unitOfWork,
            settingsResolver,
            s3ConfigResolver);
        var command = new PatchTenantStorageSettingsCommand
        {
            UserId = UserId,
            Settings = new PatchTenantStorageSettingsDto()
        };

        var result = await handler.Handle(command, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!.Any(error => error.Contains("at least one", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await storageService.DidNotReceive().ApplyPatchAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<PatchTenantStorageSettingsDto>(),
            Arg.Any<CancellationToken>());
        await Assert.That(unitOfWork.ExecutionCount).IsEqualTo(0);
        settingsResolver.DidNotReceive().InvalidateCache(Arg.Any<SettingScope?>(), Arg.Any<Guid?>());
        s3ConfigResolver.DidNotReceive().InvalidateCache(Arg.Any<Guid?>());
    }

    [Test]
    public async Task Handle_WhenTransactionCommits_InvalidatesCachesOnceAfterCommit()
    {
        var calls = new List<string>();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsTenantAdminAsync(TenantA, Arg.Any<CancellationToken>()).Returns(true);
        var storageService = Substitute.For<ITenantStorageSettingService>();
        storageService.ReadSettingsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(CreateCurrentSettings(isReadOnly: false));
        storageService.ApplyPatchAsync(
                TenantA,
                UserId,
                Arg.Any<PatchTenantStorageSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("persisted");
                return Task.CompletedTask;
            });
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        settingsResolver.When(resolver => resolver.InvalidateCache(SettingScope.Tenant, TenantA))
            .Do(_ => calls.Add("hierarchical-invalidated"));
        var s3ConfigResolver = Substitute.For<IS3ConfigResolver>();
        s3ConfigResolver.When(resolver => resolver.InvalidateCache(TenantA))
            .Do(_ => calls.Add("s3-invalidated"));
        var unitOfWork = new RecordingUnitOfWork(calls);
        var handler = CreateHandler(
            tenantContext,
            adminContext,
            storageService,
            unitOfWork,
            settingsResolver,
            s3ConfigResolver);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(unitOfWork.ExecutionCount).IsEqualTo(1);
        await Assert.That(calls.Count).IsEqualTo(4);
        await Assert.That(calls[0]).IsEqualTo("persisted");
        await Assert.That(calls[1]).IsEqualTo("committed");
        await Assert.That(calls[2]).IsEqualTo("hierarchical-invalidated");
        await Assert.That(calls[3]).IsEqualTo("s3-invalidated");
        await storageService.Received(1).ApplyPatchAsync(
            TenantA,
            UserId,
            Arg.Any<PatchTenantStorageSettingsDto>(),
            Arg.Any<CancellationToken>());
        settingsResolver.Received(1).InvalidateCache(SettingScope.Tenant, TenantA);
        s3ConfigResolver.Received(1).InvalidateCache(TenantA);
    }

    [Test]
    public async Task Handle_WhenTransactionFails_DoesNotInvalidateCaches()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsTenantAdminAsync(TenantA, Arg.Any<CancellationToken>()).Returns(true);
        var storageService = Substitute.For<ITenantStorageSettingService>();
        storageService.ReadSettingsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(CreateCurrentSettings(isReadOnly: false));
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var s3ConfigResolver = Substitute.For<IS3ConfigResolver>();
        var unitOfWork = new RecordingUnitOfWork([]) { FailAfterOperation = true };
        var handler = CreateHandler(
            tenantContext,
            adminContext,
            storageService,
            unitOfWork,
            settingsResolver,
            s3ConfigResolver);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await handler.Handle(CreateCommand(), CancellationToken.None));

        await Assert.That(unitOfWork.ExecutionCount).IsEqualTo(1);
        settingsResolver.DidNotReceive().InvalidateCache(Arg.Any<SettingScope?>(), Arg.Any<Guid?>());
        s3ConfigResolver.DidNotReceive().InvalidateCache(Arg.Any<Guid?>());
    }

    private static PatchTenantStorageSettingsCommandHandler CreateHandler(
        ITenantContext tenantContext,
        IAdminContext adminContext,
        ITenantStorageSettingService storageService,
        IUnitOfWork unitOfWork,
        IHierarchicalSettingsResolver settingsResolver,
        IS3ConfigResolver s3ConfigResolver)
        => new(
            tenantContext,
            adminContext,
            storageService,
            unitOfWork,
            settingsResolver,
            s3ConfigResolver);

    private static PatchTenantStorageSettingsCommand CreateCommand(long maxUploadBytes = 64)
        => new()
        {
            UserId = UserId,
            Settings = new PatchTenantStorageSettingsDto
            {
                Policy = new PatchTenantStoragePolicyDto
                {
                    MaxUploadBytes = OptionalUpdate<long>.Set(maxUploadBytes)
                }
            }
        };

    private static TenantStorageSettingsDto CreateCurrentSettings(
        bool isReadOnly,
        long instanceMaxUploadBytes = 128)
        => new()
        {
            TenantId = TenantA,
            Provider = StorageProviders.Local,
            MaxUploadBytes = 64,
            TenantQuotaBytes = 1024,
            IsReadOnly = isReadOnly,
            TenantOverridesAllowed = !isReadOnly,
            EffectivePolicy = new TenantStorageEffectivePolicyDto
            {
                Provider = StorageProviders.Local,
                MaxUploadBytes = 64,
                TenantQuotaBytes = 1024,
                InstanceMaxUploadBytes = instanceMaxUploadBytes,
                TenantOverridesAllowed = !isReadOnly
            }
        };

    private sealed class RecordingUnitOfWork(List<string> calls) : IUnitOfWork
    {
        public bool FailAfterOperation { get; init; }
        public int ExecutionCount { get; private set; }

        public async Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default)
        {
            ExecutionCount++;
            await operation(ct);
            if (FailAfterOperation)
            {
                throw new InvalidOperationException("Simulated transaction rollback.");
            }

            calls.Add("committed");
        }

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
