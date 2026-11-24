# One Pull Request Execution Lifecycle

Use the [shared scope contract](../../implementation-plan/resources/pr-scope-contract.md)
for the canonical registry and ownership interface. This resource defines execution
with native file tools, Bash, Git, GitHub CLI and Plane. It adds no automated state
engine or concurrency guarantee.

## Selection And Topology

1. Read canonical `*-context.md` first. Resolve its active scope in `*-tasks.md`,
   then load that task's named design headings, shared invariants and dependencies.
   Verify actual user approval; a CTO or I-VSD disposition does not supply it.
2. Inspect explicit delivery, reconciliation, cleanup, execution location, owner and
   actual PR identity. Resume the existing Implementing scope. For In review,
   report/recover review state and work only on authorized review repairs. A
   Merged scope with pending reconciliation/cleanup resumes finalization.
3. Missing storage, stale delegation or conflicting owners requires an inventory
   and ownership recovery before setup. Do not infer in-tree execution from old
   checked tasks, reuse an unrelated directory, or duplicate the live ledger.
4. Otherwise evaluate the earliest Planned candidate with merged and reconciled
   required predecessors. Refresh its contracts against the actual merged target.
   Require independently mergeable acceptance, approval, paths, shared-context
   extraction, verification and exact commit packets before recording Ready.
   Record blockers instead of treating a Planned candidate as already executable.
5. Complete the selected scope's execution-start
   [tracking checklist](../../plane-compose/resources/plan-tracking.md).
   Perform live deduplication before minting. Validate, inspect work-only dry run,
   publish only owned items with conflict checks, bind actual IDs into packets,
   assign the driving developer and verify Version Module membership. Missing
   tools/evidence stays pending; never invent IDs or silently bypass prerequisites.

The complete canonical triad remains under `dev/active/<programme>/` in the main
checkout. Its registry identifies the selected scope and source revision before
delegation. Existing ownership wins over default setup:

| Situation | Action |
|---|---|
| Recorded execution worktree exists | Resume it at its recorded branch/path; do not recreate or move the canonical triad |
| User explicitly selects current checkout | Keep current branch and execute the canonical selected section in place; no second live copy |
| Ready scope has no previous delegation | Create a fresh PR worktree from updated merged target, then extract only that scope and shared contracts |
| Legacy plan has no scope registry | Inventory actual delivery, dirty work, remaining requirements, PRs and location; normalize one selected scope before execution |

For fresh isolation, resolve literal values for `<programme>`, `<scope>`,
`<item>` and the approved target before running native commands. Example naming:
`.worktrees/<programme>--<scope>` and `feat/EVENT-<item>-<programme>-<scope>`.
Fetch the target (`develop` by default), verify the prerequisite merge evidence,
then use `git worktree add -b <branch> <path> origin/<target>`. Never switch the
main checkout's branch. Never overwrite an existing path or change sibling worktrees.
Filter any worktree listing by the exact programme namespace.

Write a scoped plan/tasks/context copy with native editing tools. Include the source
path/revision, scope ID, owner, selected phases, acceptance, commit packets and all
shared constraints listed in the scope contract. Omit future task detail. Mark the
canonical selected checklist a delegated snapshot and record the live-ledger path.
For explicit in-tree execution, record that the canonical selected section remains
live instead. Do not automatically copy the whole programme.

Set every command's working directory and every edit path to the recorded execution
location. In an isolated execution directory, copy root `AGENTS.local.md` if missing,
never move it. Copy root `.env` only if missing, with `install -m 600`, without
printing it or overwriting task-specific values, and only where local overrides
permit copying those inputs. Resolve missing secrets through the selected approved
authority. Never log/stage `.env`, keys or tokens. Link shared `event/` to root
(`ln -s ../../event event` from a directly nested worktree); never copy or move it.
Copy engineering constraints even when local secret/runtime paths cannot be shared.

Hub/spoke execution is confined to this selected PR. Use disjoint literal ownership
packets and names within `.worktrees/<programme>--<scope>--<cohort>`, with at most
3-5 active worktrees across the task. Migrate leaf consumers before callers where
dependencies require it. Verify one project at a time across all workers. Integrate
only selected-scope changes into its lead; future PRs start from merged target.
Remove integrated spokes only with current authorization and preservation checks;
resource pressure does not authorize deletion.

## Execution And Verification

