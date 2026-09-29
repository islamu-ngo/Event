<!-- ABOUTME: Architectural decision record for the AI Context Disclosure Policy. -->
<!-- ABOUTME: Establishes field-level PII classification, gateway, consent, and provider-trust rules for AI flows. -->

# ADR-012: AI Context Disclosure Policy

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-06-28 |
| **Deciders** | ISLAMU Event Platform — Architecture, Security, AI workstreams |
| **Supersedes** | None |
| **Superseded by** | — |

## Context

The AI Assistant (Blazor rail + `Explore.Application/Features/AiAssistant/**` + `Explore.API/Mcp/AiAssistantMcp*`) currently exposes a **flat, single-context allow-list** to AI prompts:

- `AiSafeDataContextRegistry.CreateDefault()` registers a single context kind (`event-reference-summary`) mapped to `AiReferenceSearchResultDto` with eight fields (`kind`, `referenceId`, `displayName`, `summary`, `firstSessionDate`, `lastSessionDate`, `visibility`, `format`).
- `AiReferencePromptPacker.BuildReferenceBlock()` renders only three of those fields (`kind`/`id`/`displayName`/`summary`) into the prompt XML with hard limits of 500 characters per reference and 2 000 characters total.
- `ProcessAiRunCommandHandler.BuildSelectedReferenceContextAsync` enriches references via `GetEventDetailsRequest` and builds a comma-separated summary string (`status, format, visibility, host, subtitle, description, dates, timezone, sessions count`) — the visible symptom of the user-reported bug.

This bounded design was an intentional security-by-default decision, but it has two consequences:

1. **Functional gap:** Users cannot ask the AI for session detail, speakers, languages, audience composition, or attendance for a referenced event (the original bug in this workstream).
2. **Latent risk:** There is no centralized, machine-checked policy that prevents future contributors from widening DTOs or repository methods in ways that leak regulated PII (user email, names, addresses, precise geo) into AI prompts, logs, or transcript storage.

A field-by-field opt-in is also insufficient because the effective disclosure decision depends on the **intersection** of viewer scope, tenant policy, user consent, provider trust tier, and Phase-gated capability flags. That intersection must be evaluated at a single point — the AI Context Gateway (Phase 2) — backed by a classified registry.

## Decision

Adopt the **AI Context Disclosure Policy** as the authoritative framework for any AI flow that reads platform data and emits it to a model provider, a prompt transcript, or an MCP tool response.

### Components (in execution order)

1. **Classification enums** (`Explore.Domain/Enums/`): `AiContextSensitivityEnum`, `AiContextDisclosureRuleEnum`, `AiProviderTrustTierEnum`, `AiAdministrativeContextScopeEnum`.
2. **Disclosure registry** (`Explore.Application/Features/AiAssistant/Disclosure/`): `AiContextDisclosureRegistry` seeded from `dev/active/ai-context-disclosure-policy/field-classification-matrix.md`, with `AiContextDisclosureEntry` rows per persisted `*Pii` property.
3. **Governing policy doc** (`docs/AI_CONTEXT_SECURITY.md`): the human-readable counterpart to this ADR.
4. **Reflection test** (`Event.Architecture.Tests/AiContextDisclosureSchemaTests.cs`): machine-enforces that every `*Pii` property has a registry entry.
5. **AI Context Gateway** (`IAiContextGateway` — Phase 2): the single evaluation point where effective disclosure rules are computed and where sanitized envelopes are emitted. No AI flow may bypass it.
6. **Consent engine** (Phase 2): `AiContextConsent` domain model capturing per-user, per-tenant consent records.
7. **Transcript hygiene** (Phase 4): persistence max-sensitivity flag, log redaction, deletion propagation.
8. **MCP narrowing** (Phase 5): every MCP tool routes through the gateway and exposes the same metadata.
9. **UI + JIT consent + admin deployment modes** (Phase 6).

### Non-decisions

