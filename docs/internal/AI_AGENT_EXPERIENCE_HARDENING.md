ABOUTME: Documents guarded AI agent experience hardening for context summaries, plan previews, and replay reports.
ABOUTME: Defines proposal-first, HAL-gated, redacted boundaries for Phase 10 assistant and MCP workflows.

# AI Agent Experience Hardening

> Status: implemented guardrail contracts and deterministic fake/replay diagnostics.
> Last Updated: 2026-06-25

## Boundary

Agent-experience hardening remains additive over the AI Tool Contract Registry (ATCR). It does not add reflection execution, direct Blazor service calls, arbitrary EF/SQL/LINQ access, remote MCP command execution, or model-granted authorization.

The allowed control flow is:

1. Application contracts describe safe context, tool metadata, and proposal previews.
2. API/HAL responses decide mutating affordance availability.
3. The assistant or MCP adapter may request a proposed action only when registry validation passes.
4. Existing confirmation endpoints dispatch native CQS commands after user confirmation, idempotency, tenant checks, and authorization checks.

Plan previews and catalog visibility never execute commands and never grant execution authority.

## Schema-Only Context Summaries

`AiSafeDataContextRegistry` exposes an explicit allow-list for future prompt grounding. The current default context kind is `event-reference-summary`, backed by the existing `AiReferenceSearchResultDto` projection. Allowed fields are limited to public reference metadata such as `kind`, `referenceId`, `displayName`, `summary`, public session dates, `visibility`, and `format`.

Rules:

- No EF entity, `DbContext`, repository, SQL, LINQ, private note, attendee, registration, prompt, response, or raw content access.
- Unknown context kinds and model-selected fields fail closed through `AiSafeDataContextSummaryPolicy`.
- Empty field requests use the platform default allow-list; explicit field requests must match the allow-list exactly.
- Failure messages are stable and do not echo rejected field names.

## Multi-Step Plan Previews

`AiProposedPlanValidator` validates proposal-only multi-step plans before any proposed actions are persisted or confirmed. It uses registry metadata, payload schemas, HAL rels, context freshness, step status, and recovery metadata.

A plan can be confirmation-ready only when all steps satisfy:

- Tenant and conversation context are present and match the validation context.
- Step count is bounded.
- Each step is unique and still in the `Proposed` state.
- Referenced tools are registered and exposed through ATCR.
- Required HAL rels, including tool metadata rels such as `create-event`, are present.
- Captured context is fresh.
- Payload JSON passes the same registry/schema guard used by provider and MCP paths.
- No step requires clarification, is failed, or was already confirmed/executed.

The validator always returns `ExecutionAuthorityGranted = false`. A ready preview only means the UI/API may create proposed actions and then route confirmation through existing command handlers.

## Fake/Replay Usability Reports

Normal CI can run deterministic fake/replay usability scenarios without live provider credentials:

```bash
dotnet run --project Explore.Diagnostic/Explore.Diagnostic.csproj --configuration Release --no-restore -- ai-replay-report --output artifacts/ai-replay
```

The replay report exercises:

- assistant rail catalog + proposal preview readiness;
- MCP Inspector discovery checklist coverage for tools/resources/resource templates/prompts;
- MCP projected-tool selection and proposal-first/confirmation-required validation;
- missing HAL affordance blocking;
- invalid payload recovery and clarification metadata.

Artifacts are written as JSON and Markdown and intentionally omit prompts, provider responses, selected-reference content, raw tool payloads, tenant/user identifiers, endpoint URLs, API keys, model secrets, screenshots with user content, and raw exception bodies. Live-provider usability runs remain manual/nightly only and require separately governed artifact retention.

## MCP Inspector Redacted Smoke Runbook

Use the Inspector only after the deterministic replay report is green. The normal CI-safe command is still the fake/replay report above; Inspector is a manual smoke tool for an operator-owned local/staging environment with fake or disposable data. See [MCP_DEBUGGING.md](MCP_DEBUGGING.md) for Debug-build startup, redacted client config templates, GitHub Copilot Agent Mode smoke, JSON-RPC fallback, and the compatibility matrix.

