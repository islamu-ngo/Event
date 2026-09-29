---
name: work-criticality-matrix
description: Path-scoped rule governing dynamic tier execution, high-leverage adversarial testing, and multi-agent debate across the repository.
paths:
  - "**/*.cs"
  - "**/*.razor"
  - ".agents/contract/intents.yaml"
related_skills:
  - criticality-guardrail
  - epistemic-mad-review
  - grill-me
related_docs:
  - docs/internal/QUICK_REFERENCE.md
  - docs/internal/OPERATIONS.md
  - docs/internal/SECURITY-MODEL.md
minimum_tests:
  - Event.Architecture.Tests
related_intents:
  - registration-data-collection
  - webhook-delivery-redesign
  - platform-privacy-erasure
  - add-write-endpoint
  - bff-auth-bug
  - update-ai-context-disclosure
---
<!-- ABOUTME: Path-scoped rule defining the Dynamic Tier Execution Matrix (DTEM) and operational gates. -->
<!-- ABOUTME: Twin copy at .omo/rules/work-criticality-matrix.md. When modifying this file, update both paths. -->

# Dynamic Work Criticality Matrix

> **Applies to:** All C# backend files, Blazor components, and agent contract definitions across the solution.  
> **Authority:** `AGENTS.md` § 3, `.agents/CONTEXT_ENGINEERING.md`, and `docs/internal/QUICK_REFERENCE.md`.

## Rules (Correct / Wrong)

| # | Rule | Correct | Wrong |
|---|---|---|---|
| 1 | **Proactive Intake Clarification** | Trigger `/grill-me` alignment on Tier 0/1/2 tasks before writing code if requirements have ambiguity. | Jumping directly into coding Tier 0 financial handlers with unverified assumptions. |
| 2 | **Model Tier Selection** | Use `advanced` model tier for Tiers 0–2 (Sovereign/Security/Privacy) and `economical` for Tier 4. | Using low-capability models for money/security logic or high-cost models for simple file reading. |
| 3 | **Testing Done Right** | Write failing Invariant-Breaker tests first (concurrency races, double capture, tenant header spoofing). | Relying solely on happy-path unit tests with extensive mocks that hide concurrency races. |
| 4 | **Exploration Depth** | Trace full caller/callee trees, outbox linkages, and DB lock contention for high tiers via knowledge graph. | Scoping exploration to a single local file when editing cross-tenant security boundaries. |
| 5 | **Multi-Agent Deliberation** | Execute Epistemic MAD with Response Anonymization and weighted voting on Tier 0–2 pull requests. | Merging sovereign or security mutations without independent specialist persona review. |
| 6 | **Zero Log-PII Invariant** | Apply `[PiiData]` / `[SensitiveData]` attributes and use `StarRedactor` / `HmacRedactor` for all telemetry. | Passing raw user emails, tokens, cardholder fragments, or billing payloads to `ILogger`. |
| 7 | **Economical Path Autonomy** | Allow Tier 4 (UI/CSS/Docs) tasks to proceed autonomously with minimal friction and fast local checks. | Forcing heavy multi-agent debate and exhaustive interview cycles on minor CSS or doc edits. |

## Must-Reads for This Path

- `AGENTS.md` — Section 3 Cold-Start Flow & Dynamic Criticality Alignment.
- `docs/internal/QUICK_REFERENCE.md` — Authoritative hard invariants and multi-tenancy rules.
- `.agents/rules/payments-commerce.md` — Tier 0 Sovereign financial invariants.
- `.agents/skills/criticality-guardrail/resources/adversarial-archetypes.md` — Invariant-Breaker adversarial test recipes (concurrency, spoofing, replay, PII).
- `.agents/rules/auth-trust-boundaries.md` — Tier 1 Security trust boundaries.
- `.agents/rules/privacy-and-pii.md` — Tier 2 Privacy and erasure rules.

## Anti-Patterns (Forbidden on These Paths)

- **Uniform Trust**: Treating a Stripe webhook handler or privacy-erasure routine with the same governance as a UI component.
- **Mock-Heavy Security Verification**: Asserting security correctness via unit-test mocks rather than real multi-provider test containers and Postgres transactions.
- **Unredacted Logging**: Logging unmasked PII, credentials, or third-party provider payloads in ProblemDetails, logs, or metrics.
- **Bypassing Outbox**: Emitting external side effects or integration events directly in API controllers instead of the local Outbox table.

## Verification

- `dotnet build --configuration Release --verbosity quiet`
- `dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet`

## Related

- Skills: [criticality-guardrail](file:///home/amir/ISLAMU/Github/Event/.agents/skills/criticality-guardrail/SKILL.md), [epistemic-mad-review](file:///home/amir/ISLAMU/Github/Event/.agents/skills/epistemic-mad-review/SKILL.md), [grill-me](file:///home/amir/ISLAMU/Github/Event/.agents/skills/grill-me/SKILL.md)
- Docs: [docs/internal/QUICK_REFERENCE.md](file:///home/amir/ISLAMU/Github/Event/docs/internal/QUICK_REFERENCE.md), [docs/internal/OPERATIONS.md](file:///home/amir/ISLAMU/Github/Event/docs/internal/OPERATIONS.md)
