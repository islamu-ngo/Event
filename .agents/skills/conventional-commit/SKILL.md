---
name: conventional-commit
description: "Load when authoring/reviewing commit messages or PR titles/descriptions, or replacing a materially diverged phase commit contract; not for ordinary implementation or executing an unchanged approved contract."
type: guardrail
enforcement: block
priority: high
---

# Conventional Commits
## Invariants & Rules

1. **Smallest Releasable Vertical Slice**: One commit is the smallest complete, independently reviewable outcome—not every change related to a broad feature or workstream. Include only the layers and artifacts required for that exact behavior.
2. **No Orphaned Generated Code**: Generated clients/schemas must travel in the commit that triggered them.
3. **No Layer Scopes**: Scopes describe capability/engineering concern—never code layers (`api`, `domain`, `persistence`, `blazor`, `client`, `dto` are forbidden).
4. **Cross-Domain Precedence**: When a feature spans domains, select the primary initiating capability (`registration`).
5. **Reader-First Messages**: Subjects state the concrete user/operator or engineering outcome in imperative mood, without decoding internal jargon. Bodies explain the problem and changed behavior before the mechanism. Apply [reader-first writing](resources/reader-first-writing.md) to messages and PR descriptions; preserve exact identifiers, breaking-change instructions, and trailers.
6. **Breaking Work & Optional High-Impact Fragments**: Breaking changes require both `!` in the header and a non-empty `BREAKING CHANGE:` footer, remain release-visible regardless of type or scope, and cannot be skipped. Optional high-impact fragments in `docs/internal/releases/changes/CHG-*.yaml` are used only when structured multi-category impact metadata or grouping is needed; when referenced by `Change-Id: CHG-...`, the staged/committed fragment must pass full `ReleaseInputPolicy` validation.
7. **Automatic Internal Omission & Explicit Skip**: Non-breaking internal types (`test`, `refactor`, `style`, `build`, `ci`, `chore`) and engineering scopes (`ci`, `dependencies`, `architecture`, `database`, `observability`, `documentation`, `release`, `testing`, `build`) are automatically omitted by both `CommitPolicy` and `eng/release/cliff.toml` without requiring `Changelog: skip`. Use `Changelog: skip` paired with a non-empty `Changelog-Reason: <reason>` only when explicitly opting a non-breaking public-scope commit out of public release notes.
8. **Safe Staging**: Never use blind `git add .` on mixed trees. Explicitly name staged files per atomic commit. On a shared checkout, inspect the existing index first; never unstage another contributor's work. If unrelated paths are already staged, use an explicit path-limited commit only when you own the complete diff of every named file, then verify the resulting commit file list. A file containing another contributor's hunks is a blocker until ownership is separated or coordinated.
9. **Self-Sufficient Planned Contract**: Planning writes exact metadata, commit paths, inspection commands, `git add`, path-limited `git commit`, and post-commit verification in `tasks.md`. Pathspecs equal declared paths and the command encodes metadata/trailers. A truthful packet executes without loading this skill.
10. **Material-Divergence Override Gate**: The executor loads this skill only when it will not use the planned packet due to user change, atomic split, material divergence, changed breaking/change-fragment classification, or factual invalidity. Before committing, record the reason and a complete metadata/path/command packet for every resulting commit. Style is insufficient.
11. **Execution Protocol**: Show the proposed commit plan by default; execute stage-and-commit directly when explicitly instructed. An approved implementation-plan phase-close task is explicit instruction for the implementing agent to commit in the same session.
12. **History Invariants**: Never rewrite published history on `develop` or release lines. Release preparation metadata commits remain normal Conventional Commits (`docs(release): prepare <version>`).
13. **Oversized Commit Gate**: A large dirty tree is evidence that more clustering is required, not permission for one umbrella commit. Split independent behaviors, refactors, tests, documentation, plans, cleanup, provider integrations, and operational changes even when they share a capability scope.
14. **Rare Large-Commit Exception**: A commit may touch dozens or hundreds of files only when the same indivisible change necessarily applies across them—for example a mechanical repository-wide rename, generated artifacts from one source change, or one schema/migration regeneration whose files cannot build or remain truthful independently. State that necessity in the commit plan; “same feature,” “same workstream,” or “all currently dirty” is never sufficient.

## Scope Registry

| Category | Allowed Scopes | Description |
|---|---|---|
| **Public** | `events`, `registration`, `ticketing`, `discovery`, `notifications`, `privacy`, `access`, `storage`, `onboarding`, `federation`, `webhooks`, `localization`, `accessibility`, `self-hosting` | User/operator capabilities (in release notes). |
| **Engineering** | `ci`, `dependencies`, `architecture`, `database`, `observability`, `documentation`, `release`, `testing`, `build` | Codebase health, build, testing, dev tooling. |

## File Clustering (Atomic Slicing)

Sort dirty working trees using this priority order:

1. **Vertical Feature/Fix Slice**: Domain + App + Persistence + UI + Generated Artifacts + Tests + Docs.
2. **Technical/Resilience Fix**: Independent database execution strategies, retries, or middleware.
3. **Test Suite Hardening**: Test fixtures, schema isolation (`current_schema()`), characterization models.
4. **Build & Package Config**: Central props (`Directory.Build.props`), lockfiles, CI pipelines.
5. **Governance & Legal Docs**: `CLA.md`, `CONTRIBUTING.md`, `README.md`, ADRs (`docs/internal/adr/*`), durable findings (`dev/_journal/*`). *(Note: Active task tracking in `dev/active/*` is gitignored local working memory and excluded from commits).*

