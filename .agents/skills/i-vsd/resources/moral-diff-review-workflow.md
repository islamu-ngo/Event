<!-- ABOUTME: Pre-PR I-VSD workflow for reviewing complete git diffs across code, docs, config, and generated artifacts. -->
<!-- ABOUTME: Classifies moral compliance issues as hard violations or soft warnings before pushing or opening a pull request. -->

# Moral Diff Review Workflow

Use this action before pushing commits, opening a pull request, or handing changes to CI/CD. It is a local pre-flight review for the complete changeset, not a replacement for tests, security review, legal review, or qualified Islamic scholarly review.

When the user explicitly asks to run this action, proceed directly to diff collection and report creation/update. Ask a follow-up question only when there is no accessible diff, the output destination is unclear, or the user asks to compare against a specific base branch that is not available.

## Purpose

Review every changed artifact, including unpushed commits, staged changes, unstaged changes, intended untracked files, code, docs, tests, configs, migrations, generated files, scripts, policies, marketing copy, and metadata. The goal is to detect whether the complete outgoing changeset introduces moral, trust, privacy, autonomy, truthfulness, or provider-responsibility regressions before the work leaves the local workspace.

## Required Diff Collection

Prefer the current version-control system when available. For git workspaces, collect:

- `git status --short` to identify modified, staged, deleted, renamed, and untracked files.
- The current branch, target branch, and upstream branch when available.
- Unpushed commit titles, bodies, authors, dates, and commit IDs, usually from the upstream range such as `@{u}..HEAD`.
- A cumulative diff for unpushed commits, usually `git diff @{u}...HEAD` or the host project's chosen base branch range.
- `git diff --stat` for scope and file mix.
- `git diff` for unstaged tracked changes.
- `git diff --cached` for staged changes.
- Untracked file contents when they are intended to be part of the review.

If no upstream branch exists, use the best available target branch or ask the user which base to compare against. If the workspace is not git-based, review the user-provided patch, changed file list, export bundle, source package diff, release notes, or change metadata. If no diff or changed artifacts are available, ask for them instead of guessing.

## Commit Context Review

Do not evaluate raw diffs in isolation when commit context is available. Review commit titles, descriptions, PR metadata supplied by the user, target branch names, release branch names, migration notes, and documented rationale. This context can justify changes that would otherwise look suspicious.

Commit context may prevent a hard or soft violation only when it is specific, relevant, and consistent with the changed artifacts. Acceptable justification examples include:

- A broad permission is introduced only for a clearly named opt-in feature, with local processing, user-facing disclosure, purpose limitation, and matching implementation evidence.
- A deprecated API is removed on a major-version branch after prior deprecation notice, migration documentation, or compatibility policy evidence.
- A data-retention, telemetry, moderation, or support change is tied to a documented user-facing policy update and implementation controls.

Do not accept vague justification such as “cleanup,” “refactor,” “business requirement,” or “security” when the diff affects privacy, permissions, user rights, compatibility promises, or trust-sensitive behavior. If commit context claims a safeguard that the diff does not implement or document, keep the violation and mention the mismatch.

## Pipeline Input Contract

When this workflow is used by an automation wrapper, ask it to provide this shape. Missing fields are allowed, but the review must note the missing context.

```text
Target branch: <target branch or unknown>
Current branch: <current branch or unknown>
Upstream branch: <upstream branch or none>

Unpushed commits:
- <commit sha> <title>
  Body: <commit description/body, if any>

Staged diff:
<git diff --cached>

Unstaged diff:
<git diff>

Unpushed commit diff:
<git diff upstream...HEAD or chosen base...HEAD>

Untracked files intended for review:
<paths and contents or summaries>
```

The evaluator must heavily weigh commit context and target branch when classifying permission, data extraction, telemetry, or breaking changes, but only when the context is specific and supported by implementation or documentation evidence.

## Review Framework

Evaluate the complete diff against these principles:

- Transparency and truthfulness: no misleading claims, hidden behavior, false documentation, dark patterns, or promises that the implementation does not honor.
- Autonomy and privacy: no hidden telemetry, unnecessary permissions, excessive collection, manipulative defaults, forced network dependency, or avoidable surveillance.
- Trust and continuity: no silent removal of documented commitments, compatibility promises, support guarantees, user controls, portability, deletion rights, or safety processes.
- Minimal extraction: collect, retain, transmit, expose, or request only what is necessary for the stated purpose.
- Provider responsibility: changes should preserve safety, accessibility, moderation, support, incident handling, auditability, and escalation duties created by the product.

## Hard Violations

Hard violations are deal-breakers that should block the PR or push until fixed. Examples include:

- Hidden tracking, covert telemetry, or data exfiltration.
- Permission escalation unrelated to the feature purpose.
- Malicious, deceptive, or deliberately misleading logic.
- Documentation that knowingly misrepresents product behavior, safety, privacy, pricing, or compatibility.
- Removing user rights, deletion, export, consent, appeal, moderation, or safety controls without clear disclosure and replacement.
- Introducing high-risk harm to vulnerable users, religious/legal claims, finance/riba exposure, public safety, or high-stakes AI without escalation.

## Soft Violations

Soft violations are warnings that degrade the ethical baseline but may not block the PR by themselves. Examples include:

- Ambiguous documentation or weaker explanation of user-impacting behavior.
- Missing rationale for a privacy, permission, retention, or architecture change.
- Removing comments or docs that signaled future support, compatibility, or operational expectations.
- Incomplete accessibility, support, moderation, incident, or audit evidence for a lower-risk change.
- Changes that are probably acceptable but need clearer claim boundaries, user messaging, or tests.

## Report Output

Create or update `islamic-value-sensitive-design/workstreams/i-vsd-moral-diff-review.md`. Use the standard report contract from [action-routing.md](action-routing.md), plus this action-specific YAML block near the top of `## Findings`:

```yaml
pass: true
hard_violations: []
soft_violations: []
soft_violation_count: 0
```

When violations exist, use this shape:

```yaml
pass: false
hard_violations:
  - type: "Hidden telemetry"
    details: "The diff sends usage data without user-facing disclosure or purpose limitation."
soft_violations:
  - type: "Ambiguous documentation"
    details: "The README promises local-only behavior but the implementation can call a network service."
soft_violation_count: 1
```

Set `pass: false` whenever one or more hard violations exist. Soft violations do not automatically fail the review unless the user or host project defines a stricter policy. Always include file paths or diff references in `details` when available.

If commit context justifies an otherwise suspicious change, do not count it as a violation. Instead, record it under `## Trust And Promise Changes` or `## Evidence Reviewed` with the commit ID/title and the evidence that made it acceptable.

## Required Report Sections

In addition to the standard report headings, include:

```text
## Diff Scope
## Commit Context Reviewed
## Moral Gate Result
## Hard Violations
## Soft Violations
## Trust And Promise Changes
## Data, Permission, And Telemetry Changes
## Documentation And User-Facing Claim Changes
## Recommended Fixes Before PR
```

The report should be useful to a developer before PR creation: concise enough to act on, but specific enough that each finding can be traced to changed files or diff hunks.

## Evaluation Rules

- Review documentation changes with the same seriousness as code changes.
- Review unpushed commits with the same seriousness as uncommitted changes; the outgoing review scope is everything that would reach the remote or PR.
- Weigh commit messages, commit descriptions, target branch, PR metadata, release notes, and migration docs before classifying permission escalation, data extraction, telemetry, or breaking changes.
- Treat removal of a promise as a finding even when no code changed.
- Treat new code behavior that contradicts docs as a finding even when tests pass.
- Do not invent intent. If a change might be harmless but evidence is missing, classify it as a soft violation or missing evidence rather than a hard violation.
- Do not let commit metadata override facts in the diff; justification must be specific, user-facing or operationally evidenced when relevant, and consistent with implementation.
- Do not claim the changes are halal, Sharia-compliant, certified, safe, or proven ethical.
- Escalate religious-legal, finance/riba, vulnerable-user, public-harm, or high-stakes AI/privacy uncertainty.
