using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Tests.Fixtures;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
[Category(InfrastructureTestCategories.RabbitMQ)]
public sealed class EmailDispatchRabbitMqConsumerDecisionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ParsePointerWhenPayloadIsValidReturnsPointer()
    {
        EmailDispatchPointer pointer = CreatePointer();
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(pointer, JsonOptions);

        EmailDispatchRabbitMqPointerParseResult result = EmailDispatchRabbitMqConsumerDecision.ParsePointer(body);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Pointer?.TenantId).IsEqualTo(pointer.TenantId);
        await Assert.That(result.Pointer?.PublishEventId).IsEqualTo(pointer.PublishEventId);
        await Assert.That(result.FailureCategory).IsEqualTo("none");
    }

    [Test]
    public async Task ParsePointerWhenPayloadIsMalformedRejectsAsMalformedPointer()
    {
        byte[] body = "{ not-json"u8.ToArray();

        EmailDispatchRabbitMqPointerParseResult result = EmailDispatchRabbitMqConsumerDecision.ParsePointer(body);
        EmailDispatchRabbitMqSettlement decision = EmailDispatchRabbitMqSettlement.Reject(result.FailureCategory);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("malformed_pointer");
        await Assert.That(decision.Action).IsEqualTo(EmailDispatchRabbitMqSettlementAction.Reject);
        await Assert.That(decision.Requeue).IsFalse();
    }

    [Test]
    public async Task ParsePointerWhenPayloadHasEmptyDurableIdentifiersRejectsAsInvalidPointer()
    {
        var pointer = new EmailDispatchPointer(
            Guid.Empty,
            Guid.Empty,
            EmailDispatchKind.RegistrationConfirmation,
            "event-registration",
            SourceId: null,
            EventId: null,
            RegistrationOrderId: null);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(pointer, JsonOptions);

        EmailDispatchRabbitMqPointerParseResult result = EmailDispatchRabbitMqConsumerDecision.ParsePointer(body);
        EmailDispatchRabbitMqSettlement decision = EmailDispatchRabbitMqSettlement.Reject(result.FailureCategory);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("invalid_pointer");
        await Assert.That(decision.Action).IsEqualTo(EmailDispatchRabbitMqSettlementAction.Reject);
        await Assert.That(decision.Requeue).IsFalse();
    }

    [Test]
    public async Task FromDrainResultWhenOutcomeIsDurableAcknowledgesDelivery()
    {
        foreach (EmailDispatchDrainOutcome outcome in new[]
        {
            EmailDispatchDrainOutcome.Sent,
            EmailDispatchDrainOutcome.RetryScheduled,
            EmailDispatchDrainOutcome.DeadLettered,
            EmailDispatchDrainOutcome.Unknown,
            EmailDispatchDrainOutcome.Skipped,
            EmailDispatchDrainOutcome.TenantPaused,
            EmailDispatchDrainOutcome.AlreadyClaimed,
            EmailDispatchDrainOutcome.AlreadySettled,
            EmailDispatchDrainOutcome.Deferred,
            EmailDispatchDrainOutcome.Parked
        })
        {
            EmailDispatchRabbitMqSettlement decision = EmailDispatchRabbitMqConsumerDecision.DecideForDrainResult(
                new EmailDispatchSingleDrainResult(outcome, Guid.CreateVersion7()));

            await Assert.That(decision.Action).IsEqualTo(EmailDispatchRabbitMqSettlementAction.Ack);
            await Assert.That(decision.Requeue).IsFalse();
            await Assert.That(decision.FailureCategory).IsEqualTo("durable_outcome");
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(int.MaxValue)]
    public async Task FromDrainResultWhenOutcomeIsUnspecifiedOrUndefinedDoesNotAcknowledge(int outcomeValue)
    {
        var result = new EmailDispatchSingleDrainResult((EmailDispatchDrainOutcome)outcomeValue);
        var decision = EmailDispatchRabbitMqConsumerDecision.DecideForDrainResult(result);

        await Assert.That(result.IsDurableOutcome).IsFalse();
        await Assert.That(decision.Action).IsEqualTo(EmailDispatchRabbitMqSettlementAction.Reject);
        await Assert.That(decision.Requeue).IsFalse();
    }

    [Test]
    public async Task FromDrainResultWhenOutcomeIsMissingRejectsDeliveryToDeadLetterQueue()
    {
        EmailDispatchRabbitMqSettlement decision = EmailDispatchRabbitMqConsumerDecision.DecideForDrainResult(
            new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Missing));

        await Assert.That(decision.Action).IsEqualTo(EmailDispatchRabbitMqSettlementAction.Reject);
        await Assert.That(decision.Requeue).IsFalse();
        await Assert.That(decision.FailureCategory).IsEqualTo("missing_outbox");
    }

    [Test]
    public async Task NackTransientRequeuesUnexpectedFailures()
    {
        EmailDispatchRabbitMqSettlement decision = EmailDispatchRabbitMqConsumerDecision.DecideForUnexpectedFailure();

        await Assert.That(decision.Action).IsEqualTo(EmailDispatchRabbitMqSettlementAction.Nack);
        await Assert.That(decision.Requeue).IsTrue();
        await Assert.That(decision.FailureCategory).IsEqualTo("consumer_exception");
    }

    private static EmailDispatchPointer CreatePointer() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            EmailDispatchKind.RegistrationConfirmation,
            "event-registration",
            Guid.CreateVersion7(),
            EventId: Guid.CreateVersion7(),
            RegistrationOrderId: Guid.CreateVersion7());
}
