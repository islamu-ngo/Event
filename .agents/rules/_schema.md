<!-- ABOUTME: Schema for path-scoped rule files under .agents/rules/. -->
<!-- ABOUTME: Twin copy at .omo/rules/_schema.md. When modifying this file, update both paths. -->

# Rule File Schema

## Purpose

Path-scoped rules are concise supplements to the intent contract. Claude Code auto-loads them when an edited file matches a rule's `paths:` glob.

## Required YAML Frontmatter

| Field | Type | Requirement |
|---|---|---|
| `name` | string | kebab-case; usually matches filename |
| `description` | string | one sentence saying when the rule applies |
| `paths` | string[] | valid globs matching real repo layout |
| `related_skills` | string[] | existing skill names only |
| `related_docs` | string[] | existing repo docs only |
| `minimum_tests` | string[] | existing test project names |
| `related_intents` | string[] | ids from `.agents/contract/intents.yaml` |

## Required Body Sections

1. `# Title`
2. `> **Applies to:** ...`
3. `> **Authority:** ...`
4. `## Rules (Correct / Wrong)` with a table
5. `## Must-Reads for This Path`
6. `## Anti-Patterns (Forbidden on These Paths)`
7. `## Verification`
8. `## Related`

## Rules Table Contract

- Use columns: `#`, `Rule`, `Correct`, `Wrong`.
- Keep 5-10 rows.
- Source rows from governing docs and skills.
- Cross-reference `docs/internal/QUICK_REFERENCE.md`; do not paste large invariant blocks into rule files.

## Authoring Limits

- Keep each path-scoped rule short and surgical.
- Prefer tables over long prose.
- No ASCII architecture diagrams.
- Keep examples minimal or omit them.
