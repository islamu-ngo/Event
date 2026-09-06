// ABOUTME: Tests tenant-scoped encrypted OAuth session repository tracking and concurrency behavior.
// ABOUTME: Proves the central IConcurrencyAware interceptor rejects stale session writers.

using System.Text;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Secrets;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services.Federation;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class UserAuthenticationTokenRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("0198ac00-0000-7000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("0198ac00-0000-7000-8000-000000000002");

    [Test]
    public async Task ReadAndUpdateQueriesHaveExplicitTrackingIntent()
    {
        var database = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        await using var context = CreateContext(database, root);
        var repository = new UserAuthenticationTokenRepository(context);
        await repository.CreateAtprotoSessionAsync(CreateSession(), CancellationToken.None);
        context.ChangeTracker.Clear();

        var read = await repository.GetAtprotoSessionForReadAsync(
            TenantId, UserId, "atproto", "did:plc:alice", CancellationToken.None);
        var update = await repository.GetAtprotoSessionForUpdateAsync(
            TenantId, UserId, "atproto", "did:plc:alice", CancellationToken.None);

        await Assert.That(read).IsNotNull();
        await Assert.That(context.Entry(read!).State).IsEqualTo(EntityState.Detached);
        await Assert.That(update).IsNotNull();
        await Assert.That(context.Entry(update!).State).IsEqualTo(EntityState.Unchanged);
    }

    [Test]
    public async Task StaleWriterFailsOptimisticConcurrencyAfterCentralStampRotation()
    {
        var database = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        await using (var seedContext = CreateContext(database, root))
        {
            await new UserAuthenticationTokenRepository(seedContext)
                .CreateAtprotoSessionAsync(CreateSession(), CancellationToken.None);
        }

        await using var firstContext = CreateContext(database, root);
        await using var secondContext = CreateContext(database, root);
        var firstRepository = new UserAuthenticationTokenRepository(firstContext);
        var secondRepository = new UserAuthenticationTokenRepository(secondContext);
        var first = (await firstRepository.GetAtprotoSessionForUpdateAsync(
            TenantId, UserId, "atproto", "did:plc:alice", CancellationToken.None))!;
        var second = (await secondRepository.GetAtprotoSessionForUpdateAsync(
            TenantId, UserId, "atproto", "did:plc:alice", CancellationToken.None))!;
        var originalStamp = first.ConcurrencyStamp;
        first.SessionCiphertext = Enumerable.Repeat((byte)2, 29).ToArray();
        second.SessionCiphertext = Enumerable.Repeat((byte)3, 29).ToArray();

        await firstRepository.UpdateAtprotoSessionAsync(first, CancellationToken.None);

        await Assert.That(first.ConcurrencyStamp).IsNotEqualTo(originalStamp);
        await Assert.That(async () =>
                await secondRepository.UpdateAtprotoSessionAsync(second, CancellationToken.None))
            .Throws<DbUpdateConcurrencyException>();
    }

    [Test]
    public async Task ScopedMethodsHonorCallerCancellation()
    {
        var database = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        await using var context = CreateContext(database, root);
        var repository = new UserAuthenticationTokenRepository(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(async () => await repository.CreateAtprotoSessionAsync(
                CreateSession(),
                cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    private static ExploreDbContext CreateContext(string database, InMemoryDatabaseRoot root)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(database, root)
            .Options;
        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Encrypted OAuth session repository test.");
        return context;
    }

    private static UserAuthenticationToken CreateSession() => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = UserId,
        User = null!,
        TenantId = TenantId,
        Tenant = null!,
        Provider = "atproto",
        SubjectDid = "did:plc:alice",
        SessionCiphertext = Enumerable.Repeat((byte)1, 29).ToArray(),
        EncryptionKeyId = "active-key",
        OAuthClientKeyId = "oauth-client-key",
        EnvelopeVersion = 1,
        PdsHost = "https://pds.example/",
        ExpiresAt = DateTime.UtcNow.AddHours(1)
    };
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgreSqlUserAuthenticationTokenRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task EncryptedStoreRoundTripsWithoutPlaintextAndPartitionsSameDidByTenant()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (firstTenant, user) = await SeedScopeAsync(context, "first");
        var (secondTenant, _) = await SeedScopeAsync(context, "second", user);
        var repository = new UserAuthenticationTokenRepository(context);
        var resolver = new StaticSecretResolver(CreateRing());
        var protector = new AtprotoSessionEnvelopeProtector(resolver);
        var firstSession = CreateOAuthSession("first-access-canary", "first-refresh-canary");
        var firstStore = CreateStore(repository, protector, firstTenant.Id, user.Id);

        await firstStore.StoreAsync("did:plc:alice", firstSession, CancellationToken.None);

        var restored = await firstStore.GetAsync("did:plc:alice", CancellationToken.None);
        await Assert.That(restored!.TokenSet.AccessToken).IsEqualTo("first-access-canary");
        var secondStore = CreateStore(repository, protector, secondTenant.Id, user.Id);
        await Assert.That(await secondStore.GetAsync("did:plc:alice", CancellationToken.None)).IsNull();
        await secondStore.StoreAsync(
            "did:plc:alice",
            CreateOAuthSession("second-access-canary", "second-refresh-canary"),
            CancellationToken.None);
        await Assert.That(await context.UserAuthenticationTokens.CountAsync(token =>
            token.Provider == "atproto" && token.SubjectDid == "did:plc:alice")).IsEqualTo(2);

        var duplicate = CreatePersistedSession(firstTenant.Id, user.Id);
        context.ChangeTracker.Clear();
        await Assert.That(async () => await new UserAuthenticationTokenRepository(context)
                .CreateAtprotoSessionAsync(duplicate, CancellationToken.None))
            .Throws<DbUpdateException>();
        context.ChangeTracker.Clear();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT session_ciphertext FROM user_authentication_tokens WHERE tenant_id = @tenant_id",
            connection);
        command.Parameters.AddWithValue("tenant_id", firstTenant.Id);
        var rawCiphertext = (byte[])(await command.ExecuteScalarAsync())!;
        await Assert.That(Contains(rawCiphertext, "first-access-canary")).IsFalse();
        await Assert.That(Contains(rawCiphertext, "first-refresh-canary")).IsFalse();
        await Assert.That(Contains(rawCiphertext, firstSession.DPoPKey.D!)).IsFalse();
    }

    [Test]
    public async Task PostgreSqlRejectsTheSecondWriterWithTheSameConcurrencyStamp()
    {
        await fixture.ResetAsync();
        Guid tenantId;
        Guid userId;
        await using (var seedContext = fixture.CreateDbContext())
        {
            var (tenant, user) = await SeedScopeAsync(seedContext, "concurrency");
            tenantId = tenant.Id;
            userId = user.Id;
            await new UserAuthenticationTokenRepository(seedContext)
                .CreateAtprotoSessionAsync(CreatePersistedSession(tenantId, userId), CancellationToken.None);
        }

        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var firstRepository = new UserAuthenticationTokenRepository(firstContext);
        var secondRepository = new UserAuthenticationTokenRepository(secondContext);
        var first = (await firstRepository.GetAtprotoSessionForUpdateAsync(
            tenantId, userId, "atproto", "did:plc:alice", CancellationToken.None))!;
        var second = (await secondRepository.GetAtprotoSessionForUpdateAsync(
            tenantId, userId, "atproto", "did:plc:alice", CancellationToken.None))!;
        first.SessionCiphertext = Enumerable.Repeat((byte)2, 29).ToArray();
        second.SessionCiphertext = Enumerable.Repeat((byte)3, 29).ToArray();

        await firstRepository.UpdateAtprotoSessionAsync(first, CancellationToken.None);

        await Assert.That(async () =>
                await secondRepository.UpdateAtprotoSessionAsync(second, CancellationToken.None))
            .Throws<DbUpdateConcurrencyException>();
    }

    private static RepositoryBackedOAuthSessionStore CreateStore(
        UserAuthenticationTokenRepository repository,
        AtprotoSessionEnvelopeProtector protector,
        Guid tenantId,
        Guid userId) =>
        new(repository, protector, new(
            tenantId,
            userId,
            Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:alice"),
            new Uri("https://pds.example/"),
            "oauth-client-key"));

    private static async Task<(Tenant Tenant, User User)> SeedScopeAsync(
        ExploreDbContext context,
        string suffix,
        User? existingUser = null)
    {
        var activeStatus = await context.TenantStatuses.SingleAsync(status =>
            status.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"ATProto {suffix}",
            Slug = $"atproto-{suffix}-{Guid.NewGuid():N}"[..32],
            TenantStatusId = activeStatus.Id,
            TenantStatus = activeStatus
        };
        var user = existingUser ?? new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"atproto-{Guid.NewGuid():N}@example.test",
                FirstName = "ATProto",
                LastName = "User"
            }
        };
        context.Tenants.Add(tenant);
        if (existingUser is null)
        {
            context.Users.Add(user);
        }

        await context.SaveChangesAsync();
        return (tenant, user);
    }

    private static OAuthSessionData CreateOAuthSession(string accessToken, string refreshToken)
    {
        using var dpopKey = DPoPKeyPair.Generate();
        return new()
        {
            DPoPKey = dpopKey.ExportKeyPair(),
            AuthMethod = "private_key_jwt",
            TokenSet = new TokenSet
            {
                Issuer = "https://issuer.example/",
                Sub = "did:plc:alice",
                Audience = "https://pds.example/",
                Scope = "atproto transition:generic",
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            },
            ClientId = "https://events.example/oauth/client-metadata.json",
            RedirectUri = "https://events.example/signin-atproto",
            Scope = "atproto transition:generic"
        };
    }

    private static UserAuthenticationToken CreatePersistedSession(Guid tenantId, Guid userId) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        User = null!,
        TenantId = tenantId,
        Tenant = null!,
        Provider = "atproto",
        SubjectDid = "did:plc:alice",
        SessionCiphertext = Enumerable.Repeat((byte)1, 29).ToArray(),
        EncryptionKeyId = "active-key",
        OAuthClientKeyId = "oauth-client-key",
        EnvelopeVersion = 1,
        PdsHost = "https://pds.example/"
    };

    private static string CreateRing()
    {
        var key = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{{\"keys\":[{{\"kid\":\"active-key\",\"k\":\"{key}\",\"status\":\"active\"}}]}}";
    }

    private static bool Contains(byte[] bytes, string value) =>
        bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(value)) >= 0;

    private sealed class StaticSecretResolver(string value) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Resolved(new ResolvedSecret(
                settingKey,
                value,
                SecretSourceType.Infisical,
                SecretScope.Instance,
                null,
                DateTimeOffset.UtcNow)));

        public Task<SecretResolutionResult> ResolveQualifiedAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            string qualifier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(
            Guid tenantId,
            Guid bindingId,
            CancellationToken cancellationToken = default) =>
            ResolveAsync(bindingId.ToString("N"), tenantId, cancellationToken);

        public Task InvalidateAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
