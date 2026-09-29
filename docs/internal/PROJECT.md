ABOUTME: Product context and scope statement for the current implementation state.
ABOUTME: Separates implemented capabilities from roadmap items to reduce ambiguity.

# Project Context

## What ISLAMU Event Is
ISLAMU Event (solution: `Explore`) is an open-source event discovery and management platform designed for multi-tenant deployments, with a public ISLAMU-hosted instance and support for self-hosting.

## Organization And Repository
- Organization: ISLAMU NGO
- Repository: `https://github.com/islamu-ngo/Event`
- License: AGPL-3.0-or-later
- Public instance: `https://event.openislamu.org`

## Current Scope (Implemented)
1. Event and session lifecycle management (create, update, delete, discover).
2. Organization and membership management.
3. Lookup-driven filtering (type, status, format, audience, language, etc.).
4. Multi-tenant runtime support with tenant-aware data filters.
5. Blazor BFF architecture with OIDC-based authentication.
6. Runtime-selectable authorization provider (Cerbos or local).
7. HAL/HATEOAS API responses and build-time OpenAPI generation for client generation.
8. Modular event aspects (Islamic and Tech aspect models).
9. Linked-account AT Protocol OAuth plus governed event federation through database-first PDS delivery, exact-collection Jetstream ingestion, and typed tenant-gated discovery.

## Platform Positioning & Market Strategy

### 1. The Red Ocean vs. Blue Ocean Distinction
The commercial event management space (e.g., Eventbrite, Luma, Meetup, Splash) is a crowded **Red Ocean**. Commercial platforms compete on proprietary UI iterations while imposing heavy per-ticket fees, rigid vendor lock-in, forced platform co-branding, and zero data sovereignty.

ISLAMU Event operates strictly in the **Blue Ocean**:
* **Open Source Core (AGPL-3.0-or-later):** 100% free software engine with zero ticketing commissions or volume caps.
* **Self-Hostable Sovereignty:** Operators retain absolute control over hosting infrastructure (Docker, Coolify, Aspire, On-Prem) and attendee database governance.
* **Purpose-Agnostic White-Label Engine:** Switchable branding, custom domains, dynamic tenant governance, and extensible aspect models (Islamic, Tech, Corporate, Community).
* **AI-Native MCP Endpoint (`/mcp`):** Native Model Context Protocol adapter for autonomous AI agents to query events and draft actions safely.
* **Decentralized Federation:** Built for open protocol event discovery (AT Protocol / Bluesky Jetstream ingestion and PDS delivery).
* **Ethical Non-Compete Model:** ISLAMU does not run a competing proprietary SaaS; we endorse vetted Official Partners and offer an optional commercial fleet-management Control Plane for multi-instance operators.

### 2. Deployment Versatility
1. **General-purpose software platform:** Easily adapted beyond Islamic use-cases via modular aspect schemas.
2. **ISLAMU-hosted instance:** Curated specifically for Islamic community events, policies, and prayer-aware metadata.
3. **White-label tenant engine:** Complete isolation and custom branding for single communities or multi-tenant organizations.


## Federation Status
Implemented AT Protocol surface:
- Confidential-client OAuth links an already-associated DID to a platform account without exposing AT credentials to the browser.
- Local event publication commits before immutable PDS delivery intent; a fenced worker delivers eligible event/RSVP records and settles normalized URI/CID state.
- A globally leased, exact-collection Jetstream consumer with optional DID curation materializes authoritative community event/RSVP records for governed tenant discovery.
- Administrator capability/profile controls, user publication consent, safe source HAL, and delivery status are available through the API and Blazor client.

Roadmap protocol surface:
- Full ActivityPub gateway/interoperability endpoints.
- First-party ATProto PDS or AppView hosting.

This implemented-surface inventory is not a release-readiness claim. The Bluesky, Eurosky, and self-hosted-PDS live-provider matrix remains release evidence outside the automated ATProto phase gates.

## Non-Inferable Product Notes
1. Deployment mode (`SingleTenant` / `MultiTenant`) is runtime-governed, not compile-time.
2. Instance and tenant governance settings can lock or delegate behavior.
3. Authorization behavior can change by configuration without changing controller code.

## Near-Term Documentation Contract
Use the following docs as source of truth while implementing:
- `docs/QUICK_REFERENCE.md`
- `docs/ARCHITECTURE.md`
- `docs/API.md`
- `docs/SECURITY-MODEL.md`
- `docs/MULTI_TENANCY.md`
