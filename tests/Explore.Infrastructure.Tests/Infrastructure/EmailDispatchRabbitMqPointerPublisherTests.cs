using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
[Category(InfrastructureTestCategories.RabbitMQ)]
public sealed class EmailDispatchRabbitMqPointerPublisherTests
{
    [Test]
    public async Task PublishDuePointersAsync_WhenPublishIsConfirmed_MarksRabbitMqPublishSucceeded()
    {
        var dispatch = CreateDispatch();
        var store = new PublicationStore(dispatch);
        var transport = Substitute.For<IEmailDispatchTransport>();
        EmailDispatchPointer? published = null;
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                published = call.ArgAt<EmailDispatchPointer>(0);
                return EmailDispatchPublishResult.Confirmed(123);
            });
        var publisher = CreatePublisher(store, transport);

        var result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.EligibleCount).IsEqualTo(1);
        await Assert.That(result.ConfirmedCount).IsEqualTo(1);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(published).IsEqualTo(EmailDispatchPointer.FromOutbox(dispatch));
        await Assert.That(dispatch.RabbitMqPublishAttemptCount).IsEqualTo(1);
        await Assert.That(dispatch.RabbitMqLastPublishedAt.HasValue).IsTrue();
        await Assert.That(dispatch.RabbitMqLastPublishAttemptAt).IsEqualTo(dispatch.RabbitMqLastPublishedAt);
        await Assert.That(dispatch.RabbitMqLastPublishFailureCategory).IsNull();
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Pending);
    }

    [Test]
    public Task PublishDuePointersAsync_WhenPublishIsNacked_MarksRabbitMqPublishFailed() =>
        AssertFailureAsync(EmailDispatchPublishOutcome.Nacked, EmailDispatchPublishFailure.PublisherNack, "publisher_nack");

    [Test]
    public Task PublishDuePointersAsync_WhenPublishIsReturned_MarksRabbitMqPublishFailed() =>
        AssertFailureAsync(EmailDispatchPublishOutcome.Returned, EmailDispatchPublishFailure.MandatoryReturn, "mandatory_return");

    [Test]
    public Task PublishDuePointersAsync_WhenPublishTimesOut_MarksRabbitMqPublishFailed() =>
        AssertFailureAsync(EmailDispatchPublishOutcome.Failed, EmailDispatchPublishFailure.PublishTimeout, "publish_timeout");

    [Test]
    public async Task PublishDuePointersAsync_WhenSettingsAreDisabled_DoesNotQueryOrPublish()
    {
        var dispatch = CreateDispatch();
        var store = new PublicationStore(dispatch) { RejectReads = true };
        var transport = Substitute.For<IEmailDispatchTransport>();
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns<Task<EmailDispatchPublishResult>>(_ => throw new InvalidOperationException("Publishing is disabled."));
        var publisher = CreatePublisher(store, transport, new EmailDispatchRabbitMqSettings { Enabled = false });

        var result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.EligibleCount).IsEqualTo(0);
        await Assert.That(result.ConfirmedCount).IsEqualTo(0);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(result.SkippedCount).IsEqualTo(0);
        await Assert.That(dispatch.RabbitMqPublishAttemptCount).IsEqualTo(0);
        await Assert.That(dispatch.RabbitMqLastPublishAttemptAt).IsNull();
    }

    [Test]
    public async Task PublishDuePointersAsync_WhenTransportThrows_MarksRabbitMqPublishFailedAndContinues()
    {
        var dispatch = CreateDispatch();
        var nextDispatch = CreateDispatch();
        var store = new PublicationStore(dispatch, nextDispatch);
        var transport = Substitute.For<IEmailDispatchTransport>();
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<EmailDispatchPointer>(0).PublishEventId == dispatch.PublishEventId
                ? throw new InvalidOperationException("broker unavailable")
                : EmailDispatchPublishResult.Confirmed(123));
        var publisher = CreatePublisher(store, transport);

        var result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.FailedCount).IsEqualTo(1);
        await Assert.That(result.ConfirmedCount).IsEqualTo(1);
        await Assert.That(dispatch.RabbitMqLastPublishFailureCategory).IsEqualTo("pointer_publish_exception");
        await Assert.That(dispatch.RabbitMqLastPublishedAt).IsNull();
        await Assert.That(dispatch.RabbitMqPublishAttemptCount).IsEqualTo(1);
        await Assert.That(nextDispatch.RabbitMqLastPublishedAt.HasValue).IsTrue();
    }

    [Test]
    [Arguments(EmailDispatchPublishOutcome.Returned, EmailDispatchPublishFailure.MandatoryReturn, "mandatory_return")]
    [Arguments(EmailDispatchPublishOutcome.Nacked, EmailDispatchPublishFailure.PublisherNack, "publisher_nack")]
    [Arguments(EmailDispatchPublishOutcome.Failed, EmailDispatchPublishFailure.PublishTimeout, "publish_timeout")]
    [Arguments(EmailDispatchPublishOutcome.Failed, EmailDispatchPublishFailure.BrokerPublishFailed, "broker_publish_failed")]
    [Arguments(EmailDispatchPublishOutcome.Failed, EmailDispatchPublishFailure.PointerPublishException, "pointer_publish_exception")]
    [Arguments(EmailDispatchPublishOutcome.Failed, EmailDispatchPublishFailure.Unknown, "unknown")]
    [Arguments(EmailDispatchPublishOutcome.Failed, (EmailDispatchPublishFailure)int.MaxValue, "unknown")]
    [Arguments(EmailDispatchPublishOutcome.Returned, null, "returned")]
    [Arguments(EmailDispatchPublishOutcome.Nacked, null, "nacked")]
    [Arguments(EmailDispatchPublishOutcome.Failed, null, "failed")]
    [Arguments((EmailDispatchPublishOutcome)int.MaxValue, null, "unknown")]
    public Task PublishDuePointersAsync_PersistsOnlyBoundedFailureCodes(
        EmailDispatchPublishOutcome outcome, EmailDispatchPublishFailure? failure, string expectedCode) =>
        AssertFailureAsync(outcome, failure, expectedCode);

    [Test]
    public async Task PublishDuePointersAsync_WaitsForConfirmationBeforeMarkingAndAllowsLaterDuplicates()
    {
        var dispatch = CreateDispatch();
        var store = new PublicationStore(dispatch);
        var confirmation = new TaskCompletionSource<EmailDispatchPublishResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = Substitute.For<IEmailDispatchTransport>();
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns(confirmation.Task);
        var publisher = CreatePublisher(store, transport,
            new EmailDispatchRabbitMqSettings { Enabled = true, PublisherRetryDelaySeconds = 0 });

        var publishing = publisher.PublishDuePointersAsync(CancellationToken.None);
        await Assert.That(publishing.IsCompleted).IsFalse();
        await Assert.That(dispatch.RabbitMqPublishAttemptCount).IsEqualTo(0);
        await Assert.That(dispatch.RabbitMqLastPublishedAt).IsNull();
        confirmation.SetResult(EmailDispatchPublishResult.Confirmed(123));
        await publishing;
        var duplicate = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(duplicate.EligibleCount).IsEqualTo(1);
        await Assert.That(duplicate.ConfirmedCount).IsEqualTo(1);
        await Assert.That(dispatch.RabbitMqPublishAttemptCount).IsEqualTo(2);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Pending);
    }

    [Test]
    public async Task PublishDuePointersAsync_WhenTransportIsDisabled_LeavesPointerEligible()
    {
        var dispatch = CreateDispatch();
        var store = new PublicationStore(dispatch);
        var transport = Substitute.For<IEmailDispatchTransport>();
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns(new EmailDispatchPublishResult(EmailDispatchPublishOutcome.Disabled));
        var publisher = CreatePublisher(store, transport);

        var result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.SkippedCount).IsEqualTo(1);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(dispatch.RabbitMqPublishAttemptCount).IsEqualTo(0);
        await Assert.That(dispatch.RabbitMqLastPublishAttemptAt).IsNull();
        await Assert.That(dispatch.RabbitMqLastPublishedAt).IsNull();
    }

    [Test]
    public async Task PublishDuePointersAsync_WhenCallerCancels_DoesNotMarkFailure()
    {
        var dispatch = CreateDispatch();
        var store = new PublicationStore(dispatch);
        using var cancellation = new CancellationTokenSource();
        var transport = Substitute.For<IEmailDispatchTransport>();
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns<Task<EmailDispatchPublishResult>>(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<EmailDispatchPublishResult>(cancellation.Token);
            });
        var publisher = CreatePublisher(store, transport);
        bool cancelled = false;
        try
        {
            await publisher.PublishDuePointersAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        await Assert.That(cancelled).IsTrue();
        await Assert.That(dispatch.RabbitMqPublishAttemptCount).IsEqualTo(0);
        await Assert.That(dispatch.RabbitMqLastPublishFailureCategory).IsNull();
        await Assert.That(dispatch.RabbitMqLastPublishedAt).IsNull();
    }

    private static async Task AssertFailureAsync(
        EmailDispatchPublishOutcome outcome, EmailDispatchPublishFailure? failure, string expectedCode)
    {
        var dispatch = CreateDispatch();
        var originalStatus = dispatch.Status;
        var store = new PublicationStore(dispatch);
        var transport = Substitute.For<IEmailDispatchTransport>();
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns(new EmailDispatchPublishResult(
                Outcome: outcome,
                ReplyText: "External broker text is not a durable failure code.",
                FailureCategory: failure));
        var publisher = CreatePublisher(store, transport);

        var result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.FailedCount).IsEqualTo(1);
        await Assert.That(result.ConfirmedCount).IsEqualTo(0);
        await Assert.That(dispatch.RabbitMqLastPublishFailureCategory).IsEqualTo(expectedCode);
        await Assert.That(dispatch.Status).IsEqualTo(originalStatus);
        await Assert.That(dispatch.RabbitMqLastPublishedAt).IsNull();
        await Assert.That(dispatch.RabbitMqPublishAttemptCount).IsEqualTo(1);
        await Assert.That(dispatch.RabbitMqLastPublishAttemptAt.HasValue).IsTrue();
        await Assert.That(dispatch.UpdatedAt).IsEqualTo(dispatch.RabbitMqLastPublishAttemptAt);
    }

    private static EmailDispatchRabbitMqPointerPublisher CreatePublisher(
        IEmailDispatchPointerPublicationStore store,
        IEmailDispatchTransport transport,
        EmailDispatchRabbitMqSettings? settings = null) =>
        new(store, transport,
            new StaticOptionsMonitor<EmailDispatchRabbitMqSettings>(settings ?? new() { Enabled = true }),
            NullLogger<EmailDispatchRabbitMqPointerPublisher>.Instance);

    private static EmailDispatchOutbox CreateDispatch() => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        PublishEventId = Guid.CreateVersion7(),
        Kind = EmailDispatchKind.RegistrationConfirmation,
        SourceType = "registration_order",
        SourceId = Guid.CreateVersion7(),
        EventId = Guid.CreateVersion7(),
        RegistrationOrderId = Guid.CreateVersion7(),
        RecipientUserId = Guid.CreateVersion7(),
        RecipientEmail = "recipient@example.test",
        Subject = "Registration confirmed",
        Status = EmailDispatchStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };

    private sealed class PublicationStore(params EmailDispatchOutbox[] rows) : IEmailDispatchPointerPublicationStore
    {
        public bool RejectReads { get; init; }

        public Task<IReadOnlyList<EmailDispatchPointerCandidate>> GetPublishBatchAsync(
            int batchSize, DateTime now, DateTime retryAttemptsBefore, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RejectReads) throw new InvalidOperationException("Publication reads are disabled.");
            IReadOnlyList<EmailDispatchPointerCandidate> candidates = rows
                .Where(row => row.Status is EmailDispatchStatus.Pending or EmailDispatchStatus.RetryScheduled)
                .Where(row => row.RabbitMqLastPublishAttemptAt == null
                    || row.RabbitMqLastPublishAttemptAt <= retryAttemptsBefore)
                .OrderBy(row => row.CreatedAt)
                .Take(batchSize)
                .Select(row => new EmailDispatchPointerCandidate(row.Id, EmailDispatchPointer.FromOutbox(row)))
                .ToArray();
            return Task.FromResult(candidates);
        }

        public Task MarkPublishSucceededAsync(
            EmailDispatchPointerCandidate candidate, DateTime publishedAt, CancellationToken cancellationToken)
        {
            var row = Find(candidate, cancellationToken);
            row.RabbitMqLastPublishedAt = publishedAt;
            row.RabbitMqLastPublishAttemptAt = publishedAt;
            row.RabbitMqPublishAttemptCount++;
            row.RabbitMqLastPublishFailureCategory = null;
            row.UpdatedAt = publishedAt;
            return Task.CompletedTask;
        }

        public Task MarkPublishFailedAsync(
            EmailDispatchPointerCandidate candidate, string failureCategory, DateTime attemptedAt,
            CancellationToken cancellationToken)
        {
            var row = Find(candidate, cancellationToken);
            row.RabbitMqLastPublishAttemptAt = attemptedAt;
            row.RabbitMqPublishAttemptCount++;
            row.RabbitMqLastPublishFailureCategory = failureCategory;
            row.UpdatedAt = attemptedAt;
            return Task.CompletedTask;
        }

        private EmailDispatchOutbox Find(EmailDispatchPointerCandidate candidate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return rows.Single(row => row.Id == candidate.OutboxId && row.TenantId == candidate.Pointer.TenantId);
        }
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
