using System.Data.Common;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentResponseTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Native_response_retry_reacquires_custody_without_duplicate_tracking_or_receipts(bool lostCommit)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var checkpoint = new ResponseRetryCheckpoint(lostCommit);
        using var original = fixture.Database.Context();
        var options = (DbContextOptions<ExploreDbContext>)original.GetService<IDbContextOptions>();
        var context = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .AddInterceptors(new RetryAfterResponseSave(checkpoint), new RetryAfterResponseCommit(checkpoint))
            .ReplaceService<IExecutionStrategyFactory, ResponseRetryingStrategyFactory>().Options)
        { TenantContext = fixture.Database };
        await using var session = new ResponseSession(fixture, fixture.Database.SubjectId, context);
        var command = Command(fixture);
        var accepted = await session.Handler.ExecuteAsync(command);
        await Assert.That(checkpoint.Injected).IsTrue();
        await Assert.That(accepted.IsSuccess).IsTrue();
        await Assert.That(accepted.Id).IsEqualTo(command.OperationId);
        await Assert.That(checkpoint.Saves).IsEqualTo(lostCommit ? 1 : 2);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await using var inspect = Read(fixture);
        var response = await inspect.Set<ModerationContentResponse>().SingleAsync();
        await Assert.That(response.Id).IsEqualTo(command.OperationId);
        await Assert.That(response.AssignmentId).IsEqualTo(command.AssignmentId);
        await Assert.That(response.RepresentativeHumanId).IsEqualTo(fixture.Database.SubjectId);
        await Assert.That(response.RetainUntil).IsEqualTo(fixture.Approval.RetainUntil);
        await Assert.That(response.SubmittedAt).IsEqualTo(fixture.Clock.UtcNow);
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
        var replay = await session.Handler.ExecuteAsync(command);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(response.Id);
        await Assert.That(await inspect.Set<ModerationContentResponse>().CountAsync()).IsEqualTo(1);
    }

    private sealed class ResponseRetryCheckpoint(bool lostCommit)
    {
        public bool LostCommit { get; } = lostCommit;
        public bool Injected { get; set; }
        public int Saves { get; set; }
    }

    private sealed class RetryAfterResponseSave(ResponseRetryCheckpoint checkpoint) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext primary
                || !primary.ChangeTracker.Entries<ModerationContentResponse>().Any())
                return ValueTask.FromResult(result);
            checkpoint.Saves++;
            if (!checkpoint.LostCommit && !checkpoint.Injected)
            {
                checkpoint.Injected = true;
                // The real INSERT completed; its owning transaction must roll back. The next
                // full custody attempt must discard the first attempt's tracked entity.
                throw new SqliteException("Injected transient failure after native response save.", 5);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RetryAfterResponseCommit(ResponseRetryCheckpoint checkpoint) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (checkpoint.LostCommit && checkpoint.Saves > 0 && !checkpoint.Injected)
            {
                checkpoint.Injected = true;
                // The actual commit succeeded. Retry must return the same native receipt,
                // rather than rewrite text or create a second response.
                throw new SqliteException("Injected lost native response commit acknowledgement.", 5);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ResponseRetryingStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new ResponseRetryingStrategy(dependencies);
    }

    private sealed class ResponseRetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is SqliteException;
    }
}
