
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Event.Persistence.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

[Timeout(180_000)]
public sealed class LocalCredentialBindingTests
{
    public enum ActivationWriteBoundary
    {
        OperationUpdated,
        TokenUpdated
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PendingCredentialCannotActivateBeforeExactApplicationBinding(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalIdentityCredentialStateStore store = fixture.StateStore(scope);
        LocalCredentialProvisioningSnapshot snapshot = (await store.ReadProvisioningAsync(
            fixture.Receipt.OperationId, fixture.CancellationToken))!;

        LocalCredentialActivationOutcome result = await store.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(operationId: snapshot.Receipt.OperationId,
                expectedOperationConcurrencyStamp: snapshot.OperationConcurrencyStamp), fixture.CancellationToken);

        await Assert.That(result).IsEqualTo(LocalCredentialActivationOutcome.BindingIncomplete);
        await fixture.AssertNoGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task StaleOperationStampAndSupersededTokenOwnerCannotActivatePendingCredential(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.SeedExactGraphAsync();
        await using AsyncServiceScope request = fixture.Provider.CreateAsyncScope();
        LocalIdentityCredentialStateStore store = fixture.StateStore(request);
        LocalCredentialProvisioningSnapshot snapshot = (await store.ReadProvisioningAsync(
            fixture.Receipt.OperationId, fixture.CancellationToken))!;
        Guid currentStamp = Guid.CreateVersion7();
        await using (AsyncServiceScope writer = fixture.Provider.CreateAsyncScope())
        {
            int changed = await fixture.Identity(writer).Set<LocalIdentityCredentialOperation>()
                .Where(operation => operation.Id == snapshot.Receipt.OperationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(operation => operation.ConcurrencyStamp, currentStamp),
                    fixture.CancellationToken);
            await Assert.That(changed).IsEqualTo(1);
        }

        LocalCredentialActivationOutcome stale = await store.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(operationId: snapshot.Receipt.OperationId,
                expectedOperationConcurrencyStamp: snapshot.OperationConcurrencyStamp), fixture.CancellationToken);