Manual smoke scope:

1. Use the target API instance's default MCP startup posture (`Mcp:Enabled=true`, `Mcp:EndpointPath=/mcp`, `Mcp:Stateless=true`, and `Mcp:EnableLegacySse=true`) unless the deployment explicitly disables MCP.
2. Connect MCP Inspector to the Streamable HTTP endpoint configured by `Mcp:EndpointPath` (usually `/mcp`) using one authenticated context only: either `Authorization: Bearer <redacted-token>` or `X-API-Key: <redacted-api-key>`. For multi-tenant routes, include the same tenant binding used by the API edge, such as trusted host routing or `X-Tenant-Slug: <redacted-tenant-slug>`, but never persist the real value in artifacts.
3. List tools, resources, resource templates, and prompts. Expected bounded surface: anonymous-safe `list_ai_tool_contracts` and public event reads; scoped AI resources such as `ai_conversations`; scoped event-management reads/resources such as `list_my_events`, `get_event_creation_context`, `get_event_publish_readiness`, and `event_management_context`; prompt guidance such as `create_event_draft_with_confirmation` and `manage_event_with_confirmation`; and registry-projected `propose_*` event-management tools such as `propose_create_event_draft`, `propose_update_event_draft`, `propose_publish_event`, `propose_delete_event`, and Phase 5 sub-resource proposals.
4. Call `list_ai_tool_contracts` and verify the registry advertises proposal/confirmation semantics. Do not save raw response bodies if they contain fixture labels or private metadata.
5. Optional proposal-only smoke: call one low-risk projected proposal tool, preferably `propose_create_event_draft` or `propose_update_event_draft`, only against a disposable test conversation and fixture tenant. Stop after the proposed action is persisted; do not call product confirmation endpoints, do not mutate repositories, and do not claim an event was created or changed.
6. Rebuild and restart the API/client before treating missing tools as a product bug; stale Debug builds and stale MCP client registrations are the common failure mode.
7. Redact or discard Inspector screenshots, exports, browser storage, proxy logs, Copilot transcripts, and copied JSON. Retain only scenario codes, pass/fail status, redacted endpoint path, redacted auth mode, and bounded failure categories.

Forbidden Inspector/Copilot artifacts: prompt transcripts, selected-reference content, raw tool payload JSON, provider responses, tenant/user identifiers, endpoint URLs, bearer/API-key values, model IDs, screenshots with user content, raw MCP request/response bodies, and raw exceptions.

The automated contract harness in `Event.API.IntegrationTests/Features/McpProtocolContractTests.cs`, `EventManagementMcpPublicReadTests.cs`, `EventManagementMcpAuthenticatedReadTests.cs`, `EventManagementMcpRedactionTests.cs`, and `McpAuthorizationTests.cs` covers MCP `initialize`, discovery lists, registry discovery, public event read parity, authenticated event-management reads, generic/projected proposal-only calls, disabled endpoints, malformed requests, unknown tools, hidden fields, rate-limit partitioning, and redaction behavior without live clients or provider credentials. `McpDebugReadinessDoctorCheck` adds a read-only review gate for MCP debug docs/tests/replay/evaluation evidence without starting clients or printing secrets.

## Validation

Run:

```bash
dotnet test --project Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet --no-restore
dotnet test --project Explore.Diagnostic.UnitTests/Explore.Diagnostic.UnitTests.csproj --configuration Release --verbosity quiet --no-restore
dotnet run --project Explore.Diagnostic/Explore.Diagnostic.csproj --configuration Release --no-restore -- ai-replay-report --output /tmp/explore-ai-replay-check
```

Relevant tests:

- `AiSafeDataContextSummaryPolicyTests`
- `AiProposedPlanValidatorTests`
- `AiReplayReportGeneratorTests`
- `AiReplayReportWriterTests`
