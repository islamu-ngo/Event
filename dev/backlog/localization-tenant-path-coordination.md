# Localization And Tenant-Path Coordination

- **Status:** Deferred; not implemented.
- **Trigger:** A localization workstream proposes language segments in browser URLs.
- **Decision dependency:** [ADR-034](../../docs/internal/adr/ADR-034-root-path-tenant-resolution.md).

## Problem

The current browser contract assigns the first eligible root segment to the
tenant: `/{slug}`. A future localized shape such as `/{lang}/{slug}` would assign
that same segment to culture. Two-letter tenant slugs are currently invalid, but
length is not a sufficient routing protocol and must not become an implicit
culture detector.

## Required Decision

Before adding localized URL segments, choose one deterministic owner and ordering
for culture and tenant resolution. Update route generation, inbound matching,
base-path rewriting, circuit navigation, redirects/canonical URLs, and the
reserved-slug catalog as one contract. Preserve configurable nonempty tenant path
prefixes and custom-domain behavior explicitly.

## Acceptance Boundary

- Ambiguous paths cannot select a tenant or culture by guesswork.
- The BFF still derives tenant context server-side and emits the trusted API
  header; the API does not parse localized browser paths.
- Existing system routes and language identifiers have machine-checked reserved
  coverage.
- Generated links, bookmarks, refreshes, and supported RTL/localized navigation
  use one canonical shape.
- Tests cover root-default, configured-prefix, and custom-domain deployments
  without sleeps or browser-header shortcuts.

## Non-Goals

This note does not select URL culture strategy, add redirects, reserve a language
list, or change the current `/{slug}` contract.
