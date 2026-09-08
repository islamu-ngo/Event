---
name: conventional-commit
description: "Load when asked to author/review commit messages or when material divergence requires replacing an approved phase commit contract; not for implementing code or executing a still-truthful planned contract."
type: guardrail
enforcement: block
priority: high
---
<!-- ABOUTME: Changelog-first Conventional Commits policy for reader-friendly release notes. -->
<!-- ABOUTME: Groups each commit by releasable outcome instead of code layer or file type. -->

# Conventional Commits
## Invariants & Rules

1. **Smallest Releasable Vertical Slice**: One commit is the smallest complete, independently reviewable outcome—not every change related to a broad feature or workstream. Include only the layers and artifacts required for that exact behavior.
2. **No Orphaned Generated Code**: Generated clients/schemas must travel in the commit that triggered them.
3. **No Layer Scopes**: Scopes describe capability/engineering concern—never code layers (`api`, `domain`, `persistence`, `blazor`, `client`, `dto` are forbidden).
4. **Cross-Domain Precedence**: When a feature spans domains, select the primary initiating capability (`registration`).
5. **Subject Quality**: State user/operator benefit in imperative mood; clear without reading the body.
6. **Breaking Work & Change-Id**: Breaking changes require `!` and `BREAKING CHANGE:` footer. Governed security/migration work requires its change fragment in `docs/internal/releases/changes/` and matching `Change-Id: CHG-...` footer.
7. **Internal Nonbreaking Work**: Commits of type `test`, `build`, `ci`, `refactor`, `style`, or internal `docs`/`fix` must carry both `Changelog: skip` and non-empty `Changelog-Reason: <reason>`.
8. **Safe Staging**: Never use blind `git add .` on mixed trees. Explicitly name staged files per atomic commit. On a shared checkout, inspect the existing index first; never unstage another contributor's work. If unrelated paths are already staged, use an explicit path-limited commit only when you own the complete diff of every named file, then verify the resulting commit file list. A file containing another contributor's hunks is a blocker until ownership is separated or coordinated.
9. **Self-Sufficient Planned Contract**: Planning writes exact metadata, commit paths, inspection commands, `git add`, path-limited `git commit`, and post-commit verification in `tasks.md`. Pathspecs equal declared paths and the command encodes metadata/trailers. A truthful packet executes without loading this skill.
10. **Material-Divergence Override Gate**: The executor loads this skill only when it will not use the planned packet due to user change, atomic split, material divergence, changed breaking/change-fragment classification, or factual invalidity. Before committing, record the reason and a complete metadata/path/command packet for every resulting commit. Style is insufficient.
11. **Execution Protocol**: Show the proposed commit plan by default; execute stage-and-commit directly when explicitly instructed. An approved implementation-plan phase-close task is explicit instruction for the implementing agent to commit in the same session.
12. **History Invariants**: Commit `B` is the sole commit whose terminal footers are `Changelog: skip` and `Changelog-Reason: release metadata commit`. Never rewrite published history on `develop` or release lines.
13. **Oversized Commit Gate**: A large dirty tree is evidence that more clustering is required, not permission for one umbrella commit. Split independent behaviors, refactors, tests, documentation, plans, cleanup, provider integrations, and operational changes even when they share a capability scope.
14. **Rare Large-Commit Exception**: A commit may touch dozens or hundreds of files only when the same indivisible change necessarily applies across them—for example a mechanical repository-wide rename, generated artifacts from one source change, or one schema/migration regeneration whose files cannot build or remain truthful independently. State that necessity in the commit plan; “same feature,” “same workstream,” or “all currently dirty” is never sufficient.

## Canonical Scope Registry

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

Optional description explaining motivation and data flow.

Changelog: skip
Changelog-Reason: concise explanation of why commit is excluded from public release notes
```

| Type | Meaning |
|---|---|
| `feat` / `fix` / `perf` | User/operator capability, bugfix, or efficiency improvement |
| `revert` / `docs` | Rollback with stated outcome, or documentation-only change |
| `test/build/ci/refactor/chore` | Internal outcome (skipped from public release notes) |

### CLI Recipes

```bash
# Vertical feature commit (single-outcome staging)
git add path/to/Domain.cs path/to/Page.razor path/to/ApiClient.g.cs
git commit -m "feat(registration): present tenant-branded intermediary disclaimer on paid events" -m "Format canonical directory notice dynamically based on tenant branding."

# Internal nonbreaking commit (with required skip trailers)
git add path/to/ProjectionUpdater.cs
git commit -m "fix(database): wrap session projection rebuilds in db execution strategy" \
  -m "Execute projection rebuilds within execution strategies." -m "Changelog: skip" -m "Changelog-Reason: internal projection resilience enhancement"

# Checkout with unrelated paths already staged
git status --short
git diff --cached --name-only
git add -- path/to/OwnedChange.cs path/to/OwnedChangeTests.cs
git commit --only -m "fix(registration): reject expired holds before attendee confirmation" -m "Keep registration state unchanged when the submitted hold is no longer valid." \
  -- path/to/OwnedChange.cs path/to/OwnedChangeTests.cs
git show --name-only --format=fuller HEAD
```

## Anti-Pattern Catalog

| ❌ Anti-Pattern | ✅ Best Practice | Why |
|---|---|---|
| `feat(api): add disclaimer` | `feat(registration): present disclaimer on paid events` | Layer scope rejected; use product capability. |
| `fix(persistence): retry query` | `fix(database): wrap session projection in execution strategy` | `persistence` is a layer; use `database` scope. |
| `chore: update client` | *[Bundle in originating feature commit]* | Never split generated client from triggering feature. |
| `docs: update cla` | `docs(documentation): clarify legal entity status` | Explicit engineering scope and benefit-led subject. |
| `test: update tests` | `test(testing): harden persistence integration tests` | Descriptive subject and canonical scope. |
| One commit for an entire multi-feature dirty tree | Separate commits for each independently reviewable behavior | Shared timing or scope does not make changes atomic. |
| “Vertical slice” containing hundreds of loosely related files | Large commit only for one provably indivisible transformation or generated set | Atomic means smallest complete outcome, not largest complete workstream. |
| Normal `git commit` while unrelated paths are already staged | Explicit path-limited commit plus post-commit file-list verification | Shared index state must not leak another contributor's work into the commit. |
| Path-limited commit of a file containing another contributor's hunks | Stop and separate or coordinate ownership before committing | Path limitation isolates files, not mixed-author hunks inside one file. |
| Loading this skill to reuse a truthful contract, or silently replacing a false one | Execute the self-sufficient default directly; load only to record material-divergence replacement contracts | Avoid context waste while making necessary drift explicit. |

## Resources

- [Governed releases](../../../docs/internal/releases/README.md)
- [Release policy](../../../docs/internal/RELEASE_POLICY.md)
- [Release policy schema](../../../eng/release/policy/release-policy.yaml)
- [Scope registry](../../../eng/release/policy/scope-registry.yaml)
- [Conventional Commits 1.0](https://www.conventionalcommits.org/en/v1.0.0/)

## Verification

- `git log --format='- %s' "$(git merge-base HEAD origin/develop)"..HEAD`
- Confirm every visible subject is plain-language, every skipped commit has both trailers, and every breaking commit has the required footer.
