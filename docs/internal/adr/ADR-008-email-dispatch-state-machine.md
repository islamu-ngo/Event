ABOUTME: Decision record for Basic EmailDispatch state machine and optional RabbitMQ transport.
ABOUTME: Defines durable-intent ownership, dispatch profiles, and side-effect boundaries.

# ADR-008: Email Dispatch State Machine And Dispatch Profiles

- **Status:** Accepted
- **Date:** 2026-05
- **Deciders:** Core team

## Context

ISLAMU Event needs reliable event-lifecycle email delivery, starting with registration confirmation. The platform already has PostgreSQL, EF Core, CQS operations, SMTP abstraction, tenant-scoped configuration, a general transactional outbox, and specialized outbox precedent. It also has an optional RabbitMQ transport workstream for EmailDispatch pointers, but RabbitMQ must not become mandatory for self-hosters.

CRMWorx analysis showed that durable side effects become operable when business state, attempts, receipts, retries, dead-letter, parking, and unknown outcomes are persisted before transport is involved. The CTO-approved direction for ISLAMU Event is to adapt that lesson without copying CRMWorx architecture and without centering RabbitMQ.

## Decision

Use a PostgreSQL-owned `EmailDispatchOutbox` state machine for email side effects.

`EmailDispatchOutbox`, `EmailDispatchAttempt`, `EmailDispatchReceipt`, and `EmailDispatchTenantControl` are specialized durable state for email dispatch. Application handlers create durable intent only. Background workers own SMTP or future broker side effects.

### Dispatch Profiles

| Profile | Runtime shape | Decision |
|---|---|---|
| Dispatch disabled | API + PostgreSQL, worker disabled | Valid when operators intentionally do not send email; health must make this visible. |
| Basic Dispatch Mode | API + PostgreSQL + SMTP provider | Default and first implemented mode. Registration confirmation works without RabbitMQ. |
| RabbitMQ Dispatch Mode | API + PostgreSQL + RabbitMQ + SMTP provider | Optional transport profile. It must reuse the same PostgreSQL state machine. |

### Side-Effect Boundary

Handlers, controllers, automation executors, sequence processors, and domain services must not send SMTP directly or publish RabbitMQ directly. They may only create durable intent or invoke Application-layer orchestration that persists durable intent.

The approved direct side-effect locations are Infrastructure/API background dispatch components and provider adapters, guarded by architecture tests.

### Basic Dispatch Mode Flow

```text
Registration handler
  -> same serializable registration transaction creates EmailDispatchOutbox row
  -> EmailDispatchProcessor claims due row from PostgreSQL
  -> worker rebinds tenant context
  -> worker sends through IEmailService / SMTP abstraction
  -> worker records EmailDispatchAttempt and EmailDispatchReceipt
  -> worker transitions row to Sent, RetryScheduled, DeadLettered, Parked, or Unknown
```

### Transport Capability Gate

The prior generic MQContract/provider-abstraction direction is retired for this EmailDispatch workstream. EmailDispatch must use a capability-aware transport adapter, not a semantics-free message bus. PostgreSQL remains the durable source of truth, while RabbitMQ is an optional pointer transport that must prove the broker capabilities required for reliable settlement.

RabbitMQ Dispatch Mode is allowed only through the Event-specific transport adapter that proves:

- pointer-only payloads;
- stable publish event identity;
- mandatory routing;
- publisher confirms;
- returned/unroutable handling;
- manual ack/reject/nack;
- bounded prefetch;
- DLQ and parking topology;
- health checks and metrics;
- graceful shutdown;
- duplicate-consume idempotency through persisted receipts.

Kafka or other future provider modes are out of scope for this ADR and this workstream. Reintroducing a platform integration-event broker or a cross-provider abstraction requires a separate product requirement, ADR, failure model, and verification plan. Basic Dispatch Mode remains independent of all broker providers.

## Consequences

1. Basic self-hosters can run registration confirmation with API + PostgreSQL + SMTP only.
2. RabbitMQ outages or missing RabbitMQ configuration do not affect Basic Dispatch Mode.
3. PostgreSQL remains the source of truth for dispatch status and operator diagnostics.
4. The system accepts at-least-once delivery risk and records ambiguous provider outcomes as `Unknown` instead of blind retry.
5. Operators can inspect safe status fields without exposing recipient/body/secret/provider-error content.
6. Future replay, parking, pause/resume, and RabbitMQ features must use the same state machine and HAL-gated actions.
7. Architecture tests enforce the no-direct-SMTP/no-direct-RabbitMQ boundary as the codebase evolves.

## Related

- [OUTBOX_PATTERN.md](../OUTBOX_PATTERN.md) — transactional and specialized outbox guidance.
- [ARCHITECTURE.md](../ARCHITECTURE.md) — Clean Architecture and background-service ownership.
- [CONFIGURATION.md](../CONFIGURATION.md) — `EmailDispatchProcessor:*` settings.
- [OPERATIONS.md](../OPERATIONS.md) — health checks, metrics, and operator behavior.
- [SELF_HOSTING.md](../SELF_HOSTING.md) — Basic mode and future optional RabbitMQ profile.
- [ADR-002](ADR-002-outbox-pattern.md) — general transactional outbox pattern.
