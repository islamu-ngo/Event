<!-- ABOUTME: Input contract for Senior CTO review of implementation-plan workstreams and missing-context handling. -->
<!-- ABOUTME: Defines what artifacts to read, what minimum evidence is required, and how to refine partial implementation-plan inputs safely. -->
# Input Contract

Use this file to determine whether enough material exists for a useful Senior CTO review and how to proceed when the input is partial. Refinements are applied directly into the active workstream triad without creating review files.

## Preferred Input Shape

Best input is an `implementation-plan` workstream:

- `dev/active/[task-name]/[task-name]-plan.md`
- `dev/active/[task-name]/[task-name]-context.md`
- `dev/active/[task-name]/[task-name]-tasks.md`
- `islamic-value-sensitive-design/workstreams/i-vsd-[task-name].md`

Also useful:

- `.agents/skills/implementation-plan/SKILL.md` and its resources
- referenced repository docs, rules, skills, and source files;
- related `dev/active/` or `dev/pause/` workstreams;
- a concrete user goal such as “review this before implementation” or “rewrite this plan to be executable.”

Review the canonical triad in the main checkout. A scoped execution copy in a worktree is the live ledger for one delegated PR only; it is not a review input that can replace canonical sections, and reviewing another programme's worktree is out of scope.

Before an approval-capable review, require:

- current plan and tasks artifacts (`plan.md`, `tasks.md`);
- linked I-VSD report with `current` status and `plan-aligned` disposition;
- resolved mappings from every material `IVSD-*` ID to scenario/task, explicit non-applicability, or escalation;
- current user-approval state, which the CTO review cannot change.

## Minimum Reviewable Inputs

You can still provide useful CTO feedback with partial inputs. Use this order:

1. `...-plan.md` only:
   - allowed, but call out missing tasks coverage.
2. `plan.md` + `tasks.md`:
   - standard dual-artifact input; fully sufficient to review architecture, sequencing, and verification.
3. `plan.md` + `tasks.md` + `context.md`:
   - full triad input with resume/handoff state.
4. `plan.md` + referenced repo files:
   - allowed when the user wants architecture critique more than workflow critique.

If the user provides only a vague idea and no implementation plan, this skill should recommend creating an `implementation-plan` workstream first instead of pretending there is a real plan to approve.

Partial inputs may receive useful feedback but never an `Approve` verdict. A missing/stale I-VSD report is `Changes required`.

## Required Reviewer Checks

For every review, determine:

1. What artifact set was provided?
2. Was the workstream clearly created from the `implementation-plan` skill, or does it diverge?
3. Which claims are verified by files you actually read?
4. Which claims remain assumptions?
5. What is missing that blocks a credible approval?
6. Do the plan, tasks, and I-VSD report match the review metadata?
7. Did any proposed rewrite trigger I-VSD refresh under its integration contract?
8. Which PR scopes are delegated (Implementing or In review), who owns their live ledgers, and which proposed changes must therefore be recorded as amendments for that owner rather than applied?

## Missing-Context Handling

When inputs are incomplete:

- do a best-effort review;
- explicitly separate:
  - verified findings,
  - inferred findings,
  - missing evidence;
- avoid fake certainty;
- do not ask for clarification unless the missing input prevents any meaningful decision.

Recommended wording:

- “I can review the architecture direction, but I cannot approve implementation readiness because `...-tasks.md` is missing.”
- “This plan may be viable, but the current artifacts do not prove verification scope or operator impact.”

## Implementation-Plan Alignment Checks

When the plan claims to follow `implementation-plan`, verify these concrete things:

- `Last Updated: YYYY-MM-DD Europe/Brussels` exists in each file;
- `plan.md` includes current state, future state, constraints, decisions, PR scopes with their phases, testing, docs/internal/ops/security/multi-tenancy/risk/success sections, and no mutable registry or delegation state;
- `context.md` includes session progress, quick resume, key files, decisions, constraints, validation baseline, risks, and handoff notes;
- `tasks.md` includes implementation-maintenance rules, a PR registry with delivery, reconciliation, cleanup and delegation fields, phases grouped under stable scope IDs, a targeted gate at each phase end, an acceptance gate per PR, and remaining required work assigned to later scopes;
- each scope is independently mergeable and states prerequisites and readiness conditions; later scopes may carry rolling detail but retain required outcomes, dependencies and acceptance;
- context resume selects the active or next eligible PR scope from the registry, not a hard-coded first phase or first unchecked historical task;
- plan/context/tasks agree on current status, registry state and next scope.
- plan/context/tasks agree on I-VSD path, reviewed-input revision, status/disposition, CTO-review state, and user approval.
- the report is current for the reviewed plan/tasks and every material `IVSD-*` mapping resolves.

Every original requested outcome must map to a PR scope and phase/task, or to an explicit user-authorized scope change. A large phase or scope count is not permission to defer scope to backlog, and a merged PR does not satisfy an outcome whose acceptance remains unmet.

If they do not align, treat that as a planning quality issue, not a formatting nit.

## When To Escalate Severity

Raise severity when missing input hides risk in these areas:

- tenant isolation,
- authorization,
- data migration,
- operator recovery,
- external dependency failure behavior,
- contract churn without regeneration/testing,
- background job idempotency or delivery guarantees.

Missing evidence in these categories is often `Blocker` or `Critical`, not `Moderate`.
