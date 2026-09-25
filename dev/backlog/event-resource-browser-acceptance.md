# Authenticated event-resource browser acceptance

- Owner: Accessibility/UI quality and the event-resource security owner.
- Activate when: A signed-in operator, active tenant, private file provider and protected external destination can be exercised in real Split and Combined deployments.
- Status: Eight of the 42 planned scenarios remain Partial; component, HTTP and provider tests do not substitute for these rendered interactions.

## Acceptance

- **S02, concurrent editing:** With two signed-in editors, mutate the parent event separately, then compete on the resource itself. Confirm the parent mutation does not advance the resource version and a stale resource save is denied without losing the winner's data.
- **S05, lifecycle:** Exercise withdraw, valid republish, archive and delete from the organizer UI. Saved file and external actions must deny immediately when unavailable; the UI must update its HAL affordances from the latest response.
- **S12, current authority:** Render eligible and ineligible subjects with different HAL actions, revoke eligibility after rendering, and prove a stale visible action receives the server's current denial and a safe refreshed state. Do not infer authority from local roles.
- **S17, unscanned file:** Show the instance-approved, constrained unscanned state and its explanation without an unauthorized download action; tighten policy and verify it disappears. Never describe unscanned bytes as clean.
- **S18, private delivery:** Download a permitted attachment through the same-origin route in a real browser. Confirm attachment behavior and no protected locator, storage key or byte URL enters rendered markup, history, diagnostics or an unintended request.
- **S22, external navigation:** Display the safe origin and warning before explicit navigation; do not preview-fetch the destination. Check the resulting permitted redirect and denied/revoked action without exposing a plaintext destination in page data or logs.
- **S31, accessibility:** Drive keyboard order, dialog focus restoration, native form errors, headings, accessible names and linked download/external warnings using desktop and mobile layouts in English, French and RTL Arabic. Record observed assistive-technology behavior rather than asserting WCAG certification from bUnit alone.
- **S34, Split/Combined isolation:** Capture actual browser-to-BFF and browser-to-API requests plus controlled external-origin receipts. No internal Authorization, cookie, tenant, antiforgery or privileged forwarding header may reach the outside service.

Subscribe to the exact navigation or response signal before an action; do not pass by fixed sleeps. Use approved, dynamically bound provider credentials and redact personal data, URL secrets and session tokens from saved evidence. Record a reproducible result for every scenario before promoting it from Partial to Verified. The additional multi-hop controlled-origin and durable-key tests are tracked in [delivery security verification](event-resource-delivery-security-verification.md).
