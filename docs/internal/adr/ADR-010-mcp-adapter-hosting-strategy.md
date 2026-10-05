ABOUTME: Decision record for the AI Tool Contract Registry MCP adapter hosting strategy.
ABOUTME: Defines transport, auth, tenancy, disable posture, and registry boundary for Phase 7.

# ADR-010: MCP Adapter Hosting Strategy

- **Status:** Accepted
- **Date:** 2026-06
- **Deciders:** Core team

## Context

The AI Tool Contract Registry is now the Application-layer source of truth for assistant tool definitions, JSON schema, payload validation, confirmation posture, authorization metadata, and execution routing. Product AI uses that registry to emit provider schemas and to validate untrusted proposed actions before persistence.

Phase 7 adds an MCP adapter for external agents. The adapter must not become a second authority for tools, authorization, tenancy, or mutation semantics. It must also stay optional for self-hosters that do not want to expose an agent-facing protocol endpoint.

Current repository evidence:

- No MCP server package or adapter exists in the codebase today.
- `Explore.API` is already the authenticated composition root for HTTP APIs, HAL link generation, rate limiting, tenant resolution, health checks, OpenTelemetry, and operational middleware.
- The official C# MCP SDK provides ASP.NET Core hosting via `ModelContextProtocol.AspNetCore`, `AddMcpServer()`, `WithHttpTransport(...)`, and `MapMcp()`. Current SDK guidance recommends explicitly choosing stateless Streamable HTTP for most HTTP servers, and treats legacy SSE as obsolete/high-risk unless isolated and trusted.
- Phase 8 completed retention cleanup, run polling, run cancellation, and operational guardrails before enabling another AI surface.

## Decision

Host the MCP adapter inside `Explore.API` as an optional ASP.NET Core endpoint, mapped by default at `/mcp` unless startup `Mcp:Enabled=false`, using the official `ModelContextProtocol.AspNetCore` package and stateless Streamable HTTP transport.

The adapter is an API presentation adapter over the existing Application registry and native CQS flows:

1. MCP tool definitions are generated from `IAiToolContractRegistry` definitions.
2. First-class projected MCP proposal tools use registry JSON schema fields plus only a minimal `conversationId`/`summary` proposal envelope.
3. MCP payload validation reuses the registry guard path.
4. Mutating MCP tools default to the same proposal/confirmation model as product AI. The adapter must not insert event data directly through repositories.
5. Tool execution that mutates state must route through existing Application commands/handlers or proposal records.
6. Tenant context must be explicit and fail closed before tenant/private registry-backed tools or resources are exposed. Anonymous-safe registry discovery is the only no-principal surface.

The endpoint is not enabled by default. Self-hosters must opt in through static configuration, and the platform must remain fully functional when MCP is disabled.

## Transport

- Use Streamable HTTP through ASP.NET Core.
- Configure stateless mode explicitly for horizontal scaling and to avoid in-memory MCP session affinity.
- Do not enable legacy SSE runtime transport for the first implementation. A startup `Mcp:EnableLegacySse` value may be accepted only as a ceiling for future governance; actual runtime SSE requires a separate trusted-isolated deployment decision.
- Map the MCP endpoint under an explicit path such as `/mcp` only when enabled.
- Do not run a product `stdio` MCP server from this host. `stdio` is useful for local developer tools, but the product boundary remains authenticated HTTP through `Explore.API`.

## Registration And AOT Posture

- Register MCP tools, resources, and prompts explicitly in the API host with `WithTools<T>()`, `WithResources<T>()`, and `WithPrompts<T>()` instead of assembly-wide discovery.
- Add registry-projected first-class proposal tools through the configured SDK tool collection, but keep the definitions sourced from `IAiToolContractRegistry`.
- Avoid `WithToolsFromAssembly()` in the product host because it performs runtime assembly scanning and increases reflection/trimming risk.
- Do not promise Native AOT compatibility for the MCP-enabled API host until a dedicated publish profile and verification gate prove the SDK registrations, registry-projected dynamic tools, JSON schema metadata, and authorization metadata survive trimming/AOT.
- Any future stdio host, legacy SSE host, stateful session support, or Native AOT profile requires an ADR/task update before it becomes a supported deployment posture. The local-only stdio diagnostic-host decision is tracked separately in [ADR-011](ADR-011-local-mcp-stdio-diagnostic-host.md).

## Protocol Evolution Review Gate

The initial MCP adapter intentionally exposes the smallest compatible surface: stateless Streamable HTTP, anonymous-safe registry discovery, authenticated/scoped tools/resources/prompts, registry-derived proposal tools, and no server-to-client requests. Current SDK guidance says stateless mode does not use `Mcp-Session-Id`, disables legacy SSE runtime behavior, and disables server-to-client requests such as sampling, elicitation, roots, and unsolicited notifications.

Before enabling any new MCP protocol capability, complete an ADR/task review that covers transport, auth, tenancy, rate limits, redaction, tests, self-hosting, and rollback. This gate applies to:

