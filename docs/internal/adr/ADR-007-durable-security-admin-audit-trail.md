ABOUTME: Decision gate for durable security and administrative audit trails.
ABOUTME: Defines semantic audit boundaries and blocks generic HTTP body capture.

# ADR-007: Durable Security and Administrative Audit Trail

- **Status:** Proposed
- **Date:** 2026-05
- **Deciders:** Core team

Maintenance note, October 26, 2026: the context below records the original
proposal, not a delivered unified audit trail. SecretKit adoption retires the
old Secrets lifecycle logger and polling decorators. Current resolver/health
diagnostics are value-free operational output; they do not provide a durable
secret-read audit trail or satisfy this ADR's proposed semantic audit model.

## Context

The platform already has several audit-adjacent mechanisms, but they serve different purposes and are not yet a unified durable security/admin audit trail:

- `Explore.Domain/AuditLog.cs` stores tenant-owned entity-level create/update/delete snapshots with `EntityType`, `EntityId`, `Action`, `OldValues`, `NewValues`, `AffectedColumns`, `ActorId`, `Timestamp`, and `TenantId`.
- `Explore.Persistence/Repositories/AuditLogRepository.cs` currently exposes template-sync history through `GetTemplateSyncHistoryAsync(...)`.
- `Explore.Persistence/Configurations/Entities/AuditLogConfiguration.cs` configures JSONB value snapshots and tenant/time/actor indexes.
- `Explore.Infrastructure/Services/ConfigurationChangeLogService.cs` records settings changes with actor, setting key, old/new values, scope, and action type.
- `Explore.Application/Notifications/Handlers/SettingAuditLogHandler.cs` writes settings-change audit events to structured logs.
- `Explore.Secrets/Abstractions/ISecretAuditLogger.cs` and `Explore.Secrets/Services/StructuredSecretAuditLogger.cs` define structured logging for secret access, refresh, and initialization events.
- `Explore.API/Middleware/RequestLoggingMiddleware.cs` is intentionally metadata-only and explicitly does not log headers or request/response bodies.

Active MVP work also calls out audit needs for PII reads, admin changes, preference changes, authorization denials, setup-secret validation, and external API key readiness. Those needs require a durable semantic model, not generic HTTP request archival.

## Decision

Adopt a **semantic durable security and administrative audit trail** as the approved direction, but keep implementation blocked until this ADR is accepted and a follow-up implementation slice defines exact schema/API changes.

The audit trail must record security/admin facts as first-class events with this minimum envelope:

- `Id`
- `OccurredAtUtc`
- `TenantId` when tenant-scoped; explicit `Scope` when instance/global
- `ActorId` when authenticated
- `ActorKind` such as `User`, `ApiKey`, `SetupSecret`, `System`, or `Unknown`
- `Action` using a bounded catalog
- `TargetKind`
- `TargetId` when available
- `Outcome` such as `Succeeded`, `Denied`, `Failed`, or `Noop`
- `CorrelationId`
- `RequestId` or trace identifier when available
- `Source` such as `Api`, `Bff`, `Worker`, `Secrets`, or `System`
- `ReasonCode` for denies/failures
- optional redacted metadata constrained by a reviewed allowlist

### Event Categories

Initial durable categories are:

1. **Setup and onboarding** — setup-secret validation, setup completion, setup timeout, bootstrap-sensitive decisions.
2. **Tenant lifecycle and routing** — tenant create/update/archive, custom-domain binding, tenant resolution failures that affect privileged flows.
3. **Instance/admin settings** — instance, tenant, organization, group, and user-scoped governance changes.
4. **Authorization provider changes** — Cerbos/local provider selection, BYO Cerbos endpoint/failure-mode changes, policy sync administrative actions.
5. **Authorization decisions for privileged operations** — denied privileged writes and explicit safe-mode/fail-closed outcomes.
6. **API key lifecycle** — create, rotate, revoke, disable, quota-affecting operations, and privileged API-key authentication failures.
7. **Role and membership lifecycle** — role grants/revocations, event-scoped operational roles, tenant/org/group membership changes.
8. **PII or sensitive read access** — exports, contact-share access, audit-log reads, admin reads of user/contact data.
9. **Template/governance sync actions** — template sync apply, localization governance changes, custom-property governance changes.
10. **Secrets operations** — secret access, refresh, provider initialization, and failures using the existing secret-audit abstraction.

### Explicit Non-Decisions

This ADR does **not** approve runtime implementation by itself. It does not authorize:

- new EF Core migrations;
- new API endpoints;
- new middleware;
- request or response body capture;
- durable archival of HTTP headers, cookies, bearer tokens, setup secrets, provider response bodies, or raw exception text;
- replacing `RequestLoggingMiddleware` with durable HTTP audit middleware.

### Request/Response Body Policy

Request and response body capture is **off by default** and is not part of normal audit.

Any future body capture requires a separate ADR or explicit amendment that defines:

- exact endpoints and compliance need;
- opt-in activation;
- content-type allowlist;
- size limits;
- PII/secret masking;
- endpoint opt-out controls;
- retention period;
- tests proving that tokens, secrets, cookies, raw provider responses, and request bodies outside the allowlist are not persisted.

### Middleware Boundary

Middleware-level audit, if later implemented, is limited to semantic boundary events such as authentication failure/success classes, tenant-resolution failure for privileged paths, setup-secret validation outcomes, API-key authentication failures, and authorization denial outcomes.

Normal request telemetry remains the responsibility of `RequestLoggingMiddleware`, OpenTelemetry traces, metrics, and structured logs. Request telemetry must stay metadata-only and must not become a durable body-capture mechanism.

