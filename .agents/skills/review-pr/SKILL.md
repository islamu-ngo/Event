---
name: "review-pr"
description: "Load for pre-PR self-review or pull-request review that must verify the matched intent, allowed scope, required tests/docs, forbidden actions, architecture gates, and merge readiness; not for a general code-quality diff review alone."
type: guardrail
enforcement: block
priority: high
---
<!-- ABOUTME: Pre-PR and PR review checklist driven by the matched intent. -->
<!-- ABOUTME: Verifies paths in scope, tests, docs, rules, and forbidden actions before opening or approving a PR. -->

# review-pr

## Command Template

# /review-pr — Pull Request Review Checklist

> **Primary reference:** [`.agents/contract/intents.yaml`](../../contract/intents.yaml) — every intent owns its `pr_checklist`.
> **Authority:** [`AGENTS.md`](../../../AGENTS.md) §5 CRITICAL RULES.

## When to Run

- **Self-review** — before opening a PR.
- **Review mode** — when asked to review someone else's PR.

## Step 0 — Upstream Freshness Gate

Fetch latest upstream to ensure diffs and merge-bases reflect current `develop`:
```bash
git fetch origin develop
```
Ensure the PR branch is cleanly mergeable with `origin/develop` with zero stale base drift.

## Step 1 — Re-Identify the Intent

Which matching entry in [`intents.yaml`](../../contract/intents.yaml) does this PR implement? If the PR spans multiple intents, review each in turn and merge the checklists without loading the full registry into task context.

## Step 2 — Execute the Intent's PR Checklist

For each item in the intent's `pr_checklist`, collect evidence:

| Checklist Item | Evidence Type |
|---|---|
| Tests added / updated | File paths + `dotnet test` output |
| Docs updated | File paths + diff |
| Rule adherence | Lint / architecture test output |
| Authorization correct | `AuthorizationParityTests` output |
| Contract unchanged (or CHANGELOG entry) | `ApiContractArchitectureTests` output + `docs/internal/API_CHANGELOG.md` diff |

Any item without evidence is a blocker.

## Step 3 — Scope Discipline

Run this mental check on the diff:

1. Is every changed file inside the intent's `paths_in_scope`?
2. Is no changed file inside the intent's `paths_forbidden`?
3. Are the changes minimal (not bundling unrelated refactors)?

If any answer is "no", split the PR or escalate to the user.

### 3.1 Two-Axis Quality Gate
- **Axis 1: Standards & Clean Architecture**: Verify layer boundaries, absence of raw DTO returns in repositories, manually instantiated validators, and check against the Fowler 12-Smell Baseline (no Primitive Obsession, Feature Envy, or Shotgun Surgery).
- **Axis 2: Intent & Spec Fidelity**: Verify every acceptance criterion from the matched intent without unasked-for scope creep or speculative abstractions.

## Step 4 — Verification Evidence

Paste the exact commands and their outputs:

```bash
dotnet build --configuration Release --verbosity quiet
# Expected: exit 0

dotnet test --project <MinimumTestProject>/<MinimumTestProject>.csproj --configuration Release --verbosity quiet
# Expected: Passed
```

For architectural PRs, also run:

```bash
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet
```

## Step 5 — Critical Rules Checklist (applies to every PR)

- [ ] No `as any`, `@ts-ignore`, `@ts-expect-error`, or equivalent C# type-suppression hack.
- [ ] No new `rm`, `mv`, or `>` shell redirection in scripts.
- [ ] No `.env`, credentials, or secrets staged.
- [ ] Self-documenting code: no synthetic `ABOUTME:` headers on code (`.cs`, `.razor`, `.css`, project files); standard C# XML doc comments (`/// <summary>`) where non-inferable.
- [ ] No duplicated content across docs, skills, or agents (point — don't copy).

## Step 6 — Documentation Sanity

- [ ] `docs/internal/QUICK_REFERENCE.md` updated if a new invariant was introduced.
- [ ] `docs/internal/API_CHANGELOG.md` updated if a public contract changed.
- [ ] `dev/_journal/journal.md` appended if a non-obvious finding emerged.
- [ ] `docs/internal/index.md` cross-references still resolve; use [`AGENTS.md`](../../../AGENTS.md) and [`docs/internal/OPERATIONS.md`](../../../docs/internal/OPERATIONS.md) as the canonical verification entrypoints.

## Step 7 — Forbidden-Without-Approval Gate

Cross-check the diff against the intent's `forbidden_without_approval` list. If any item triggers, the PR MUST carry explicit approval in its description or be rejected.

## Step 8 — Output Contract

Produce a summary the reviewer can paste into the PR:

```
Intent: <intent-id>
Files changed: <count> (all in scope ✔)
Build: ✔
Tests: <project list> — all green
Docs: <files> updated
Forbidden actions: none triggered
Outstanding questions: <list or None>
```

## When Review MUST Fail

- Any checklist item lacks evidence.
- Any file is outside `paths_in_scope` or inside `paths_forbidden`.
- Any `forbidden_without_approval` item triggered without approval.
- Any architecture test (`Event.Architecture.Tests.*`) fails.
- Any critical rule violated.

## Related

- [`AGENTS.md`](../../../AGENTS.md) — get to a valid PR state.
- [`docs/internal/OPERATIONS.md`](../../../docs/internal/OPERATIONS.md) — run the repository verification commands.
- [`docs/internal/DOCUMENTATION_STYLE_GUIDE.md`](../../../docs/internal/DOCUMENTATION_STYLE_GUIDE.md) — review documentation accuracy and links.