On cold resume inspect `git log -n 5 --oneline`, current dirty files and the index
in the execution location; all Git commands use `GIT_MASTER=1 GIT_PAGER=cat`.
Before a product edit establish upstream freshness and the required baseline once.
Never pull into a dirty shared checkout or switch it for a baseline comparison.
Documentation-only scope uses document checks, not an unconditional sliced test.

Load only the active phase plus referenced design and downstream contracts.
Expand retrieval when a concrete contradiction or changed dependency needs it,
not by rereading the whole programme every session.

- Author failing tests first for core domain/state/concurrency/security invariants
  where required by criticality. Implement their real behavior; no no-op stubs ship.
- Track necessary discovered work under the selected outcome with acceptance,
  dependencies and owned paths before verification. Material scope/trust changes
  require alignment. Optional unrelated work may graduate to backlog; required
  scope remains visible in this programme unless the user removes it.
- Separate implemented tasks, verification disposition and successful commits.
  Keep context below 200-300 lines / 15 KB; store durable lessons in the journal
  and decisions in ADRs. Git history owns commit history, not a Markdown hash diary.
- Apply Ring 1 in-memory slices and Ring 2 single-project/single-provider phase
  checks as applicable, with explicit programme overrides retained. Compile once
  with `dotnet build <Project.csproj> --configuration Release -m:1 -nr:false --verbosity quiet`;
  then `dotnet test --project <Tests.csproj> --configuration Release --no-build --verbosity quiet`.
  Run projects sequentially. Never run bare solution-wide `dotnet test`.
- Honor `AGENTS.local.md` and the constrained profile: disk-backed mode-0700
  `TMPDIR`, `ISLAMU_EXECUTION_PROFILE=constrained`,
  `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`, `MSBUILDDISABLENODEREUSE=1`,
  `DOTNET_GCConserveMemory=5`. Dedicated hosts may use balanced/performance.
- Tier 4 Markdown work uses metadata/link/format/scenario checks only; no
  .NET suites, containers, app/browser startup or live lifecycle pilot.

Every selected PR has a mandatory acceptance gate before merge: run its applicable
architecture, affected provider matrix, migrations, public-contract, security,
UI/runtime and documentation checks required by its actual changes. Do not defer
the first validation of merged behavior merely because later programme PRs remain.
Final programme acceptance verifies cross-capability promises after their
prerequisites exist. Record any explicit user verification override, its boundaries,
unrun checks and resulting limitations rather than silently generalizing it.

Attribute failures before repair: Class A is a direct regression and must be fixed;
Class B is induced caller/fixture drift, aligned when mechanical or escalated when
structural; Class C reproduces on untouched base and is quarantined in context.
Use a real `dotnet test --project ...` in a clean authorized comparison location;
`git test` is not a command. Do not repair unrelated baseline rot. Above ten failures,
cluster by root cause; widespread architectural mismatch requires a decision brief
instead of dozens of repair tasks.

After resolved phase checks, run the exact planned literal-path packet. Inspect
existing index and ownership first; never commit someone else's hunks. Confirm
`core.hooksPath` selects `.githooks` (activate it only if unset; preserve custom
checks). Never bypass hooks. Use actual `Refs: EVENT-N`, required co-author and
any applicable breaking-change, optional `Change-Id:`, or explicit public-scope
skip trailers (internal types and engineering scopes omit automatically). If a
packet materially diverges, load conventional-commit and record the reason and
complete replacement packet before execution.
Account for residual dirty work; clean status alone does not prove preservation.

## Review Reconciliation And Handoff

1. Graduate durable architecture/lessons and explicitly optional follow-ups before
   publication. Do not relocate unmet requested work to backlog.
2. Run the selected PR's exit checks once. Fetch and update its isolated branch
   against the current target using native Git; do not rewrite published mainline,
   force-push or rebase published history without current explicit authority.
   If integration changes code, rerun only invalidated checks and fix induced failures.
3. Reconcile original intent against actual selected-scope delivery, changed
   contracts, necessary discoveries, verification/limits and incomplete criteria.
   Patch only that canonical section and justified shared decisions. Check source
   revision and preserve independent future edits; coordinate shared-owner conflicts.
4. Audit authorized commits with
   `git log --format='%s' "$(git merge-base HEAD origin/develop)"..HEAD`
   (substitute the actual target). Confirm native release-range validation and
   repository hooks required by the plan. Do not rewrite history as an automatic
   way to repair metadata.
