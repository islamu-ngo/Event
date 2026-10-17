---
description: Govern long-tail event and session data with explicit exposure and use grants.
---

# Custom Properties Governance

Custom properties allow organizers and tenant administrators to capture legitimate long-tail fields on events, sessions, and attendee registrations. They provide flexible data collection without polluting the core relational schema or weakening domain invariants (see [Modular Event Aspects](modular-event-aspects.md)).

---

## Governance & Exposure Ceilings

Each property definition enforces strict access and privacy controls:

* **Value Type**: Text, Number, Boolean, Single-Select, Multi-Select, or File Attachment.
* **Exposure Ceilings**:
  * `Public`: Visible on public event listings and marketing pages.
  * `Private`: Visible strictly to authenticated organizers and event staff.
  * `System`: Restricted to platform background processes and administrative tooling.
* **Purpose Grants**: Explicit permission flags determine whether an answer may be used in search indexes, CSV attendee exports, or moderation reviews.

> [!IMPORTANT]
> The configured `ExposureLevel` is a strict ceiling. Client UI code and analytics jobs can never widen access beyond what the server authorizes.

---

## Governance Report

Instance administrators and authorized tenant administrators can review active
event and session definitions, usage counts, last-use dates, exposure settings,
and promotion recommendations. Instance-administrator report access works with
local authorization as well as Cerbos; a separate tenant-admin grant is not required.
The report does not include collected answer values. Its tenant must match the
current tenant context; selecting another tenant does not grant access.

Scope and recommendation filters apply to the complete matching report before
pagination, so totals and page navigation describe the selected rows. A failed
read remains an error, not an empty successful report.

Committed administrative role revocations apply to subsequent report requests
against the authoritative database. They do not cancel a request that was
already authorized before revocation.

## Projection Administration

Tenant administrators can inspect event and session projection status, pending work and
projected values in the current tenant. Selecting another tenant ID does not grant
access. Unlike the governance report, projection row inspection includes collected
values: restrict these responses to authorized administrators. A requested exposure
ceiling narrows the returned rows; without one, all exposure levels are available
to the authorized administrator.

Pending-work pages now show distinct, ID-ordered portions of the backlog instead
of repeating the first page. Reading a page does not process it. A full event
rebuild refreshes values, drains event work and records its status. Refreshing one
event does not clear the tenant backlog. Drain processes the selected event or
session projection only. If a drain fails or is cancelled, pending work remains
available for retry and its transaction does not leave partially replaced rows.

Full session rebuild refreshes session values, drains only session work and records
session status. Refreshing one session does not clear pending tenant work. Failed or
cancelled session rebuilds roll back their transaction; retry after resolving the
failure. Event work remains independent.

Invalid event or session rebuild requests return a structured ProblemDetails error (HTTP 400);
quota exhaustion remains HTTP 422. Successful responses and routes are unchanged.
Session validation errors use code `validation_failed` and the
`eventSessionCustomPropertyProjection` error key; event validation retains
`customPropertyProjection`.
No database migration or manual data repair is required for these corrections.

## Lifecycle: Retirement vs. Hard Purge

1. **Normal Deletion (Retirement)**: Soft-deletes the field definition. Existing event registrations preserve their historical answers for auditability and financial reporting, but no new events can select the retired question.
2. **Hard Purge**: An audited administrator operation that permanently deletes only dependency-free definitions and their options. Historical answers, audit references and other blocking dependencies prevent definition purge; account-data erasure is a separate workflow (see [Privacy Erasure & GDPR Compliance](../security-and-identity/privacy-erasure.md)).
3. **Template Immutability**: Editing a registration template never retroactively alters published events or past tickets.

---

## Shared Organization and Group Definition Lists

New shared option definitions can be saved with their initial choices and selected default in one operation. The server assigns definition and option identities; a failed database commit does not leave a partial definition or refresh cached lists.

Shared definition lists are isolated by the server-resolved tenant, including cached pages. A tenant cannot select another tenant's cached definitions by supplying an ID in the request body or query string.

