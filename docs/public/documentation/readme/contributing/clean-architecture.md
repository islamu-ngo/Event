---
description: Understand layer ownership, CQRS request flow, repository boundaries, and HAL response assembly.
---

# Clean Architecture Conventions

ISLAMU Event strictly enforces Clean Architecture. Business domain rules are completely isolated from web frameworks, persistence mechanisms, and third-party SDKs. Dependencies point inward:

```
Domain ──> Application ──> Persistence / Infrastructure ──> API & BFF Composition
```

*(Note: The arrows describe compile-time project dependencies, not runtime request execution flow).*

---

## Layer Responsibilities

### 1. Domain (`Explore.Domain`)
The core of the system. Owns aggregates, entities, value objects, domain invariants, state machines, and domain events.
* **Strict Rule**: Zero dependencies on EF Core, ASP.NET Core, HTTP, or third-party libraries.

### 2. Application (`Explore.Application`)
Orchestrates business use cases. Owns CQRS commands and queries, MediatR handlers, specifications, authorization requirements, and immutable request/result contracts.
* **Strict Rule**: Validators are manually instantiated (no reflection/DI magic). Repositories return entities, never API DTOs.

### 3. Persistence & Infrastructure (`Explore.Persistence`, `Explore.Infrastructure`)
* **Persistence**: Implements database DbContext, entity type configurations, multi-tenant global query filters, and EF Core migrations.
* **Infrastructure**: Implements external adapters: [Email SMTP](../communications-and-notifications/email-smtp.md), [Storage Providers](../integrations-and-ai/storage.md), [Stripe Payments](../events-and-ticketing/paid-events-and-payouts.md), and [AT Protocol Federation](../federation-and-open-protocols/at-protocol-and-bluesky-jetstream.md).

### 4. API & BFF (`Explore.API`, `Explore.Blazor`)
* **`Explore.API`**: Thin REST controllers that dispatch commands/queries to MediatR, map entities to DTOs, and assemble [Server-Issued HAL Links](../security-and-identity/authorization.md#the-golden-rule-of-client-ui-affordances).
* **`Explore.Blazor`**: Blazor WebAssembly UI and Backend-for-Frontend (BFF) managing encrypted session cookies and proxying API calls.

---

## Core Invariants

* **Identifiers**: Aggregates use UUIDv7 `Guid`; lookup tables use `int`; pagination cursors use `long`.
* **Endpoints**: GET requests default to `[AllowAnonymous]`; state-mutating commands require `[Authorize]`.
* **Concurrent edits**: When an endpoint requires `If-Match`, send the observed non-empty concurrency GUID in double quotes, for example `If-Match: "0194d714-6800-7000-8000-000000000001"`. Bare GUIDs, weak tags, wildcard/list values, and malformed quotes are rejected. If the version is stale, reload the resource before deciding whether to retry the edit.
* **Multi-Tenancy**: Tenant context resolves strictly from ambient session headers, never from untrusted request body parameters (see [Multi-Tenancy Architecture](../security-and-identity/multi-tenancy.md)).
* **Outbox Reliability**: Side effects (email dispatch, webhooks, search indexing) commit to transactional outboxes within the same database transaction (see [Architecture & Request Flows](../getting-started/architecture-and-request-flows.md#2-write-command-flow)).

---

## Related Guides & Next Steps

* **[Local Development Guide](local-development.md)** — Set up your developer environment.
* **[TUnit Testing Conventions](tunit.md)** — Authoring unit and integration tests.
* **[Authorization & HAL Affordances](../security-and-identity/authorization.md)** — Understand why the server issues action links.
* **[Architecture & Request Flows](../getting-started/architecture-and-request-flows.md)** — Detailed sequence diagrams of the CQRS pipeline.
