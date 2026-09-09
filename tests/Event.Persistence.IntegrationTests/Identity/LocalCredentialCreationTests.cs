
using System.Data.Common;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Authentication;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalCredentialCreationTests
{
    public enum ReplayMismatch
    {
        Initiator,
        Profile,
        Email
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task CreationCommitsHashedPendingCredentialAndReceiptWithoutNormalLogin(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalCredentialCreateRequest request = fixture.Request();

        LocalCredentialCreateResult result = await fixture.CreatePendingAsync(request);

        await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        await Assert.That(string.IsNullOrEmpty(result.TemporaryPassword)).IsFalse();
        LocalIdentityUser user = await AssertPendingAsync(fixture, request, result.Receipt!);
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        LocalIdentityUser current = (await manager.FindByIdAsync(user.Id.ToString("D")))!;
        await Assert.That(await manager.CheckPasswordAsync(current, result.TemporaryPassword!).WaitAsync(fixture.CancellationToken))
            .IsTrue();
        await Assert.That(string.Equals(current.PasswordHash, result.TemporaryPassword, StringComparison.Ordinal)).IsFalse();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var settings = new SystemSettingRepository(dbContext: application,
            mutationLock: new RelationalSettingMutationLock(dbContext: application,
                unitOfWork: new EfCoreUnitOfWork(application)));
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, null, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                SettingKey: SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
                Value: Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                Source: SecretSourceType.EnvironmentVariable, Scope: SecretScope.Instance,
                ScopeId: null, ResolvedAt: DateTimeOffset.UtcNow)));
        var authentication = new LocalIdentityAuthService(
            userManager: manager,
            tokenGenerator: new LocalJwtTokenGenerator(secretResolver: secrets,
                options: Options.Create(new LocalIdentityOptions()), timeProvider: TimeProvider.System),
            systemSettings: settings,
            credentialStates: fixture.StateStore(scope));

        LocalAuthResponseDto login = await authentication.AuthenticateAsync(
            new LocalAuthRequestDto(Identifier: request.Email, Password: result.TemporaryPassword!), fixture.CancellationToken);

        await Assert.That(string.IsNullOrEmpty(login.Token)).IsTrue();
        await Assert.That(login.Success).IsFalse();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task SameOperationReplayReturnsOnlySafeReceiptWithoutResettingCredential(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalCredentialCreateRequest request = fixture.Request();
        LocalCredentialCreateResult first = await fixture.CreatePendingAsync(request);
        await Assert.That(first.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        LocalIdentityUser before = await AssertPendingAsync(fixture, request, first.Receipt!);

        LocalCredentialCreateResult replay = await fixture.CreatePendingAsync(request);

        await Assert.That(replay.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Replayed);
        await Assert.That(string.IsNullOrEmpty(replay.TemporaryPassword)).IsTrue();
        await Assert.That(replay.Receipt).IsEqualTo(first.Receipt);
        LocalIdentityUser after = await AssertPendingAsync(fixture, request, replay.Receipt!);
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task FailureBeforePendingStateSaveRollsBackCredentialAndOperation(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        fixture.RejectPendingSave.Enabled = true;

        await Assert.ThrowsAsync<InjectedPersistenceFailure>(() => fixture.CreatePendingAsync(fixture.Request()));

        await Assert.That(fixture.RejectPendingSave.Observed).IsTrue();
        await using AsyncServiceScope read = fixture.Provider.CreateAsyncScope();
        DbContext identity = fixture.Identity(read);
        await Assert.That(await identity.Set<LocalIdentityUser>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
        await Assert.That(await identity.Set<IdentityUserToken<Guid>>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
        await Assert.That(await identity.Set<LocalIdentityCredentialOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task LostCommitResponseReplaysPersistedReceiptWithoutRevealingPassword(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalCredentialCreateRequest request = fixture.Request();
        fixture.LoseCommitResponse.Enabled = true;

        await Assert.ThrowsAsync<InjectedPersistenceFailure>(() => fixture.CreatePendingAsync(request));

        await Assert.That(fixture.LoseCommitResponse.Observed).IsTrue();
        LocalCredentialCreateResult retry = await fixture.CreatePendingAsync(request);
        await Assert.That(retry.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Replayed);
        await Assert.That(string.IsNullOrEmpty(retry.TemporaryPassword)).IsTrue();
        await AssertPendingAsync(fixture, request, retry.Receipt!);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ConcurrentSameOperationHasOneCredentialAndOnePlaintextWinner(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalCredentialCreateRequest request = fixture.Request();

        LocalCredentialCreateResult[] results = await fixture.CreateConcurrentlyAsync(request, request);

        await Assert.That(results.Count(result => result.Outcome == LocalCredentialCreateOutcome.Created)).IsEqualTo(1);
        await Assert.That(results.Count(result => result.Outcome == LocalCredentialCreateOutcome.Replayed)).IsEqualTo(1);
        await Assert.That(results.Count(result => !string.IsNullOrEmpty(result.TemporaryPassword))).IsEqualTo(1);
        await Assert.That(results[0].Receipt).IsEqualTo(results[1].Receipt);
        await AssertPendingAsync(fixture, request, results[0].Receipt!);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ConcurrentNormalizedEmailCannotCreateOrRevealASecondCredential(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalCredentialCreateRequest first = fixture.Request();
        LocalCredentialCreateRequest second = fixture.Request(email: first.Email.ToUpperInvariant());

        LocalCredentialCreateResult[] results = await fixture.CreateConcurrentlyAsync(first, second);

        await Assert.That(results.Count(result => result.Outcome == LocalCredentialCreateOutcome.Created)).IsEqualTo(1);
        await Assert.That(results.Count(result => result.Outcome == LocalCredentialCreateOutcome.Conflict)).IsEqualTo(1);
        await Assert.That(results.Count(result => !string.IsNullOrEmpty(result.TemporaryPassword))).IsEqualTo(1);
        int winnerIndex = Array.FindIndex(results, result => result.Outcome == LocalCredentialCreateOutcome.Created);
        LocalCredentialCreateRequest winnerRequest = winnerIndex == 0 ? first : second;
        await AssertPendingAsync(fixture, winnerRequest, results[winnerIndex].Receipt!);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, ReplayMismatch.Initiator)]
    [Arguments(IdentityDatabaseTopology.Colocated, ReplayMismatch.Profile)]
    [Arguments(IdentityDatabaseTopology.Colocated, ReplayMismatch.Email)]
    [Arguments(IdentityDatabaseTopology.External, ReplayMismatch.Initiator)]
    [Arguments(IdentityDatabaseTopology.External, ReplayMismatch.Profile)]
    [Arguments(IdentityDatabaseTopology.External, ReplayMismatch.Email)]
    public async Task ReplayWithDifferentIntentCannotRevealOrMutateCredential(
        IdentityDatabaseTopology topology, ReplayMismatch mismatch)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalCredentialCreateRequest original = fixture.Request();
        LocalCredentialCreateResult first = await fixture.CreatePendingAsync(original);
        await Assert.That(first.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        LocalIdentityUser before = await AssertPendingAsync(fixture, original, first.Receipt!);
        var changed = new LocalCredentialCreateRequest(
            operationId: original.OperationId,
            initiatingApplicationUserId: mismatch == ReplayMismatch.Initiator ? Guid.CreateVersion7() : original.InitiatingApplicationUserId,
            email: mismatch == ReplayMismatch.Email ? $"different-{Guid.CreateVersion7():N}@example.test" : original.Email,
            firstName: mismatch == ReplayMismatch.Profile ? "Different profile" : original.FirstName,
            lastName: original.LastName);

        LocalCredentialCreateResult result = await fixture.CreatePendingAsync(changed);

        await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Conflict);
        await Assert.That(result.Receipt).IsNull();
        await Assert.That(string.IsNullOrEmpty(result.TemporaryPassword)).IsTrue();
        LocalIdentityUser after = await AssertPendingAsync(fixture, original, first.Receipt!);
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(after.Email).IsEqualTo(before.Email);
        await Assert.That(after.FirstName).IsEqualTo(before.FirstName);
        await Assert.That(after.LastName).IsEqualTo(before.LastName);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task DirtyCallerContextIsRefusedWithoutSavingOrDetachingCallerWork(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await using AsyncServiceScope owner = fixture.Provider.CreateAsyncScope();
        DbContext identity = fixture.Identity(owner);
        var role = new LocalIdentityRole($"caller-{Guid.CreateVersion7():N}");
        identity.Set<LocalIdentityRole>().Add(role);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.StateStore(owner)
            .CreatePendingAsync(fixture.Request(), fixture.CancellationToken));

        await Assert.That(identity.Entry(role).State).IsEqualTo(EntityState.Added);
        await using (AsyncServiceScope observer = fixture.Provider.CreateAsyncScope())
        {
            await Assert.That(await fixture.Identity(observer).Set<LocalIdentityRole>()
                .AnyAsync(row => row.Id == role.Id, fixture.CancellationToken)).IsFalse();
        }
        await identity.SaveChangesAsync(fixture.CancellationToken);
        await AssertOnlyCallerRolePersistedAsync(fixture, role.Id);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task CallerTransactionIsRefusedWithoutCommittingOrRollingBackItsWork(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await using AsyncServiceScope owner = fixture.Provider.CreateAsyncScope();
        DbContext identity = fixture.Identity(owner);
        await using var transaction = await identity.Database.BeginTransactionAsync(fixture.CancellationToken);
        var role = new LocalIdentityRole($"caller-{Guid.CreateVersion7():N}");
        identity.Set<LocalIdentityRole>().Add(role);
        await identity.SaveChangesAsync(fixture.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.StateStore(owner)
            .CreatePendingAsync(fixture.Request(), fixture.CancellationToken));

        await Assert.That(ReferenceEquals(identity.Database.CurrentTransaction, transaction)).IsTrue();
        await Assert.That(identity.Entry(role).State).IsEqualTo(EntityState.Unchanged);
        await using (AsyncServiceScope observer = fixture.Provider.CreateAsyncScope())
        {
            await Assert.That(await fixture.Identity(observer).Set<LocalIdentityRole>()
                .AnyAsync(row => row.Id == role.Id, fixture.CancellationToken)).IsFalse();
        }
        await transaction.CommitAsync(fixture.CancellationToken);
        await AssertOnlyCallerRolePersistedAsync(fixture, role.Id);
    }

    [Test]
    [Arguments(LocalCredentialOperationKind.Create, LocalCredentialOperationStage.ChangeRequired)]
    [Arguments(LocalCredentialOperationKind.Reset, LocalCredentialOperationStage.ProvisioningPending)]
    public async Task CreatedResultCannotExposePasswordForNonPendingCreationReceipt(
        LocalCredentialOperationKind kind, LocalCredentialOperationStage stage)
    {
        var receipt = new LocalCredentialOperationReceipt(
            operationId: Guid.CreateVersion7(),
            kind: kind,
            stage: stage,
            initiatingApplicationUserId: Guid.CreateVersion7(),
            localSubjectId: Guid.CreateVersion7(),
            personalActorId: Guid.CreateVersion7(),
            externalLoginId: Guid.CreateVersion7(),
            createdAt: DateTime.UtcNow);
        string temporaryPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            LocalCredentialCreateResult.Created(receipt, temporaryPassword);
            return Task.CompletedTask;
        });
    }

    private static async Task AssertOnlyCallerRolePersistedAsync(Fixture fixture, Guid roleId)
    {
        await using AsyncServiceScope observer = fixture.Provider.CreateAsyncScope();
        DbContext identity = fixture.Identity(observer);
        await Assert.That(await identity.Set<LocalIdentityRole>().CountAsync(row => row.Id == roleId, fixture.CancellationToken))
            .IsEqualTo(1);
        await Assert.That(await identity.Set<LocalIdentityUser>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
        await Assert.That(await identity.Set<IdentityUserToken<Guid>>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
        await Assert.That(await identity.Set<LocalIdentityCredentialOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
    }

    private static async Task<LocalIdentityUser> AssertPendingAsync(
        Fixture fixture, LocalCredentialCreateRequest request, LocalCredentialOperationReceipt receipt)
    {
        await Assert.That(receipt).IsNotNull();
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        DbContext identity = fixture.Identity(scope);
        LocalIdentityUser user = await identity.Set<LocalIdentityUser>().AsNoTracking().SingleAsync(fixture.CancellationToken);
        LocalIdentityCredentialOperation operation = await identity.Set<LocalIdentityCredentialOperation>()
            .AsNoTracking().SingleAsync(fixture.CancellationToken);
        LocalCredentialStateMetadata? metadata = await fixture.StateStore(scope)
            .ReadAsync(localSubjectId: user.Id, expectedSecurityStamp: user.SecurityStamp!,
                cancellationToken: fixture.CancellationToken);
        await Assert.That(operation.Id).IsEqualTo(request.OperationId);
        await Assert.That(operation.Kind).IsEqualTo(LocalCredentialOperationKind.Create);
        await Assert.That(operation.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
        await Assert.That(operation.LocalSubjectId).IsEqualTo(user.Id);
        await Assert.That(operation.ApplicationUserId).IsEqualTo(user.Id);
        await Assert.That(operation.InitiatingApplicationUserId).IsEqualTo(fixture.InitiatorId);
        await Assert.That(operation.VerifiedByApplicationUserId).IsEqualTo(fixture.InitiatorId);
        await Assert.That(operation.VerifiedAt).IsEqualTo(operation.CreatedAt);
        await Assert.That(receipt.LocalSubjectId).IsEqualTo(user.Id);
        await Assert.That(receipt.ApplicationUserId).IsEqualTo(user.Id);
        await Assert.That(receipt.PersonalActorId).IsEqualTo(operation.PersonalActorId);
        await Assert.That(receipt.ExternalLoginId).IsEqualTo(operation.ExternalLoginId);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.State).IsEqualTo(LocalCredentialState.ProvisioningPending);
        await Assert.That(metadata.OperationId).IsEqualTo(operation.Id);
        await Assert.That(metadata.ApplicationUserId).IsEqualTo(user.Id);
        await Assert.That(string.IsNullOrEmpty(user.PasswordHash)).IsFalse();
        await Assert.That(user.EmailConfirmed).IsTrue();
        await Assert.That(user.NormalizedEmail).IsEqualTo(request.Email.ToUpperInvariant());
        await Assert.That(await identity.Set<IdentityUserToken<Guid>>().CountAsync(fixture.CancellationToken)).IsEqualTo(1);
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await application.LocalIdentityUsers.CountAsync(fixture.CancellationToken))
            .IsEqualTo(fixture.Topology == IdentityDatabaseTopology.Colocated ? 1 : 0);
        return user;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _applicationPath = Path.Combine(Path.GetTempPath(), $"credential-app-{Guid.CreateVersion7():N}.db");
        private readonly string _externalPath = Path.Combine(Path.GetTempPath(), $"credential-identity-{Guid.CreateVersion7():N}.db");
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private readonly MemoryCache _metadataCache = new(new MemoryCacheOptions());
        private ServiceProvider? _provider;
        internal ServiceProvider Provider => _provider!;
        internal Guid InitiatorId { get; } = Guid.CreateVersion7();
        internal IdentityDatabaseTopology Topology { get; private set; }
        internal CancellationToken CancellationToken => _timeout.Token;
        internal RejectPendingSaveInterceptor RejectPendingSave { get; } = new();
        internal LoseCommitResponseInterceptor LoseCommitResponse { get; } = new();

        internal static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology)
        {
            await LocalIdentitySqliteTemplate.InitializeAsync();
            var fixture = new Fixture { Topology = topology };
            try
            {
                await fixture.InitializeAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal DbContext Identity(AsyncServiceScope scope) => Topology == IdentityDatabaseTopology.External
            ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>()
            : scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        internal LocalIdentityCredentialStateStore StateStore(AsyncServiceScope scope) => new(
            identityDbContext: Identity(scope),
            applicationDbContext: scope.ServiceProvider.GetRequiredService<ExploreDbContext>(),
            userManager: scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(),
            timeProvider: TimeProvider.System);

        internal LocalCredentialCreateRequest Request(string? email = null) => new(
            operationId: Guid.CreateVersion7(), initiatingApplicationUserId: InitiatorId,
            email: email ?? $"created-{Guid.CreateVersion7():N}@example.test", firstName: "Created", lastName: "Administrator");

        internal async Task<LocalCredentialCreateResult> CreatePendingAsync(LocalCredentialCreateRequest request)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            ILocalCredentialAdministration administration = StateStore(scope);
            return await administration.CreatePendingAsync(request, CancellationToken);
        }

        internal async Task<LocalCredentialCreateResult[]> CreateConcurrentlyAsync(
            LocalCredentialCreateRequest first, LocalCredentialCreateRequest second)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LocalCredentialCreateResult> Run(LocalCredentialCreateRequest request, TaskCompletionSource ready) => Task.Run(async () =>
            {
                await using AsyncServiceScope scope = Provider.CreateAsyncScope();
                ILocalCredentialAdministration administration = StateStore(scope);
                ready.SetResult();
                await start.Task.WaitAsync(CancellationToken);
                return await administration.CreatePendingAsync(request, CancellationToken);
            }, CancellationToken);
            Task<LocalCredentialCreateResult> firstTask = Run(first, firstReady);
            Task<LocalCredentialCreateResult> secondTask = Run(second, secondReady);
            await Task.WhenAll(firstReady.Task, secondReady.Task).WaitAsync(CancellationToken);
            start.SetResult();
            return await Task.WhenAll(firstTask, secondTask).WaitAsync(CancellationToken);
        }

        private async Task InitializeAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ExploreDbContext>(options => ConfigureDatabase(options, _applicationPath));
            IdentityBuilder identity = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>();
            if (Topology == IdentityDatabaseTopology.External)
            {
                services.AddDbContext<ExternalIdentityDbContext>(options => ConfigureDatabase(options, _externalPath));
                identity.AddEntityFrameworkStores<ExternalIdentityDbContext>();
            }
            else
            {
                identity.AddEntityFrameworkStores<ExploreDbContext>();
            }
            _provider = services.BuildIsolatedServiceProvider();
            await LocalIdentitySqliteTemplate.CopyAsync(_applicationPath,
                Topology == IdentityDatabaseTopology.External ? _externalPath : null, seedLookups: false, CancellationToken);
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            application.Users.Add(new User
            {
                Id = InitiatorId,
                Pii = new UserPii { Email = $"initiator-{InitiatorId:N}@example.test", FirstName = "Instance", LastName = "Operator" },
                EmailVerified = true, CreatedAt = DateTime.UtcNow
            });
            application.Set<SettingValueTypeLookup>().Add(new SettingValueTypeLookup
            {
                Id = (int)SettingValueType.Boolean, MasterCode = "BOOLEAN", FullName = "Boolean"
            });
            application.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
                Value = "false", ValueType = SettingValueType.Boolean, CreatedAt = DateTime.UtcNow
            });
            await application.SaveChangesAsync(CancellationToken);
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options, string path) => options
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .UseMemoryCache(_metadataCache)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, RejectPendingSave, LoseCommitResponse);

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_provider is not null) await _provider.DisposeAsync();
            }
            finally
            {
                _metadataCache.Dispose();
                foreach (string path in new[] { _applicationPath, _externalPath })
                {
                    File.Delete(path);
                    File.Delete(path + "-wal");
                    File.Delete(path + "-shm");
                }
                _timeout.Dispose();
            }
        }
    }

    private sealed class InjectedPersistenceFailure : Exception
    {
    }

    private sealed class RejectPendingSaveInterceptor : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; }
        internal bool Observed { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<IdentityUserToken<Guid>>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && entry.Entity.Name == LocalCredentialStateMetadata.TokenName))
            {
                Observed = true;
                throw new InjectedPersistenceFailure();
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class LoseCommitResponseInterceptor : DbTransactionInterceptor
    {
        internal bool Enabled { get; set; }
        internal bool Observed { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Enabled = false;
                Observed = true;
                throw new InjectedPersistenceFailure();
            }
            return Task.CompletedTask;
        }
    }
}