After a successful create, update, retirement or dependency-free purge, subsequent list reads refresh all affected page sizes and page numbers. Moving a definition between Organization and Group refreshes both lists. Other tenants keep their own cached lists. A failed or rolled-back mutation leaves the committed lists unchanged; no cache-expiry wait is required.

Shared-definition purge remains an audited administrator action: historical values, audit references and other blocking dependencies prevent purge. It does not erase referenced historical answers as part of this cache repair.

When upgrading, replace every older API instance before considering the tenant-isolation fix deployed. Updated instances never read the previous unscoped cache keys; no database migration or manual all-tenant cache flush is needed. If a request fails after the database commit, reload the definition before retrying: a cache-service error does not roll back a committed change.

---

## Shared Definition Option Order

Shared Organization and Group definition details, including the administration option table, display options in ascending **Sort Order**. A default option stays marked as default but no longer jumps ahead of a lower-ranked option. Equal ranks retain their existing relative order.

This presentation correction does not change option identities, stored ranks, the selected default, or retained inactive options and their historical references. It requires no database migration or manual data repair.

---

## Pattern Validation and Intentional Field Clearing

Shared Organization/Group, event and session definitions now use one bounded
pattern contract. `RegexPattern` accepts at most 2,048 characters and only syntax
supported by .NET's non-backtracking engine. Backreferences and lookarounds are
not supported; malformed or excessive patterns are rejected when creating or
editing a definition. Matching has a 100 ms timeout and checks the property's
configured input-length bounds first. Null or empty patterns mean no pattern
constraint; whitespace remains a literal pattern.

Text defaults must satisfy the configured length and pattern; number and date
defaults must satisfy their bounds. Length bounds cannot be negative, and an
upper bound cannot precede its lower bound. Editing a definition validates the
whole saved candidate, so changing only one bound cannot bypass the other bound
or an existing default.

On create, omitted `options` means an empty collection. An empty array is legal
for a non-option property; an option property needs at least one valid option.
Explicit `options: null` or an array containing null items is rejected.
On PATCH, omit the `options` group to preserve the saved choices. If the group
is supplied, `items` must be a non-null array without null entries; an empty
array still has to satisfy the resulting property type.

Nullable PATCH fields use a presence envelope. Omission, bare JSON null, and
`hasValue: false` preserve the saved value. To clear Description, send:

```json
{
  "metadata": {
    "description": { "hasValue": true, "value": null }
  }
}
```

Use the same envelope with `value: ""` to save an empty description, or with
a string to replace it. The nullable validation fields, including `regexPattern`
and the bounds, use the same envelope in the `validation` group. Send PATCH to
the definition's existing route with `If-Match: "<current concurrency stamp>"`
from its latest detail response. A stale stamp still requires a reload before
retrying.

Invalid candidates return HTTP 400 with `validation_failed`; the response does
not echo the submitted pattern or collected answer. Rejection leaves the stored
definition and answers unchanged. Historical unsupported patterns also reject
new event/session single or multi-value writes safely. Re-author such a pattern
using supported syntax, or intentionally clear it with
`{"hasValue":true,"value":null}` in `validation.regexPattern`, before retrying.
Correct any conflicting default or bounds in that same patch. No database
migration or automatic rewriting of stored patterns is part of this change.

This is an intentional pre-release syntax restriction. Deploy the matching API
and regenerated clients together; an older instance retains the unsafe pattern
behavior and is not a security rollback. If a save response is lost, reload the
definition and its concurrency stamp before retrying.

## Related Guides & Next Steps

* **[Modular Event Aspects](modular-event-aspects.md)** — When to use typed relational aspects vs. custom properties.
* **[Ticketing & Check-In](ticketing-and-check-in.md)** — How custom registration questions integrate into ticket checkout.
* **[Google & Microsoft Forms](../integrations-and-ai/google-and-microsoft-forms.md)** — Map external survey responses into native custom properties.
* **[Privacy Erasure & GDPR](../security-and-identity/privacy-erasure.md)** — Learn how custom property responses are purged upon user account deletion.
