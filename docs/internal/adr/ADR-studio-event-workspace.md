# ADR — Native Creation And Canonical Studio Event Workspace

> **Status:** Proposed; technical direction and bounded implementation approved, I-VSD r2 plan-aligned; adoption and runtime assurance pending.
> **Date:** 2026-10-12
> **Owner:** Studio/Event architecture
> **Source anchors:** `CreateEventCommandHandler`, `CreateEventGraphSessionDto`, `Routes.razor`, `StudioEventContextState`; repository revision `072f2b6dbd8e4b87e16b795bc02a9ea23f902046`.

## Context

Event creation can atomically persist a rich nested graph, but its UI clears sessions for drafts and uses save-then-navigate for additional sessions. Studio's event root renders details while persisted editor/session flows return outside Studio. Model defaults imply a schedule that the organizer never supplied.

The combined workspace/reporting proposal also introduced complete portfolio reads and a new consented discovery store. Those have different authorization, persistence, disposal and operational responsibilities and can ship independently.

## Decision

Use one native graph contract and transaction. Nullable nested StartTime represents a genuine unscheduled draft; manually instantiated validation distinguishes parent draft/publish intent and existing Fixed/OpenEnded/RelativeToPrayer semantics before rollups and persistence. Existing Domain publication/readiness, owner assignment, tenant-qualified reference authority and transactional outbox remain authoritative.

Prepare optional sessions locally through the existing callback form/timeline/model and one pure generated-contract snapshot mapper. End date and event timezone are explicit. Current native daylight-saving rejection/preserved-offset behavior remains; no new ambiguity selector or draft-storage service is required.

Use `/studio/events/{eventId}` for Event Overview, `/details` for the existing editor and nested session management routes. RouteConfig, page routes and browser link adapters change together; obsolete success/edit management paths are removed without aliases. API HAL URLs remain HTTP URLs. HAL determines UI affordances; native handlers enforce rights.

Accept event/context/module responses by request generation, including force reload, A→B→A and disposal, not only EventId. Dirty navigation uses the existing framework path when proved by real routed tests, with a task-specific adapter only if required.

Complete portfolio events/attendees/orders, operational reports and optional discovery collection are separate contracts. Operational reports do not depend on tracking. Preserve the approved optional built-in analytics choice but revalidate its privacy/retention/withdrawal design in its own workstream.

## Alternatives And Trade-offs

- Save parent before adding sessions is currently available but interrupts local preparation and risks incomplete saved programs.
- A second draft/session model or generic workflow/autosave layer duplicates native graph semantics and requires new lifecycle ownership without a requested need.
- A standalone success screen gives a simple destination but disconnects the organizer from the event's management context.
- One combined reporting/workspace PR preserves one administrative envelope but mixes independent security/data responsibilities and delays useful creation fixes.

The selected approach narrows delivery while retaining rich persisted workflows. Browser-local state does not survive crashes; unknown create outcomes remain unknown and are not automatically retried. Exactly-once create semantics are not introduced by disabling double-clicks.

## Verification And Recovery

Failing draft contract tests precede handler edits; existing atomic/tenant/publication guards remain passing. A selected real SQLite native HTTP gate proves graph persistence and injected pre-commit rollback before the graph commit. Final affected-provider/security/architecture evidence is required; no such product evidence exists at this decision date.

Generated artifacts are API-first. A standalone client build only consumes checked-in schema; API export runs automatically from the client target only in a solution build. Produce API inputs explicitly, then inventory/client, and retain changed outputs with their producer.

No database migration is required solely for transport nullability. Once nullable drafts exist, old code may be unsafe: prefer a forward correction or compatible complete outcome rollback, never silently reset retained data. Route outcomes revert together before release; published history is never rewritten.

## Delivery And Evidence Ownership

The primary active slice contains graph, inline composition and canonical workspace outcomes. Preserved follow-ups are [authorized portfolio](../../../dev/backlog/studio-authorized-portfolio.md), [operational reporting](../../../dev/backlog/studio-operational-reporting.md), and [optional discovery reporting](../../../dev/backlog/studio-discovery-reporting.md). These files are ignored working memory until explicitly graduated in an owned documentation commit; their presence is not execution approval.

This ADR is proposed, not proof of adopted code, runtime accessibility, privacy/legal compliance or user approval. Review provenance uses official documentation facts and repository-native structures without third-party expression.
