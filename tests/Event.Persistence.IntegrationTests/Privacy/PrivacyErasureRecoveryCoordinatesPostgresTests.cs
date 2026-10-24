using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<ExternalDatabasePrivacyErasurePostgreSqlFixture>(Shared = SharedType.None)]
[NotInParallel("PersistenceDb")]
public sealed class PrivacyErasureRecoveryCoordinatesPostgresTests(
    ExternalDatabasePrivacyErasurePostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(240_000)]
    public async Task RecoveryTupleIsProtectedByTheActualCounterRowLock(bool colocated)
    {
        var (context, authority) = await CreateOwnerAsync(colocated);
        await using var ownedContext = context;
        var identity = (IPrivacyIdentityFenceAuthority)authority;
        var recovery = (IPrivacyErasureRecoveryAuthority)authority;
        await Assert.That(() => recovery.AcquireRecoveryStateAsync(Token))
            .Throws<InvalidOperationException>();
        await identity.ExecuteSerializedAsync(_ => Task.FromResult(true), Token);
        PrivacyErasureRecoveryState original = await identity.ExecuteSerializedAsync(async token =>
        {
            PrivacyErasureRecoveryState acquired = await recovery.AcquireRecoveryStateAsync(token);
            await using PrivacyErasureAuthorityDbContext observer = fixture.CreateAuthorityAdminDbContext();
            await observer.Database.OpenConnectionAsync(token);
            var entity = context.Model.FindEntityType(typeof(PrivacyErasureCounter))!;
            using var identifiers = new NpgsqlCommandBuilder();
            string table = identifiers.QuoteIdentifier(entity.GetSchema()!) + "."
                + identifiers.QuoteIdentifier(entity.GetTableName()!);
            await using var competing = new NpgsqlCommand(
                $"SELECT singleton FROM {table} WHERE singleton FOR UPDATE NOWAIT",
                (NpgsqlConnection)observer.Database.GetDbConnection());

            PostgresException? blocked = await Assert.That(async () =>
                await competing.ExecuteScalarAsync(token)).Throws<PostgresException>();

            await Assert.That(blocked!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            await Assert.That(await recovery.AcquireRecoveryStateAsync(token)).IsEqualTo(acquired);
            return acquired;
        }, Token);
        await Assert.That(original.StoreId.Version).IsEqualTo(7);
        await Assert.That(await identity.ExecuteSerializedAsync(recovery.AcquireRecoveryStateAsync, Token))
            .IsEqualTo(original);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(240_000)]
    public async Task UncommittedAppendCannotAdvancePublishedCoordinatesAndReplayDoesNotAdvanceRevision(bool colocated)
    {
        var (context, authority) = await CreateOwnerAsync(colocated);
        await using var ownedContext = context;
        var identity = (IPrivacyIdentityFenceAuthority)authority;
        var recovery = (IPrivacyErasureRecoveryAuthority)authority;
        await identity.ExecuteSerializedAsync(_ => Task.FromResult(true), Token);
        PrivacyErasureRecoveryState original = await identity.ExecuteSerializedAsync(
            recovery.AcquireRecoveryStateAsync, Token);
        var request = new PrivacyErasureRequest(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1);

        await Assert.That(() => identity.ExecuteSerializedAsync(async token =>
        {
            await authority.AppendAsync(request, token);
            return await recovery.AcquireRecoveryStateAsync(token);
        }, Token)).Throws<InvalidOperationException>();

        await Assert.That(await identity.ExecuteSerializedAsync(recovery.AcquireRecoveryStateAsync, Token))
            .IsEqualTo(original);
        await authority.AppendAsync(request, Token);
        PrivacyErasureRecoveryState committed = await identity.ExecuteSerializedAsync(
            recovery.AcquireRecoveryStateAsync, Token);
        await Assert.That(committed.StoreId).IsEqualTo(original.StoreId);
        await Assert.That(committed.Revision).IsEqualTo(original.Revision + 1);
        await Assert.That(committed.RecoveryGeneration).IsEqualTo(original.RecoveryGeneration);
        await Assert.That(committed.AuthorityState.HighWaterSequence)
            .IsEqualTo(original.AuthorityState.HighWaterSequence + 1);
        await authority.AppendAsync(request, Token);
        await Assert.That(await identity.ExecuteSerializedAsync(recovery.AcquireRecoveryStateAsync, Token))
            .IsEqualTo(committed);
    }

    [Test]
    [Timeout(240_000)]
    public async Task ExternalRuntimeCannotRewriteStoreIdentityRevisionOrRecoveryGeneration()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityDbContext();
        var authority = new EfCorePrivacyErasureAuthorityRepository(
            context, Options.Create(new PrivacyErasureOptions()));
        PrivacyErasureRecoveryState original = await authority.ExecuteSerializedAsync(
            authority.AcquireRecoveryStateAsync, Token);

        PostgresException? denied = await Assert.That(async () =>
            await context.Database.ExecuteSqlRawAsync("""
                UPDATE privacy_erasure_authority.authority_counter
                SET store_id = uuidv7(), revision = revision + 1, recovery_generation = recovery_generation + 1
                """, Token)).Throws<PostgresException>();

        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(await authority.ExecuteSerializedAsync(authority.AcquireRecoveryStateAsync, Token))
            .IsEqualTo(original);
    }

    private async Task<(DbContext Context, IPrivacyErasureAuthority Authority)> CreateOwnerAsync(bool colocated)
    {
        if (!colocated)
        {
            PrivacyErasureAuthorityDbContext external = fixture.CreateAuthorityDbContext();
            return (external, new EfCorePrivacyErasureAuthorityRepository(
                external, Options.Create(new PrivacyErasureOptions())));
        }

        await using PrivacyErasureAuthorityDbContext admin = fixture.CreateAuthorityAdminDbContext();
        var options = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>()
            .UseNpgsql(admin.Database.GetConnectionString())
            .UseSnakeCaseNamingConvention().Options;
        var context = new CoLocatedPrivacyErasureAuthorityDbContext(options);
        try
        {
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), Token);
            return (context, new CoLocatedPostgresPrivacyErasureAuthorityRepository(
                context, TimeProvider.System, Options.Create(new PrivacyErasureOptions())));
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    [Test]
    [Timeout(240_000)]
    public async Task ExternalKeyBindingAndHoldReleaseAdvanceOnlyTheirCommittedRevision()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityAdminDbContext();
        var authority = new EfCorePrivacyErasureAuthorityRepository(
            context, Options.Create(new PrivacyErasureOptions()));
        PrivacyErasureRecoveryState initial = await authority.ExecuteSerializedAsync(
            authority.AcquireRecoveryStateAsync, Token);
        using var keyProvider = new RetainedIdentityFenceTests.TestKeyProvider();
        using PrivacyIdentityFenceKey key = await keyProvider.ResolveAsync(Token);
        await Assert.That(() => authority.ExecuteSerializedAsync(async token =>
        {
            await authority.ValidateKeyAsync(key.KeyId, key.VerificationTag, token);
            return await authority.AcquireRecoveryStateAsync(token);
        }, Token)).Throws<InvalidOperationException>();
        await Assert.That(await authority.ExecuteSerializedAsync(authority.AcquireRecoveryStateAsync, Token))
            .IsEqualTo(initial);
        await authority.ExecuteSerializedAsync(async token =>
        {
            await authority.ValidateKeyAsync(key.KeyId, key.VerificationTag, token);
            return true;
        }, Token);
        PrivacyErasureRecoveryState bound = await authority.ExecuteSerializedAsync(
            authority.AcquireRecoveryStateAsync, Token);
        await Assert.That(bound.Revision).IsEqualTo(initial.Revision + 1);
        await authority.ExecuteSerializedAsync(async token =>
        {
            await authority.ValidateKeyAsync(key.KeyId, key.VerificationTag, token);
            return true;
        }, Token);
        await Assert.That(await authority.ExecuteSerializedAsync(authority.AcquireRecoveryStateAsync, Token))
            .IsEqualTo(bound);

        PrivacyErasureIntent fact = await authority.AppendAsync(new PrivacyErasureRequest(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion, 1), Token);
        DateTime asOf;
        await using (var expiry = await context.Database.BeginTransactionAsync(Token))
        {
            asOf = await context.Database.SqlQueryRaw<DateTime>(
                "SELECT statement_timestamp() AS \"Value\"").SingleAsync(Token);
            await context.Database.ExecuteSqlRawAsync("""
                SELECT set_config('privacy_erasure_authority.maintenance', 'on', true);
                UPDATE privacy_erasure_authority.erasure_intents
                SET requested_at_utc = {0}, recorded_at_utc = {0}, retention_expires_at_utc = {1}
                WHERE authority_sequence = {2};
                """, [asOf.AddDays(-2), asOf.AddDays(-1), fact.AuthoritySequence], Token);
            await expiry.CommitAsync(Token);
        }
        PrivacyErasureRecoveryState beforeCompaction = await authority.ExecuteSerializedAsync(
            authority.AcquireRecoveryStateAsync, Token);
        var heldRequest = new PrivacyErasureRetentionRequest(asOf, 100, [fact.AuthoritySequence]);
        await Assert.That(() => authority.ExecuteSerializedAsync(async token =>
        {
            await authority.CompactExpiredIntentsAsync(heldRequest, token);
            return await authority.AcquireRecoveryStateAsync(token);
        }, Token)).Throws<InvalidOperationException>();
        await Assert.That(await authority.ExecuteSerializedAsync(authority.AcquireRecoveryStateAsync, Token))
            .IsEqualTo(beforeCompaction);

        await authority.CompactExpiredIntentsAsync(heldRequest, Token);
        PrivacyErasureRecoveryState held = await authority.ExecuteSerializedAsync(
            authority.AcquireRecoveryStateAsync, Token);
        await authority.CompactExpiredIntentsAsync(new(asOf, 100, []), Token);
        PrivacyErasureRecoveryState released = await authority.ExecuteSerializedAsync(
            authority.AcquireRecoveryStateAsync, Token);
        await Assert.That(held.Revision).IsEqualTo(beforeCompaction.Revision + 1);
        await Assert.That(released.Revision).IsEqualTo(held.Revision + 1);
        await Assert.That(released.AuthorityState).IsEqualTo(held.AuthorityState);
        await Assert.That(released.StoreId).IsEqualTo(initial.StoreId);
    }
}
