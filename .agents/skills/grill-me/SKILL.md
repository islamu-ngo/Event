---
name: grill-me
description: "Load when the user asks to be grilled or stress-tested on a plan, OR when starting Tier 0 (Sovereign), Tier 1 (Security), or Tier 2 (Privacy) tasks with underspecified requirements."
type: workflow
enforcement: suggest
priority: medium
# Grill-Me & Proactive Criticality Intake

## Rules

- Ask exactly one decision question per response and wait for the user's answer.
- **Reader-First Decision Briefs**: Follow [reader-first writing](../conventional-commit/resources/reader-first-writing.md#feedback-and-decision-briefs). Questions must be completely self-contained; never reference bare task IDs, phase codes, or internal file paths without explaining their functional context inline. The developer must be able to evaluate the choice without opening an implementation plan.
- **Structure Each Request**:
  1. *Context & Problem*: What decision is required, who is affected, and why it matters now.
  2. *Practical Alternatives*: Explain the concrete consequences of each option for users, operators, and data integrity.
  3. *Technical Mechanisms & Trade-offs*: Name the relevant components, data flows, and architectural constraints.
  4. *Recommended Answer*: State the recommended option and its rationale clearly before asking.
  5. *Immediate Next Action*: State what the agent will execute the moment the choice is made.
- Resolve upstream decisions before asking about choices that depend on them.
- When repository evidence can answer a question, inspect the codebase directly and treat the item as resolved instead of questioning the user.
- Continue until every relevant architectural branch is resolved and both sides share the same understanding.

### Balanced Decision Request Example

> **Decision Needed**: How should expired search snapshots handle subsequent pagination requests?
>
> **Context & Consequence**: When an attendee browses event listings, the search engine captures a stable snapshot of matching IDs. If they leave the tab open and click "Next Page" after the snapshot expires, the application must decide how to respond.
> - **Option A (Automatic Refresh)**: Silently capture a fresh snapshot and return the next batch. This saves the user a click, but if events were added or removed in the interim, listings can shift unexpectedly, causing duplicate or skipped cards.
> - **Option B (Recommended — Explicit Restart)**: Return HTTP 410 Gone and present a clear button: "Search results updated — Show new results". This keeps changes visible and prevents disorienting attendees mid-browse. Technically, both approaches create a new snapshot; the difference is whether the user is aware of the shift.
>
> **Immediate Next Action**: Implement the selected error handling in `GetPublicEventDiscoveryRequestHandler.cs`.

## High-Criticality Intake Decision Trees

When triggered by the [criticality-guardrail](file:///home/amir/ISLAMU/Github/Event/.agents/skills/criticality-guardrail/SKILL.md) for high-criticality intents, focus questions on:

1. **Tier 0 Sovereign (Money / Stripe Connect)**:
   - Hold expiration vs. payment finalization race resolution.
   - Payout authority routing (OrganizerDirect vs. Instance Admin).
   - Partial refund and fee allocation boundaries.
2. **Tier 1 Security (Auth / Tenancy / Migrations)**:
   - Fallback order for token extraction (`sub` -> `nameidentifier` -> `sid`).
   - Cross-tenant data isolation and fail-closed defaults.
   - Expand/Contract schema evolution and zero-downtime rolling deployment.
3. **Tier 2 Privacy (PII / Erasure)**:
   - Erasure authority commit ordering (authority-first vs. local purge).
   - Anti-resurrection fencing for erased users.
   - Receipt token entropy and cryptographic hashing.

## Workflow

1. Find the nearest unresolved decision that gates the remaining design.
2. Resolve it from repository evidence when possible; otherwise recommend an answer and ask one question.
3. Use the answer to select the next dependent branch, revisiting earlier decisions when it exposes a conflict.
4. When no relevant branches remain, summarize the agreed decisions, assumptions, and open risks.