Then apply the atomicity gate:

1. Describe each candidate commit in one benefit-led sentence.
2. Remove every file not required to make that sentence true.
3. Split files that implement another behavior, cleanup, plan, test-hardening effort, or operator concern.
4. Keep generated outputs with their exact source change, but do not use generated files to absorb unrelated handwritten work.
5. For an unusually large candidate, explain why splitting would create a broken build, orphan generated output, or a false intermediate contract. If no concrete break exists, split it.

File count is a warning signal, not the definition of atomicity. Small commits are the default; very large commits are exceptional and must be structurally indivisible.

## Format & Non-Interactive CLI Recipes

```text
type(scope): benefit-led subject

Explain the problem and what changes for the reader.
Then explain the relevant technical mechanism and constraints.

Refs: EVENT-XXX
```

Add `Changelog: skip` + `Changelog-Reason: <reason>` only when explicitly excluding a non-breaking public-scope commit from public release notes. Internal types and engineering scopes are omitted automatically.

| Type | Meaning |
|---|---|
| `feat` / `fix` / `perf` | User/operator capability, bugfix, or efficiency improvement |
| `revert` / `docs` | Rollback with stated outcome, or documentation-only change |
| `test/build/ci/refactor/style/chore` | Internal outcome (automatically omitted from public release notes unless breaking) |

### Plane Work Item Trailers

Commits linking to Plane work items use standard Git trailers placed above terminal changelog skip trailers, using **canonical uppercase `EVENT-XXX`** so Plane's native Git integration attaches commits directly to the work item's **Development** panel (lowercase `event-xxx` and custom local slugs are not linked by Plane):
- **Progress / Feature Deliverable**: `Refs: EVENT-XXX` (omit if the ticket has not yet been pushed and assigned a canonical ID — never fabricate or borrow an unrelated `EVENT-XXX`).
- **Resolved Defect / Bug**: `Fixes: EVENT-XXX`
- **Multiple References**: `Refs: EVENT-123, EVENT-456`
- **Subject Guardrail**: Never prepend `[EVENT-XXX]` *before* `type(scope):` in commit or PR titles (that fails `ISLAMU.ReleaseEngineering` `CommitPolicy.HeaderPattern`). Use `[EVENT-XXX]` inside the PR description (`## Issues`) or trailing after the subject if PR State Automation is desired.

Example:
```text
feat(notifications): add user preference dispatch routing

Implement per-category user notification preference channels for email and in-app alerts.

Refs: EVENT-4968
```

### CLI Recipe

Confirm `git config --get core.hooksPath` resolves to the repository `.githooks`
directory. If unset, use `git config core.hooksPath .githooks`; do not discard
an existing custom hook chain. The tracked `commit-msg` hook runs the native
`preflight-commit` command before Git records the commit. It validates the proposed
message and indexed Change-Id provenance with the policy used by range validation.
Never use `--no-verify` or override hooks to bypass this gate.

```bash
git status --short
git diff --cached --name-only
git add -- path/to/OwnedChange.cs path/to/OwnedChangeTests.cs
git commit --only -m "test(access): isolate committed cancellation observation" \
  -m "Observe only keys created by this invocation." \
  -m "Refs: EVENT-123" \
  -- path/to/OwnedChange.cs path/to/OwnedChangeTests.cs
git show --name-only --format=fuller HEAD
```

Use literal owned paths. Path-limited commits isolate files, not another contributor's hunks inside a shared file. Add `BREAKING CHANGE:` for breaking commits, optional `Change-Id:` when an optional high-impact fragment is attached, or `Changelog: skip` + `Changelog-Reason:` only when explicitly opting a public-scope commit out of public release notes.

When an explicit opt-out is used, place both skip declarations in one `-m` argument (after `Refs:`). Separate `-m` arguments introduce blank paragraphs; native policy tolerates whitespace between terminal trailer paragraphs, while keeping duplicates, conflicting values, missing reasons, and breaking metadata violations invalid. Ordinary body prose stops the terminal trailer scan.
For a saved proposed message, run the same gate explicitly before committing:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj \
  --configuration Release -- preflight-commit /absolute/path/to/proposed-message.txt --target develop
```

Keep `preflight-range --target develop --head HEAD` as the final local/CI check;
successful hook validation does not replace complete-range validation.

## Resources

- [Reader-first writing](resources/reader-first-writing.md) - use for commit wording, PR composition, reports, and decision briefs; includes balanced examples and a PR template.
- [Governed releases](../../../docs/internal/releases/README.md)
- [Release policy](../../../docs/internal/RELEASE_POLICY.md)
- [Release policy schema](../../../eng/release/policy/release-policy.yaml)
- [Scope registry](../../../eng/release/policy/scope-registry.yaml)
- [Conventional Commits 1.0](https://www.conventionalcommits.org/en/v1.0.0/)

## Verification

- `git log --format='- %s' "$(git merge-base HEAD origin/develop)"..HEAD`
- Apply the resource's readability check; confirm every breaking commit carries both `!` and `BREAKING CHANGE:`, every explicit skip carries a `Changelog-Reason:`, and every referenced optional fragment passes `ReleaseInputPolicy`. Readability never substitutes for release metadata.
