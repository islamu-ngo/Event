ABOUTME: Integration callback guide for incoming provider webhooks and moderation connectors.
ABOUTME: Separates incoming signed callbacks from outgoing webhook delivery provider configuration.

# Integrations

> **Audience:** Operators | Integrators | Contributors | AI agents
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-08-11
> **Source Anchors:** `Explore.API/Controllers/IncomingWebhooksController.cs`, `Explore.API/Controllers/ModerationIntegrationController.cs`, `Explore.API/Services/IncomingWebhookIntakeService.cs`, `Explore.API/Services/CoopWebhookSignatureValidator.cs`, `Explore.Infrastructure/Services/Moderation/`, `Explore.Infrastructure/Services/Registration/Providers/`, `docs/API.md`, `docs/SECURITY-MODEL.md`

Incoming integration webhooks are provider callbacks received by ISLAMU Event. They are not controlled by the outgoing webhook provider mode.

```text
Provider callback
  -> raw body read
  -> provider signature/API-key verification
  -> incoming_webhook_messages idempotency row
  -> Application command or outbox-backed side effect
```

Outgoing product webhooks use `Local`, `Svix`, `Composite`, `DryRun`, or `Disabled`; see [WEBHOOKS.md](WEBHOOKS.md).

## Incoming Routes

| Route | Provider | Authentication | Behavior |
|---|---|---|---|
| `POST /api/integrations/moderation/coop/callback` | Coop-compatible review queue | API key policy plus timestamped HMAC-SHA256 over raw body | Atomically retains the verified callback and one unique effect pointer. A fenced worker revalidates the retained payload and invokes authoritative decision execution; command success is required before receipt/pointer completion. |
| `POST /api/integrations/moderation/osprey/callback` | Osprey-compatible signal worker | API key policy | Records bounded moderation signals on the local report; it does not directly execute moderation actions. |
| `POST /api/integrations/registration/{provider}/{bindingId}/callback` | Registration-provider framework | `[AllowAnonymous]` edge plus provider-neutral callback verifier | Captures bounded provider-submission evidence as one `registration.provider_submission` effect, or a provider-specific response-sweep effect for notify-only sources such as Google Forms Pub/Sub. Non-oversize malformed, duplicate, unknown, stale, or parked evidence is acknowledged with `202 Accepted`; registration mutation happens later in the fenced worker. |
| `POST /api/integrations/svix/operational` | Svix operational webhook | `[AllowAnonymous]` at HTTP edge plus Svix-compatible signature verification | Verifies operational callbacks with `svix-id`, `svix-timestamp`, and `svix-signature`; tenant-addressed payloads are captured in the incoming ledger. |
| `POST /api/integrations/stripe/connect` | Stripe Connect account and Checkout evidence | `[AllowAnonymous]` edge plus endpoint-secret `Stripe-Signature` verification | Verifies exact UTF-8 bytes with the pinned Stripe API revision and configured live/test mode. Account events project readiness asynchronously. The Checkout allowlist is `checkout.session.completed`, `checkout.session.async_payment_succeeded`, `checkout.session.async_payment_failed`, and `checkout.session.expired`; payment events retain only a normalized identifiers envelope and schedule authoritative connected-account retrieval. |

These routes continue to work when outgoing webhooks are `Disabled`, `Local`, `Svix`, `Composite`, or `DryRun`.

Stripe Connect uses `POST /api/integrations/stripe/connect` through the same durable incoming-message boundary. The callback controller never mutates payment attempts or orders. Signed account evidence applies monotonic readiness to one historical connection owner. Signed Checkout evidence creates durable reconciliation work; the scheduled drain retrieves Checkout and PaymentIntent using the immutable connected-account snapshot, verifies exact session/payment IDs, amount, currency, and application fee, then applies the provider-neutral monotonic payment state. Ambiguous creates without a session remain `Unknown`: reconciliation uses the persisted dispatch epoch and fence to requeue the same idempotency key rather than creating a second payment attempt.

The Quartz payment drain uses the authoritative browser/BFF base URL lookup: `PublicBaseUrl`, then `App:PublicBaseUrl`, then `Application:PublicBaseUrl`. Checkout requires HTTPS; a normalized application subpath is preserved in callback and BFF routes. Browser-facing HTTP is not accepted, including loopback. Each pass drains committed Checkout creation work, always reconciles due provider state, then drains again so an Unknown same-key requeue can progress without waiting for another scheduler cycle. Missing or invalid origin configuration defers claimed pre-handoff work with a bounded failure code instead of blocking reconciliation or calling Stripe.

Deployment configuration uses `PublicBaseUrl` for the configured base URL, `Payments__Stripe__Mode` for Test/Live isolation, `Payments__Stripe__AllowedCheckoutHosts__0` for the exact destination host, and the instance/server-only `/stripe/STRIPE_PLATFORM_SECRET_KEY` plus `/stripe/STRIPE_WEBHOOK_SECRET` bindings. Split BFF navigation also requires Redis for its one-time server-side Checkout target; that transient ticket is not Stripe or payment truth.

Coop effect inspection and generation-checked dead-letter redrive are authenticated administrative operations under `/api/admin/incoming-webhook-effects`. Clients must use server-authored HAL affordances and must not receive callback bytes, hashes, signed provider decision IDs, or raw provider errors.

## Verification Rules

