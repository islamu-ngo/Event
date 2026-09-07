using Explore.API.BackgroundServices;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Management.Handlers.Commands;
using Explore.Domain;
using Explore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class OutboxProcessorDeadLetterTests
{
    [Test]
    public async Task ProcessSingleMessageAsync_CurrentFinalClaim_ReconcilesAfterDeadLetterExactlyOnce()
    {
        var sequence = new List<string>();
        var message = CreateMessage(maxRetries: 1);
        var leaseExpiresAt = DateTime.UtcNow.AddMinutes(5);
        var repository = Substitute.For<IOutboxRepository>();
        repository.TryClaimForProcessing(
                message.Id,
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(leaseExpiresAt);
        repository.MarkAsFailed(
                message.Id,
                leaseExpiresAt,
                "dispatch_failed",
                true,
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sequence.Add("dead-letter");
                return OutboxFailureTransition.DeadLettered;
            });
        repository.MarkDeadLetterReconciled(
                message.Id,
                leaseExpiresAt,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sequence.Add("acknowledge");
                return true;
            });
        var dispatcher = Substitute.For<IOutboxMessageDispatcher>();
        dispatcher.DispatchAsync(message, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("provider detail")));
        dispatcher.ReconcileDeadLetterAsync(message, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sequence.Add("reconcile");
                return Task.CompletedTask;
            });
        var processor = CreateProcessor(globalMaxRetryCount: 99);

        await processor.ProcessSingleMessageAsync(
            message,
            repository,
            dispatcher,
            CancellationToken.None);

        await Assert.That(sequence.SequenceEqual(["dead-letter", "reconcile", "acknowledge"])).IsTrue();
        await dispatcher.Received(1).ReconcileDeadLetterAsync(message, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessSingleMessageAsync_TransientAttempt_DoesNotReconcile()
    {
        var message = CreateMessage(maxRetries: 3);
        var leaseExpiresAt = DateTime.UtcNow.AddMinutes(5);
        var repository = Substitute.For<IOutboxRepository>();
        repository.TryClaimForProcessing(
                message.Id,
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(leaseExpiresAt);
        repository.MarkAsFailed(
                message.Id,
                leaseExpiresAt,
                "dispatch_failed",
                true,
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(OutboxFailureTransition.RetryScheduled);
        var dispatcher = Substitute.For<IOutboxMessageDispatcher>();
        dispatcher.DispatchAsync(message, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("transient")));
        var processor = CreateProcessor(globalMaxRetryCount: 1);

        await processor.ProcessSingleMessageAsync(
            message,
            repository,
            dispatcher,
            CancellationToken.None);

        await dispatcher.DidNotReceive().ReconcileDeadLetterAsync(
            Arg.Any<OutboxMessage>(),
            Arg.Any<CancellationToken>());
        await repository.Received(1).MarkAsFailed(
            message.Id,
            leaseExpiresAt,
            "dispatch_failed",
            true,
            1,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessSingleMessageAsync_StaleFailure_DoesNotReconcileTenantOperation()
    {
        var message = CreateMessage(maxRetries: 1);
        var leaseExpiresAt = DateTime.UtcNow.AddMinutes(5);
        var repository = Substitute.For<IOutboxRepository>();
        repository.TryClaimForProcessing(
                message.Id,
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(leaseExpiresAt);
        repository.MarkAsFailed(
                message.Id,
                leaseExpiresAt,
                Arg.Any<string>(),
                true,
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(OutboxFailureTransition.NotOwned);
        var dispatcher = Substitute.For<IOutboxMessageDispatcher>();
        dispatcher.DispatchAsync(message, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("stale")));
        var processor = CreateProcessor(globalMaxRetryCount: 1);

        await processor.ProcessSingleMessageAsync(message, repository, dispatcher, CancellationToken.None);

        await dispatcher.DidNotReceive().ReconcileDeadLetterAsync(
            Arg.Any<OutboxMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessSingleMessageAsync_FailedReconciliation_IsRetriedFromDeadLetterLease()
    {
        var message = CreateMessage(maxRetries: 1);
        var originalLease = DateTime.UtcNow.AddMinutes(5);
        var recoveryLease = originalLease.AddMinutes(5);
        var repository = Substitute.For<IOutboxRepository>();
        repository.TryClaimForProcessing(
                message.Id,
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(originalLease);
        repository.MarkAsFailed(
                message.Id,
                originalLease,
                Arg.Any<string>(),
                true,
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(OutboxFailureTransition.DeadLettered);
        repository.TryClaimDeadLetterReconciliation(
                message.Id,
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(recoveryLease);
        repository.MarkDeadLetterReconciled(
                message.Id,
                recoveryLease,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var dispatcher = Substitute.For<IOutboxMessageDispatcher>();
        dispatcher.DispatchAsync(message, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("dispatch")));
        var reconciliationAttempts = 0;
        dispatcher.ReconcileDeadLetterAsync(message, Arg.Any<CancellationToken>())
            .Returns(_ => ++reconciliationAttempts == 1
                ? Task.FromException(new InvalidOperationException("reconcile"))
                : Task.CompletedTask);
        var processor = CreateProcessor(globalMaxRetryCount: 1);

        await processor.ProcessSingleMessageAsync(message, repository, dispatcher, CancellationToken.None);

        await repository.DidNotReceive().MarkDeadLetterReconciled(
            message.Id,
            originalLease,
            Arg.Any<CancellationToken>());

        message.Status = OutboxMessageStatus.DeadLettered;
        await processor.ProcessSingleMessageAsync(message, repository, dispatcher, CancellationToken.None);

        await dispatcher.Received(2).ReconcileDeadLetterAsync(message, Arg.Any<CancellationToken>());
        await repository.Received(1).MarkDeadLetterReconciled(
            message.Id,
            recoveryLease,
            Arg.Any<CancellationToken>());
    }

    private static OutboxProcessor CreateProcessor(int globalMaxRetryCount) => new(
        Substitute.For<IServiceProvider>(),
        Options.Create(new OutboxProcessorSettings
        {
            MaxRetryCount = globalMaxRetryCount,
            InitialRetryDelaySeconds = 1,
            MaxRetryDelaySeconds = 60
        }),
        NullLogger<OutboxProcessor>.Instance);

    private static OutboxMessage CreateMessage(int maxRetries) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = nameof(ManagedTenantProvisioningOperation),
        AggregateId = Guid.CreateVersion7(),
        EventType = ManagedTenantProvisioningOutboxEvents.ProcessRequested,
        Status = OutboxMessageStatus.Pending,
        CreatedAt = DateTime.UtcNow,
        MaxRetries = maxRetries
    };
}