5. Push the authorized feature branch with `git push -u origin <branch>`; a rejected
   push requires diagnosis, never automatic `--force-with-lease`. Explicit in-tree
   execution on `develop` does not authorize publishing unrelated commits or
   inventing a feature branch. If no publishable branch exists, report that boundary.
6. Use `gh pr create --base <target> --title "<type>(<scope>): <subject>" --body <body>`.
   Write a reader-first body with `## Issues` and the actual bracketed PR item;
   an M parent remains unbracketed. Never fabricate a tracking link.
   Include `## Release Impact`, checked applicable categories and nonempty `Details:`.
   Categories cover security/auth, migration/data/rollback, configuration/secrets/
   deployment, OpenAPI/client and operator/self-hosting/release notes. If none apply,
   check `Not applicable` and explain why. Never rely on `--fill` alone.
7. Mark the PR item Review Needed using the owned tracking protocol; mirror
   automation state before another push. Record delivery In review and the review
   reconciliation snapshot, park the execution location and stop.

The handoff states PR URL/branch or unpublished status, execution location,
delivered behavior and mechanism, integration repairs, exact checks/limits,
quarantined baseline issues, retained obligations and next scope. State CI/reviewer
status only when observed. Invite later review repairs or merge/finalization
direction. Do not begin the next PR in that same execution invocation.

## Merge Reconciliation And Authorized Cleanup

On later user merge/finalization direction, use native evidence such as
`gh pr view <number> --json state,mergedAt,mergeCommit,baseRefName,headRefName,url`
and refreshed target history. Verify the actual repository, target, merged state
and resulting content. Approval alone or Closed unmerged never proves delivery.
Support squash/rebase merges through native PR merge metadata and target/content
evidence; original-tip ancestry alone is insufficient. Missing evidence remains
pending, never invented.

Reconcile final reviewed changes, final checks and unmet acceptance separately from
the pre-review snapshot. Correct affected future contracts. Mark reconciliation
current only when the actual evidence and all obligations have explicit ownership.
A merged scope with unmet criteria keeps them visible or binds an approved
successor. Dependent readiness requires merged and reconciled predecessor contracts.
Advance Plane Done only with acceptance evidence; complete the parent only when
every retained obligation is resolved.

Before cleanup, require the user's current authorization naming the intended
worktree and/or local/remote branches. Confirm:

- All owned tracked and untracked work is integrated, preserved or explicitly
  disposed of; unrelated hunks are not reverted.
- Ignored task discoveries, decisions, context and required future information
  are reconciled into canonical storage or durably graduated. Git merge and clean
  status do not preserve ignored files.
- Local overrides, credentials and machine-local runtime data have an accounted
  preservation/disposition; never expose them in committed evidence or chat.
- Final merge/target evidence is sufficient for a merged cleanup. For an unmerged
  closure/cancellation, get explicit preservation and scope-disposition direction;
  never mark it delivered.

Record cleanup pending with exact remaining operations. From outside the worktree,
remove only the authorized execution directory with `git worktree remove <path>`.
Delete a local branch only when separately authorized and accounted for; a refusal
by `git branch -d` after squash/rebase is not permission to force-delete. Remote
branch deletion is a separate authorized external operation. Never use force as a
preservation shortcut, prune siblings, or delete the canonical programme on PR exit.
Mark each completed operation immediately. Interrupted cleanup resumes the
remaining operations without replaying implementation or recreating the old scope.

After the final scope's reconciled acceptance, retire only owned programme tracking
as described in plan-tracking. Intermediate merges keep the canonical triad and
active YAML. Canonical programme retirement requires all retained obligations
resolved and durable graduation; removing a delegated worktree cannot retire it.

## Recovery Decisions

| Observed condition | Resume action |
|---|---|
| Existing implementation/review owner | Resume or coordinate with that owner; no duplicate scope |
| Missing worktree, branch or source revision | Inventory recoverable work and reconcile ownership before setup |
| Missing/unreconciled predecessor | Leave candidate Planned with the exact blocker |
| Future canonical section changed | Patch selected section only; resolve shared-contract conflicts |
| Review modifies delivered behavior | Refresh post-merge evidence and affected future dependencies |
| PR closed unmerged | Preserve requested scope; record closure without claiming delivery |
| Merge known, cleanup interrupted | Resume remaining authorized cleanup only |
| Legacy full-triad worktree | Inventory and preserve canonical programme information before any scoped normalization; no bulk migration |
