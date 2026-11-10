# Agentic delivery loop: implement software and track Plane honestly

**Read when:** the agent is asked to write/fix code in a Plane-managed repository and should maintain project-management state as part of the work.

## Operational contract

The skill is not a substitute for engineering. **Implement the code requested** and use Plane to make the work traceable, not as the main activity. Respect user instructions and local repository conventions. Under a standing delegation to manage the project's tasks, non-destructive ticket updates and conflict-free additive pushes can be part of the implementation workflow; external mutations outside that delegation require approval.

## Before editing code

1. Find the project and inspect repository state. Never assume `plane.yaml` at repo root or that the default connection is appropriate.
2. Locate an existing matching work item by ID or intent. If remote-only work may exist, reconcile before issuing a duplicate (see `progress-reporting.md`).
3. If missing and the task is substantial, create one scoped work item (`work/active-<slug>.yaml`) with outcome, acceptance criteria, grounded priority, and appropriate initial state; omit `id:` on creation so the first push writes canonical `PROJ-N` back into YAML for Git branch/commit/PR linking (see `work-item-authoring.md`). For an independent trivial edit, skip ticket creation unless user wants it.
4. Make a realistic implementation plan with checks. Handle blockers rather than marking progress just because you intend to work on it.
5. When active development begins and push is authorized, push the initial work item once (if unpushed) so canonical `PROJ-N` is minted before cutting your Git branch (`feat/PROJ-N-<slug>`), and move the ticket to the project's actual **started** state if the workflow allows. Do not invent `In Progress` in a schema that lacks it.

## During implementation

- Make the smallest coherent code changes needed. Read tests and relevant source; don't use Plane as a source for unverified runtime behavior.
- Keep acceptance criteria aligned with the actual deliverable. Add follow-up work **only** for newly confirmed and separable gaps; avoid creating ticket-per-file noise.
- Link genuine dependencies (`blocked_by`) and relationships (`relates_to`, `implements`) when the referenced items exist.
- Reference uppercase `PROJ-N` in branch names (`feat/PROJ-N-<slug>`), commits (`Refs: PROJ-N`), and PRs (`[PROJ-N]`) so Plane's native **Development** panel tracks Git activity instead of pasting commit logs or PR URLs into YAML `description:`. Treat PR, commit and test results as evidence only when observed. Do not fabricate code review or merge status.
- If a blocker occurs, leave the ticket in the appropriate incomplete state and record the blocker, instead of representing it as delivered.
- If the implementation exceeds the ticket significantly, stop and adjust scope or decompose; do not silently redefine success.

## Completion gates

A local `Done` state is appropriate only after **all** applicable checks:

- Requested behavior is implemented and code paths have been inspected.
- Relevant tests/build/lint/manual verification were actually run and pass, or the user explicitly accepts an exception. Record any incomplete verification.
- Acceptance criteria are met and any required security/docs/migrations are handled.
- Any required code review or approval in the actual team workflow is complete. If pending, use a review/testing state instead of `Done`.
- The item isn't blocked by an unresolved dependency.

Do not confuse “code written”, “tests pass”, “ready for review”, “merged”, and “deployed”. They are different milestones. When CI requires a remote PR, report that CI is pending instead of assuming it ran.

## Publishing a routine task update

Inside the selected project directory:

```bash
plane schema validate
plane validate
plane diff
plane push --dry-run
# Then, only with ongoing delegation or explicit approval, and no unresolved conflicts:
plane push --work-only
```

`--work-only` is useful when the task edits only `work/`, avoiding unnecessary schema writes. It still processes supported work sections, so review the plan. `plane validate` should run **before** any full `plane push`, since normal push applies schema before validating work.

Check the command result, canonical ID write-backs, `git diff`, and remote diff where appropriate. If no authorization or connectivity, keep local edits and report **remote sync pending**.

## End-of-task update format

Use compact evidence-based prose: implemented behavior and files, Plane work item ID/current state, actual verification results, whether the **remote** was synced, outstanding issues and the next highest-priority unblocked task. Do not mark remote work synced merely because the YAML was edited.

**Official:** https://developers.plane.so/dev-tools/plane-compose/sync-projects · https://developers.plane.so/dev-tools/plane-compose/project-files