- stateful sessions, `Mcp-Session-Id`, GET/DELETE MCP endpoints, session migration, resumability, or resource subscriptions;
- legacy SSE or any client-specific compatibility shim;
- sampling, elicitation, roots, completions, progress notifications, tool/resource/prompt list-changed notifications, or server-initiated messages;
- dynamic client-visible tool/resource/prompt changes beyond the registry-projected proposal tools;
- protocol-version upgrades, new required headers, or client-specific behavior differences;
- any change that treats SDK annotations as authorization or execution authority rather than non-binding hints.

The default answer to compatibility pressure is to keep the current stateless surface unchanged, or explicitly set `Mcp:Enabled=false` for that deployment, until the review and targeted smoke tests pass.

## Authentication And Tenancy

- The MCP endpoint is mapped without endpoint-wide authorization so the official SDK authorization filters can list/call explicitly anonymous-safe registry discovery.
- External MCP clients should use `X-API-Key` machine credentials for scoped operations; bearer tokens remain available only for user-delegated/local smoke where appropriate.
- Official SDK authorization filters are enabled, and private MCP tool/resource/prompt methods carry `[Authorize]` metadata. These method-level attributes do not replace tenant resolution, registry validation, operation authorization, or HAL confirmation.
- Anonymous or invalid-key MCP access is limited to explicitly anonymous-safe capabilities such as registry discovery.
- Tenant context must be resolved before tenant/private tools/resources/prompts are listed or executed; valid tenant-bound API keys may provide tenant context.
- Fail closed when required tenant or authenticated principal identity cannot be resolved.
- Do not expose tenant IDs, provider endpoints, model IDs, prompts, tool payloads, API keys, or raw provider errors in MCP metadata, logs, metrics, health data, or error responses.

## Configuration

Add a static `Mcp:*` configuration section for the adapter posture:

| Key | Default | Purpose |
|---|---:|---|
| `Mcp:Enabled` | `false` | Enables mapping the MCP endpoint. Disabled is the default self-hosting posture. |
| `Mcp:EndpointPath` | `/mcp` | Route prefix for Streamable HTTP transport. |
| `Mcp:Stateless` | `true` | Keeps Streamable HTTP stateless for horizontal scaling and no session affinity. |
| `Mcp:EnableLegacySse` | `false` | Startup ceiling for future trusted-isolated legacy-SSE governance; runtime transport remains disabled. |

## Health And Operations

- Add a bounded health check for MCP adapter posture when implementation lands.
- Health output may report enabled/disabled, endpoint path, stateless posture, legacy-SSE startup ceiling, and legacy-SSE runtime state.
- Health output must not include tenant IDs, tool payloads, prompts, provider data, endpoint credentials, or API keys.
- Metrics must use low-cardinality labels such as `tool`, `outcome`, and `mode` only after reviewing cardinality. Avoid tenant/principal labels.

## Consequences

1. The first MCP implementation can reuse API authentication, rate limiting, telemetry, health, and tenant infrastructure.
2. Self-hosters do not need a separate MCP process for baseline operation.
3. A future separate MCP host remains possible if isolation, capacity, or transport requirements justify it.
4. MCP tool behavior stays aligned with product AI because generic and first-class projected MCP tools both read from the same registry.
5. Mutating tools remain slower than direct repository writes, but they preserve validation, authorization, idempotency, audit, and confirmation semantics.

## Alternatives Considered

### Separate MCP Host In Phase 7

Rejected for the first slice. A separate host may be cleaner for isolation later, but it adds deployment, tenant resolution, auth, health, and self-hosting complexity before there is runtime evidence that a separate process is needed.

### Stdio MCP Server

Rejected for the product platform. Stdio is useful for local developer tools, but it does not match the multi-tenant authenticated HTTP platform boundary or self-hosted API deployment model.

### Legacy SSE Transport

Rejected for the first implementation. Current C# SDK docs warn that legacy SSE has weaker request backpressure and should only be used for trusted isolated clients. Streamable HTTP is the selected transport.

### Direct Repository Mutation From MCP Tools

Rejected. It would bypass existing CQRS validation, authorization behavior, actor resolution, transactions, audit, and confirmation semantics.

## Implementation Gates

Phase 7.2 may start only after this decision is recorded and reviewed in active workstream docs. The implementation must include:

1. Central package version and project reference changes for the selected MCP SDK.
2. Disabled-by-default configuration and startup validation.
3. API-hosted MCP endpoint mapping behind explicit enablement, with per-operation authorization filters and anonymous-safe discovery only.
4. Registry-backed tool definitions and validation.
5. Tests proving disabled mode, auth/tenant fail-closed behavior, registry-backed tool metadata, and no direct repository mutation for mutating tools.
6. Self-hosting and operations docs.

## Related

- [ARCHITECTURE.md](../ARCHITECTURE.md) — composition root and Clean Architecture boundary.
- [CONFIGURATION.md](../CONFIGURATION.md) — static configuration surface.
- [OPERATIONS.md](../OPERATIONS.md) — health, metrics, and runbook posture.
- [SELF_HOSTING.md](../SELF_HOSTING.md) — self-hosting behavior and optional services.
- `dev/active/ai-tool-contract-registry/ai-tool-contract-registry-plan.md` — AI Tool Contract Registry implementation plan.