        await Assert.That(stale).IsEqualTo(LocalCredentialActivationOutcome.Conflict);
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
        Guid replacementOperationId = Guid.CreateVersion7();
        await using (AsyncServiceScope writer = fixture.Provider.CreateAsyncScope())
        {
            var manager = writer.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = (await manager.FindByIdAsync(fixture.Receipt.LocalSubjectId.ToString("D")))!;
            IdentityResult changed = await manager.SetAuthenticationTokenAsync(user,
                LocalCredentialStateMetadata.TokenLoginProvider, LocalCredentialStateMetadata.TokenName,
                JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                    version: LocalCredentialStateMetadata.CurrentVersion, state: LocalCredentialState.ProvisioningPending,
                    operationId: replacementOperationId, applicationUserId: fixture.Receipt.LocalSubjectId)));
            await Assert.That(changed.Succeeded).IsTrue();
        }
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();

        LocalCredentialActivationOutcome superseded = await store.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(operationId: snapshot.Receipt.OperationId,
                expectedOperationConcurrencyStamp: currentStamp), fixture.CancellationToken);

        await Assert.That(superseded).IsEqualTo(LocalCredentialActivationOutcome.Conflict);
        LocalCredentialStateMetadata? retained = JsonSerializer.Deserialize<LocalCredentialStateMetadata>(
            (await fixture.ReadTokenValueAsync())!);
        await Assert.That(retained!.OperationId).IsEqualTo(replacementOperationId);
        await Assert.That(retained.State).IsEqualTo(LocalCredentialState.ProvisioningPending);
        LocalIdentityUser after = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, ActivationWriteBoundary.OperationUpdated)]
    [Arguments(IdentityDatabaseTopology.Colocated, ActivationWriteBoundary.TokenUpdated)]
    [Arguments(IdentityDatabaseTopology.External, ActivationWriteBoundary.OperationUpdated)]
    [Arguments(IdentityDatabaseTopology.External, ActivationWriteBoundary.TokenUpdated)]
    public async Task FailureBetweenActivationWritesRollsBackEveryIdentityAuthorityRow(
        IdentityDatabaseTopology topology, ActivationWriteBoundary boundary)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.SeedExactGraphAsync();
        LocalIdentityUser beforeUser = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation beforeOperation = await fixture.ReadOperationAsync();
        string? beforeToken = await fixture.ReadTokenValueAsync();
        await using AsyncServiceScope request = fixture.Provider.CreateAsyncScope();
        DbContext identity = fixture.Identity(request);
        Type targetType = boundary == ActivationWriteBoundary.OperationUpdated
            ? typeof(LocalIdentityCredentialOperation) : typeof(IdentityUserToken<Guid>);
        fixture.ActivationWriteFault.Arm(identity: identity,
            table: identity.Model.FindEntityType(targetType)!.GetTableName()!);

        await Assert.ThrowsAsync<InjectedBindingFailure>(() => fixture.StateStore(request).ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(operationId: beforeOperation.Id,
                expectedOperationConcurrencyStamp: beforeOperation.ConcurrencyStamp), fixture.CancellationToken));

        await Assert.That(fixture.ActivationWriteFault.Observed).IsTrue();
        await Assert.That(fixture.ActivationWriteFault.AffectedRows).IsEqualTo(1);
        LocalIdentityCredentialOperation afterOperation = await fixture.ReadOperationAsync();
        LocalIdentityUser afterUser = await fixture.ReadIdentityUserAsync();
        await Assert.That(afterOperation.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
        await Assert.That(afterOperation.ConcurrencyStamp == beforeOperation.ConcurrencyStamp).IsTrue();
        await Assert.That(afterOperation.UpdatedAt).IsEqualTo(beforeOperation.UpdatedAt);
        await Assert.That(string.Equals(await fixture.ReadTokenValueAsync(), beforeToken, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterUser.PasswordHash, beforeUser.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterUser.SecurityStamp, beforeUser.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterUser.ConcurrencyStamp, beforeUser.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await fixture.AssertExactGraphAsync();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ConcurrentActivationConvergesWithoutRotatingStampsAgainOnReplay(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.SeedExactGraphAsync();
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation pending = await fixture.ReadOperationAsync();
        var request = new LocalCredentialActivationRequest(operationId: pending.Id,
            expectedOperationConcurrencyStamp: pending.ConcurrencyStamp);

        LocalCredentialActivationOutcome[] outcomes = await fixture.ActivateConcurrentlyAsync(request);

        await Assert.That(outcomes.Count(outcome => outcome == LocalCredentialActivationOutcome.Activated)).IsEqualTo(1);
        await Assert.That(outcomes.All(outcome => outcome is LocalCredentialActivationOutcome.Activated
            or LocalCredentialActivationOutcome.AlreadyActivated or LocalCredentialActivationOutcome.Conflict)).IsTrue();
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        LocalIdentityUser activated = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation activatedOperation = await fixture.ReadOperationAsync();
        string? activatedToken = await fixture.ReadTokenValueAsync();
        await Assert.That(string.Equals(activated.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(activated.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(activated.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();

        LocalCredentialActivationOutcome replay = await fixture.ActivateAsync(request);

        await Assert.That(replay).IsEqualTo(LocalCredentialActivationOutcome.AlreadyActivated);
        LocalIdentityUser afterReplay = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(afterReplay.SecurityStamp, activated.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterReplay.ConcurrencyStamp, activated.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterReplay.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That((await fixture.ReadOperationAsync()).ConcurrencyStamp == activatedOperation.ConcurrencyStamp).IsTrue();
        await Assert.That(string.Equals(await fixture.ReadTokenValueAsync(), activatedToken, StringComparison.Ordinal)).IsTrue();
        await fixture.AssertExactGraphAsync();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task LostIdentityCommitAcknowledgementReplaysWithoutSecondStampRotation(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.SeedExactGraphAsync();
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation pending = await fixture.ReadOperationAsync();
        var request = new LocalCredentialActivationRequest(operationId: pending.Id,
            expectedOperationConcurrencyStamp: pending.ConcurrencyStamp);
        fixture.ActivationCommitFault.LoseNextAcknowledgement = true;

        await Assert.ThrowsAsync<InjectedBindingFailure>(() => fixture.ActivateAsync(request));

        await Assert.That(fixture.ActivationCommitFault.LostAcknowledgementObserved).IsTrue();
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        LocalIdentityUser committed = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation committedOperation = await fixture.ReadOperationAsync();
        string? committedToken = await fixture.ReadTokenValueAsync();
        await Assert.That(committedOperation.Stage).IsEqualTo(LocalCredentialOperationStage.ChangeRequired);
        await Assert.That(string.Equals(committed.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(committed.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(committed.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();

        LocalCredentialActivationOutcome retry = await fixture.ActivateAsync(request);

        await Assert.That(retry).IsEqualTo(LocalCredentialActivationOutcome.AlreadyActivated);
        LocalIdentityUser afterRetry = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(afterRetry.SecurityStamp, committed.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterRetry.ConcurrencyStamp, committed.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterRetry.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That((await fixture.ReadOperationAsync()).ConcurrencyStamp == committedOperation.ConcurrencyStamp).IsTrue();
        await Assert.That(string.Equals(await fixture.ReadTokenValueAsync(), committedToken, StringComparison.Ordinal)).IsTrue();
        await fixture.AssertExactGraphAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _applicationPath = Path.Combine(Path.GetTempPath(), $"binding-app-{Guid.CreateVersion7():N}.db");
        private readonly string _identityPath = Path.Combine(Path.GetTempPath(), $"binding-identity-{Guid.CreateVersion7():N}.db");
        private readonly CancellationTokenSource _timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        private readonly MemoryCache _metadataCache = new(new MemoryCacheOptions());
        private ServiceProvider? _provider;
        private IdentityDatabaseTopology _topology;
        internal ServiceProvider Provider => _provider!;
        internal Guid InitiatorId { get; } = Guid.CreateVersion7();
        internal CancellationToken CancellationToken => _timeout.Token;
        internal LocalCredentialOperationReceipt Receipt { get; private set; } = null!;
        internal ActivationCommitFaultInterceptor ActivationCommitFault { get; } = new();
        internal ActivationWriteFaultInterceptor ActivationWriteFault { get; } = new();

        internal static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology)
        {
            await LocalIdentitySqliteTemplate.InitializeAsync();
            var fixture = new Fixture { _topology = topology };
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

        internal ExploreDbContext Application(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        internal DbContext Identity(AsyncServiceScope scope) => _topology == IdentityDatabaseTopology.External
            ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>() : Application(scope);
        internal LocalIdentityCredentialStateStore StateStore(AsyncServiceScope scope) => new(
            identityDbContext: Identity(scope), applicationDbContext: Application(scope),
            userManager: scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), timeProvider: TimeProvider.System,
            identityFence: NativeIdentityRecognitionBoundary.For(Application(scope), TimeProvider.System));

        internal async Task<LocalIdentityUser> ReadIdentityUserAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await Identity(scope).Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(user => user.Id == Receipt.LocalSubjectId, CancellationToken);
        }

        internal async Task<LocalIdentityCredentialOperation> ReadOperationAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await Identity(scope).Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleAsync(operation => operation.Id == Receipt.OperationId, CancellationToken);
        }

        internal async Task<string?> ReadTokenValueAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await Identity(scope).Set<IdentityUserToken<Guid>>().AsNoTracking()
                .Where(token => token.UserId == Receipt.LocalSubjectId
                    && token.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && token.Name == LocalCredentialStateMetadata.TokenName)
                .Select(token => token.Value).SingleAsync(CancellationToken);
        }

        internal async Task<LocalCredentialActivationOutcome> ActivateAsync(LocalCredentialActivationRequest request)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await StateStore(scope).ActivateChangeRequiredAsync(request, CancellationToken);
        }

        internal async Task<LocalCredentialActivationOutcome[]> ActivateConcurrentlyAsync(LocalCredentialActivationRequest request)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LocalCredentialActivationOutcome> Run(TaskCompletionSource ready) => Task.Run(async () =>
            {
                await using AsyncServiceScope scope = Provider.CreateAsyncScope();
                LocalIdentityCredentialStateStore store = StateStore(scope);
                ready.SetResult();
                await start.Task.WaitAsync(CancellationToken);
                return await store.ActivateChangeRequiredAsync(request, CancellationToken);
            }, CancellationToken);
            Task<LocalCredentialActivationOutcome> first = Run(firstReady);
            Task<LocalCredentialActivationOutcome> second = Run(secondReady);
            await Task.WhenAll(firstReady.Task, secondReady.Task).WaitAsync(CancellationToken);
            start.SetResult();
            return await Task.WhenAll(first, second).WaitAsync(CancellationToken);
        }

        internal async Task AssertStateAsync(LocalCredentialState expected)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            LocalCredentialOperationStatus? status = await StateStore(scope)
                .ReadOperationAsync(Receipt.OperationId, CancellationToken);
            await Assert.That(status).IsNotNull();
            await Assert.That(status!.IsCurrent).IsTrue();
            await Assert.That(status.CredentialState).IsEqualTo(expected);
            await Assert.That(status.Receipt.OperationId).IsEqualTo(Receipt.OperationId);
            await Assert.That(status.Receipt.ApplicationUserId).IsEqualTo(Receipt.LocalSubjectId);
        }

        internal async Task AssertNoGraphAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            ExploreDbContext application = Application(scope);
            await Assert.That(await application.Users.AnyAsync(user => user.Id == Receipt.LocalSubjectId, CancellationToken)).IsFalse();
            await Assert.That(await application.Actors.AnyAsync(actor => actor.Id == Receipt.PersonalActorId, CancellationToken)).IsFalse();
            await Assert.That(await application.UserExternalLogins.AnyAsync(login => login.Id == Receipt.ExternalLoginId, CancellationToken)).IsFalse();
        }

        internal async Task AssertExactGraphAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            ExploreDbContext application = Application(scope);
            User user = await application.Users.SingleAsync(row => row.Id == Receipt.LocalSubjectId, CancellationToken);
            Actor actor = await application.Actors.SingleAsync(row => row.Id == Receipt.PersonalActorId, CancellationToken);
            UserExternalLogin login = await application.UserExternalLogins.SingleAsync(row => row.Id == Receipt.ExternalLoginId, CancellationToken);
            LocalIdentityUser identity = await Identity(scope).Set<LocalIdentityUser>().SingleAsync(row => row.Id == Receipt.LocalSubjectId, CancellationToken);
            await Assert.That(user.Email).IsEqualTo(identity.Email);
            await Assert.That(user.EmailVerified).IsEqualTo(true);
            await Assert.That(user.IsDeleted).IsFalse();
            await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.User);
            await Assert.That(actor.UserId).IsEqualTo(user.Id);
            await Assert.That(actor.IsDeleted || actor.IsSuspended).IsFalse();
            await Assert.That(login.UserId).IsEqualTo(user.Id);
            await Assert.That(login.AuthenticationProviderId).IsEqualTo((int)AuthenticationProviderKind.Local);
            await Assert.That(login.ProviderKey).IsEqualTo(Receipt.LocalSubjectId.ToString("D"));
            await Assert.That(await application.Actors.CountAsync(row => row.UserId == user.Id, CancellationToken)).IsEqualTo(1);
            await Assert.That(await application.UserExternalLogins.CountAsync(row => row.UserId == user.Id, CancellationToken)).IsEqualTo(1);
        }

        internal async Task SeedExactGraphAsync()
        {
            LocalIdentityUser credential = await ReadIdentityUserAsync();
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            ExploreDbContext application = Application(scope);
            var user = new User
            {
                Id = Receipt.LocalSubjectId,
                Pii = new UserPii { Email = credential.Email!, FirstName = credential.FirstName, LastName = credential.LastName },
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow
            };
            application.Actors.Add(new Actor
            {
                Id = Receipt.PersonalActorId,
                UserId = user.Id,
                User = user,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Local credential owner" },
                CreatedAt = DateTime.UtcNow
            });
            application.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Receipt.ExternalLoginId,
                UserId = user.Id,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                AuthenticationProvider = null!,
                ProviderKey = Receipt.LocalSubjectId.ToString("D"),
                CreatedAt = DateTime.UtcNow
            });
            await application.SaveChangesAsync(CancellationToken);
        }

        private async Task InitializeAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ExploreDbContext>(options => Configure(options, _applicationPath));
            IdentityBuilder identity = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>();
            if (_topology == IdentityDatabaseTopology.External)
            {
                services.AddDbContext<ExternalIdentityDbContext>(options => Configure(options, _identityPath));
                identity.AddEntityFrameworkStores<ExternalIdentityDbContext>();
            }
            else identity.AddEntityFrameworkStores<ExploreDbContext>();
            _provider = services.BuildIsolatedServiceProvider();
            await LocalIdentitySqliteTemplate.CopyAsync(_applicationPath,
                _topology == IdentityDatabaseTopology.External ? _identityPath : null, seedLookups: true, CancellationToken);
            await using (AsyncServiceScope seed = Provider.CreateAsyncScope())
            {
                ExploreDbContext application = Application(seed);
                var initiator = new User
                {
                    Id = InitiatorId,
                    Pii = new UserPii { Email = $"admin-{InitiatorId:N}@example.test", FirstName = "Instance", LastName = "Administrator" },
                    EmailVerified = true,
                    CreatedAt = DateTime.UtcNow
                };
                Role role = await application.Set<Role>().SingleAsync(row => row.MasterCode == "platform.admin", CancellationToken);
                application.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = initiator.Id,
                    User = initiator,
                    RoleId = role.Id,
                    Role = role,
                    GrantedAt = DateTime.UtcNow
                });
                await application.SaveChangesAsync(CancellationToken);
            }
            await using AsyncServiceScope create = Provider.CreateAsyncScope();
            LocalCredentialCreateResult result = await StateStore(create).CreatePendingAsync(new LocalCredentialCreateRequest(
                operationId: Guid.CreateVersion7(), initiatingApplicationUserId: InitiatorId,
                email: $"binding-{Guid.CreateVersion7():N}@example.test", firstName: "Credential", lastName: "Owner"), CancellationToken);
            await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            Receipt = result.Receipt!;
        }

        private void Configure(DbContextOptionsBuilder options, string path) => options
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .UseMemoryCache(_metadataCache)
            .UseSnakeCaseNamingConvention().AddInterceptors(
                SqliteNamedLockTransactionInterceptor.Instance, ActivationCommitFault, ActivationWriteFault);

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_provider is not null) await _provider.DisposeAsync();
            }
            finally
            {
                _metadataCache.Dispose();
                foreach (string path in new[] { _applicationPath, _identityPath })
                {
                    File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm");
                }
                _timeout.Dispose();
            }
        }
    }

    private sealed class InjectedBindingFailure : Exception { }

    private sealed class ActivationCommitFaultInterceptor : DbTransactionInterceptor
    {
        internal bool LoseNextAcknowledgement { get; set; }
        internal bool LostAcknowledgementObserved { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (LoseNextAcknowledgement)
            {
                LoseNextAcknowledgement = false;
                LostAcknowledgementObserved = true;
                throw new InjectedBindingFailure();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ActivationWriteFaultInterceptor : DbCommandInterceptor
    {
        private DbContext? _identity;
        private string? _table;
        internal bool Observed { get; private set; }
        internal int AffectedRows { get; private set; }

        internal void Arm(DbContext identity, string table)
        {
            _identity = identity;
            _table = table;
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _identity) && _table is not null
                && command.CommandText.TrimStart().StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(_table, StringComparison.Ordinal))
            {
                _table = null;
                Observed = true;
                AffectedRows = result;
                throw new InjectedBindingFailure();
            }
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
