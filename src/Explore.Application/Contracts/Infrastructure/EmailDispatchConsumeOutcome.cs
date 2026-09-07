// ABOUTME: Enumerates outcomes for consuming and replaying durable email-dispatch broker pointers.
// ABOUTME: Keeps consumer telemetry typed without leaking RabbitMQ.Client types into Application.

namespace Explore.Application.Contracts.Infrastructure;

public enum EmailDispatchConsumeOutcome
{
    Other = 0,
    Acked = 1,
    Rejected = 2,
    Nacked = 3,
    Replayed = 4,
    Parked = 5
}
