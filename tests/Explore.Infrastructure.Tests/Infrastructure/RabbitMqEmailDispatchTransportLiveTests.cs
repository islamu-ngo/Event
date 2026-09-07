// ABOUTME: Runtime RabbitMQ tests for the EmailDispatch transport topology and health path.
// ABOUTME: Proves enabled RabbitMQ Dispatch Mode can declare broker resources against Testcontainers.

using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.RabbitMQ)]
[Category(InfrastructureTestCategories.Runtime)]
[Explicit]
[ClassDataSource<RabbitMqContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("RabbitMqBroker")]
public sealed class RabbitMqEmailDispatchTransportLiveTests(RabbitMqContainerFixture rabbitMq)
{
    [Test]
    [Timeout(180_000)]
    public async Task DeclareTopologyAsync_WithEnabledRabbitMq_DeclaresDispatchDeadLetterAndParkingTopology()
    {
        var settings = CreateSettings(rabbitMq);
        await using var transport = CreateTransport(settings);

        await transport.DeclareTopologyAsync(CancellationToken.None);
        var health = await transport.CheckHealthAsync(CancellationToken.None);

        await Assert.That(health.Enabled).IsTrue();
        await Assert.That(health.Healthy).IsTrue();
        await Assert.That(health.Data["exchange"]).IsEqualTo(settings.ExchangeName);

        var exchange = await rabbitMq.GetExchangeAsync(settings.ExchangeName);
        var deadLetterExchange = await rabbitMq.GetExchangeAsync(settings.DeadLetterExchangeName);
        var dispatchQueue = await rabbitMq.GetQueueAsync(settings.DispatchQueueName);
        var deadLetterQueue = await rabbitMq.GetQueueAsync(settings.DeadLetterQueueName);
        var parkingQueue = await rabbitMq.GetQueueAsync(settings.ParkingQueueName);
        var dispatchBindings = await rabbitMq.GetQueueBindingsAsync(settings.DispatchQueueName);
        var deadLetterBindings = await rabbitMq.GetQueueBindingsAsync(settings.DeadLetterQueueName);
        var parkingBindings = await rabbitMq.GetQueueBindingsAsync(settings.ParkingQueueName);

        await Assert.That(exchange.Name).IsEqualTo(settings.ExchangeName);
        await Assert.That(exchange.Type).IsEqualTo("direct");
        await Assert.That(exchange.Durable).IsTrue();
        await Assert.That(deadLetterExchange.Name).IsEqualTo(settings.DeadLetterExchangeName);
        await Assert.That(deadLetterExchange.Type).IsEqualTo("direct");
        await Assert.That(deadLetterExchange.Durable).IsTrue();
        await Assert.That(dispatchQueue.Durable).IsTrue();
        await Assert.That(deadLetterQueue.Durable).IsTrue();
        await Assert.That(parkingQueue.Durable).IsTrue();

        await AssertQueueArgument(dispatchQueue, "x-dead-letter-exchange", settings.DeadLetterExchangeName);
        await AssertQueueArgument(dispatchQueue, "x-dead-letter-routing-key", settings.DeadLetterRoutingKey);
        await AssertBinding(
            dispatchBindings,
            settings.ExchangeName,
            settings.DispatchQueueName,
            settings.DispatchRoutingKey);
        await AssertBinding(
            deadLetterBindings,
            settings.DeadLetterExchangeName,
            settings.DeadLetterQueueName,
            settings.DeadLetterRoutingKey);
        await AssertBinding(
            parkingBindings,
            settings.DeadLetterExchangeName,
            settings.ParkingQueueName,
            settings.ParkingRoutingKey);
    }