- Read the raw request body before JSON parsing.
- Verify signatures before processing side effects.
- Enforce configured max body sizes.
- Use provider message IDs as replay/idempotency keys.
- Acknowledge duplicate verified callbacks without replaying side effects.
- Persist only bounded intake metadata: tenant when present, provider, provider message ID, idempotency key, event type, payload hash, redacted headers, status, and failure category.
- Stripe payment intake hashes the exact signed body but persists only event, event type, Checkout object, connected account, mode, API revision, and created time; customer, card, billing, shipping, and buyer payload fields are not retained.
- Route side effects through Application commands or outbox-backed services; provider callbacks must not directly mutate sensitive aggregates.

ProblemDetails, logs, metrics, traces, screenshots, and issue templates must not include raw bodies, signature headers, authorization headers, provider secrets, API keys, bearer tokens, tenant/user identifiers, provider message IDs, or raw provider exceptions.

## Coop Flow

Coop integration is independent of outgoing product webhooks:

```text
User reports event
  -> local report, case, and provider-sync intent are stored
  -> optional Coop review-queue mirror runs from provider settings
  -> Coop sends signed callback
  -> ISLAMU verifies API key and HMAC signature
  -> incoming_webhook_messages records idempotency
  -> local report decision is created
  -> light moderation or heavy redaction command executes through existing local paths
```

The Coop mirror request is metadata-first. It excludes reporter text, reporter IP/User-Agent hashes, event titles, slugs, URLs, raw provider payloads, and provider secrets.

## Svix Operational Callback

Svix operational callbacks use Svix-compatible verification. Configure:

| Key | Purpose |
|---|---|
| `Webhooks:Svix:OperationalWebhookSecretRef` | Secret binding key used by the verifier. |
| `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET` | Local/deployment environment value used by the development seeder when configured. |

This callback does not require `Webhooks:Provider=Svix`; it is an incoming integration endpoint, not the outgoing SvixProvider.

## Registration Provider Framework

Phase 9 ships only the provider-neutral framework. Concrete provider claims start in Phase 10+ after dated conformance evidence. The exact capability tuple is `(ProviderCode, DeploymentKind, ApiVersion, AdapterPolicyVersion, ConformanceEvidenceRevision)`, and automatic finalization fails closed when the tuple is unknown, drift is blocking, trust is below the required sync mode, or receipt verification fails.

Callbacks follow the shared intake pattern: exact bytes are retained, the verifier returns a Data Protection receipt, and a durable effect pointer is created. The worker validates that receipt against tenant, connection, binding, provider, tuple, payload hash, provider submission id, and timestamp before using Phase 8 normalization/finalization. `NONE` stores nothing; `COMPLETION_ONLY` records evidence/fulfillment but zero answers; `SELECTED_FIELDS` and `FULL_SYNC` map approved fields; `MIRROR_ONLY` records evidence only when a sink capability exists, otherwise it parks.

Management APIs expose connection, binding, mapping, channel, health, and reconciliation resources under `/api/tenants/{tenantId}/events/{eventId}/registration-providers`. Health and queue data is privacy-bounded: connection validity, callback age class, drift class, reconciliation lag, queue depth, issue codes, generation, and timestamps only. Studio and integrations must use HAL relations (`manage-registration-channels`, `view-registration-provider-health`, `provider-create`, `origins`, `mappings`, `publish`, `manual-import`, `poll`, `retry`, `resolve`) and server-derived binding capabilities; they must not infer actions from provider names, local role checks, or caller-supplied capability codes.

Microsoft Forms uses the exact `MICROSOFT_FORMS|MICROSOFT_365|POWER_AUTOMATE_V1|ISLAMU_EVENT_MICROSOFT_FORMS_V1|2026-08-11` tuple. An organizer-owned Power Automate flow posts a bounded completion envelope with a binding-scoped API key; publication requires complete required mappings, the required attempt correlation mapping, and one successfully processed verified callback. Manual-import evidence cannot activate the binding. See [Microsoft Forms Power Automate Template](integrations/microsoft-forms-flow-template.md) for setup and CSV reconciliation.

Google Forms uses the exact `GOOGLE_FORMS|GOOGLE_WORKSPACE|v1|ISLAMU_EVENT_GOOGLE_FORMS_PUBSUB_V1|2026-08-11` tuple. The adapter supports OAuth-backed presentation, schema read, managed form provisioning, response read, OIDC-authenticated Pub/Sub notification intake, seven-day watch create/renew, immediate initial sweep, six-hour recovery sweeps, and opaque continuation cursors for unfinished batches. It does not advertise submission write/sink, auto-finalize, or file upload/Drive capability, and its descriptor-aware connection requires `WebhookSecretBindingId` to stay unset. Required import scopes are `openid`, `email`, `forms.body.readonly`, and `forms.responses.readonly`; managed provisioning additionally requires `forms.body` and deliberately does not request Drive scope. See [Google Forms Pub/Sub Integration](integrations/google-forms-pubsub.md).

## Related

- [WEBHOOKS.md](WEBHOOKS.md)
- [API.md](API.md#incoming-integration-webhooks)
- [CONFIGURATION.md](CONFIGURATION.md)
- [SECURITY-MODEL.md](SECURITY-MODEL.md)
- [Microsoft Forms Power Automate Template](integrations/microsoft-forms-flow-template.md)
- [Google Forms Pub/Sub Integration](integrations/google-forms-pubsub.md)
