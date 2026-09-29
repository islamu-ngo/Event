<!-- ABOUTME: Cold-start agent benchmark suite. Proves the context system delivers measurable contribution success. -->
<!-- ABOUTME: Scenarios live in cold-start-tasks.yaml and are executed manually by a fresh agent session. -->

# Cold-Start Benchmarks

> **Purpose**: Empirically verify that a zero-knowledge AI agent, entering this repo with no prior session memory, can produce a correct, architecture-compliant change using only the context system (`AGENTS.md` → intents → rules → skills → agents).
>
> **Hypothesis under test**: If the context system is well-designed, a cold-start agent should pass each benchmark scenario without human hand-holding.

Last Updated: 2026-08-30

---

## 1. Files

| File | Purpose |
|---|---|
| [`cold-start-tasks.yaml`](cold-start-tasks.yaml) | 13 reference scenarios with acceptance criteria, context budgets, and expected intent classification |

The YAML structure is documented by the scenario format below.

---

## 2. How to Run a Benchmark

Benchmarks are **manual** today. A future enhancement may wire them into CI as an optional nightly job.

### Procedure (manual)

1. Open a **fresh Claude Code session** (no prior conversation memory in this workspace).
2. Pick one scenario from [`cold-start-tasks.yaml`](cold-start-tasks.yaml).
3. Paste the scenario's `prompt` verbatim to the agent. Do **not** add hints.
4. Observe and record:
   - Did the agent correctly classify the intent? (`intent_id` match)
   - Did it read the documented `expected_must_reads`?
   - Did it make changes only inside `expected_paths_in_scope`?
   - Did it run the `expected_verification_commands`?
   - Does the final diff pass the scenario's `acceptance_criteria`?
   - First-turn input tokens, maximum live context, cumulative input/cache-read tokens, and tool-result bytes.
   - Duplicate unchanged bytes by content hash, full-file reads, full intent-registry reads, and scout result sizes.
5. Mark the scenario `PASS` / `FAIL` / `PARTIAL` in a benchmark-report file under `dev/_journal/benchmark-reports/YYYY-MM-DD-<scenario-id>.md`.

### Scoring

| Outcome | Meaning |
|---|---|
| `PASS` | All acceptance criteria met, correct intent classification, tests green, and every top-level `context_budget` limit satisfied. |
| `PARTIAL` | Correct result with one acceptance miss or one context-budget breach that did not affect correctness. |
| `FAIL` | Incorrect classification OR missed ≥ 2 acceptance criteria OR tests red. |

Target: ≥ 11 of 13 scenarios `PASS` on first attempt. If < 11 pass, the context system needs repair (see §4).

Context is product quality: prompt caching may reduce cost but does not satisfy a live-context or duplicate-content budget.

---

## 3. Scenarios (v1)

Thirteen scenarios covering the primary contribution surfaces:

| ID | Intent Mapped | Acceptance Summary |
|---|---|---|
| `add-get-endpoint` | `add-get-endpoint` | New `[HttpGet]` controller action with named route + output cache + HAL link policy |
| `add-hal-link` | `add-hal-link` | New entry in an existing `*LinkPolicy.cs` producing an `edit`/`cancel`/etc. link |
| `add-cqrs-handler` | `add-cqrs-handler` | New `Command` + `Handler` + `Validator` with manual instantiation, returning `BaseCommandResponse<Guid>` |
| `add-ef-migration` | `add-ef-migration` | New migration creating a table with UUIDv7, auditing fields, `SoftDelete` query filter |
| `update-repository-query` | `update-repository-query` | Add `IQuerySpecification<T>` filter preserving tenant isolation and `AsNoTracking` |
| `blazor-component-affordance` | `blazor-component-affordance` | MudBlazor v9 Edit button gated by `dto.HasHalLink("edit")` |
| `bff-auth-bug` | `bff-auth-bug` | Fix a UserId fallback failure returning 401 on valid Keycloak token |
| `cerbos-policy-change` | `cerbos-policy-change` | New Cerbos policy file + `AuthorizationParityTests` passes |
| `external-infrastructure-bootstrap` | `external-infrastructure-bootstrap` | Setup-time external infrastructure onboarding with bounded secrets and recovery evidence |
| `webhook-delivery-redesign` | `webhook-delivery-redesign` | Provider-neutral durable webhook delivery and reconciliation contract |
| `registration-data-collection` | `registration-data-collection` | Cross-layer registration workflow with tenant, durability, API/HAL, and provider boundaries |
| `test-suite-rationalization` | `test-suite-rationalization` | Test architecture cleanup with invariant disposition and runtime-isolated fast lanes |
| `secrets-authority` | `secrets-authority` | Deployment-owned secret authority, zero-secret boundaries, generated migrations, and replica-safe recovery |

See [`cold-start-tasks.yaml`](cold-start-tasks.yaml) for the full scenario schema (prompt, expected intent, paths, tests, acceptance criteria).

---

## 4. What to Do When a Benchmark Fails

A failing benchmark is a **signal**, not a bug. Triage in this order:

1. **Intent misclassification** → the agent picked the wrong intent from `intents.yaml` or no intent.
   - Fix: tighten `triggers` in the correct intent; add disambiguating keywords.
2. **Context overflow or duplication** → the agent loaded full registries/files, repeated unchanged context, or returned raw scout output.
   - Fix: improve intent routing, retrieve one heading/symbol, deduplicate by `path + heading/symbol + revision`, or tighten the scout output contract. Do not solve this by adding more must-reads.
3. **Missing evidence** → a concrete decision lacked required context.
   - Fix: add the smallest relevant heading or symbol to the intent/skill route; a whole-file must-read is the last resort.
4. **Out-of-scope edits** → the agent touched files outside the intent's `paths_in_scope`.
   - Fix: sharpen `paths_in_scope` / `paths_forbidden` on the intent.
5. **Skipped verification** → the agent did not run `verification_commands`.
   - Fix: reinforce the verification policy in `AGENTS.md` §7 and the intent's `pr_checklist`.
6. **Architecture violation** → the agent produced code that breaks a rule in `docs/QUICK_REFERENCE.md`.
   - Fix: promote the rule into the matching `.agents/rules/*.md` so it auto-loads when editing that path.

Record every failure in a benchmark-report file (see §2). Promote recurring failures into the journal with `PROMOTION_RULES.md` guidance.

---

## 5. Refining Scenarios

The v1 scenarios are **synthetic**. Over the next 4–6 weeks, replace each with a **real closed issue or PR** from this repo that exercised the same surface. That will give the benchmarks ecological validity.

To propose a refinement:

1. Open the scenario YAML.
2. Replace the `prompt` with the original issue text (or an anonymized version).
3. Update `acceptance_criteria` to match the merged PR's diff scope.
4. Commit with the scenario ID in the commit subject.

---

## 6. Related

- [`.agents/contract/intents.yaml`](../contract/intents.yaml) — intent registry that scenarios reference.
- [`AGENTS.md`](../../AGENTS.md) — the contract being validated.
- [`dev/_journal/PROMOTION_RULES.md`](../../dev/_journal/PROMOTION_RULES.md) — how to promote recurring benchmark failures.