    [Test]
    [Timeout(180_000)]
    public async Task PublishDispatchPointerAsync_WithBoundRoutingKey_ReturnsConfirmed()
    {
        var settings = CreateSettings(rabbitMq);
        await using var transport = CreateTransport(settings);

        EmailDispatchPublishResult result = await transport.PublishDispatchPointerAsync(
            CreatePointer(),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Confirmed);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.PublishSequenceNumber).IsNotNull();
        await Assert.That(result.PublishSequenceNumber!.Value).IsGreaterThan(0UL);
    }

    [Test]
    [Timeout(180_000)]
    public async Task PublishDispatchPointerAsync_WithUnroutableMandatoryMessage_ReturnsReturned()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var topologySettings = CreateSettings(rabbitMq, suffix);
        var publishSettings = CreateSettings(
            rabbitMq,
            suffix,
            dispatchRoutingKey: $"test.email-dispatch.unrouted.{suffix}");
        await using var transport = CreateTransport(new SequenceOptionsMonitor(
            publishSettings,
            topologySettings));

        EmailDispatchPublishResult result = await transport.PublishDispatchPointerAsync(
            CreatePointer(),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Returned);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo(EmailDispatchPublishFailure.MandatoryReturn);
    }

    [Test]
    [Timeout(180_000)]
    public async Task PublishDispatchPointerAsync_WithSensitiveOutboxSnapshot_PublishesPointerOnlyPayload()
    {
        var settings = CreateSettings(rabbitMq);
        await using var transport = CreateTransport(settings);
        var dispatch = CreateDispatchWithSensitiveSnapshot();
        var pointer = EmailDispatchPointer.FromOutbox(dispatch);

        EmailDispatchPublishResult result = await transport.PublishDispatchPointerAsync(
            pointer,
            CancellationToken.None);
        var messages = await rabbitMq.GetQueueMessagesAsync(settings.DispatchQueueName, count: 1);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Confirmed);
        await Assert.That(messages.Count).IsEqualTo(1);

        var payload = messages.Single().Payload;
        await Assert.That(payload).Contains(dispatch.TenantId.ToString());
        await Assert.That(payload).Contains(dispatch.PublishEventId.ToString());
        await Assert.That(payload).Contains(dispatch.SourceType);
        await Assert.That(payload).Contains(dispatch.SourceId.ToString());
        await Assert.That(payload).Contains(dispatch.EventId!.Value.ToString());
        await Assert.That(payload).Contains(dispatch.RegistrationOrderId!.Value.ToString());
        await Assert.That(payload).Contains(dispatch.RecipientUserId.ToString());
        await Assert.That(payload).DoesNotContain(dispatch.RecipientEmail);
        await Assert.That(payload).DoesNotContain(dispatch.Subject);
        await Assert.That(payload).DoesNotContain(dispatch.PlainTextBody);
        await Assert.That(payload).DoesNotContain(dispatch.HtmlBody);
        await Assert.That(payload).DoesNotContain(dispatch.ReplyTo);
        await Assert.That(payload).DoesNotContain(dispatch.ProviderMessageId);
        await Assert.That(payload).DoesNotContain(dispatch.LastError);
        await Assert.That(payload).DoesNotContain("recipientEmail");
        await Assert.That(payload).DoesNotContain("subject");
        await Assert.That(payload).DoesNotContain("plainTextBody");
        await Assert.That(payload).DoesNotContain("htmlBody");
        await Assert.That(payload).DoesNotContain("replyTo");
        await Assert.That(payload).DoesNotContain("providerMessageId");
        await Assert.That(payload).DoesNotContain("lastError");
    }

    private static async Task AssertQueueArgument(
        RabbitMqContainerFixture.RabbitMqQueueDetail queue,
        string key,
        string expected)
    {
        var containsArgument = queue.Arguments.TryGetValue(key, out var value);

        await Assert.That(containsArgument).IsTrue();
        await Assert.That(value.GetString()).IsEqualTo(expected);
    }

    private static async Task AssertBinding(
        IReadOnlyList<RabbitMqContainerFixture.RabbitMqBindingDetail> bindings,
        string source,
        string destination,
        string routingKey)
    {
        var exists = bindings.Any(binding =>
            binding.Source == source
            && binding.Destination == destination
            && binding.DestinationType == "queue"
            && binding.RoutingKey == routingKey);

        await Assert.That(exists).IsTrue();
    }

    private static RabbitMqEmailDispatchTransport CreateTransport(EmailDispatchRabbitMqSettings settings)
    {
        var options = Substitute.For<IOptionsMonitor<EmailDispatchRabbitMqSettings>>();
        options.CurrentValue.Returns(settings);
        return CreateTransport(options);
    }

    private static RabbitMqEmailDispatchTransport CreateTransport(
        IOptionsMonitor<EmailDispatchRabbitMqSettings> options)
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new RabbitMqEmailDispatchTransport(
            new ConfigurationBuilder().Build(),
            options,
            new BusinessMetrics(meterFactory),
            NullLogger<RabbitMqEmailDispatchTransport>.Instance);
    }

    private static EmailDispatchPointer CreatePointer() => new(
        TenantId: Guid.CreateVersion7(),
        PublishEventId: Guid.CreateVersion7(),
        Kind: EmailDispatchKind.RegistrationConfirmation,
        SourceType: "event-registration",
        SourceId: Guid.CreateVersion7(),
        EventId: Guid.CreateVersion7(),
        RegistrationOrderId: Guid.CreateVersion7());

    private static EmailDispatchOutbox CreateDispatchWithSensitiveSnapshot() => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        PublishEventId = Guid.CreateVersion7(),
        Kind = EmailDispatchKind.RegistrationConfirmation,
        SourceType = "event-registration",
        SourceId = Guid.CreateVersion7(),
        EventId = Guid.CreateVersion7(),
        RegistrationOrderId = Guid.CreateVersion7(),
        RecipientUserId = Guid.CreateVersion7(),
        RecipientEmail = $"attendee-{Guid.CreateVersion7():N}@example.test",
        Subject = $"registration-subject-{Guid.CreateVersion7():N}",
        PlainTextBody = $"plain-body-sentinel-{Guid.CreateVersion7():N}",
        HtmlBody = $"<p>html-body-sentinel-{Guid.CreateVersion7():N}</p>",
        ReplyTo = $"reply-{Guid.CreateVersion7():N}@example.test",
        ProviderMessageId = $"provider-message-{Guid.CreateVersion7():N}",
        LastError = $"raw-provider-error-smtp-password-{Guid.CreateVersion7():N}"
    };

    private static EmailDispatchRabbitMqSettings CreateSettings(RabbitMqContainerFixture rabbitMq)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        return CreateSettings(rabbitMq, suffix);
    }

    private static EmailDispatchRabbitMqSettings CreateSettings(
        RabbitMqContainerFixture rabbitMq,
        string suffix,
        string? dispatchRoutingKey = null)
    {
        return new EmailDispatchRabbitMqSettings
        {
            Enabled = true,
            ConnectionString = rabbitMq.AmqpConnectionString,
            ExchangeName = $"test.email-dispatch.{suffix}",
            DispatchQueueName = $"test.email-dispatch.dispatch.{suffix}",
            DispatchRoutingKey = dispatchRoutingKey ?? $"test.email-dispatch.dispatch.{suffix}",
            DeadLetterExchangeName = $"test.email-dispatch.dlx.{suffix}",
            DeadLetterQueueName = $"test.email-dispatch.dead-letter.{suffix}",
            DeadLetterRoutingKey = $"test.email-dispatch.dead-letter.{suffix}",
            ParkingQueueName = $"test.email-dispatch.parking.{suffix}",
            ParkingRoutingKey = $"test.email-dispatch.parking.{suffix}",
            ClientProvidedName = $"test-email-dispatch-{suffix}",
            PublishTimeoutSeconds = 5
        };
    }

    private sealed class SequenceOptionsMonitor(
        EmailDispatchRabbitMqSettings first,
        EmailDispatchRabbitMqSettings second) : IOptionsMonitor<EmailDispatchRabbitMqSettings>
    {
        private int _readCount;

        public EmailDispatchRabbitMqSettings CurrentValue => Interlocked.Increment(ref _readCount) == 1
            ? first
            : second;

        public EmailDispatchRabbitMqSettings Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<EmailDispatchRabbitMqSettings, string?> listener) =>
            NoopDisposable.Instance;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
