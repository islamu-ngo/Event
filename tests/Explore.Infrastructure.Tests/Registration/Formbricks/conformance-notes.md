# Formbricks Conformance Evidence

Verified: 2026-08-10

## Evidence sources

- Context7 library `/formbricks/formbricks`, queried for Management API, response, survey, webhook, and Standard Webhooks contracts.
- Official Formbricks documentation:
  - <https://formbricks.com/docs/api-reference/rest-api>
  - <https://formbricks.com/docs/surveys/best-practices/headless-surveys>
  - <https://formbricks.com/docs/platform/features/integrations/webhooks>
  - <https://formbricks.com/docs/self-hosting/configuration/environment-variables>
- Official source revision `b204241c9dd7844f2315314b3f3d46d49330069d`, including v2 response/webhook OpenAPI and webhook crypto implementation.
- Supplied `schemas/openapi-formbricks.yml`, reviewed as secondary evidence only.
- Tavily MCP was requested and invoked, but no Tavily MCP server was registered in this session. No Tavily-derived claim is included.

## Pinned tuples

| Field | Cloud | Self-hosted |
|---|---|---|
| Provider code | `FORMBRICKS` | `FORMBRICKS` |
| Deployment kind | `CLOUD` | `SELF_HOSTED` |
| API version | `v1` | `v1` |
| Adapter policy | `ISLAMU_EVENT_FORMBRICKS_V1` | `ISLAMU_EVENT_FORMBRICKS_V1` |
| Evidence revision | `2026-08-10` | `2026-08-10` |

Unknown API versions, deployment codes, policy versions, or evidence revisions are unsupported and must fail closed.

## Proven contracts

- Management requests are server-side and authenticate with `x-api-key`.
- Documented v1 operations used by this adapter:
  - survey read/create/update under `/api/v1/management/surveys`;
  - response read/create under `/api/v1/management/responses`;
  - webhook list/create/delete under `/api/v1/webhooks`;
  - anonymous headless response writes under `/api/v1/client/{workspaceId}/responses` are documented, but ISLAMU uses server-side management writes so browser credentials and provider URLs remain hidden.
- Link surveys use the public origin and `/s/{surveyId}`; embed adds `embed=true`.
- Standard Webhooks verification uses raw body bytes and headers `webhook-id`, `webhook-timestamp`, and `webhook-signature`.
- Signed content is `{webhook-id}.{webhook-timestamp}.{raw-body}`.
- Secret material is the Base64-decoded suffix of `whsec_...`; signature is Base64 HMAC-SHA256 and the header form is `v1,{signature}`.
- Timestamp tolerance is five minutes and comparisons are constant-time.
- Supported callback events are `responseCreated`, `responseUpdated`, and `responseFinished`; registration completion subscribes only to `responseFinished`.
- Webhook create returns the signing secret once. Webhook list/read responses omit it.
- Response list pagination is bounded; the adapter never follows an unbounded checkpoint.
- A response can carry one language value, but the adapter does not claim multilingual form provisioning or mixed-language fallback. File answers remain unsupported because the supplied schema omits file-upload contracts and official response writes require workspace-owned storage references.

## Proven capability profile

`REDIRECT`, `EMBED`, `MANUAL`, `SCHEMA_READ`, `FORM_PROVISION`, `SUBMISSION_WRITE`, `SUBMISSION_READ`, `CALLBACK_VERIFICATION`, `SUBSCRIPTION_MANAGEMENT`, `RECONCILIATION`, `SUBMISSION_SINK`, and `AUTO_FINALIZE` are available only when the exact tuple above is configured and the binding's mode/mapping/trust checks pass.

`SCHEMA_READ` means extracting the documented survey question model into the provider-neutral snapshot. It does not claim a dedicated Formbricks schema endpoint.

`FILE_UPLOAD` and `MULTILINGUAL_FORMS` are absent from the provider capability vocabulary and resolve to the empty capability set. The adapter compatibility check also rejects file fields. A single immutable ISLAMU form version may carry its own language tag; this is not a multilingual provider capability.

`MirrorOnly` collection is a post-commit sink mode. ISLAMU owns validation, canonical answers, fulfillment, and finalization; the provider receives only mapped fields explicitly marked `IsProviderTransferAllowed`. The sink uses the existing fenced provider write-effect worker, not a second delivery path.

Remote response creation has no documented idempotency-key contract. Ambiguous provider acceptance is therefore never automatically retried; it is parked for reconciliation.

## Supplied schema discrepancy

`schemas/openapi-formbricks.yml` declares document version `2.0.0`, base URL `https://app.formbricks.com/api`, and only unversioned response operations. It omits `x-api-key`, management/client path prefixes, survey CRUD, webhooks, pagination metadata, webhook signing, and current self-host URL separation. It is useful for response field vocabulary but is not authoritative for routing, authentication, version dispatch, or capability proof.

Current Formbricks v2 source exposes response and webhook management paths, while survey CRUD is disabled in the v2 OpenAPI source. The Phase 10 adapter therefore follows the plan's documented v1 management contract rather than silently mixing v1 and v2 operations.

## Self-host runtime evidence

On 2026-08-11, the optional Compose profile was validated and started with digest-pinned Formbricks `5.2.2`, Hub, and Valkey images plus version-tagged PostgreSQL and Cube `1.6.6`. Both migration jobs exited `0`; PostgreSQL, Valkey, Hub, and Cube reported healthy; the Formbricks root and login surfaces returned HTTP `200`; an invalid route returned `404`. The default Compose service list remained unchanged when the profile was omitted.
