using System.Data.Common;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Privacy;

[NotInParallel("RetainedIdentityFence")]
public sealed class RetainedIdentityFenceTests
{
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;
    private static ProviderAccountKey Account(string subject = "OpaqueSubject") =>
        PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://identity.example.test/realm", subject);

    [Test]
    public async Task AppendFailureRollsBackBothIntentAndFingerprint()
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid user = await fixture.EnrollAsync(Account());
        await using (var db = fixture.AuthorityFactory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_fence BEFORE INSERT ON ie_identity_fences
                BEGIN SELECT RAISE(ABORT, 'injected_fence_failure'); END;
                """, Token);

        await Assert.That(async () => { await fixture.AppendFingerprintAsync(Request(user)); })
            .Throws<DbUpdateException>();

        await Assert.That((await fixture.Authority.GetStateAsync(Token)).HighWaterSequence).IsEqualTo(0);
        await using var verification = fixture.AuthorityFactory.CreateDbContext();
        await Assert.That(await verification.ErasureIntents.CountAsync(Token)).IsEqualTo(0);
        await Assert.That(await verification.Set<PrivacyErasureIdentityFence>().CountAsync(Token)).IsEqualTo(0);
        await Assert.That(await fixture.Primary.UserExternalLogins.CountAsync(Token)).IsEqualTo(1);
    }

    [Test]
    public async Task ErasedIdentityCanEnrollFreshUuidAndSurviveRestartReplay()
    {
        await using var fixture = await Fixture.CreateAsync();
        ProviderAccountKey account = Account();
        Guid original = await fixture.EnrollAsync(account);
        await using (var runtime = fixture.CreateRuntime())
            await runtime.Service.EraseUserAsync(original, Guid.CreateVersion7(), Token);
        fixture.Primary.ChangeTracker.Clear();

        await using ExploreDbContext restarted = fixture.CreatePrimary();
        PrivacyIdentityFenceOperation operation = fixture.CreateOperation(restarted);
        Guid fresh = await fixture.EnrollAsync(account, restarted, operation);
        await Assert.That(fresh).IsNotEqualTo(original);
        await Assert.That(await restarted.Users.CountAsync(Token)).IsEqualTo(1);
        UserExternalLogin login = await restarted.UserExternalLogins.SingleAsync(Token);
        await Assert.That(login.UserId).IsEqualTo(fresh);
        await using var replay = GlobalLocationPrivacyErasureTests.CreateRuntime(restarted, fixture.Authority);
        await replay.ReplayService.ReplayAsync(Token);
        await Assert.That(await restarted.Users.AnyAsync(user => user.Id == fresh, Token)).IsTrue();
        await Assert.That(await restarted.UserPii.AnyAsync(pii => pii.UserId == original, Token)).IsFalse();
        await Assert.That((await fixture.Authority.GetStateAsync(Token)).HighWaterSequence).IsEqualTo(1);
        await Assert.That((await fixture.Authority.ReadAfterAsync(0, 10, Token)).Single().IdentityFences).IsEmpty();
    }

    [Test]
    public async Task KeylessEnrollmentStillRejectsUnavailableAuthorityBeforeApplicationWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.EnrollAsync(Account());
        await using ExploreDbContext other = fixture.CreatePrimary();
        await Assert.That((await fixture.Authority.GetStateAsync(Token)).IdentityKeyId).IsNull();
        await using (var corrupt = fixture.AuthorityFactory.CreateDbContext())
            await corrupt.Database.ExecuteSqlRawAsync("DROP TABLE ie_authority_counter", Token);
        await Assert.That(() => fixture.EnrollAsync(Account("other"), other, fixture.CreateOperation(other)))
            .Throws<SqliteException>();
        await Assert.That(await other.Users.CountAsync(Token)).IsEqualTo(1);
    }

    [Test]
    public async Task CompleteAppendPayloadAndLegalHoldAssociationRemainImmutable()
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid user = await fixture.EnrollAsync(Account());
        PrivacyErasureRequest request = Request(user);
        PrivacyErasureIntent retained = await fixture.AppendFingerprintAsync(request);
        await Assert.That(async () => { await fixture.Authority.AppendAsync(request, Token); })
            .Throws<InvalidOperationException>();
        fixture.Clock.Now = fixture.Clock.Now.Add(fixture.Options.AuthorityRetention).AddTicks(1);
        await fixture.Authority.CompactExpiredIntentsAsync(new(fixture.Clock.Now.UtcDateTime, 100,
            [retained.AuthoritySequence]), Token);

        await using var db = fixture.AuthorityFactory.CreateDbContext();
        PrivacyErasureIntent held = await db.ErasureIntents.Include(intent => intent.IdentityFences).SingleAsync(Token);
        await Assert.That(held.SubjectId).IsNotEqualTo(user);
        await Assert.That(held.IsLegalHoldPseudonymized).IsTrue();
        await Assert.That(held.IdentityFences.Single().AuthoritySequence).IsEqualTo(retained.AuthoritySequence);
        await fixture.Primary.UserExternalLogins.ExecuteDeleteAsync(Token);
        Guid fresh = await fixture.EnrollAsync(Account());
        await Assert.That(fresh).IsNotEqualTo(user);
        await fixture.Authority.CompactExpiredIntentsAsync(new(fixture.Clock.Now.UtcDateTime, 100, []), Token);
        await Assert.That(await db.Set<PrivacyErasureIdentityFence>().CountAsync(Token)).IsEqualTo(0);
        await Assert.That(await fixture.Primary.Users.AnyAsync(value => value.Id == fresh, Token)).IsTrue();
    }

    [Test]
    public async Task RetentionCannotReleaseFenceBeforeSupportedBackupHorizon()
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid user = await fixture.EnrollAsync(Account());
        await fixture.AppendFingerprintAsync(Request(user));
        fixture.Clock.Now = fixture.Clock.Now.Add(fixture.Options.MaximumBackupHorizon);
        var result = await fixture.Authority.CompactExpiredIntentsAsync(
            new(fixture.Clock.Now.UtcDateTime, 100, []), Token);
        await Assert.That(result.DeletedCount).IsEqualTo(0);
        await using var retained = fixture.AuthorityFactory.CreateDbContext();
        await Assert.That(await retained.Set<PrivacyErasureIdentityFence>().CountAsync(Token)).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthorityGateSpansApplicationCommitIncludingColocatedSqlite(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        ProviderAccountKey account = Account();
        Guid userId = Guid.CreateVersion7();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Guid> enrollment = fixture.Operation.ExecuteEnrollmentAsync(account,
            token => new EfCoreUnitOfWork(fixture.Primary).ExecuteSerializableAsync(async inner =>
            {
                await fixture.WriteUserAsync(fixture.Primary, account, userId);
                entered.SetResult();
                await commit.Task.WaitAsync(TimeSpan.FromSeconds(10), inner);
                await fixture.Operation.AfterEnrollmentCommitAsync(async () =>
                {
                    await using ExploreDbContext committed = fixture.CreatePrimary();
                    await Assert.That(await committed.Users.AnyAsync(user => user.Id == userId, Token)).IsTrue();
                });
                return userId;
            }, token), Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var starting = new TransactionStartingSignal();
        await using ExploreDbContext erasing = fixture.CreatePrimary();
        EmbeddedPrivacyErasureAuthorityRepository eraseAuthority = fixture.CreateAuthority(erasing, starting);
        Task<PrivacyErasureIntent> erasure = Task.Run(
            () => eraseAuthority.AppendAsync(Request(userId), Token), Token);
        await starting.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        commit.SetResult();
        await enrollment.WaitAsync(TimeSpan.FromSeconds(10), Token);
        PrivacyErasureIntent fact = await erasure.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await Assert.That(fact.IdentityFences).IsEmpty();
        await Assert.That(await erasing.Users.AnyAsync(user => user.Id == userId, Token)).IsTrue();
        await Assert.That(() => fixture.Operation.ExecuteEnrollmentAsync(Account("AnotherSubject"), async token =>
        {
            await fixture.Operation.EnsureSubjectMayEnrollAsync(userId, token);
            return true;
        }, Token)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CapturedAppendWinsAgainstSubscribedLateEnrollment()
    {
        await using var fixture = await Fixture.CreateAsync();
        ProviderAccountKey account = Account();
        Guid userId = await fixture.EnrollAsync(account);
        var appended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task erasure = fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            await fixture.Authority.AppendAsync(Request(userId), token);
            appended.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            return true;
        }, Token);
        await appended.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var starting = new TransactionStartingSignal();
        await using ExploreDbContext enrolling = fixture.CreatePrimary();
        PrivacyIdentityFenceOperation operation = fixture.CreateOperation(enrolling, interceptor: starting);
        Task<Guid> enrollment = Task.Run(() => operation.ExecuteEnrollmentAsync(
            Account("AnotherSubject"), async token =>
            {
                await operation.EnsureSubjectMayEnrollAsync(userId, token);
                await fixture.WriteUserAsync(enrolling, Account("AnotherSubject"), Guid.CreateVersion7());
                return userId;
            }, Token), Token);
        await starting.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        release.SetResult();
        await erasure.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await Assert.That(async () => await enrollment.WaitAsync(TimeSpan.FromSeconds(10), Token))
            .Throws<InvalidOperationException>();
        await Assert.That(await enrolling.Users.CountAsync(Token)).IsEqualTo(1);
    }

    [Test]
    public async Task ColocatedHandlerTranslatedFailureCannotCommitPartialEnrollment()
    {
        await using var fixture = await Fixture.CreateAsync(colocated: true);
        await Assert.That(() => fixture.Operation.ExecuteEnrollmentAsync(Account(), async token =>
        {
            try
            {
                return await new EfCoreUnitOfWork(fixture.Primary).ExecuteSerializableAsync<bool>(async _ =>
                {
                    await fixture.WriteUserAsync(fixture.Primary, Account(), Guid.CreateVersion7());
                    throw new InvalidOperationException("injected_application_failure");
                }, token);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }, Token)).Throws<InvalidOperationException>();
        await Assert.That(await fixture.Primary.Users.CountAsync(Token)).IsEqualTo(0);
        await Assert.That(await fixture.Primary.UserExternalLogins.CountAsync(Token)).IsEqualTo(0);
    }

    [Test]
    public async Task DifferentIdentityCannotLinkWhilePrimaryUuidFenceLagsRetainedAppend()
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid userId = await fixture.EnrollAsync(Account());
        await fixture.Authority.AppendAsync(Request(userId), Token);
        await Assert.That(await new PrivacyErasureStateRepository(fixture.Primary)
            .GetBySubjectAsync(userId, Token)).IsNull();
        ProviderAccountKey newIdentity = Account("AnotherSubject");

        await Assert.That(() => fixture.Operation.ExecuteEnrollmentAsync(newIdentity, async token =>
        {
            await fixture.Operation.EnsureSubjectMayEnrollAsync(userId, token);
            await new UserExternalLoginRepository(fixture.Primary).Create(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(), UserId = userId, User = null!,
                AuthenticationProviderId = (int)newIdentity.ProviderKind, AuthenticationProvider = null!,
                ProviderKey = newIdentity.Value
            });
            return true;
        }, Token)).Throws<InvalidOperationException>();

        await Assert.That(await fixture.Primary.UserExternalLogins.CountAsync(Token)).IsEqualTo(1);
    }

    private static PrivacyErasureRequest Request(Guid user) => PrivacyErasureRequest.Create(
        Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, user, PrivacyErasureReasonCode.AccountDeletion, 1);

    [Test]
    public async Task FreshRegistrationSurvivesReplayAfterLegalHoldPseudonymization()
    {
        await using var fixture = await Fixture.CreateAsync();
        ProviderAccountKey account = Account();
        Guid original = await fixture.EnrollAsync(account);
        await using var runtime = fixture.CreateRuntime();
        await runtime.Service.EraseUserAsync(original, Guid.CreateVersion7(), Token);
        fixture.Clock.Now = fixture.Clock.Now.Add(fixture.Options.AuthorityRetention).AddTicks(1);
        await fixture.Authority.CompactExpiredIntentsAsync(
            new(fixture.Clock.Now.UtcDateTime, 100, [1L]), Token);
        fixture.Primary.ChangeTracker.Clear();
        Guid fresh = await fixture.EnrollAsync(account);
        fixture.Primary.ChangeTracker.Clear();

        await runtime.ReplayService.ReplayAsync(Token);

        await Assert.That(fresh).IsNotEqualTo(original);
        await Assert.That(await fixture.Primary.UserExternalLogins.AnyAsync(login => login.UserId == fresh, Token)).IsTrue();
        await Assert.That(await fixture.Primary.UserPii.AnyAsync(pii => pii.UserId == fresh, Token)).IsTrue();
        await Assert.That(await fixture.Primary.Users.AnyAsync(user => user.Id == fresh, Token)).IsTrue();
        await Assert.That((await fixture.Authority.GetStateAsync(Token)).HighWaterSequence).IsEqualTo(1);
    }

    private sealed class TransactionStartingSignal : DbTransactionInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class TestKeyProvider : IPrivacyIdentityFenceKeyProvider, IDisposable
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public Task<PrivacyIdentityFenceKey> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PrivacyIdentityFenceKey("test-key", _key));
        public void Dispose() => CryptographicOperations.ZeroMemory(_key);
    }

    private sealed class AuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("identity-fence-");
        private readonly TestKeyProvider _key = new();
        public PrivacyErasureOptions Options { get; } = new();
        public TestClock Clock { get; } = new();
        public ExploreDbContext Primary { get; private set; } = null!;
        public AuthorityFactory AuthorityFactory { get; private set; } = null!;
        public EmbeddedPrivacyErasureAuthorityRepository Authority { get; private set; } = null!;
        public PrivacyIdentityFenceOperation Operation { get; private set; } = null!;
        private string PrimaryPath => Path.Combine(_root.FullName, "primary.db");
        private string AuthorityPath { get; set; } = null!;

        public static async Task<Fixture> CreateAsync(bool colocated = false)
        {
            var fixture = new Fixture();
            fixture.AuthorityPath = colocated ? fixture.PrimaryPath : Path.Combine(fixture._root.FullName, "authority.db");
            await LocalIdentitySqliteTemplate.CopyAsync(fixture.PrimaryPath, null, true, Token);
            fixture.Primary = fixture.CreatePrimary();
            fixture.AuthorityFactory = fixture.CreateAuthorityFactory();
            await using (var authority = fixture.AuthorityFactory.CreateDbContext())
            {
                if (colocated)
                    await authority.Database.ExecuteSqlRawAsync(authority.Database.GenerateCreateScript(), Token);
                else
                    await authority.Database.EnsureCreatedAsync(Token);
            }
            fixture.Authority = new(fixture.AuthorityFactory, fixture.Clock,
                Microsoft.Extensions.Options.Options.Create(fixture.Options), applicationContext: fixture.Primary);
            fixture.Operation = new(fixture.Authority);
            return fixture;
        }

        public ExploreDbContext CreatePrimary() => new(
            TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(
                new SqliteConnectionStringBuilder { DataSource = PrimaryPath, Pooling = false }.ToString())
                .UseSnakeCaseNamingConvention().Options);

        private AuthorityFactory CreateAuthorityFactory(DbTransactionInterceptor? interceptor = null)
        {
            var builder = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = AuthorityPath, Pooling = false, DefaultTimeout = 10
                }.ToString()).UseSnakeCaseNamingConvention();
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            return new(builder.Options);
        }

        public EmbeddedPrivacyErasureAuthorityRepository CreateAuthority(
            ExploreDbContext primary, DbTransactionInterceptor? interceptor = null) =>
            new(CreateAuthorityFactory(interceptor), Clock,
                Microsoft.Extensions.Options.Options.Create(Options), applicationContext: primary);

        public PrivacyIdentityFenceOperation CreateOperation(ExploreDbContext primary,
            DbTransactionInterceptor? interceptor = null) => new(CreateAuthority(primary, interceptor));

        public async Task<PrivacyErasureIntent> AppendFingerprintAsync(PrivacyErasureRequest request)
        {
            using PrivacyIdentityFenceKey key = await _key.ResolveAsync(Token);
            return await Authority.AppendAsync(new PrivacyErasureRequest(
                request.IntentId, request.SubjectKind, request.SubjectId, request.ReasonCode,
                request.PolicyVersion, [key.Fingerprint(Account())], key.KeyId, key.VerificationTag), Token);
        }

        public GlobalLocationPrivacyErasureTests.ErasureRuntime CreateRuntime() =>
            GlobalLocationPrivacyErasureTests.CreateRuntime(Primary, Authority);

        public Task<Guid> EnrollAsync(ProviderAccountKey account, ExploreDbContext? primary = null,
            PrivacyIdentityFenceOperation? operation = null)
        {
            ExploreDbContext context = primary ?? Primary;
            return (operation ?? Operation).ExecuteEnrollmentAsync(account,
                token => new EfCoreUnitOfWork(context).ExecuteSerializableAsync(
                    async _ =>
                    {
                        Guid id = Guid.CreateVersion7();
                        await WriteUserAsync(context, account, id);
                        return id;
                    }, token), Token);
        }

        public async Task WriteUserAsync(ExploreDbContext context, ProviderAccountKey account, Guid id)
        {
            var user = await new UserRepository(context).Create(new User
            {
                Id = id, Pii = new UserPii { Email = "", FirstName = "Fence", LastName = "Test" }
            });
            await new UserExternalLoginRepository(context).Create(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(), UserId = id, User = user,
                AuthenticationProviderId = (int)account.ProviderKind, AuthenticationProvider = null!,
                ProviderKey = account.Value
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Primary.DisposeAsync();
            _key.Dispose();
            _root.Delete(recursive: true);
        }
    }
}
