using System.Data;
using System.Security.Claims;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationRecoveryStoreStatePersistenceTests
{
    [Test]
    public async Task Native_case_commit_advances_the_store_but_identical_operation_replay_does_not()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var before = await ReadAsync(database);
        var command = database.OpenCase();
        var first = await database.ExecuteAsync(database.AdministratorId, command);
        var committed = await ReadAsync(database);
        var replay = await database.ExecuteAsync(database.AdministratorId, command);
        var repeated = await ReadAsync(database);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(committed.Id).IsEqualTo(before.Id);
        await Assert.That(committed.Generation).IsEqualTo(before.Generation);
        await Assert.That(committed.Position).IsEqualTo(before.Position + 1);
        await Assert.That(repeated).IsEqualTo(committed);
    }

    [Test]
    public async Task Denied_native_case_operation_does_not_advance_the_store()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var before = await ReadAsync(database);
        await Assert.That((await database.ExecuteAsync(database.SubjectId, database.OpenCase())).IsSuccess).IsFalse();
        await Assert.That(await ReadAsync(database)).IsEqualTo(before);
    }

    [Test]
    public async Task A_rolled_back_case_flush_cannot_advance_the_committed_store_position()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var before = await ReadAsync(database);
        var id = Guid.CreateVersion7();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var allegation = ModerationCase.Open(id,
                SubjectReference.Create(ModerationSubjectKind.Human, database.SubjectId, database.Scope),
                database.Scope, Guid.CreateVersion7(), new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
            writer.Add(allegation);
            await writer.SaveChangesAsync();
            var staged = await writer.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
            await Assert.That(staged.Position).IsEqualTo(before.Position + 1);
            await transaction.RollbackAsync();
        }
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationCases.AnyAsync(row => row.Id == id)).IsFalse();
        await Assert.That(await ReadAsync(database)).IsEqualTo(before);
    }

    [Test]
    public async Task A_caller_cannot_install_or_advance_a_recovery_head_as_ordinary_tracked_work()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var before = await ReadAsync(database);
        await using var writer = database.Context();
        var state = await writer.Set<ModerationRecoveryStoreState>().SingleAsync();
        state.Advance(state.Revision, state.Position);
        await Assert.That(async () => await writer.SaveChangesAsync()).Throws<InvalidOperationException>();
        writer.ChangeTracker.Clear();
        writer.Add(ModerationRecoveryStoreState.Start(Guid.CreateVersion7()));
        await Assert.That(async () => await writer.SaveChangesAsync()).Throws<InvalidOperationException>();
        await Assert.That(await ReadAsync(database)).IsEqualTo(before);
    }

    private static async Task<(Guid Id, long Revision, long Generation, long Position)> ReadAsync(SourceDatabase database)
    {
        await using var reader = database.Context();
        var state = await reader.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
        state.ValidatePersistedState();
        return (state.Id, state.Revision, state.Generation, state.Position);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_flushed_canonical_mutation_is_not_a_committed_recovery_requirement(bool separateCommittedReader)
    {
        var (database, _) = await ModerationResourceTargetAdoptionWriterTests.CreateAsync();
        await using var databaseOwner = database;
        var path = Path.Combine(Path.GetTempPath(), $"moderation-recovery-{Guid.CreateVersion7():N}.db");
        try
        {
            await using var fileConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Pooling = false
            }.ToString());
            if (separateCommittedReader)
            {
                await fileConnection.OpenAsync();
                await using var fixtureSource = database.Context();
                // Fixture bootstrap precedes authority acquisition. This is not a native
                // operational backup witness or a completed recovery measurement.
                ((SqliteConnection)fixtureSource.Database.GetDbConnection()).BackupDatabase(fileConnection);
            }
            await using var context = separateCommittedReader
                ? new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
                    .UseSqlite(fileConnection).UseSnakeCaseNamingConvention()
                    .AddInterceptors(Explore.Persistence.Database.SqliteNamedLockTransactionInterceptor.Instance).Options)
                    { TenantContext = database }
                : database.Context();
            var clock = new RecoveryClock();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, database.AdministratorId.ToString()),
                            new Claim(ClaimTypes.Role, "platform.admin")], "native-recovery-witness"))
                }
            };
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(http);
            var authorityOwner = new ModerationQualificationAuthority(context, admin, machine);
            var authority = await authorityOwner.AcquireAsync(database.Scope, default);
            await Assert.That(authority).IsNotNull();
            var publication = await context.ModerationPolicyPublications.AsNoTracking().SingleAsync();
            var compiled = ModerationPolicyPublicationProjection.RestoreHistory(publication)!;
            var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
            using var evidence = new ModerationConsumerEvidenceSession(context, transaction, authority!, compiled,
                async token =>
                {
                    var current = await authorityOwner.AcquireAsync(database.Scope, token);
                    return current is not null && current.Binding.Id == authority!.Binding.Id
                        && current.Epoch.Revision == authority.Epoch.Revision;
                }, publications, database.Inventory, null, null, clock, null);
            var before = await context.AcquireModerationRecoveryStateAsync(
                evidence, transaction.TransactionId, clock.GetUtcNow(), default);
            await Assert.That(before).IsNotNull();
            var committed = await context.ReadSqliteCommittedModerationRecoveryStateAsync(
                evidence, transaction.TransactionId, clock.GetUtcNow(), default);
            await Assert.That(committed is not null).IsEqualTo(separateCommittedReader);
            if (committed is not null)
                await Assert.That((committed.Id, committed.Revision, committed.Generation, committed.Position))
                    .IsEqualTo((before!.Id, before.Revision, before.Generation, before.Position));
            await Assert.That(await context.ReadSqliteCommittedModerationRecoveryStateAsync(
                evidence, Guid.CreateVersion7(), clock.GetUtcNow(), default)).IsNull();

            var id = Guid.CreateVersion7();
            context.Add(ModerationCase.Open(id,
                SubjectReference.Create(ModerationSubjectKind.Human, database.SubjectId, database.Scope),
                database.Scope, Guid.CreateVersion7(), clock.GetUtcNow()));
            await context.SaveChangesAsync();
            await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
            await Assert.That(await context.AcquireModerationRecoveryStateAsync(
                evidence, transaction.TransactionId, clock.GetUtcNow(), default)).IsNull();
            await Assert.That(await context.ReadSqliteCommittedModerationRecoveryStateAsync(
                evidence, transaction.TransactionId, clock.GetUtcNow(), default)).IsNull();
            await transaction.RollbackAsync();
            await Assert.That((await context.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync()).Position)
                .IsEqualTo(before!.Position);
            await Assert.That(await context.ModerationCases.AnyAsync(row => row.Id == id)).IsFalse();
            evidence.Dispose();
            await Assert.That(await context.ReadSqliteCommittedModerationRecoveryStateAsync(
                evidence, transaction.TransactionId, clock.GetUtcNow(), default)).IsNull();
        }
        finally
        {
            if (separateCommittedReader)
                foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                    File.Delete(path + suffix);
        }
    }

    private sealed class RecoveryClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
