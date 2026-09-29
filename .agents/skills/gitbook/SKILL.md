---
name: gitbook
description: "Load when configuring GitBook documentation sites, authoring GitBook-flavored markdown pages and custom blocks, integrating OpenAPI/Swagger specs, driving GitBook Change Request workflows (create, review, comment, resolve), or building GitBook platform integrations. Do not use for general repository code reviews, general .NET OpenAPI schema generation, or non-GitBook markdown."
type: workflow
enforcement: suggest
priority: high
---
<!-- ABOUTME: Master router and invariant guardian for GitBook documentation sites, spaces, and platform integrations. -->
<!-- ABOUTME: Enforces transport hierarchy, Git Sync primacy, Two-Link preview rules, and progressive resource disclosure. -->

# GitBook Platform & Documentation

A unified skill for building, configuring, authoring, reviewing, and extending GitBook documentation and integrations. For our hosted public portal, see `docs/public/` and `docs/public/gitbook-docs.yaml`.

## Resources

- [Resource Index](resources/index.md) — Load to identify the exact workflow, syntax guide, or recipe for any GitBook task.
- [Site Configuration & Structure](resources/site-configuration.md) — Load when planning site IA, scaffolding monorepos, configuring spaces/sections, or branding.
- [Content Authoring & Formatting](resources/content-authoring.md) — Load when writing markdown pages, frontmatter, space/page variables, or custom blocks.
- [OpenAPI & API References](resources/openapi-integration.md) — Load when publishing OpenAPI specs, configuring the Scalar runner, or customizing endpoints.
- [Change Requests Lifecycle](resources/change-requests.md) — Load when creating, pushing, reviewing, diffing, or resolving GitBook Change Requests via REST or MCP.
- [Integrations Platform](resources/integrations-platform.md) — Load when building apps running inside GitBook (ContentKit UI, blocks, manifest, events).
- [Migration Guide](resources/migration-guide.md) — Load when migrating docs from Mintlify, Docusaurus, ReadTheDocs, or GitBook v1.

## Rules

1. **Transport Hierarchy**: Prefer GitBook MCP (`gitbook-mcp`) tools first when connected. Fall back to the REST API (`https://api.gitbook.com/v1`) using `GITBOOK_TOKEN` from the environment or `.env`. Never commit or echo tokens.
2. **Git Sync Primacy for Content**: For spaces with active Git Sync and local checkout availability, edit files directly in Git and push; do not bypass Git via direct API content-pushes unless the space is un-synced, the edit is an isolated typo fix, or no local repo is accessible.
3. **Mandatory Two-Link Rule for Change Requests**: Whenever a Change Request is created or updated, you MUST report both:
   - Diff link: `urls.app` from the change request.
   - Rendered site preview: Site URL with `/~/changes/<number>/` appended (derived from the underlying `Site` object). A bare site URL is not a preview.
4. **Confirmation Gates**: Require explicit user confirmation before executing state-changing actions: creating sites/spaces, requesting reviewers, posting public comments, resolving comments, submitting review verdicts, or dispatching Slack notifications.
5. **Lossless Markdown Round-Trip**: When pushing content via API, strip the duplicate leading `# <Title>` line (stored separately) and collapse multi-line integration blocks (`{% @mermaid/diagram %}`) to single lines to prevent escaping corruptions.
6. **Frontmatter Quoting**: Always quote `description:` values in frontmatter if they contain `:`, `#`, or YAML special characters to prevent silent Git Sync parsing failures.

## Task Routing

| User Intent / Task | Primary Guide | Deep Reference |
|---|---|---|
| Design site IA, create site/spaces, setup Git Sync | [site-configuration.md](resources/site-configuration.md) | [git-sync-handoff.md](resources/git-sync-handoff.md), [site-structure-design.md](resources/site-structure-design.md) |
| Author pages, insert blocks, set layout/frontmatter | [content-authoring.md](resources/content-authoring.md) | [blocks.md](resources/blocks.md), [frontmatter.md](resources/frontmatter.md), [block-ecosystem.md](resources/block-ecosystem.md) |
| Connect OpenAPI/Swagger spec, fix CORS/Scalar runner | [openapi-integration.md](resources/openapi-integration.md) | [openapi-extensions.md](resources/openapi-extensions.md), [openapi-test-it.md](resources/openapi-test-it.md) |
| Create CR, push edits, pull/fix comments, resolve | [change-requests.md](resources/change-requests.md) | [api-cheatsheet.md](resources/api-cheatsheet.md), [env.example](resources/env.example) |
| Review open CRs, inspect diffs, approve / request changes | [change-requests.md](resources/change-requests.md) | [api-cheatsheet.md](resources/api-cheatsheet.md) |
| Build custom editor blocks, ContentKit UI, OAuth app | [integrations-platform.md](resources/integrations-platform.md) | [contentkit.md](resources/contentkit.md), [integration-manifest.md](resources/integration-manifest.md) |
| Migrate from Mintlify, Docusaurus, or ReadTheDocs | [migration-guide.md](resources/migration-guide.md) | [cross-space-links.md](resources/cross-space-links.md), [configuration.md](resources/configuration.md) |

## Verification

- **Schema Check**: Confirm `SKILL.md` frontmatter satisfies `_SKILL_SCHEMA.md` and all linked resources exist.
- **Preview Link Check**: Confirm change-request preview URLs contain the `/~/changes/<number>/` segment.
- **Git Sync Syntax**: Verify `SUMMARY.md` adheres to strict list grammar (flat bullets under `## Group` headings) and frontmatter values with colons are quoted.
- **Token Check**: Confirm `GITBOOK_TOKEN` is present in the environment before invoking REST or non-OAuth MCP calls: `[ -n "$GITBOOK_TOKEN" ]`.
