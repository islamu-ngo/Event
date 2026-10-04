---
name: conventional-commit
description: "Load when authoring/reviewing commit messages or PR titles/descriptions, or replacing a materially diverged phase commit contract; not for ordinary implementation or executing an unchanged approved contract."
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
5. **Reader-First Messages**: Subjects state the concrete user/operator or engineering outcome in imperative mood, without decoding internal jargon. Bodies explain the problem and changed behavior before the mechanism. Apply [reader-first writing](resources/reader-first-writing.md) to messages and PR descriptions; preserve exact identifiers, breaking-change instructions, and trailers.
6. **Breaking Work & Change-Id**: Breaking changes require `!` and `BREAKING CHANGE:` footer. Governed security/migration work requires its change fragment in `docs/internal/releases/changes/` and matching `Change-Id: CHG-...` footer.
7. **Internal Nonbreaking Work**: Commits of type `test`, `build`, `ci`, `refactor`, `style`, or internal `docs`/`fix` must carry both `Changelog: skip` and non-empty `Changelog-Reason: <reason>`.
8. **Safe Staging**: Never use blind `git add .` on mixed trees. Explicitly name staged files per atomic commit. On a shared checkout, inspect the existing index first; never unstage another contributor's work. If unrelated paths are already staged, use an explicit path-limited commit only when you own the complete diff of every named file, then verify the resulting commit file list. A file containing another contributor's hunks is a blocker until ownership is separated or coordinated.
9. **Self-Sufficient Planned Contract**: Planning writes exact metadata, commit paths, inspection commands, `git add`, path-limited `git commit`, and post-commit verification in `tasks.md`. Pathspecs equal declared paths and the command encodes metadata/trailers. A truthful packet executes without loading this skill.
10. **Material-Divergence Override Gate**: The executor loads this skill only when it will not use the planned packet due to user change, atomic split, material divergence, changed breaking/change-fragment classification, or factual invalidity. Before committing, record the reason and a complete metadata/path/command packet for every resulting commit. Style is insufficient.
11. **Execution Protocol**: Show the proposed commit plan by default; execute stage-and-commit directly when explicitly instructed. An approved implementation-plan phase-close task is explicit instruction for the implementing agent to commit in the same session.
12. **History Invariants**: Commit `B` is the sole commit whose terminal footers are `Changelog: skip` and `Changelog-Reason: release metadata commit`. Never rewrite published history on `develop` or release lines.
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

Changelog: skip
Changelog-Reason: concise explanation of why commit is excluded from public release notes
```

| Type | Meaning |
|---|---|
| `feat` / `fix` / `perf` | User/operator capability, bugfix, or efficiency improvement |
| `revert` / `docs` | Rollback with stated outcome, or documentation-only change |
| `test/build/ci/refactor/chore` | Internal outcome (skipped from public release notes) |

### CLI Recipe

```bash
git status --short
git diff --cached --name-only
git add -- path/to/OwnedChange.cs path/to/OwnedChangeTests.cs
git commit --only -m "fix(registration): reject expired holds before confirming attendance" \
  -m "An expired reservation must not become a confirmed registration. Check the hold expiry before changing registration state." \
  -- path/to/OwnedChange.cs path/to/OwnedChangeTests.cs
git show --name-only --format=fuller HEAD
```

Use literal owned paths. Path-limited commits isolate files, not another contributor's hunks inside a shared file. Add the required changelog/breaking-change trailers for the classified change.

## Resources

- [Reader-first writing](resources/reader-first-writing.md) - use for commit wording, PR composition, reports, and decision briefs; includes balanced examples and a PR template.
- [Governed releases](../../../docs/internal/releases/README.md)
- [Release policy](../../../docs/internal/RELEASE_POLICY.md)
- [Release policy schema](../../../eng/release/policy/release-policy.yaml)
- [Scope registry](../../../eng/release/policy/scope-registry.yaml)
- [Conventional Commits 1.0](https://www.conventionalcommits.org/en/v1.0.0/)

## Verification

- `git log --format='- %s' "$(git merge-base HEAD origin/develop)"..HEAD`
- Apply the resource's readability check; confirm every skipped commit has both trailers and every breaking commit has its required footer. Readability never substitutes for release metadata.
