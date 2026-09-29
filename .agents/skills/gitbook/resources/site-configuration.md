<!-- ABOUTME: Workflow for designing, scaffolding, configuring, and branding GitBook documentation sites. -->
<!-- ABOUTME: Covers site IA, monorepo scaffolding, site-level Git Sync handoffs, and REST/MCP orchestration. -->

# GitBook Site Configuration & Structure

A guide for creating and maintaining entire GitBook documentation sites end-to-end. Covers structure design, monorepo repository scaffolding, GitBook REST/MCP orchestration, and branding customization. For authoring page content, refer to [content-authoring.md](content-authoring.md).

## Transport & Credentials

- **MCP First**: If GitBook MCP tools are connected (`gitbook-mcp`), prioritize them for creating sites, opening change requests, or inspecting spaces.
- **REST API Fallback**: When MCP is unavailable, use `https://api.gitbook.com/v1` with a Bearer token. See [api-cheatsheet.md](api-cheatsheet.md) for endpoints.
- **Token Hygiene**: Read `GITBOOK_TOKEN` from the environment or `.env`. Never commit, print, or log tokens. If missing, prompt the user to create a personal access token at [app.gitbook.com/account/developer](https://app.gitbook.com/account/developer) and export it.

## The Fundamental Constraint: Git Sync Setup

GitBook programmatic APIs (REST and MCP) can create sites, sections, spaces, and apply branding, but **authorizing the Git provider (GitHub/GitLab), picking the repo/branch, and configuring initial sync direction are UI-only operations**.

The standard end-to-end flow:
1. **Scaffold Git Repo Locally**: Monorepo layout (one directory per space), with root `gitbook-docs.yaml` pre-mapping spaces, then push to remote.
2. **Create Resources Programmatically**: Create site, sections, and empty spaces via MCP or REST API.
3. **User Completes 1 UI Step**: Connect site-level Git Sync in the GitBook UI and confirm space mapping. See [git-sync-handoff.md](git-sync-handoff.md).
4. **Apply Branding & Cross-Space Links**: Resolve sentinels and configure branding customization.

## Inputs to Gather Up Front

Before scaffolding or calling APIs, establish:
- **Target Organization**: List user orgs (`GET /orgs`) and ask the user to confirm the target org title by name.
- **Site Plan & Visibility**: Default to `type: "site"` on the Ultimate plan (custom domain, AI assistant, custom fonts, unbranded) with public visibility unless specified otherwise.
- **Content Seed**: Identify existing markdown, platform migration source (see [migration-guide.md](migration-guide.md)), or raw notes.
- **API Spec Availability**: If documenting an API, check for an OpenAPI/Swagger spec up front. Prefer auto-generated `builtin:openapi` references over hand-authored endpoint pages. See [openapi-integration.md](openapi-integration.md).
- **Branding Essentials**: Primary hex color, light/dark logos, favicon, header/footer links, theme preset (`clean`, `muted`, `bold`, `gradient`).
- **Site Structure**: Section list with titles, Font Awesome icon names (e.g. `book-open`, `code`, `clock-rotate-left`), and descriptions.
- **Git Remote**: Confirm availability of `gh` or `glab`. If missing, notify user and provide manual remote creation steps.

## Verification & Confirmation Gates

### Verify Content Source First
Before designing structure, confirm access to user-provided repos/URLs. Git hosts return 404 for private repos; never substitute or guess an alternative source.

### Confirmation Gates for State-Changing Operations
Site/space creation and customization overrides are immediately visible org-wide. Never execute state-changing calls without displaying a concise preview and obtaining explicit approval:
```text
About to create in org Acme Inc (org_abc123):
- Site: "Acme Platform Docs" (type: site, plan: ultimate, visibility: public)
- Spaces: Guides, API Reference, Changelog
- Sections: Guides (default), API Reference, Changelog
Proceed? (yes/no)
```

## Designing the Site Structure

1. **Space List**: 1–4 spaces per coherent audience (e.g., Guides, API Reference, Changelog). A space is a unit of navigation and Git Sync.
2. **Section Grouping**: Top-level partitions in site nav (e.g., "Product", "Developers", "Resources").
3. **Section Groups**: Use recursive section groups only when 3+ related sections require visual grouping in navigation.
4. **Page Tree per Space**: Keep hierarchies shallow (1–2 levels). See [site-structure-design.md](site-structure-design.md).

## Scaffolding the Monorepo

Standard monorepo layout:
```
my-docs/
├── .gitignore
├── README.md                    # Repo-level readme (not space homepage)
├── gitbook-docs.yaml            # Site-level space-to-directory mapping
├── guides/                      # Space 1
│   ├── README.md                # Space homepage
│   ├── SUMMARY.md               # Table of contents
│   └── .gitbook/
│       └── vars.yaml            # Space variables
├── api-reference/               # Space 2
│   ├── README.md
│   └── SUMMARY.md
└── changelog/                   # Space 3
    ├── README.md
    └── SUMMARY.md
```

### SUMMARY.md Rules
- Top-level `README.md` is a sibling at the top, not a parent: `* [Homepage](README.md)`.
- Group headings use `## Group Name`. Bulleted pages under headings are flat: `* [Title](path/to/page.md)`.
- Do not indent all pages under `README.md`.
- Group names reflect user navigation intent, not raw folder names.
- OpenAPI pages use fenced YAML bullets (`type: builtin:openapi`).

### Cross-Space Links
Link between spaces using `https://app.gitbook.com/s/<spaceId>/<path>`.
During initial scaffolding, use sentinels: `https://app.gitbook.com/s/XSPACE_<KEY>/path`.
After space creation, resolve `XSPACE_<KEY>` to real space IDs via sed or script. See [cross-space-links.md](cross-space-links.md).

## Multi-Language & Auto-Translated Spaces

- GitBook supports auto-translated site-spaces attached to the same section.
- **Do not scaffold per-language folders in Git.** Keep one primary English tree in the repository.
- Translations are managed in GitBook UI under **Section Settings → Translations**.
- Customization payloads carry `localizedTitle` maps. Preserve existing language entries.

## Branding & Customization

Compose customization payloads by reading existing settings first, updating desired fields in memory, and writing back.
- Theme presets, light/dark color pairs, logos, favicons, header links, footer copyright.
- See [customization-recipes.md](customization-recipes.md) for tested payloads and schema details.

## Related Resources

- [site-structure-design.md](site-structure-design.md) — Heuristics for spaces, sections, and page trees.
- [git-sync-handoff.md](git-sync-handoff.md) — Copyable instructions for user UI Git Sync connection.
- [customization-recipes.md](customization-recipes.md) — Full styling and branding recipes.
- [cross-space-links.md](cross-space-links.md) — Sentinel patterns and replacement scripts.
- [migration-guide.md](migration-guide.md) — Platform migration workflows.
- [api-cheatsheet.md](api-cheatsheet.md) — REST endpoints for sites, spaces, and customizations.
