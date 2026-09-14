# Session Custom-Property Management

Session-local custom-property definitions and values stay scoped to the current
tenant. Cached definition pages no longer reuse another tenant's results, and
creating a definition for a foreign session is refused without writing data.
Claiming an administrator role in a request does not replace persisted authority.

Successful creation, editing, retirement and dependency-free purge refresh all
affected cached page numbers and page sizes. Other tenants retain their own
cached pages. Failed database commits leave the previously committed definitions,
values and audit history intact. New option definitions save their choices and
selected default together with server-assigned identities.

Value writes now persist their derived session projections in the same
transaction. Replacing or clearing multiple values removes obsolete projection
rows rather than leaving stale data. Exposure settings and value constraints
continue to apply; this does not grant access to additional values.

Definition and value validation failures return HTTP 400 ProblemDetails with code
`validation_failed` and the respective error key
`eventSessionCustomPropertyDefinition` or `eventSessionCustomPropertyValue`.
Quota exhaustion remains HTTP 422. PATCH still requires the current `If-Match`
stamp, and conflicting edits return HTTP 409. Use returned HAL links for available
actions; collection reads now correctly include authorized creation links.

Retirement preserves history. Administrator purge requires a reason and refuses
definitions referenced by historical values, projections, audit records or
template provenance. Successful purge writes its audit atomically with deletion;
it is not an account-data erasure mechanism.

Replace **all older API instances** to deploy the tenant cache fix. No database
migration, new setting or manual all-tenant cache flush is required. If a request
fails after commit because the cache service is unavailable, reload before
retrying: a cache failure cannot reverse committed data.

See [Custom Properties Governance](../documentation/readme/events-and-ticketing/custom-properties.md)
for the wider definition lifecycle and projection administration model.
