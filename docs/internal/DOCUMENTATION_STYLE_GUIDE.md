ABOUTME: Defines the writing and formatting conventions for repository documentation.
ABOUTME: Optimized for concise, implementation-accurate docs that are easy for juniors to use.

# Documentation Style Guide

> **Audience:** Contributors | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-05-06
> **Source Anchors:** `docs/DOCUMENTATION_ARCHITECTURE.md`, `docs/index.md`

## Writing Principles

- Write for action: what to do, where to look, what is enforced.
- Prefer factual language over promotional wording.
- Prefer short sections over long narratives.
- Prioritize non-inferable facts (exact keys, fallback order, defaults, constraints).
- If a statement can drift, link it to a concrete source file.

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
