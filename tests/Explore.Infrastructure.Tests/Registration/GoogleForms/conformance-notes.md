# Google Forms conformance notes

Date: 2026-08-11

Pinned tuple: `GOOGLE_FORMS|GOOGLE_WORKSPACE|v1|ISLAMU_EVENT_GOOGLE_FORMS_PUBSUB_V1|2026-08-11`

## Official evidence captured

- Google Forms REST v1 form creation accepts title-only form metadata; form content is mutated after creation with `forms.batchUpdate`.
- API-created forms default unpublished, so managed provisioning must call `forms.setPublishSettings` and then `forms.get` to verify `publishSettings.publishState.isPublished == true` and `isAcceptingResponses == true` before returning success.
- Google Forms responses are read-only through REST v1; the adapter must not advertise submission write, submission sink, headless submit, or auto-finalize.
- Google Forms watches are Pub/Sub only; durable authenticated watch state, sweep, callback verification, and renewal are deferred to the downstream subscription-state worker.
- Pub/Sub notifications do not carry a response ID suitable for unauthenticated completion; this adapter fails callback verification closed until the authenticated Task12.3 lifecycle resolves response IDs by server-side read/sweep.
- Google Forms file-upload questions require Drive-backed uploads and are not supported here because this adapter intentionally requests no Drive scope.
- OAuth refresh exchanges are always sent to Google's canonical token endpoint (`https://oauth2.googleapis.com/token`); tenant-supplied `token_uri` metadata is ignored so refresh/client secrets cannot be posted to arbitrary hosts.
- Google Forms preserves choice labels, not ISLAMU option identifiers. Managed provisioning rejects duplicate labels because they would make submission answers ambiguous; compatible option keys use the label fingerprint that remote schema reads can reproduce.
- Google Forms long text is represented by `textQuestion.paragraph == true`; schema reads preserve it as `LongText` instead of collapsing to short text.
- Unsupported question shapes are imported as opaque external/blocking fields, never guessed as `ShortText`, so drift review fails closed instead of silently changing semantics.

## Capability boundary

Advertised now: Redirect, Embed, Manual, SchemaRead, FormProvision, SubmissionRead.

Not advertised: CallbackVerification, SubscriptionManagement, Reconciliation, SubmissionWrite, SubmissionSink, headless submit, auto-finalize, multilingual, file upload.

## Minimal scopes

- Import: `openid email https://www.googleapis.com/auth/forms.body.readonly https://www.googleapis.com/auth/forms.responses.readonly`
- Managed provision: import scopes plus `https://www.googleapis.com/auth/forms.body`
- No Drive scope.