### Persistence Direction

The preferred future implementation is a dedicated security/admin audit model rather than stretching the current entity-change `AuditLog` record into every security use case.

Reasons:

- Existing `AuditLog` is entity-change shaped (`EntityType`, old/new JSON snapshots, affected columns).
- Security/admin audit needs outcome, actor kind, source, reason code, target kind/id, correlation, and retention semantics.
- Existing `ConfigurationChangeLog` and secret audit logging should be coordinated, not silently duplicated.

The implementation plan must decide whether to:

1. add a new `SecurityAuditEvent`/`AdministrativeAuditEvent` table;
2. adapt existing audit tables with a compatibility-breaking reshape while the product is still pre-v1;
3. route some categories to structured logs only and reserve durable rows for compliance-critical events.

That choice requires follow-up schema design and migration review. Applied migrations must not be edited.

### Retention and Cleanup

Implementation must define category-specific retention before writing durable rows:

- security/admin events: default retain at least 365 days unless the deployment config chooses a stricter policy;
- secret-operation audit events: retain according to security policy and never include secret values;
- failed privileged authorization/setup/API-key attempts: retain long enough for abuse investigation;
- soft-deleted tenant data: cleanup must preserve audit rows required for incident/accountability review until retention expires.

Cleanup must be explicit, observable, and safe-by-default. No cleanup task may hard-delete dead-letter or compliance-critical audit data without a documented operator action or retention policy.

### Failure Behavior

Audit persistence failures must be classified by event criticality:

- **Fail-closed candidates:** setup completion, API key lifecycle changes, authorization-provider changes, role/membership grants, policy changes, and tenant lifecycle mutations.
- **Fail-open with warning candidates:** duplicate/non-critical metadata events and best-effort read-access audit events where blocking the user would create higher product risk.

The follow-up implementation must choose fail-open/fail-closed behavior per category, emit metrics/logs for audit write failures, and document operator remediation.

### Operator Runbook Requirements

Before runtime implementation is accepted, docs must cover:

- how to verify audit ingestion health;
- how to query by tenant, actor, target, action, outcome, and correlation ID;
- how to inspect audit write failures;
- retention and cleanup commands/tasks;
- backup/restore implications;
- how to detect accidental sensitive data capture;
- what must never be deleted manually;
- how to handle audit storage saturation.

### Enablement Rule

Runtime implementation may start only after:

1. this ADR is accepted or explicitly amended;
2. the first implementation slice names exact event categories and target files;
3. schema/storage choice is approved;
4. retention and cleanup policy are documented;
5. failure behavior is defined per category;
6. tests cover tenant isolation, redaction, forbidden body/header capture, failure handling, and query paths.

## Alternatives Considered

### Capture HTTP request/response previews by default

Rejected. Generic body capture creates privacy, GDPR, secret-leakage, storage, and reviewability risks. It also duplicates observability telemetry without providing a clean business/security action model.

### Extend `RequestLoggingMiddleware` into durable audit middleware

Rejected. `RequestLoggingMiddleware` is intentionally metadata-only and logs operational request telemetry. Durable audit should be semantic and category-driven, not every-request archival.

### Use only structured logs and Loki

Rejected as the full answer. Structured logs are useful for operations and existing secret/settings audit surfaces, but durable security/admin audit requires queryable retention, tenant isolation, backup/restore semantics, and cleanup policy.

### Reuse `AuditLog` for every security/admin event

Deferred. Existing `AuditLog` is entity-diff shaped and may not fit security outcomes cleanly. Reuse is allowed only if follow-up design proves it can represent outcome/reason/source/actor-kind/correlation without abusing old/new JSON fields.

## Consequences

1. Runtime audit implementation remains blocked until this ADR is accepted and implementation details are scoped.
2. Audit work becomes action/category-driven instead of middleware/body-capture driven.
3. Existing settings, secret, configuration, and entity audit mechanisms must be reconciled during implementation to avoid duplicate or conflicting audit trails.
4. Privacy and secret-safety rules become explicit before any storage expansion.
5. Operators get a required runbook and retention model before durable data growth starts.
6. MVP launch audit needs can proceed as smaller semantic slices after approval.

## Revisit Triggers

Revisit this ADR if:

- a legal/compliance requirement mandates body capture for a specific endpoint family;
- audit storage volume threatens operational reliability;
- a tenant BYO audit sink/SIEM integration becomes a product requirement;
- external API keys ship and require different abuse/audit retention;
- impersonation/admin delegation ships and needs a specialized audit entity;
- the current `AuditLog` table is reshaped or replaced.

## Related

- [OPERATIONS.md](../OPERATIONS.md) — health, metrics, request protections, runbook entry points.
- [SECURITY-MODEL.md](../SECURITY-MODEL.md) — BFF, authorization, trust, and secret boundaries.
- [ADR-001](ADR-001-authorization-provider-architecture.md) — authorization provider architecture.
- [ADR-002](ADR-002-outbox-pattern.md) — durable reliability and dead-letter precedent.
- [ADR-006](ADR-006-custom-properties-runtime-boundary.md) — bounded runtime extension precedent.
- `Explore.API/Middleware/RequestLoggingMiddleware.cs` — metadata-only request logging boundary.
- `Explore.Domain/AuditLog.cs` — existing entity-level audit record.
- `Explore.Infrastructure/Services/ConfigurationChangeLogService.cs` — existing configuration-change audit service.
- Historical `Explore.Secrets/Abstractions/ISecretAuditLogger.cs` — retired during SecretKit adoption, not a current audit contract.
- `dev/active/mvp-launch/` — MVP audit hardening and launch gating notes.
