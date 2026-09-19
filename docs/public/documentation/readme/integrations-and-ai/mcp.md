---
description: Expose the optional stateless proposal-first MCP endpoint with API-key, scope, and tenant controls.
---

# Model Context Protocol (MCP)

ISLAMU Event exposes an optional, integrated Model Context Protocol server endpoint at `/mcp` using stateless **Streamable HTTP**. This enables external AI agents (such as Claude Desktop, OpenCode, or custom LangChain agents) to query events and propose administrative actions within strict governance guardrails.

---

## 1. Enablement & Authentication

* **Startup Gate**: Configured via `MCP_ENABLED=true` in environment variables. If disabled, `/mcp` returns `404 Not Found`.
* **Direct Authentication**: External MCP clients authenticate using dedicated API keys passed via `X-API-Key: <key>` (see [Direct API Authentication](../security-and-identity/authentication.md#direct-api-authentication)).
* **Multi-Tenant Scoping**: All MCP operations enforce strict [Multi-Tenancy Boundaries](../security-and-identity/multi-tenancy.md). Calls without a resolved tenant context fail closed.

---

## 2. Capability Model & Human-in-the-Loop Safeguards

* **Anonymous Tools**: When called without authentication, tools provide access strictly to public, published event discovery.
* **Scoped Reads (`mcp:read`)**: Authenticated tools allow querying private attendee counts, drafts, and organizer reports according to caller permissions (see [Authorization](../security-and-identity/authorization.md)).
* **Proposal-First Mutations (`mcp:propose`)**: AI agents are **strictly prohibited** from mutating database repositories directly. Modifying tools (such as creating an event draft or editing ticket prices) create pending **Proposals** that require human organizer review and approval in the management console before domain changes take effect.

---

## 3. Server Capability Boundaries

The integrated MCP server implements the official Streamable HTTP standard:
* Does **not** implement legacy SSE or persistent WebSocket sessions.
* Does **not** permit arbitrary remote code execution or SQL evaluation.
* Does **not** allow AI models to bypass [HAL Action Affordances](../security-and-identity/authorization.md#the-golden-rule-of-client-ui-affordances).

---

## Related Guides & Next Steps

* **[Direct API Authentication](../security-and-identity/authentication.md#direct-api-authentication)** — Provision and manage API keys for external integrations.
* **[Authorization & Access Control](../security-and-identity/authorization.md)** — Understand how operation permissions guard data reads.
* **[Multi-Tenancy Architecture](../security-and-identity/multi-tenancy.md)** — Verify tenant scoping on API and MCP requests.
* **[Local Development Workflow](../contributing/local-development.md)** — Test MCP endpoints locally with .NET Aspire.
