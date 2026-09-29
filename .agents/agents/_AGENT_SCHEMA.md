<!-- ABOUTME: Schema every repository subagent profile must satisfy. -->
<!-- ABOUTME: Defines narrow ownership, skill routing, tool limits, handoffs, and evidence-based completion. -->

# Agent Schema (Authoritative)

> Every `.agents/agents/*.md` except `README.md` and `_AGENT_SCHEMA.md` MUST conform to this schema.

## 1. Design Rules

1. Create an agent only for a recurring responsibility with distinct knowledge, tools, and verification.
2. Keep the role narrow and opinionated; skills provide task-specific procedures inside that role.
3. Prefer built-in `default`, `worker`, or `explorer` agents for generic work instead of duplicating them.
4. Give mutating tools only to agents that own implementation files. Review and verification agents stay read-only.
5. One agent owns a changed path at a time. Other agents may investigate or review but must not make overlapping edits.
6. Assign the cheapest capable model tier: economical for broad read-only discovery, balanced for focused execution, and advanced for architecture, security, or adversarial judgement.

## 2. File And Frontmatter

Use `.agents/agents/<kebab-case-name>.md`. The filename and `name` must match.

```yaml
---
name: <kebab-case>
description: <one sentence stating the job and invocation boundary>
type: diagnostic | review | implementation | domain | research
enforcement: suggest | inform
priority: critical | high | medium | low
model_tier: economical | balanced | advanced
tools: Read, Write, Edit, Bash, Glob, Grep
---
```

All seven fields are required. `model_tier` is a portable capability class, not a provider-specific model ID. `tools` is an allow-list. Read-only agents use `Read, Bash, Glob, Grep`; mutating agents add `Write, Edit`; research agents may add `WebSearch, WebFetch` only when their workflow requires external sources.

## 3. Required Sections In Order

### `## Purpose`

One or two sentences defining the owned outcome and the boundary it protects.

### `## When to Use`

Concrete triggers: change categories, failure signatures, or owned paths.

### `## When NOT to Use`

Explicit negatives and the correct alternate agent or built-in role.

### `## Mandatory Reads`

Link `AGENTS.md`, `docs/QUICK_REFERENCE.md`, `.agents/contract/intents.yaml`, and the smallest role-specific governing docs. These links are retrieval locations, not instructions to reread whole files: use injected `AGENTS.md`, resolve one intent entry, and retrieve only relevant headings once.

### `## Skill Routing`

Map task signals to existing `SKILL.md` files. Load only matching skills; do not duplicate their rules in the agent profile.

### `## Operating Workflow`

Numbered, executable loop from intent classification through evidence collection, implementation or review, verification, and handoff. It must state the agent's stop condition.

### `## Allowed Tools`

Mirror the frontmatter allow-list and explain the permitted purpose of each tool group.

### `## Ownership And Handoffs`

State owned files or decisions, adjacent responsibilities, handoff inputs, and what the agent must never edit concurrently.

### `## Forbidden Moves`

List role-specific safety failures. Link global rules instead of copying them.

### `## Output Contract`

Require a compact result containing outcome, changed or reviewed paths, evidence, risks, and handoffs. Read-only agents must lead with findings.

### `## Done Criteria`

Objective conditions tied to the matched intent, targeted tests, Release build policy, and observable behavior where applicable.

### `## Anti-Patterns`

Name the role's most likely drift or failure modes and the corrective behavior.

### `## Related Agents`

Link at least one sibling agent and describe the handoff boundary.

## 4. Size And Duplication

- Target 80–140 lines; hard maximum 180 lines.
- No stack overview, generic persona prose, ASCII diagrams, or copied rule catalogs.
- Do not hard-code the full test-project list; derive checks from the matched intent and `docs/OPERATIONS.md`.
- If guidance already exists in `AGENTS.md`, governing docs, rules, or a skill, link it.
- Fifteen substantially identical consecutive lines across agent files is a duplication failure.
- Read-only scout outputs use the cap in [Context Engineering](../CONTEXT_ENGINEERING.md) and contain findings plus locations, never raw source or logs.
- Every profile follows [Context Engineering](../CONTEXT_ENGINEERING.md); repeated unchanged context is a schema failure even when the prose differs.

## 5. Portfolio Gate

A proposed agent is rejected when any answer is "no":

1. Does it own a recurring repository concern rather than one feature or command?
2. Is its responsibility materially different from an existing agent, built-in agent, or skill?
3. Can the router choose it from a one-sentence description without ambiguity?
4. Does it have a distinct evidence or verification contract?
5. Does its addition reduce context/tool overload enough to justify orchestration cost?

## 6. Authoring Checklist

- Frontmatter and filename agree; tools follow least privilege.
- `model_tier` matches the role's decision risk and broad discovery defaults to `economical`.
- All 13 required sections exist in order.
- Mandatory links resolve and skill names exist.
- Workflow has an observable stop condition and handoff boundary.
- The profile is within 180 lines and does not restate global rules.
- `README.md` registry and selection guidance include the role.
- Validate agent and chat configuration as documentation/configuration; do not add automated tests for it.

## Related

- [Skill schema](../skills/_SKILL_SCHEMA.md)
- [Contribution contract](../contract/README.md)
- [Documentation style](../../docs/DOCUMENTATION_STYLE_GUIDE.md)
