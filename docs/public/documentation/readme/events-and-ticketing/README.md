---
description: Typed event data, governed properties, admission, check-in, payments, refunds, and payouts.
---

# Events & Ticketing

ISLAMU Event keeps event domain content, attendee registration, payment truth, admission issuance, and physical check-in as distinct authorities. This section explains each lifecycle and the invariants operators must preserve.

---

## In this Section

* **[Modular Event Aspects](modular-event-aspects.md)** — Relational sector models (Islamic-event details, prayer times, speakers, technology tracks) and feature module gating.
* **[Custom Properties](custom-properties.md)** — Governed custom registration questions, privacy exposure ceilings, property retirement, and GDPR data scrubbing.
* **[Ticketing & Check-In](ticketing-and-check-in.md)** — Registration vs. admission, cryptographic QR credentials, attendee recovery, and day-of-event check-in gates.
* **[Guest Participation Without Email](email-optional-participation.md)** — Save a private post-confirmation status link and keep it separate from checkout and public calendar files.
* **[Paid Events & Payouts](paid-events-and-payouts.md)** — Organizer-direct Stripe Connect onboarding, webhook reconciliation, refund workflows, and payout boundaries.

---

## Operating Invariant

Before publishing a paid or controlled-entry event, verify module policy, registration capacity, provider-confirmed payment/refund state, admission issuance, credential recovery, and exact-target check-in. Current server-issued [HAL links](../security-and-identity/authorization.md#the-golden-rule-of-client-ui-affordances) govern every operator affordance.

## Removing a Scheduled Item

Deleting a session, session group, or agenda item removes that item's venue and room references together. It does not delete the shared venue or another scheduled item's location assignment.

## Organizer resource drafts (API)

Authorized organizers manage event-resource drafts through private, no-store
API representations. Use the HAL links returned by `GET
/api/event/{eventId}/resources/management` or `GET
/api/eventresource/{id}/management`; do not retain an action URL as a standing
permission. The collection may offer create, and an item may offer edit,
archive, delete, audit, unpublish, or moderation according to current authority.

Management lists accept `page` and `pageSize` (at most 100). They do not return
global totals or count-derived navigation, because other resources may be
undisclosed. An empty page does not reveal whether other pages contain resources.

Create with `POST /api/event/{eventId}/resources` and a client-generated UUIDv7
`resourceId`. Retain that ID when retrying. Send an `Idempotency-Key` on every
write; replayed requests are authorized again, so a prior success does not
bypass a revoked or changed organizer role. Updates and state changes use the
current resource `version` as `expectedVersion`; refresh the representation
after a conflict.

These authoring requests carry semantic drafts: title, description, kind,
disclosure choice, audience rules, timing intent, and a delivery-type
placeholder. Configure files through the separate upload action below rather
than including storage references in a draft. External destinations are not
accepted by these requests. Publishing requires a completed, policy-permitted
file and a current `publish` action. Archiving is terminal and does not publish
anything; an archived draft can still be deleted when its HAL action is present.

The optional audit read is private and contains only the retained management
action, outcome, reason, time, and manager attribution. Retention zero collects
no new management audit entries and removes existing ones; expiry cleanup and
subject-erasure attribution clearing do not remove the shared draft. Administrators
set the governing limits through the existing
[event-resource governance](../administration-and-branding/admin-guide.md#event-resource-governance)
workflow.

## Reading event resources (API)

Use `GET /api/event/{eventId}/resources` and follow each item's `self` link.
Responses are private/no-store even for explicitly public materials. Eligible
readers receive the permitted metadata; other readers may see an organizer's
public teaser. Hidden and nonexistent resources both return 404. Audience reads
never expose management notes or underlying storage references. Eligible file
metadata contains only the display name, MIME type, byte size and safety state;
a teaser contains none of those file details.

Lists accept `pageSize` (20 by default, at most 100) and an opaque `cursor`.
Follow the returned `next` HAL link instead of constructing a continuation.
There are no global totals. A continuation exists only when another authorized
item was observed; it is not a saved permission. Cursors expire after 15 minutes
and are bound to the event, tenant and reader context. After signing in,
switching reader context, expiry or reordering, start again without the cursor.
Invalid cursor state returns 400 without including the token in the error.

Accessible-alternative links appear only when that alternative is independently
visible to the reader. File download and external-link navigation remain
separate capabilities; these metadata routes do not publish drafts or grant
delivery.

## Protected external destinations

An authorized manager follows `configure-destination` on an external-link
resource and sends a complete HTTPS URL with the resource's current version
and an `Idempotency-Key`. The URL is write-only: refresh the management detail
to see the configured safe origin, not the full path/query, token or stored
envelope. An instance administrator must explicitly allow the exact HTTPS
origin; a tenant may narrow but cannot widen that policy. IP addresses, local
hosts, deceptive IDN authorities, userinfo and malformed links are rejected
without echoing the submitted URL. Publish only after configuration; replace
or withdraw a link to revoke access.

Eligible readers see only the safe origin and a same-origin `access` HAL link.
Show that origin and a warning that navigation leaves this service before the
reader follows the link. It returns a temporary 302 with no-store/no-referrer
and puts the complete destination **only** in `Location`; neither the API nor
the BFF follows the external URL. No metadata, audit, export or teaser
contains the full destination or its ciphertext. Expired grants, withdrawn
resources, tightened policy or unavailable encryption keys produce no
Location. Origin validation applies only to the initial redirect: the
external site's DNS address and subsequent browser/provider redirects are
outside the platform's control. A link cannot be recalled from a third-party
service; withdraw/replace it here and rotate the third-party credential when
responding to an incident.

## Uploading and downloading resource files

Follow the management representation's `upload-file` action to reserve a file
for that resource and version. Supply its declared length, MIME type, display
name and stable replay identity. Complete the returned upload session through
the application transport. Do not construct a provider URL or attach a generic
storage object. A successful upload is not permission to download or publish.
If a version conflicts, refresh the resource before starting another operation.

Supported declarations are PDF, DOCX and PPTX, within the effective instance,
tenant and storage size/quota limits. HTML, SVG, arbitrary archives and
macro-enabled Office formats are not accepted. PDF inspection checks a
signature; Office inspection checks a conservative package subset and expansion
limits. Neither is malware scanning. Files remain explicitly **unscanned**.
Publication and access are denied by default until an instance administrator
explicitly permits this subset; a tenant cannot opt in on its own.

When eligible, follow the audience representation's `download` link. It serves
a same-origin attachment and checks current access again immediately before
response headers. An old link or uploader identity cannot bypass withdrawal,
expiry, changed membership or tighter policy. Responses are private/no-store
and do not support resumable ranges or cached conditional responses. Files
remain private in the storage provider even for a public resource audience.

Browser uploads use a subject/resource-bound opaque session. Their completion
response identifies the resource only; refresh its representation to obtain
the actions currently available. Keep the configured private storage and
persistent local-storage root described in the
[storage operator guide](../integrations-and-ai/storage.md).

---

## Exporting resource metadata

An authorized manager may follow the management collection's `export` link to
`GET /api/event/{eventId}/resources/export`. The private/no-store JSON contains
semantic metadata, audience rules and original relative timing intent. It can
include private organizer notes, so handle the exported document accordingly.
It contains no stored-file locator, destination, encryption envelope, manager
attribution or attendee history.

Use `page` and `pageSize` (20 by default, at most 100) for bounded pages.
No global count is returned. Every page requires current event and per-resource
export authority; an old link cannot bypass revocation. This exports metadata,
not file bytes, a ZIP archive, or an importable access grant.
Owned files may include the same safe file descriptor. A `download.href`
appears only when that exporting reader separately has current download
authority, and following it requires a new decision. Export authority alone
does not reveal a provider location or grant access to bytes.

## Related Guides & Next Steps

* **[Administration Guide](../administration-and-branding/admin-guide.md)** — Configure platform monetization and organization verified badges.
* **[Email SMTP Notifications](../communications-and-notifications/email-smtp.md)** — Reliable ticket delivery and event confirmation emails.
* **[Webhooks & Callbacks](../integrations-and-ai/webhooks.md)** — Reconcile external ticket sales and payment status changes.
* **[Privacy Erasure & GDPR](../security-and-identity/privacy-erasure.md)** — How attendee registrations and custom answers are scrubbed on account deletion.
