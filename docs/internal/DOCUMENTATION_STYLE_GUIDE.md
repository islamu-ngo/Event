# Documentation Style Guide

> **Audience:** Contributors | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-10-04
> **Source Anchors:** `docs/internal/DOCUMENTATION_ARCHITECTURE.md`, `docs/internal/GOVERNANCE.md`, `.agents/skills/conventional-commit/resources/reader-first-writing.md`

## Writing Principles

- **Meaning Before Mechanism**: Start with the concrete problem, who is affected, and what changes. Then explain how the technical mechanism produces that result. Follow [Reader-First Technical Communication](GOVERNANCE.md#reader-first-technical-communication) and the [shared writing guide](../../.agents/skills/conventional-commit/resources/reader-first-writing.md).
- **Expand Explanation, Preserve Substance**: Improve readability by adding explanations, context, and scenarios, never by deleting technical depth, exact identifiers, architecture constraints, or evidence.
- Write for action: what to do, where to look, what is enforced.
- Prefer factual language over promotional wording.
- Prioritize non-inferable facts (exact keys, fallback order, defaults, constraints).
- If a statement can drift, link it to a concrete source file.

## Reader-First Review Checklist

Before finalizing any documentation, architectural ADR, PR description, or I-VSD report, verify against these four questions:
1. **Practical consequence**: Does the document explain who is affected and what changes before detailing internal mechanics?
2. **Mechanism explanation**: Does it explain why the technical mechanism or data flow produces that outcome?
3. **Substance preservation**: Are exact identifiers, endpoints, configuration keys, constraints, and alternatives preserved?
4. **Truthfulness & claim boundaries**: Can the reader distinguish proposed design from verified production behavior?

## Voice and Tone

- Use direct, active voice.
- Address reader as "you" only when giving instructions.
- Avoid vague wording like "usually", "often", "might" unless uncertainty is real and explicit.

## Structure

Recommended page shape:

1. Purpose (one sentence)
2. Core rules or behavior
3. Practical usage notes
4. Related docs

Keep headings simple: `#`, `##`, `###`.

## Metadata

New primary documentation and operator-critical docs must include the metadata block defined in [DOCUMENTATION_ARCHITECTURE.md](DOCUMENTATION_ARCHITECTURE.md). Use it to make audience, status, ownership, verification date, and source anchors visible without adding process-heavy frontmatter.

Do not add metadata mechanically. Add it when the page has verified source anchors and a clear owner category.

## Formatting Rules

- Use inline code for identifiers, settings, endpoints, and paths.
- Use tables for comparisons and key lists.
- Keep code blocks short and only when needed.
- Avoid large diagram-like ASCII blocks.
- Prefer text flows and tables over decorative visuals.

## Content Rules

- Separate implemented behavior from roadmap ideas.
- Mark assumptions explicitly when unavoidable.
- Do not duplicate large sections across multiple docs.
- Update docs in the same change when behavior changes.
- Trace drift-prone claims to source anchors: code, infrastructure files, tests, workflows, or existing primary documentation.
- Label planned or draft behavior at the section where it appears; page-level `Status: Mixed` is not enough.
- Record docs impact for non-trivial changes as `Updated`, `Not needed`, or `Deferred` with a reason.
- Keep release-sensitive docs current when migrations, configuration keys, secrets, auth, storage, or operator commands change.

## Source Anchors

Use source anchors for exact facts:

| Claim Type | Preferred Anchor |
|---|---|
| Service names, ports, profiles | `docker-compose.yml`, `Explore.AppHost/` |
| Configuration keys | binding and compatibility code, then `docs/CONFIGURATION.md` |
| Secrets | `Explore.Domain/Secrets/SecretDefinitionRegistry.cs`, secret provider code, then `docs/SECRETS.md` |
| Test commands | `docs/TESTING.md`, `.github/workflows/`, test project files |
| AI-agent behavior | `AGENTS.md`, `AGENTS.md`, `.agents/contract/` |

If an anchor and doc disagree, update the doc or explicitly mark the mismatch as a follow-up. Do not preserve stale examples.

## Terminology Baseline

Use consistent core terms:

- Instance: deployment owner scope.
- Tenant: isolated community scope.
- Organization: managed entity within tenant.
- BFF: `Explore.Blazor` server host.
- Client: `Explore.Blazor.Client` WASM UI.
- API: `Explore.API`.

## Doc Review Checklist

- Is every key technical claim traceable to code?
- Is the page concise and task-relevant?
- Are examples minimal and non-repetitive?
- Are related docs linked?
- Did we remove stale or duplicate sections?
- Does the page metadata match the intended audience, status, owner, and anchors?
- Did release/operator changes update [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) or [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) when needed?
