// ABOUTME: Deterministic non-Docker tests for unit-of-work retry and rollback exception safety.
// ABOUTME: Forces an EF execution-strategy retry and verifies failed-attempt tracking is discarded.

using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.UnitOfWork;

public sealed class EfCoreUnitOfWorkRetryTests
{
    [Test]
    public async Task ExecuteInTransactionAsync_WhenExecutionStrategyRetries_RetainsExactlyOneAuditWrite()
    {
        await using var context = CreateContext();
        var transaction = new TestDbContextTransaction();
        var strategy = new RetryOnceExecutionStrategy(context);
        var unitOfWork = new EfCoreUnitOfWork(context, () => strategy, _ => Task.FromResult<IDbContextTransaction>(transaction));
        var attempts = 0;

        await unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            attempts++;
            context.TenantLifecycleLogs.Add(CreateLifecycleLog());
            if (attempts == 1)
            {
                throw new TestTransientException();
            }

            return Task.CompletedTask;
        });

        var retainedAudit = context.ChangeTracker.Entries<TenantLifecycleLog>().Single();
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(retainedAudit.State).IsEqualTo(EntityState.Added);
    }

    [Test]
    public async Task ExecuteInTransactionAsync_WhenCallerTokenIsCancelled_PreservesOriginalOperationException()
    {
        await using var context = CreateContext();
        var transaction = new TestDbContextTransaction();
        var strategy = new RetryOnceExecutionStrategy(context);
        var unitOfWork = new EfCoreUnitOfWork(context, () => strategy, _ => Task.FromResult<IDbContextTransaction>(transaction));
        using var cancellation = new CancellationTokenSource();
        var originalException = new InvalidOperationException("Original operation failure.");
        Exception? caught = null;

        try
        {
            await unitOfWork.ExecuteInTransactionAsync<object?>(_ =>
            {
                cancellation.Cancel();
                throw originalException;
            }, cancellation.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsSameReferenceAs(originalException);
        await Assert.That(transaction.RollbackTokenWasCancelled).IsFalse();
    }

    [Test]
    public async Task ExecuteInTransactionAsync_WhenRollbackThrowsDatabaseException_ClearsTrackingAndPreservesOriginalException()
    {
        await using var context = CreateContext();
        var transaction = new TestDbContextTransaction(new TestRollbackDbException());
        var strategy = new RetryOnceExecutionStrategy(context);
        var unitOfWork = new EfCoreUnitOfWork(context, () => strategy, _ => Task.FromResult<IDbContextTransaction>(transaction));
        var originalException = new InvalidOperationException("Original operation failure.");
        var attempts = 0;
        Exception? caught = null;

        try
        {
            await unitOfWork.ExecuteInTransactionAsync<object?>(_ =>
            {
                attempts++;
                context.TenantLifecycleLogs.Add(CreateLifecycleLog());
                throw originalException;
            });
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        await Assert.That(context.ChangeTracker.Entries<TenantLifecycleLog>()).IsEmpty();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(caught).IsSameReferenceAs(originalException);
    }

    private static ExploreDbContext CreateContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        return new ExploreDbContext(options);
    }

    private static TenantLifecycleLog CreateLifecycleLog() => new()
    {
        TenantId = Guid.NewGuid(),
        Tenant = null!,
        OldStatusId = (int)TenantStatusEnum.Active,
        NewStatusId = (int)TenantStatusEnum.Suspended,
        NewStatus = null!,
        TransitionedByUserId = Guid.NewGuid(),
        Reason = "Deterministic retry test",
        TransitionedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    private sealed class RetryOnceExecutionStrategy(DbContext context)
        : ExecutionStrategy(context, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) =>
            exception is TestTransientException or TestRollbackDbException;
    }

    private sealed class TestTransientException : Exception;

    private sealed class TestRollbackDbException : DbException;

    private sealed class TestDbContextTransaction(Exception? rollbackException = null) : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public bool SupportsSavepoints => false;
        public bool RollbackTokenWasCancelled { get; private set; }

        public void Commit()
        {
        }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Rollback()
        {
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackTokenWasCancelled = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            if (rollbackException is not null)
            {
                throw rollbackException;
            }

            return Task.CompletedTask;
        }

        public void CreateSavepoint(string name) => throw new NotSupportedException();

        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void RollbackToSavepoint(string name) => throw new NotSupportedException();

        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void ReleaseSavepoint(string name) => throw new NotSupportedException();

        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
