<!-- ABOUTME: Concrete checklist for writing schema-compliant SKILL.md files. -->
<!-- ABOUTME: Mirrors _SKILL_SCHEMA.md and the repository's manual skill-authoring checks. -->

# Schema Checklist

## File Shape

- Path is `.agents/skills/<kebab-case-name>/SKILL.md`.
- Folder name equals frontmatter `name`.
- YAML frontmatter is first when present, followed by two `ABOUTME` comments.
- Resources live under `.agents/skills/<name>/resources/*.md`.
- `SKILL.md` stays under 250 lines, with a target of 30 to 120 lines.
- Initial skill-router load targets 6 KB; larger migration-debt skills must not grow.

## Required Frontmatter

Use exactly the required keys:

```yaml
---
name: example-skill
description: Concrete trigger phrases and artifacts, plus exclusions where adjacent skills overlap.
type: guardrail | pattern | reference | workflow
enforcement: block | suggest | inform
priority: critical | high | medium | low
---
```

## Loaded Body

Use only the sections the skill needs:

1. `## Rules` for non-inferable constraints and decisions.
2. `## Workflow` when execution order matters.
3. `## Resources` for deeper material with an explicit retrieval condition.
4. `## Verification` for exact checks and output requirements.

Descriptive domain headings are allowed. Do not add `When to Load`, `When NOT to Load`, or fixed-count lists merely for schema symmetry.

## Routing Checks

- The description contains the phrases, artifact types, technologies, or failure symptoms users will actually mention.
- The description distinguishes neighboring skills such as create/debug/test/publish or plan/PRD/CTO review.
- The description does not merely explain what the skill contains.
- Resource links are retrieved by relevant heading once; resources do not load by default.
- Verification uses exact commands or observable checks.

## Manual Checks

- Confirm no ASCII diagrams.
- Confirm no long stack overview that belongs in governing docs.
- Confirm no duplicate copy of `docs/internal/QUICK_REFERENCE.md` rules beyond what the skill must operationalize.
- Confirm no activation section repeats the catalog description.
- Confirm the body removes advice a capable agent would infer without the skill.
- Confirm every resource file starts with two `ABOUTME` comments.