- **No broad instance-admin AI.** Instance administrators receive AI access only via `AiAdministrativeContextScopeEnum` (aggregate/redacted/operational). Row-level user PII is never authorized through the general AI assistant (CTO correction #1).
- **No PII disclosure in Phases 1–3.** All `Confidential` and `Restricted` fields stay `Deny` until Phase 4 completes the persistence max-sensitivity flag, log redaction, and deletion propagation prerequisites, and Task 4.4 performs the gated flip (CTO correction #5).
- **No naming-based provider trust.** Provider trust tiers are evidence-based, not naming-based (CTO correction #6). `Unknown` always evaluates as most-restrictive.
- **No user-consent override.** The policy hierarchy is `instance ∩ tenant ∩ user consent`. A user's consent can never override an instance or tenant deny (CTO correction #7).

### Explicit boundaries

- **Navigation properties are out of scope.** The reflection test classifies persisted public properties only; navigation properties (`UserPii.User`, `ActorPii.Actor`, etc.) are intentionally not registered.
- **Aggregates are first-class.** Aggregate values (registration counts, city-level geo buckets) are emitted without row-level consent and follow `Internal` sensitivity by default.
- **Special-category data is never disclosed.** Any future field classified `AiContextSensitivityEnum.Special` is `Deny` at every provider trust tier, including `LocalInProcessOrSameNetworkModel`.

### Persistence direction

The registry is **read-only application state**. Consent records are persisted domain entities (Phase 2). The gateway is a stateless application service that resolves the effective rule from the registry, viewer scope, consent, provider trust, and phase flags.

### Retention

- Consent records are retained per the user-data lifecycle (deleted on user deletion).
- AI transcripts retain only fields whose effective disclosure rule was `Allow` at write time (Phase 4).
- Logs are redacted of `Restricted`/`Special` values regardless of viewer.

### Failure behavior

- **Gateway failure → fail closed.** Any exception during rule resolution returns `Deny`.
- **Provider trust evidence missing → tier = `Unknown`.**
- **Tenant policy missing → policy = base registry.**
- **User consent missing → consent = `Deny`.**

### Operator runbook

When the AI assistant returns empty context for a referenced entity, check in this order:

1. Provider trust tier classification in tenant settings.
2. Tenant-level policy overrides (when supported).
3. User consent records for the requested field.
4. Phase gating flag (`AiContextDisclosureOptions.PiiDisclosureEnabled`).

### Enablement rule

Phased rollout per `dev/active/ai-context-disclosure-policy/ai-context-disclosure-policy-tasks.md`:

- Phases 1–3 enable `Public`/`Internal` disclosure only.
- Task 4.4 flips the `PiiDisclosureEnabled` flag after Tasks 4.1–4.3 verify persistence, log, and deletion controls.
- Phase 5 enables MCP narrowing.
- Phase 6 enables UI affordances and admin deployment modes.

## Alternatives Considered

1. **Ad-hoc widening of `EventDto` / `AiSelectedReferenceDto`** — Rejected. Would violate the existing security boundary used by other consumers and bypass the registry/gateway; no machine-checked drift control.
2. **Per-DTO opt-in allow-list (extend `AiSafeDataContextRegistry`)** — Rejected. Sufficient for field lists but does not model the consent × provider-trust × phase-gating intersection; would still permit accidental PII leakage through new DTOs.
3. **Centralized field-classified registry + gateway (this decision)** — Accepted. Single evaluation point, machine-checked completeness, evidence-based trust, intersection policy hierarchy, and phase-gated rollout.

## Consequences

1. `Features/AiAssistant/**` and `Explore.API/Mcp/**` may not depend on `*Pii` entities or broad repositories except via `IAiContextGateway` (Phase 3 architecture test).
2. Every persisted public property on every `*Pii` entity must have a registry entry (Phase 1 reflection test).
3. New PII-bearing entities require matrix, registry, and test updates before merge.
4. AI prompt transcripts and logs must honor the redaction rules from Phase 4 onward.
5. The original event-context bug (compressed blocks b1–b12) becomes a downstream consumer — the rich event-context retrieval path is built **on top of** the gateway in a later phase.

## Revisit Triggers

- A fifth `*Pii` entity is introduced.
- A new AI provider integration requires a different trust-tier evidence model.
- A new deployment mode (e.g., sovereign cloud) is added.
- A GDPR special-category field is added to the platform.
- A new AI consumer (e.g., agentic tools) is added outside `Features/AiAssistant/**`.

## Related

- **Plan:** `dev/active/ai-context-disclosure-policy/ai-context-disclosure-policy-plan.md`
- **Tasks:** `dev/active/ai-context-disclosure-policy/ai-context-disclosure-policy-tasks.md`
- **Matrix:** `dev/active/ai-context-disclosure-policy/field-classification-matrix.md`
- **ADRs:** ADR-001 (authorization-provider-architecture), ADR-007 (durable-security-admin-audit-trail), ADR-010 (mcp-adapter-hosting-strategy), ADR-011 (local-mcp-stdio-diagnostic-host).
- **Docs:** `docs/SECURITY-MODEL.md`, `docs/AUTHORIZATION.md`, `docs/AI_RAG_FOUNDATION.md`, `docs/AI_AGENT_CONTRACT_INVENTORY.md`, `docs/AI_CONTEXT_SECURITY.md`.
- **Source:** `Explore.Domain/Enums/AiContext*.cs`, `Explore.Application/Features/AiAssistant/Disclosure/AiContextDisclosureRegistry.cs`.
