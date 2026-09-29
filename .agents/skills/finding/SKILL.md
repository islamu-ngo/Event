---
name: "finding"
description: "Load when asked to record/log a durable finding, or after discovering a non-obvious reusable root cause or repository behavior that should be appended to `dev/_journal/journal.md`; not for routine progress notes or facts already documented."
type: workflow
enforcement: suggest
priority: medium
---

# finding

## Command Template

<!-- ABOUTME: Append a durable finding to dev/_journal/journal.md using the established template. -->
<!-- ABOUTME: Use for non-obvious behaviors, bug root causes, and insights worth surviving a session boundary. -->

# /finding — Record a Durable Finding

> **Primary references:**
> - [`dev/_journal/README.md`](../../../dev/_journal/README.md) — when and how to record.
> - [`dev/_journal/FINDING_TEMPLATE.md`](../../../dev/_journal/FINDING_TEMPLATE.md) — exact entry format.
> - [`dev/_journal/PROMOTION_RULES.md`](../../../dev/_journal/PROMOTION_RULES.md) — when a journal entry graduates to QUICK_REFERENCE / rule / skill / governance.

## When to Record

Record a finding when ALL of these are true:

1. The fact is **non-obvious** — a reader of the code alone would not infer it.
2. The fact is **durable** — it will still be true in a week, a month, a year.
3. The fact has **reusable value** — another agent or contributor would benefit.

Do NOT record:
- Run-of-the-mill refactoring notes.
- Step-by-step implementation logs.
- Personal opinions.
- Anything already in `docs/internal/QUICK_REFERENCE.md`, `docs/internal/GOVERNANCE.md`, a skill, or a rule (link instead).

## Workflow

### 1. Open the Template

Read [`dev/_journal/FINDING_TEMPLATE.md`](../../../dev/_journal/FINDING_TEMPLATE.md). Copy its block.

### 2. Fill In the Fields

- **Date prefix**: `[YYYY-MM-DD Europe/Brussels]` — today's date in the repository timezone.
- **Title**: one short sentence summarizing the finding.
- **Context**: what you were doing when the finding emerged (1-3 sentences).
- **Symptom / Observation**: the surprising behavior or fact (1-3 sentences).
- **Root Cause**: why it happens, traced to specific files and lines.
- **Resolution**: what you did about it, or what you would recommend.
- **Why This Matters for Future Work**: the reusable insight.
- **References**: `file:line` anchors, related docs, PRs, commit SHAs.
- **Promotion Consideration**: which of the 5 destinations (if any) this entry should graduate to eventually.

### 3. Append to the Journal

Open [`dev/_journal/journal.md`](../../../dev/_journal/journal.md). Append the filled-in block at the end. Leave exactly one blank line between entries.

Do NOT:
- Insert in the middle (chronological order only).
- Delete or edit prior entries (annotate them instead — see PROMOTION_RULES).
- Create a new `.md` file for a single finding.

### 4. Cross-Reference (optional)

If the finding closes a bug that a test should catch, add a comment in the test linking to the journal line. If it documents a pattern, consider opening an intent in `.agents/contract/intents.yaml` or a rule in `.agents/rules/`.

## Anti-Patterns

- ❌ Recording "I learned how to use `dotnet test`." (Not non-obvious.)
- ❌ Recording "This field is a string." (Not durable / not reusable.)
- ❌ Recording a finding that contradicts QUICK_REFERENCE without first updating QUICK_REFERENCE.
- ❌ Deleting old journal entries. They remain even after promotion — add a `→ promoted to X` annotation instead.
- ❌ Logging an entry without the `[YYYY-MM-DD Europe/Brussels]` prefix.

## Promotion Cadence

When an entry has been referenced by ≥2 subsequent entries, cited in a PR review, or generalizes beyond its original feature — it's a candidate for promotion. See [`dev/_journal/PROMOTION_RULES.md`](../../../dev/_journal/PROMOTION_RULES.md).

## Related

- [`dev/_journal/README.md`](../../../dev/_journal/README.md)
- [`dev/_journal/FINDING_TEMPLATE.md`](../../../dev/_journal/FINDING_TEMPLATE.md)
- [`dev/_journal/PROMOTION_RULES.md`](../../../dev/_journal/PROMOTION_RULES.md)
- [`AGENTS.md`](../../../AGENTS.md) §8 Memory & Findings.
- [`AGENTS.md`](../../../AGENTS.md) — closing the loop on a change.
