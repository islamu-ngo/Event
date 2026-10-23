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

`AiToolPayloadGuard` admits at most 64 KiB of UTF-8 argument JSON and nesting
depth 16. It rejects repeated decoded property names at every object depth,
including escaped duplicates, before schema validation. Byte accounting is
separate from character count. Structural rejection has bounded failure codes
and cannot enter provider alias normalization through `AiToolContractRegistry`.
Exact-limit positive inputs retain ordinary registry/schema semantics. This
argument gate does not grant tool execution or replace confirmation, current
authority, expiry or durable operation recovery.

## Durable Draft Primary Effects

`AiToolExecution` reserves one execution and Event ID for the exact confirmed
CreateEventDraft payload, conversation, principal and acting actor. Derive
`AiEventDraftRecoveryIdentity` from persisted confirmation values after commit,
not unsaved timestamp ticks. `IAiEventDraftRecoveryRepository` acquires and reads
through fresh ordinary exact-tenant transactions with current actor grants.

`CreateEventCommand.FromAiDraft` carries an internal, JSON-ignored origin whose
mapped payload must remain unchanged. The native event creator reacquires that
authority and links the event to its execution inside the event transaction.
A matching linked primary is recovered rather than rebuilding its graph.
Settlement accepts only the exact tracked execution, stamp and transaction,
and cannot fail or reopen a committed primary effect.

Image source digest and upload custody identifiers are retained for later
orchestration; these contracts themselves perform no image transfer. Exact
settlement row-fences the native upload session and object: principal,
operation key, tenant, image purpose/visibility, finalized object identity,
provider binding and object key must match. A receipt cannot invent custody
from a supplied GUID. Successful image-bearing settlement additionally
requires the retained image on the original event. Rejected custody or
incomplete image settlement leaves the committed primary recoverable and
the action Confirmed; it neither marks success nor creates a replacement.
Two native adversarial cases cover these persistence boundaries.

Native positive metadata-custody tests exercise attachment and replay on the
original draft, settlement of that retained object and rejection of overwrite
when another image was selected. They do not assert that bytes were transferred.
Rejection now carries the nested conversation ID to selected-provider
authorization and checks that parent again under the native action fence.
Cancellation is observed before reads or mutation; rejection creates no
execution marker or event.

Conversation HAL confirmation links require a live native CreateEventDraft
owner. Exact Confirmed actions can expose recovery under their original
principal, while expired/future/unimplemented proposals cannot advertise
execution. Reject links remain available for Proposed actions. All links
still pass through the selected authorization evaluator; state eligibility
alone is not permission. These eligibility rules describe the current
implemented owner, not closure of the other registered families.

`AiStructuredActionParser` rejects more than eight calls before allocating
the parsed action list. `AiProviderResponseResolver` allows only the original
response plus one correction and counts candidate calls against a 16-call
run budget. A count-limit failure is not eligible for correction. Explicit
cancellation checks before and after provider handoff prevent parsing or
retry after cancellation. `ProcessAiRunCommandHandler` observes cancellation
before settlement and uses the caller's token for proposal persistence.
Controlled provider tests cover exact limits, rejection, one correction,
no third attempt and cancellation at handoff/return. The native API seam
also tests provider-return cancellation without publishing a message or
proposal. Native concurrent confirmation and execution-insert fault tests
now target the real CreateEventDraft owner and original durable recovery.

Full executable-family routing, persisted action version/provenance and
end-to-end uncertain image transfer recovery remain separate requirements.
Native regression source is authored; runtime and generated-schema assurance
remain queued under the approved security programme.

### Current family capability alignment and recovery transport

The complete 41-family source inventory finds only CreateEventDraft wired
to confirmation execution; forty families remain proposal-only and require
explicit integration disposition. Publication metadata now requires
`Event/Publish`, matching the native command, rather than `Event/Update`.
The twelve custom-property definition/value and event/session-template
administrative families require `Tenant/Update`, matching their native
requests rather than inventing item-level permissions. Both normal and
destructive definitions use this authority mapping. The proposal resolver
derives Tenant resource identity from the conversation, not model fields.
A machine-consumed metadata test compares all thirteen requirements with
the native request attributes. This is not an execution authorization pair.

Full native draft/image regressions now invoke confirmation, real event
creation, native upload reservation/finalization, receipt custody and
attachment. A controlled byte provider verifies the original primary exists
and no database transaction is held at handoff. Success, lost byte-write
acknowledgement and lost native finalization acknowledgement retain one
primary, one upload identity and one byte handoff. Finalized custody can be
recovered without transfer; an uncertain failed upload remains recovery
required rather than being blindly repeated. These tests are authored and
editor-checked, not executed.

The API maps recovery and expired/changed-authority conflicts to HTTP 409.
Only currently authorized `ai_draft_recovery_required` responses carry the
original draft GUID in the problem's `id` extension. The browser preserves
this typed GUID while retaining `Success=false`; other failures or invalid
identifiers do not acquire a recovery identity. Confirmation/rejection
client logs emit fixed categories/status codes, not exception objects or
conversation/action IDs. Mapper and client collector regressions cover
the transport boundary; generated clients and runtime/browser assurance
remain queued.

### Exact displayed-preview consent

Each proposed-action preview exposes `Version`, a SHA-256 comparison token
over its persisted action, tenant, conversation, principal, acting actor,
source-message identity, creation ticks, kind and exact payload digest.
Mutable confirmation/execution status is excluded so the same accepted
preview can recover its original confirmed effect. The token is not a grant;
selected-provider authorization, lifetime, actor grants and native row fences
remain mandatory.

Confirmation now requires a JSON `ConfirmAiProposedActionRequestDto` body
with `ExpectedVersion`. The handler compares it after current authorization
and again under the short claim fence. Missing or changed consent produces
`proposed_action_version_changed` (HTTP 409), before creating an execution
or event. The browser passes the displayed version rather than silently
reading and consenting to a newer server-side payload. Idempotency keys
remain independent operation metadata.

Six native stale-preview cases cover payload, kind, creation-time, actor,
source-message and missing-version drift. Existing exact/replay/image cases
carry their original version; the PostgreSQL HTTP regression reads the
returned preview once and replays that same version. Controller/client/UI
companions carry the required body token.

The public contract intentionally breaks old confirmation calls. OpenAPI
and typed-client regeneration remain forbidden until all programme source
implementation is complete. Consequently the authored client/UI source
currently depends on the pending generated request DTO and `Version` member;
it is not yet compile-certified. No generated artifact was hand-edited,
compatibility fallback added, warning suppressed or executable run performed.
Razor editor checks are unavailable because its configured Roslyn cohosting
server is absent; no install or browser/server execution was attempted under
the override. Final generation, builds and real UI assurance remain required.

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
