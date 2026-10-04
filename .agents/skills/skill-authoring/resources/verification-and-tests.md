<!-- ABOUTME: Verification matrix for skill-authoring and agent-context infrastructure changes. -->
<!-- ABOUTME: Lists schema, link, intent-manifest, diff, and build checks to run proportionally. -->

# Verification And Tests

## Minimum Commands

Run a diff whitespace check for files touched:

```bash
git diff --check -- .agents/contract/intents.yaml .agents/skills
```

Validate changed frontmatter against `.agents/skills/_SKILL_SCHEMA.md` and
resolve every changed resource link manually.

## When To Run More

Run the full build only when shared test infrastructure, project files, or
application code changed. Prose-only skill changes do not run product tests.

## Manual Checks

- `SKILL.md` has valid required frontmatter.
- The description alone supports the pre-load decision and disambiguates adjacent skills.
- The loaded body contains no repeated activation section.
- Resource links resolve.
- Resource index links every resource.
- Resource files use natural Markdown metadata or standard frontmatter under the governance header policy.
- No skip-list exception was added.
- No claim exceeds the available evidence.

## Expected Failures

Do not use VSTest-style `--filter` examples for this TUnit project. If a filtered command is required in the future, use a verified TUnit `--treenode-filter` expression and record the command that actually ran.
