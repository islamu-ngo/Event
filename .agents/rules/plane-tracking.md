---
name: plane-tracking
description: Apply when editing Plane project-management standards, Plane skills, or the GitHub issue/PR templates that feed Plane traceability.
paths:
  - "docs/internal/project-management/**/*.md"
  - ".agents/skills/plane-compose/**/*.md"
  - ".agents/skills/plane-mcp/**/*.md"
  - ".github/PULL_REQUEST_TEMPLATE.md"
  - ".github/ISSUE_TEMPLATE/*"
related_skills: [plane-compose, plane-mcp]
related_docs: [docs/internal/project-management/work-item-taxonomy-and-granularity-report.md, docs/internal/adr/ADR-plane-project-management-standardization.md, docs/internal/AGENTIC_CONTEXT_ENGINEERING.md, docs/internal/QUICK_REFERENCE.md]
minimum_tests: [Event.Architecture.Tests, ISLAMU.AgentWorkflow.Tests]
related_intents: [agent-workflow-guard, create-agent-context-skill]
---

<!-- ABOUTME: Apply when editing Plane project-management standards, Plane skills, or GitHub templates feeding Plane traceability. -->
<!-- ABOUTME: Twin copy at .omo/rules/plane-tracking.md. When modifying this file, update both paths. -->

# Plane Tracking And Work-Item Taxonomy

> **Applies to:** the Plane taxonomy standard, the `plane-compose` and `plane-mcp` skills, and GitHub issue/PR templates.
> **Authority:** [Plane project management ADR](../../docs/internal/adr/ADR-plane-project-management-standardization.md), [work-item taxonomy standard](../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md), `AGENTS.md` Critical Rule 20, and [`QUICK_REFERENCE.md`](../../docs/internal/QUICK_REFERENCE.md) Rule 39.

## Rules (Correct / Wrong)

| # | Rule | Correct | Wrong |
|---|---|---|---|
| 1 | Project boundary | One Plane project `EVENT`; surfaces are Labels, programs are Epics | Workspace-level Event config or per-surface Plane projects |
| 2 | Release scope | Version Modules `v0.1.0` / `v1.0.0` | Calling modules "Cycles" or adding Event cycles for releases |
| 3 | Item size | One completable vertical deliverable or one provider | Perpetual areas, `Stripe/PayPal` blobs, or eight horizontal layer tickets |
| 4 | Hierarchy | `Epic` (no parent) → Work Item → optional Sub-item (no children) | Epic with a parent, or Sub-item with children |
| 5 | Definition of Done | Feature tests, migrations, and docs stay in acceptance criteria | "Write tests for X" or "Document X" tickets |
| 6 | Priority vs urgency | `priority` = functional criticality; urgency = Module + `due_date` | `urgent` meaning "soonest" |
| 7 | Tooling split | `plane-compose` for YAML and ID minting; Plane MCP for modules, comments, deletes | MCP-created item with hand-written YAML `id:`, or a custom REST client in `eng/` |
| 8 | Committed paths | Committed docs cite gitignored `event/` paths in backticks only | Linking `event/` files, or `file://` workstation URIs |
| 9 | Community intake | Issue templates gather evidence; maintainers promote via Plane Intake | Asking reporters to mint `EVENT-XXX` or split work by layer |
| 10 | Plan tracking size | One item per separately merged PR: single-PR plan = 1 Work Item; 2–5 PRs = 1 parent + 1 Sub-item per PR ([plan-tracking](../skills/plane-compose/resources/plan-tracking.md)) | An item per phase, test, review, or commit; several merged PRs under one card; more than 6 new items without approval |
| 11 | Ownership | Touch only the items a plan creates or binds | Opportunistic backlog clean-up, re-labelling, or stripping maintainers' assignments |
| 12 | Sync cost | Task loop: `plane validate --offline`, `plane push --work-only --dry-run`, conflict-checked push; retire task files by folding | `plane pull` or project-wide `plane diff` inside a task (each scans every item, about `items ÷ 50` minutes or more) |

## Must-Reads for This Path

- [Work-item taxonomy standard](../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md)
- [Plane project management ADR](../../docs/internal/adr/ADR-plane-project-management-standardization.md)
- [`plane-compose` skill](../skills/plane-compose/SKILL.md) and [`plane-mcp` skill](../skills/plane-mcp/SKILL.md)

## Anti-Patterns (Forbidden on These Paths)

- Growing `.agents/skills/plane-compose/SKILL.md` past its current byte size; route detail to `resources/`.
- Copying live security backlog content, member emails, or tokens from `event/` into committed files.
- Documenting legacy `plane_*` MCP tool names; the live server uses resource-action tools (`workitem`, `module`, …).

## Verification

- `git diff --check -- docs/internal/project-management/ .agents/skills/plane-compose/ .agents/skills/plane-mcp/ .github/`
- `rg -n "file:///" docs/internal/project-management/ .agents/skills/plane-*/` returns no hits.
- `wc -c .agents/skills/plane-compose/SKILL.md` does not exceed its previous size.

## Related

- [`.agents/rules/README.md`](README.md)
- [Agentic Context Engineering — taxonomy policy](../../docs/internal/AGENTIC_CONTEXT_ENGINEERING.md#single-project-event-taxonomy-granularity--importance-policy)
