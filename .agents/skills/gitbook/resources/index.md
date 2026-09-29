<!-- ABOUTME: Reading map and progressive disclosure router for the GitBook skill resource library. -->
<!-- ABOUTME: Directs agents and contributors to the exact workflow, reference, or template needed for a task. -->

# GitBook Resource Index

Use this index to locate the minimal, focused resource for your specific GitBook task. **Never load all resources at once**; load only the document directly relevant to your active step.

## Core Workflows (Task-Level Guides)

Load these when executing a high-level operational goal:

1. [site-configuration.md](site-configuration.md) — Designing IA, scaffolding monorepos, configuring spaces/sections, site-level Git Sync handoffs, and branding.
2. [content-authoring.md](content-authoring.md) — Writing and styling markdown pages, frontmatter options, space/page variables, and rich blocks.
3. [openapi-integration.md](openapi-integration.md) — Integrating OpenAPI/Swagger specs, Scalar runner setup, `builtin:openapi` nav, and CI/CD pipelines.
4. [change-requests.md](change-requests.md) — Creating, reviewing, diffing, and commenting on GitBook Change Requests via REST API or MCP.
5. [integrations-platform.md](integrations-platform.md) — Developing GitBook developer apps with ContentKit TSX, manifests, events, and dev proxies.
6. [migration-guide.md](migration-guide.md) — Migrating documentation from Mintlify, Docusaurus, ReadTheDocs, or GitBook v1.

## Deep Technical References (Just-In-Time Detail)

Load these for specific syntax rules, schemas, or implementation patterns:

### Content & Block Syntax
- [blocks.md](blocks.md) — Complete tag syntax and code examples for all GitBook custom blocks (`{% tabs %}`, `{% stepper %}`, `{% hint %}`, `{% updates %}`, `<table data-view="cards">`, etc.).
- [block-ecosystem.md](block-ecosystem.md) — Decision heuristics and smell-vs-fix table for selecting specialized blocks over plain prose.
- [frontmatter.md](frontmatter.md) — Page frontmatter schemas, layout options, cover images, conditional visibility, and expressions.
- [markdown.md](markdown.md) — GitBook markdown nuances, Mermaid diagrams, TeX math formulas, and SVG asset handling.
- [configuration.md](configuration.md) — Space anatomy, `.gitbook.yaml` options, `.gitbook/` directory layout, and strict `SUMMARY.md` grammar.

### Site Architecture & Git Sync
- [site-structure-design.md](site-structure-design.md) — Information architecture heuristics: when to use spaces vs. sections, nesting rules, and page trees.
- [git-sync-handoff.md](git-sync-handoff.md) — Copyable user instructions for the manual Git Sync UI connection step in GitBook.
- [git-sync-previews.md](git-sync-previews.md) — How to extract rendered site preview URLs from GitHub/GitLab commit statuses on PR branches.
- [cross-space-links.md](cross-space-links.md) — Cross-space link syntax, `XSPACE_<KEY>` sentinels during scaffolding, and resolution scripts.
- [customization-recipes.md](customization-recipes.md) — Theme presets, light/dark color pairs, font styling, logos, and header/footer customization schemas.

### OpenAPI & API References
- [openapi-extensions.md](openapi-extensions.md) — Complete specification of all GitBook `x-*` OpenAPI extensions with copy-paste YAML examples.
- [openapi-test-it.md](openapi-test-it.md) — End-to-end setup for the Scalar "Test it" interactive runner: authentication schemes, CORS, and GitBook proxy.

### Developer Integrations Platform
- [contentkit.md](contentkit.md) — ContentKit component library, JSX layout containers, display cards, inputs, and interactive actions.
- [integration-manifest.md](integration-manifest.md) — Field-by-field `gitbook-manifest.yaml` schema, OAuth configuration, permission scopes, and CLI commands.
- [integration-runtime.md](integration-runtime.md) — `@gitbook/runtime` API signatures, event handling catalog, context environment, and fetch handlers.

### APIs & Connectivity
- [api-cheatsheet.md](api-cheatsheet.md) — Quick reference for the GitBook v1 REST API (`https://api.gitbook.com/v1`), curl examples, and `gbapi` shell helper.

## Templates & Reference Fixtures

- [env.example](env.example) — Template for `.env` secrets (`GITBOOK_TOKEN`, `SLACK_WEBHOOK_URL`).
- [gitbook-review.config.json](gitbook-review.config.json) — Reference configuration template for space and page IDs.
- [example-site/](example-site/README.md) — Snapshot of a production multi-space documentation site illustrating real-world structure, vars, and blocks.
  > **Note**: For the ISLAMU Event platform, the primary live project reference is our repository's own [`docs/public/`](file:///home/amir/ISLAMU/Github/Event/docs/public/) portal configured via [`docs/public/gitbook-docs.yaml`](file:///home/amir/ISLAMU/Github/Event/docs/public/gitbook-docs.yaml).
