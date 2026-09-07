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
        var repository = Substitute.For<IEmailDispatchOutboxRepository>();
        var transport = Substitute.For<IEmailDispatchTransport>();
        repository.GetRabbitMqPublishBatch(
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([dispatch]);
        transport.PublishDispatchPointerAsync(
                Arg.Is<EmailDispatchPointer>(pointer =>
                    pointer.TenantId == dispatch.TenantId
                    && pointer.PublishEventId == dispatch.PublishEventId
                    && pointer.EventId == dispatch.EventId
                    && pointer.RegistrationOrderId == dispatch.RegistrationOrderId),
                Arg.Any<CancellationToken>())
            .Returns(EmailDispatchPublishResult.Confirmed(123));
        var publisher = CreatePublisher(repository, transport, new EmailDispatchRabbitMqSettings { Enabled = true });

        EmailDispatchRabbitMqPointerPublisherResult result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.EligibleCount).IsEqualTo(1);
        await Assert.That(result.ConfirmedCount).IsEqualTo(1);
        await repository.Received(1).MarkRabbitMqPublishSucceeded(
            dispatch.Id,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkRabbitMqPublishFailed(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishDuePointersAsync_WhenPublishIsNacked_MarksRabbitMqPublishFailed()
    {
        var dispatch = CreateDispatch();
        var repository = Substitute.For<IEmailDispatchOutboxRepository>();
        var transport = Substitute.For<IEmailDispatchTransport>();
        repository.GetRabbitMqPublishBatch(
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([dispatch]);
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns(new EmailDispatchPublishResult(EmailDispatchPublishOutcome.Nacked, FailureCategory: "publisher_nack"));
        var publisher = CreatePublisher(repository, transport, new EmailDispatchRabbitMqSettings { Enabled = true });

        EmailDispatchRabbitMqPointerPublisherResult result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.FailedCount).IsEqualTo(1);
        await repository.Received(1).MarkRabbitMqPublishFailed(
            dispatch.Id,
            "publisher_nack",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().MarkRabbitMqPublishSucceeded(
            Arg.Any<Guid>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishDuePointersAsync_WhenPublishIsReturned_MarksRabbitMqPublishFailed()
    {
        var dispatch = CreateDispatch();
        var repository = Substitute.For<IEmailDispatchOutboxRepository>();
        var transport = Substitute.For<IEmailDispatchTransport>();
        repository.GetRabbitMqPublishBatch(
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([dispatch]);
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns(new EmailDispatchPublishResult(EmailDispatchPublishOutcome.Returned, FailureCategory: "mandatory_return"));
        var publisher = CreatePublisher(repository, transport, new EmailDispatchRabbitMqSettings { Enabled = true });

        EmailDispatchRabbitMqPointerPublisherResult result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.FailedCount).IsEqualTo(1);
        await repository.Received(1).MarkRabbitMqPublishFailed(
            dispatch.Id,
            "mandatory_return",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishDuePointersAsync_WhenPublishTimesOut_MarksRabbitMqPublishFailed()
    {
        var dispatch = CreateDispatch();
        var repository = Substitute.For<IEmailDispatchOutboxRepository>();
        var transport = Substitute.For<IEmailDispatchTransport>();
        repository.GetRabbitMqPublishBatch(
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([dispatch]);
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns(new EmailDispatchPublishResult(EmailDispatchPublishOutcome.Failed, FailureCategory: "publish_timeout"));
        var publisher = CreatePublisher(repository, transport, new EmailDispatchRabbitMqSettings { Enabled = true });

        EmailDispatchRabbitMqPointerPublisherResult result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.FailedCount).IsEqualTo(1);
        await repository.Received(1).MarkRabbitMqPublishFailed(
            dispatch.Id,
            "publish_timeout",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishDuePointersAsync_WhenSettingsAreDisabled_DoesNotQueryOrPublish()
    {
        var repository = Substitute.For<IEmailDispatchOutboxRepository>();
        var transport = Substitute.For<IEmailDispatchTransport>();
        var publisher = CreatePublisher(repository, transport, new EmailDispatchRabbitMqSettings { Enabled = false });

        EmailDispatchRabbitMqPointerPublisherResult result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.EligibleCount).IsEqualTo(0);
        await repository.DidNotReceiveWithAnyArgs().GetRabbitMqPublishBatch(default, default, default, default);
        await transport.DidNotReceiveWithAnyArgs().PublishDispatchPointerAsync(default!, default);
    }

    [Test]
    public async Task PublishDuePointersAsync_WhenTransportThrows_MarksRabbitMqPublishFailedAndContinues()
    {
        var dispatch = CreateDispatch();
        var repository = Substitute.For<IEmailDispatchOutboxRepository>();
        var transport = Substitute.For<IEmailDispatchTransport>();
        repository.GetRabbitMqPublishBatch(
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([dispatch]);
        transport.PublishDispatchPointerAsync(Arg.Any<EmailDispatchPointer>(), Arg.Any<CancellationToken>())
            .Returns<Task<EmailDispatchPublishResult>>(_ => throw new InvalidOperationException("broker unavailable"));
        var publisher = CreatePublisher(repository, transport, new EmailDispatchRabbitMqSettings { Enabled = true });

        EmailDispatchRabbitMqPointerPublisherResult result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.FailedCount).IsEqualTo(1);
        await repository.Received(1).MarkRabbitMqPublishFailed(
            dispatch.Id,
            "pointer_publish_exception",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    private static EmailDispatchRabbitMqPointerPublisher CreatePublisher(
        IEmailDispatchOutboxRepository repository,
        IEmailDispatchTransport transport,
        EmailDispatchRabbitMqSettings settings)
    {
        return new EmailDispatchRabbitMqPointerPublisher(
            repository,
            transport,
            new StaticOptionsMonitor<EmailDispatchRabbitMqSettings>(settings),
            NullLogger<EmailDispatchRabbitMqPointerPublisher>.Instance);
    }

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
        CreatedAt = DateTime.UtcNow
    };

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
