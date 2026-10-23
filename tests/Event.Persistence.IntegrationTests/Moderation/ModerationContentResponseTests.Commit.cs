using System.Data;
using System.Data.Common;
using Explore.Application.Configuration;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentResponseTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Adopted_primary_final_disclosure_flush_revalidates_response_before_authority_commit(
        bool expireAtFlush, bool atOriginalCeiling)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var checkpoint = new ResponseCommitCheckpoint(fixture, expireAtFlush, atOriginalCeiling);
        var privateReads = new RejectExpiredOriginalPrivateSelection(fixture);
        using var original = fixture.Database.Context();
        var options = (DbContextOptions<ExploreDbContext>)original.GetService<IDbContextOptions>();
        var primary = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .AddInterceptors(new ResponseSaved(checkpoint), checkpoint, privateReads).Options)
        { TenantContext = fixture.Database };
        var authorityOptions = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
            .UseSqlite(primary.Database.GetDbConnection()).UseSnakeCaseNamingConvention().Options;
        await using (var schema = new EmbeddedPrivacyErasureAuthorityDbContext(authorityOptions))
            await schema.Database.ExecuteSqlRawAsync(schema.Database.GenerateCreateScript());
        var observedOptions = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>(authorityOptions)
            .AddInterceptors(new ResponseAuthorityCommit(checkpoint)).Options;
        var authority = new EmbeddedPrivacyErasureAuthorityRepository(new ResponseAuthorityFactory(observedOptions),
            fixture.Clock, Options.Create(new PrivacyErasureOptions()), applicationContext: primary);
        var command = Command(fixture);
        await using (var session = new ResponseSession(fixture, fixture.Database.SubjectId, primary, authority))
        {
            var result = await session.Handler.ExecuteAsync(command);
            await Assert.That(result.IsSuccess).IsEqualTo(!expireAtFlush);
            await Assert.That(primary.Database.CurrentTransaction).IsNull();
            await Assert.That(primary.ChangeTracker.HasChanges()).IsFalse();
        }
        await Assert.That(checkpoint.ResponseSaved).IsTrue();
        await Assert.That(checkpoint.FlushCount).IsEqualTo(1);
        await Assert.That(checkpoint.Committed).IsEqualTo(!expireAtFlush);
        await Assert.That(privateReads.ExpiredPrivateSelects).IsEqualTo(0);
        if (atOriginalCeiling) await Assert.That(privateReads.ExpiredMetadataReads).IsGreaterThan(0);
        await using var inspect = Read(fixture);
        await Assert.That(await inspect.Set<ModerationContentResponse>().AnyAsync()).IsEqualTo(!expireAtFlush);
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
        await Assert.That((await inspect.Events.SingleAsync()).Title == ResponseChangedTitle).IsEqualTo(!expireAtFlush);
    }

    private const string ResponseChangedTitle = "Response transaction disclosure dependency";

    private sealed class ResponseAuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }

    private sealed class ResponseSaved(ResponseCommitCheckpoint checkpoint) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var primary = (ExploreDbContext)eventData.Context!;
            if (checkpoint.ResponseSaved || !primary.ChangeTracker.Entries<ModerationContentResponse>().Any()) return result;
            checkpoint.ResponseSaved = true;
            checkpoint.ResponseTransaction = primary.Database.CurrentTransaction!.GetDbTransaction();
            if (!primary.IdentityFenceOwnsTransaction
                || !ReferenceEquals(checkpoint.AuthorityTransaction, checkpoint.ResponseTransaction))
                throw new InvalidOperationException("The native retained authority must own the response transaction.");
            (await primary.Events.SingleAsync(cancellationToken)).Title = ResponseChangedTitle;
            await primary.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    private sealed class ResponseCommitCheckpoint(
        NoticeDatabase fixture, bool expireAtFlush, bool atOriginalCeiling) : DbCommandInterceptor
    {
        public bool ResponseSaved { get; set; }
        public DbTransaction? AuthorityTransaction { get; set; }
        public DbTransaction? ResponseTransaction { get; set; }
        public int FlushCount { get; private set; }
        public bool Committed { get; set; }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ResponseSaved || eventData.Context is not ExploreDbContext primary) return ValueTask.FromResult(result);
            var entity = primary.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = primary.GetService<ISqlGenerationHelper>();
            var column = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoveryRevision.DisclosureEpoch))!
                .GetColumnName(table)!);
            if (command.Parameters.Count == 3 && command.CommandText.StartsWith(
                $"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {column} = ", StringComparison.Ordinal))
            {
                if (result != 1 || !ReferenceEquals(command.Transaction, ResponseTransaction)
                    || !primary.IdentityFenceOwnsTransaction || Committed)
                    throw new InvalidOperationException("The native final flush must precede the owning authority commit.");
                FlushCount++;
                if (expireAtFlush)
                    fixture.Clock.UtcNow = atOriginalCeiling ? fixture.Approval.RetainUntil : OriginalAt.AddDays(1);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ResponseAuthorityCommit(ResponseCommitCheckpoint checkpoint) : DbTransactionInterceptor
    {
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not EmbeddedPrivacyErasureAuthorityDbContext
                || result.IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Response custody requires native serializable retained authority.");
            checkpoint.AuthorityTransaction = result;
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(transaction, checkpoint.ResponseTransaction))
                throw new InvalidOperationException("The final authority commit must include the actual response write.");
            checkpoint.Committed = true;
            return Task.CompletedTask;
        }
    }
}
