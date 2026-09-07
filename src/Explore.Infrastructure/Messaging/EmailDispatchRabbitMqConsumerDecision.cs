using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;

namespace Explore.Infrastructure.Messaging;

internal static class EmailDispatchRabbitMqConsumerDecision
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static EmailDispatchRabbitMqPointerParseResult ParsePointer(ReadOnlySpan<byte> body)
    {
        try
        {
            var pointer = JsonSerializer.Deserialize<EmailDispatchPointer>(body, JsonOptions);
            if (pointer is null)
            {
                return EmailDispatchRabbitMqPointerParseResult.Invalid("malformed_pointer", null);
            }

            if (pointer.TenantId == Guid.Empty || pointer.PublishEventId == Guid.Empty)
            {
                return EmailDispatchRabbitMqPointerParseResult.Invalid("invalid_pointer", pointer);
            }

            return EmailDispatchRabbitMqPointerParseResult.Valid(pointer);
        }
        catch (JsonException)
        {
            return EmailDispatchRabbitMqPointerParseResult.Invalid("malformed_pointer", null);
        }
        catch (NotSupportedException)
        {
            return EmailDispatchRabbitMqPointerParseResult.Invalid("malformed_pointer", null);
        }
    }

    public static EmailDispatchRabbitMqSettlement DecideForDrainResult(EmailDispatchSingleDrainResult result) =>
        result.IsDurableOutcome
            ? EmailDispatchRabbitMqSettlement.Ack("durable_outcome")
            : EmailDispatchRabbitMqSettlement.Reject("missing_outbox");

    public static EmailDispatchRabbitMqSettlement DecideForUnexpectedFailure() =>
        EmailDispatchRabbitMqSettlement.Nack("consumer_exception");

}

internal sealed record EmailDispatchRabbitMqPointerParseResult(
    bool IsValid,
    EmailDispatchPointer? Pointer,
    string FailureCategory)
{
    public static EmailDispatchRabbitMqPointerParseResult Valid(EmailDispatchPointer pointer) =>
        new(true, pointer, "none");

    public static EmailDispatchRabbitMqPointerParseResult Invalid(string failureCategory, EmailDispatchPointer? pointer) =>
        new(false, pointer, failureCategory);
}

internal sealed record EmailDispatchRabbitMqSettlement(
    EmailDispatchRabbitMqSettlementAction Action,
    bool Requeue,
    string FailureCategory)
{
    public static EmailDispatchRabbitMqSettlement Ack(string failureCategory) =>
        new(EmailDispatchRabbitMqSettlementAction.Ack, Requeue: false, failureCategory);

    public static EmailDispatchRabbitMqSettlement Reject(string failureCategory) =>
        new(EmailDispatchRabbitMqSettlementAction.Reject, Requeue: false, failureCategory);

    public static EmailDispatchRabbitMqSettlement Nack(string failureCategory) =>
        new(EmailDispatchRabbitMqSettlementAction.Nack, Requeue: true, failureCategory);
}

internal enum EmailDispatchRabbitMqSettlementAction
{
    Ack,
    Reject,
    Nack
}
