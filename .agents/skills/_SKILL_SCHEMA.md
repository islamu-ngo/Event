<!-- ABOUTME: Schema every SKILL.md in this repo must satisfy. -->
<!-- ABOUTME: Defines routing-first descriptions, progressive disclosure, and practical skill content. -->

# Skill Schema (Authoritative)

> Every `.agents/skills/*/SKILL.md` MUST conform when it is created or materially revised.
> The catalog exposes only `name` and `description` before loading, so the description owns the complete activation decision.

## 1. File Location

```
.agents/skills/<kebab-case-name>/SKILL.md
```

- Folder name MUST match the skill `name` in YAML frontmatter.
- Resources (longer references) live in `.agents/skills/<name>/resources/*.md` — never inline.

## 2. Required YAML Frontmatter

```yaml
---
name: <kebab-case>           # Must match folder name
description: <routing sentence>  # Concrete tasks/terms that should load it; add exclusions when nearby skills overlap
type: guardrail | pattern | reference | workflow
enforcement: block | suggest | inform
priority: critical | high | medium | low
---
```

All five fields are **required** for project-authored skills. The description is routing metadata, not a summary or marketing sentence.

### Description contract

- Name the user phrases, artifacts, technologies, failure modes, or file patterns that make the skill useful.
- Include a compact `Do not use for ...` boundary when another skill is an easy false positive.
- Prefer terms users actually request (`N+1`, `MCP server`, `PR review`) over abstract labels (`quality`, `best practices`).
- Do not start with vague verbs such as “apply,” “guide,” or “support” unless the objects and triggers are concrete.
- Do not repeat the description's routing logic inside the loaded body.

## 3. Loaded Content

The body exists to change execution after the skill has loaded. Keep only instructions the agent would not reliably infer from the task, repository contract, or normal engineering practice.

### `## Rules`

Non-inferable constraints, defaults, stop conditions, or decision rules. Use descriptive headings for focused rule groups when that is clearer.

### `## Workflow` (only when order matters)

The shortest executable sequence. Omit generic steps such as “understand the task,” “write code,” or “follow best practices.”

### `## Resources` (only when deeper material exists)

Links with a one-line retrieval condition. Never say to load every resource by default.

### `## Verification`

Exact commands, observable checks, or output requirements that catch realistic failures. Omit checks unrelated to the skill's output.

Section names beyond these are allowed when they communicate the domain more directly. `Purpose`, `When to Load`, `When NOT to Load`, arbitrary “Top 5” lists, and a duplicated tech overview are discouraged because they spend post-load context on routing or ceremony.

## 4. File Length

- **Target**: 30–120 lines total.
- **Hard max**: 250 lines. If longer, split into `resources/*.md` routed from `## Resources`.
- **Initial-load target**: 6 KB. Larger existing skills are migration debt and must not grow when next modified.

## 5. Progressive Disclosure And Reuse

- Follow [Context Engineering](../CONTEXT_ENGINEERING.md).
- `SKILL.md` is the only default load. Resource indexes route deeper retrieval; resource files load only for the named unresolved decision.
- Do not reread `AGENTS.md`, the resolved intent, a skill, rule, document heading, or source symbol already present at the same revision.
- Prefer links and exact headings over copied governing rules. Repetition with different wording is still duplication.
- Broad read-only discovery belongs to an economical scout using the cap in [Context Engineering](../CONTEXT_ENGINEERING.md); the main agent owns decisions and synthesis.

## 6. Forbidden Content

- Activation sections or trigger lists already represented in `description`.
- Stack/tech-overview paragraphs (reference `AGENTS.md`).
- Duplicated invariants from `docs/internal/QUICK_REFERENCE.md` (link, don't copy).
- Fixed-count lists created to satisfy a shape rather than the skill's real decisions.
- ASCII diagrams (per `docs/internal/DOCUMENTATION_STYLE_GUIDE.md`).
- "Why clean architecture matters" prose or similar meta-content.
- Unconditional resource cascades that require every linked document before the task has identified a decision that needs it.

## 7. Enforcement

Skill metadata is reviewed against this schema and by the harness that consumes
the frontmatter. Product tests do not enforce documentation wording, routing
judgment, context budgets, or repository file inventories.

## 8. Migration Debt (v1)

Existing nonconforming skills are migration debt and must not grow. New or changed skills follow this schema without skip-list exceptions. If a skill needs more depth than the line cap allows, keep `SKILL.md` compact and move detail into `resources/*.md`.

## Related

- Agent schema → [`.agents/agents/_AGENT_SCHEMA.md`](../agents/_AGENT_SCHEMA.md)
- Intent registry → [`.agents/contract/intents.yaml`](../contract/intents.yaml)
- Documentation style → [`docs/internal/DOCUMENTATION_STYLE_GUIDE.md`](../../docs/internal/DOCUMENTATION_STYLE_GUIDE.md)
