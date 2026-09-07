using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Onboarding;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class InstanceOnboardingConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    private const string ConfigurationFingerprint =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ExactIdentityFingerprint =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private const string AttackerIdentityFingerprint =
        "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private static readonly DateTime CreatedAt =
        new(2026, 8, 31, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CompletedAt = CreatedAt.AddMinutes(1);

    [Test]
    public async Task GetCurrentForUpdate_BlocksASecondSerializableReaderUntilTheFirstCommits()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedPendingAsync(userId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstHasLock = NewSignal();
        var releaseFirst = NewSignal();
        var secondAttemptedLock = NewSignal();
        var secondInterceptor = new BootstrapLockAttemptInterceptor(secondAttemptedLock);

        Task first = RunClaimAsync(
            userId,
            ExactIdentityFingerprint,
            generation: 7,
            firstHasLock,
            releaseFirst,
            timeout.Token);
        await firstHasLock.Task.WaitAsync(timeout.Token);

        Task<ClaimDisposition> second = RunClaimAsync(
            userId,
            ExactIdentityFingerprint,
            generation: 7,
            entered: null,
            release: null,
            timeout.Token,
            secondInterceptor);
        await secondAttemptedLock.Task.WaitAsync(timeout.Token);
        await Assert.That(second.IsCompleted).IsFalse();

        releaseFirst.TrySetResult();
        await first.WaitAsync(timeout.Token);
        ClaimDisposition secondResult = await second.WaitAsync(timeout.Token);

        await Assert.That(secondResult).IsEqualTo(ClaimDisposition.ExactReplay);
    }

    [Test]
    public async Task ConcurrentExactClaims_ConvergeOnOneTransitionAndOneExactReplay()
    {
        Guid userId = Guid.CreateVersion7();
        Guid stateId = await SeedPendingAsync(userId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstHasLock = NewSignal();
        var releaseFirst = NewSignal();
        var secondAttemptedLock = NewSignal();

        Task<ClaimDisposition> first = RunClaimAsync(
            userId, ExactIdentityFingerprint, 7,
            firstHasLock, releaseFirst, timeout.Token);
        await firstHasLock.Task.WaitAsync(timeout.Token);
        Task<ClaimDisposition> second = RunClaimAsync(
            userId, ExactIdentityFingerprint, 7,
            null, null, timeout.Token,
            new BootstrapLockAttemptInterceptor(secondAttemptedLock));
        await secondAttemptedLock.Task.WaitAsync(timeout.Token);
        releaseFirst.TrySetResult();

        ClaimDisposition[] outcomes = await Task.WhenAll(first, second)
            .WaitAsync(timeout.Token);

        await Assert.That(outcomes.Count(outcome => outcome == ClaimDisposition.CompletedNow))
            .IsEqualTo(1);
        await Assert.That(outcomes.Count(outcome => outcome == ClaimDisposition.ExactReplay))
            .IsEqualTo(1);
        await using ExploreDbContext verification = fixture.CreateDbContext();
        InstanceBootstrapState persisted = await verification.InstanceBootstrapStates
            .AsNoTracking()
            .SingleAsync(state => state.Id == stateId, timeout.Token);
        await Assert.That(persisted.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(persisted.CompletedByUserId).IsEqualTo(userId);
        await Assert.That(persisted.CompletedIdentityFingerprint)
            .IsEqualTo(ExactIdentityFingerprint);
        await Assert.That(await verification.InstanceBootstrapStates.CountAsync(timeout.Token))
            .IsEqualTo(1);
    }

    [Test]
    public async Task ExactClaimRacingAttacker_AllowsOnlyExactIdentityAndLeavesNoAttackerRows()
    {
        Guid exactUserId = Guid.CreateVersion7();
        Guid attackerUserId = Guid.CreateVersion7();
        await SeedPendingAsync(exactUserId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var exactHasLock = NewSignal();
        var releaseExact = NewSignal();
        var attackerAttemptedLock = NewSignal();

        Task<ClaimDisposition> exact = RunClaimAsync(
            exactUserId, ExactIdentityFingerprint, 7,
            exactHasLock, releaseExact, timeout.Token);
        await exactHasLock.Task.WaitAsync(timeout.Token);
        Task<ClaimDisposition> attacker = RunClaimAsync(
            attackerUserId, AttackerIdentityFingerprint, 7,
            null, null, timeout.Token,
            new BootstrapLockAttemptInterceptor(attackerAttemptedLock));
        await attackerAttemptedLock.Task.WaitAsync(timeout.Token);
        releaseExact.TrySetResult();

        await Assert.That(await exact.WaitAsync(timeout.Token))
            .IsEqualTo(ClaimDisposition.CompletedNow);
        await Assert.That(await attacker.WaitAsync(timeout.Token))
            .IsEqualTo(ClaimDisposition.IdentityMismatch);

        await using ExploreDbContext verification = fixture.CreateDbContext();
        InstanceBootstrapState persisted = await verification.InstanceBootstrapStates
            .AsNoTracking()
            .SingleAsync(timeout.Token);
        await Assert.That(persisted.CompletedByUserId).IsEqualTo(exactUserId);
        await Assert.That(persisted.CompletedIdentityFingerprint)
            .IsEqualTo(ExactIdentityFingerprint);
        await Assert.That(await verification.Users.AnyAsync(
            user => user.Id == attackerUserId, timeout.Token)).IsFalse();
        await Assert.That(await verification.UserExternalLogins.AnyAsync(
            login => login.UserId == attackerUserId, timeout.Token)).IsFalse();
        await Assert.That(await verification.PlatformUserRoles.AnyAsync(
            role => role.UserId == attackerUserId, timeout.Token)).IsFalse();
    }

    [Test]
    public async Task GenerationMismatch_FailsClosedWithoutMutatingPendingState()
    {
        Guid userId = Guid.CreateVersion7();
        Guid stateId = await SeedPendingAsync(userId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        ClaimDisposition result = await RunClaimAsync(
            userId,
            ExactIdentityFingerprint,
            generation: 8,
            entered: null,
            release: null,
            timeout.Token);

        await Assert.That(result).IsEqualTo(ClaimDisposition.GenerationMismatch);
        await using ExploreDbContext verification = fixture.CreateDbContext();
        InstanceBootstrapState persisted = await verification.InstanceBootstrapStates
            .AsNoTracking()
            .SingleAsync(state => state.Id == stateId, timeout.Token);
        await Assert.That(persisted.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(persisted.Generation).IsEqualTo(7L);
        await Assert.That(persisted.CompletedAt).IsNull();
        await Assert.That(persisted.CompletedByUserId).IsNull();
    }

    [Test]
    public async Task FailureAfterIdentityAuthorityAndCompletionWrites_RollsBackEveryPartialWrite()
    {
        await fixture.ResetAsync();
        Guid stateId = Guid.CreateVersion7();
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            await new InstanceBootstrapStateRepository(seed).Create(
                InstanceBootstrapState.CreateConfiguredAdministratorPending(
                    stateId,
                    AuthenticationProviderKind.Keycloak,
                    DeploymentMode.MultiTenant,
                    7,
                    ConfigurationFingerprint,
                    ExactIdentityFingerprint,
                    CreatedAt));
        }

        Guid userId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        Guid loginId = Guid.CreateVersion7();
        const string settingKey = "onboarding.rollback.probe";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using (ExploreDbContext write = fixture.CreateDbContext())
        {
            var unitOfWork = new EfCoreUnitOfWork(write);
            await Assert.That(async () => await unitOfWork.ExecuteSerializableAsync<bool>(async token =>
            {
                InstanceBootstrapState state =
                    (await new InstanceBootstrapStateRepository(write)
                        .GetCurrentForUpdate(token))!;
                User user = CreateUser(userId);
                var tenant = new Tenant
                {
                    Id = tenantId,
                    FullName = "Rollback Tenant",
                    Slug = $"rollback-{tenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!,
                    CreatedAt = CompletedAt
                };
                write.Users.Add(user);
                write.Tenants.Add(tenant);
                write.Actors.Add(new Actor
                {
                    Id = actorId,
                    ActorTypeId = (int)ActorTypeEnum.User,
                    ActorType = null!,
                    UserId = userId,
                    User = user,
                    Pii = new ActorPii { DisplayName = "Rollback Administrator" },
                    CreatedAt = CompletedAt,
                    ConcurrencyStamp = Guid.CreateVersion7()
                });
                write.UserExternalLogins.Add(new UserExternalLogin { Id = loginId,
                UserId = userId,
                User = user,
                AuthenticationProviderId = (int)"keycloak".ParseAuthenticationProviderKind(), AuthenticationProvider = null!, ProviderKey = "rollback-subject",
                ProviderDisplayName = "keycloak",
                CreatedAt = CompletedAt });
                write.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = userId,
                    User = user,
                    RoleId = (int)RoleEnum.Admin,
                    Role = null!,
                    GrantedAt = CompletedAt,
                    GrantedBy = userId
                });
                write.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = settingKey,
                    Value = "true",
                    ValueType = SettingValueType.Boolean,
                    IsLocked = true,
                    CreatedAt = CompletedAt
                });
                state.CompleteConfiguredAdministrator(
                    AuthenticationProviderKind.Keycloak,
                    7,
                    ExactIdentityFingerprint,
                    userId,
                    CompletedAt);
                await write.SaveChangesAsync(token);
                throw new InjectedPersistenceFailureException();
            }, timeout.Token)).Throws<InjectedPersistenceFailureException>();
        }

        await using ExploreDbContext verification = fixture.CreateDbContext();
        await Assert.That(await verification.Users.AnyAsync(
            user => user.Id == userId, timeout.Token)).IsFalse();
        await Assert.That(await verification.Actors.AnyAsync(
            actor => actor.Id == actorId, timeout.Token)).IsFalse();
        await Assert.That(await verification.UserExternalLogins.AnyAsync(
            login => login.Id == loginId, timeout.Token)).IsFalse();
        await Assert.That(await verification.PlatformUserRoles.AnyAsync(
            role => role.UserId == userId, timeout.Token)).IsFalse();
        await Assert.That(await verification.Tenants.AnyAsync(
            tenant => tenant.Id == tenantId, timeout.Token)).IsFalse();
        await Assert.That(await verification.SystemSettings.AnyAsync(
            setting => setting.SettingKey == settingKey, timeout.Token)).IsFalse();
        InstanceBootstrapState pending = await verification.InstanceBootstrapStates
            .AsNoTracking()
            .SingleAsync(state => state.Id == stateId, timeout.Token);
        await Assert.That(pending.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(pending.CompletedAt).IsNull();
        await Assert.That(pending.CompletedByUserId).IsNull();
        await Assert.That(pending.CompletedIdentityFingerprint).IsNull();
    }

    private async Task<Guid> SeedPendingAsync(Guid existingUserId)
    {
        await fixture.ResetAsync();
        Guid stateId = Guid.CreateVersion7();
        await using ExploreDbContext seed = fixture.CreateDbContext();
        seed.Users.Add(CreateUser(existingUserId));
        await new InstanceBootstrapStateRepository(seed).Create(
            InstanceBootstrapState.CreateConfiguredAdministratorPending(
                stateId,
                AuthenticationProviderKind.Keycloak,
                DeploymentMode.MultiTenant,
                7,
                ConfigurationFingerprint,
                ExactIdentityFingerprint,
                CreatedAt));
        return stateId;
    }

    private async Task<ClaimDisposition> RunClaimAsync(
        Guid userId,
        string identityFingerprint,
        long generation,
        TaskCompletionSource? entered,
        TaskCompletionSource? release,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors)
    {
        await using ExploreDbContext context = fixture.CreateDbContext(interceptors);
        var repository = new InstanceBootstrapStateRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        int gateEntry = 0;
        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            InstanceBootstrapState? state = await repository.GetCurrentForUpdate(token);
            if (Interlocked.Increment(ref gateEntry) == 1 && entered is not null)
            {
                entered.TrySetResult();
                await release!.Task.WaitAsync(token);
            }

            if (state is null
                || state.Mode != InstanceBootstrapMode.ConfiguredAdministrator
                || state.ProviderKind != AuthenticationProviderKind.Keycloak)
            {
                return ClaimDisposition.IdentityMismatch;
            }
            if (state.Generation != generation)
            {
                return ClaimDisposition.GenerationMismatch;
            }
            if (!string.Equals(
                    state.SelectorFingerprint,
                    identityFingerprint,
                    StringComparison.Ordinal))
            {
                return ClaimDisposition.IdentityMismatch;
            }
            if (state.Status == InstanceBootstrapStatus.Completed)
            {
                return state.CompletedByUserId == userId
                       && string.Equals(
                           state.CompletedIdentityFingerprint,
                           identityFingerprint,
                           StringComparison.Ordinal)
                    ? ClaimDisposition.ExactReplay
                    : ClaimDisposition.IdentityMismatch;
            }

            state.CompleteConfiguredAdministrator(
                AuthenticationProviderKind.Keycloak,
                generation,
                identityFingerprint,
                userId,
                CompletedAt);
            await repository.Update(state);
            return ClaimDisposition.CompletedNow;
        }, cancellationToken);
    }

    private static User CreateUser(Guid userId) => new()
    {
        Id = userId,
        Pii = new UserPii
        {
            Email = $"claim-{userId:N}@example.test",
            FirstName = "Configured",
            LastName = "Administrator"
        },
        EmailVerified = true,
        ConcurrencyStamp = Guid.CreateVersion7(),
        CreatedAt = CreatedAt
    };

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private enum ClaimDisposition
    {
        CompletedNow,
        ExactReplay,
        IdentityMismatch,
        GenerationMismatch
    }

    private sealed class BootstrapLockAttemptInterceptor(TaskCompletionSource attempted)
        : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                attempted.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class InjectedPersistenceFailureException : Exception;
}
