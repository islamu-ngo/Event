<!-- ABOUTME: Journal entrypoint: how to record, read, and promote durable findings for ISLAMU Event. -->
<!-- ABOUTME: Complements dev/_journal/journal.md + MAJOR_DECISIONS.md with operational policy. -->

# Dev Journal

> **Purpose**: Capture durable findings — bug root causes, non-obvious patterns, system quirks, and major decisions — that should outlive a single session.
>
> **Not for**: Short-term TODOs, tentative ideas, or anything you would not want to read again in six months.

Last Updated: 2026-09-08

---

## 1. Structure & Domain Ledgers

Findings are organized by domain under `dev/_journal/domains/` to minimize agent context overhead:

| Domain | File Path | Focus Area |
|---|---|---|
| **Persistence** | [`domains/persistence-and-db.md`](domains/persistence-and-db.md) | EF Core, migrations, query filters, Postgres/SQLite, Quartz ADO. |
| **Auth & Tenancy** | [`domains/auth-and-tenancy.md`](domains/auth-and-tenancy.md) | Keycloak, Cerbos policies, BFF cookies, tenant isolation, role grants. |
| **Presentation** | [`domains/presentation-and-blazor.md`](domains/presentation-and-blazor.md) | MudBlazor v9, CSS isolation, HAL affordance gating, Dock Layout. |
| **Application** | [`domains/application-and-messaging.md`](domains/application-and-messaging.md) | MediatR/CQRS handlers, Outbox dispatch, RabbitMQ/MQContract, EAV. |
| **Testing** | [`domains/testing-and-environment.md`](domains/testing-and-environment.md) | TUnit runner, WebApplicationFactory, Podman/Docker, SDK workloads. |
| **Email-Optional Self-Hosting** | [`domains/email-optional-self-hosting.md`](domains/email-optional-self-hosting.md) | Global Local credentials, delivery fences, anonymous recovery/cancellation, and purpose retention. |
| **Index & Recent** | [`journal.md`](journal.md) | Central index and recent cross-cutting stream (last 30 days). |

---

## 2. When to Record a Finding

Open the matching domain ledger under `domains/` (or `journal.md` for cross-cutting insights) and append an entry when you:

1. Discover a **non-obvious behavior** (e.g., middleware ordering that breaks rate limiting, EF Core soft-delete interceptor behavior, MudBlazor v9 runtime quirks).
2. Fix a **bug whose root cause is not visible from the fix alone** (the "what you really needed to know" insight).
3. Make a **design decision** that future contributors will want to understand, but that is not yet mature enough to promote to `docs/GOVERNANCE.md`.
4. Confirm or refute an **assumption from a skill/agent/rule** that was ambiguous.

If your finding is a **system-wide decision** (changes layer boundaries, affects all tenants, sets a new invariant), record it in `MAJOR_DECISIONS.md` instead.

---

## 3. How to Record a Finding

Append to the relevant domain ledger under `domains/` or [`journal.md`](journal.md) using [`FINDING_TEMPLATE.md`](FINDING_TEMPLATE.md):

- Date prefix in the form `[YYYY-MM-DD Europe/Brussels]`.
- One blank line between entries.
- At least one referenced file path or commit SHA.
- Grounded in testable, observed evidence.

---

## 4. Promotion Rules (Journal → Canonical Docs)

A finding is not meant to live in the journal forever. See [`PROMOTION_RULES.md`](PROMOTION_RULES.md) for the promotion policy.

| Signal | Promote To |
|---|---|
| Rule that applies to new code going forward | `docs/QUICK_REFERENCE.md` (non-inferable rule) OR `.agents/rules/*.md` entry |
| Pattern that teaches a way of working | `.agents/skills/*/SKILL.md` |
| Architectural decision | `docs/MAJOR_DECISIONS.md` + ADR under `docs/adr/` |
| Discovery that informs governance / conventions | `docs/GOVERNANCE.md` |
| One-off debugging lesson | **Stays in domain journal** — no promotion needed |

---

## 5. Related

- [`journal.md`](journal.md) — central index & recent findings.
- [`MAJOR_DECISIONS.md`](MAJOR_DECISIONS.md) — system-wide decisions.
- [`FINDING_TEMPLATE.md`](FINDING_TEMPLATE.md) — canonical entry format.
- [`PROMOTION_RULES.md`](PROMOTION_RULES.md) — journal-to-docs promotion policy.
- [`AGENTS.md`](../../AGENTS.md) §8 — Memory & Findings.
