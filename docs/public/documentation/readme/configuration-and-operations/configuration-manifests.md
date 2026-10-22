---
description: Bootstrap and export governed configuration without secrets, PII, or application data.
---

# Configuration Manifests

Configuration manifests move governed tenant settings and typed configuration documents across environments. They are specifically **not** database dumps, secret bundles, subject-data exports, or infrastructure snapshots.

---

## What Belongs in a Manifest

Only allowlisted instance/tenant settings and approved typed documents belong in the manifest contract. Typical uses include reproducible policy, [white-labeling presentation tokens](../administration-and-branding/white-labeling.md), email templates, and feature flags that the application explicitly recognizes through the [Administration Console](../administration-and-branding/admin-guide.md).

---

## Prepare Tenant Rows with Configurator

The [Event README](https://github.com/ISLAMU/Event#self-hosting) provides the pinned
definition source and digest for a complete workspace on your deployed
Configurator origin. Load the matching local definition if the source cannot be
fetched. The definition is public metadata; your answers remain local in memory.

Choose the configuration-manifest workflow, then add or remove tenant rows and
edit their identifiers, display names, allowlisted settings and declared typed
documents. Removing a row removes that tenant from the prepared manifest. Tenant
identifiers must be unique lowercase slugs. Event permits up to 256 tenants;
complex inputs may reach Configurator's resource budgets sooner. Operators do
not need to invoke a development exporter to change the tenant count.

Review every artifact before use. Preparation checks the declared data shape;
Event still owns semantic validation, authorization and deployment acceptance.
Manifest tenants never carry secrets. The separate environment workflow may use
explicit secret-entry consent or draft output for values you will fill later.

---

## Event-Resource Policy Arrays

Instance manifests and tenant configuration packages accept these settings as
JSON arrays of strings, not JSON encoded inside a string:

| Setting | Accepted items |
|---|---|
| `event_resources.enabled_delivery_types` | `StoredFile`, `ExternalLink` |
| `event_resources.enabled_audiences` | `Public`, `AuthenticatedTenantMember`, `SessionRegistrant`, `TicketHolder`, `CheckedInParticipant`, `AnyEventSessionSpeaker`, `SessionSpeaker`, `EventStaff`, `Organizer` |
| `event_resources.permitted_file_types` | `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.openxmlformats-officedocument.presentationml.presentation` |
| `event_resources.external_origins` | Authoritative HTTPS origins such as `https://resources.example.org`; no path, trailing slash, credentials, query, or fragment |

For example, `"event_resources.enabled_delivery_types": ["StoredFile"]` permits
only stored-file delivery. Names and MIME types are case-sensitive; whitespace,
numeric enum values, and comma-combined names are not accepted. Use separate
array items for multiple values. Null, objects, and non-string items are rejected.

An empty array explicitly permits nothing; it does not restore defaults.
Duplicate items are accepted but count only once. Omitting a setting preserves
the normal default/inheritance behavior. External origins default to an empty
set and must pass the native safety checks, which also reject local, IP-literal,
and wildcard hosts. The shipped JSON schemas describe the array types and closed
item sets; importing still checks origin safety and tenant policy ceilings.
Tenant choices can only narrow the instance policy, never broaden it.

---

## What is Strictly Excluded

Manifests deliberately exclude:

* Users, credentials, and identity claims (see [Authentication](../security-and-identity/authentication.md)).
* [Registrations, admissions, orders, and payments](../events-and-ticketing/paid-events-and-payouts.md).
* Attendee and data subject PII.
* Operational queues and transactional outbox state.
* Provider runtime bindings, endpoints, and connection strings.
* Database or [Privacy-Erasure Authority topology](../security-and-identity/privacy-erasure.md).
* Passwords, tokens, and secret values (see [Secrets Management](secrets.md)).

> [!NOTE]
> Whole-instance export preserves governed configuration only. It does not replace full database backups (see [Backup, Restore & Upgrade](backup-restore-upgrade.md)) or privacy-erasure compliance obligations.

---

## Safe Ingestion & Trust Boundary

Manifest file ingestion represents an administrative trust boundary:
1. Restrict file access, ownership, and write permissions to the application service user.
2. Validate schema version and every included section before committing.
3. Apply outcomes are strictly atomic: if a single section fails validation, the entire manifest is rejected rather than leaving the system in a partially trusted state.

For unattended bootstrap, the trust origin is the owning post-migration host's
deployment-selected `Bootstrap` mode, local regular file, and selected
database/namespace. Review and protect that deployment configuration and mount
the manifest read-only. A supplied parsed manifest, database credential, or
checksum alone does not authorize bootstrap.

The reader computes a SHA-256 checksum to identify the exact file; it does not
verify a digital signature or authenticate a signer. Do not treat a checksum
as proof of who supplied the manifest. Authenticated Day 2 import uses its own
administrator admission, not this request-free startup origin.

In split deployments, the API owns this startup step after MigrationService
finishes successfully. Docker Compose mounts the reviewed source read-only
into `islamu-event-api`; Aspire passes mode/path to `Explore.API`, which waits
for migration completion. MigrationService receives neither the manifest nor
Global credentials. Standalone keeps its own migrate-then-manifest sequence
and does not repeat it in shared API startup.

Start or recreate the API with `CONFIGURATION_MANIFEST_MODE=ValidateOnly`,
check its startup outcome, then repeat with `Bootstrap`. Return to `Off` after
successful application. Validation starts the ordinary API host; it is no
longer a one-shot MigrationService command. If you deploy the API directly,
complete migrations first. Native authority that is absent or incompatible
rejects bootstrap rather than falling back to migration credentials.

The new bootstrap file-custody implementation currently supports Linux only;
other operating systems reject Bootstrap rather than using weaker file checks.
PostgreSQL installation enrolls only the selected Global login in the dedicated
`event_manifest_bootstrap_global` execution group. Runtime receives no bootstrap
grant. These registration and installation hooks are authored; the bootstrap
source review, native installation and runtime acceptance remain pending.

ValidateOnly reads a consistent snapshot without taking the bootstrap manifest
mutex or retaining source row locks. It rolls back its read transaction and
does not grant permission to apply that preview later; Bootstrap must originate
its own deployment-file attempt.

Failed-bootstrap history records acknowledged rollback, not successful application.
The backend uses the database model's audit-state representation consistently;
an applied receipt cannot be submitted as failed history. Keep matching backend
and native capability definitions, and investigate drift rather than bypassing
readiness or manually relabeling an operation.

Bootstrap coordinates configuration changes in the API. It acquires ordered
coordination leases before starting its atomic database transaction and retains
them through commit or rollback. A cooperating API writer waits, then reads the
settled state; no privileged table/row-lock routine or broader table permissions
are introduced. Global still does not receive the dedicated native-owner role.

Use the API for configuration changes. Direct database edits bypass this
coordination and can race, including inserting a setting Bootstrap observed as
absent. Transaction isolation and conflict checks do not guarantee detection of
every out-of-protocol edit. Conflicts can fail an attempt; it is not automatically
replayed. Keep database credentials restricted and do not treat direct SQL edits
as an equivalent administration workflow.

Bootstrap commits its configuration and a durable effect record together. API
startup then leaves that record for the normal outbox worker rather than trying
to deliver it with the bootstrap database identity. Keep the outbox worker enabled
and monitor pending/retry/dead-letter work: successful bootstrap does not mean
every notification or cache effect has already completed. Worker downtime delays
delivery without discarding the committed record. Do not manually claim, mark
complete or delete an effect to force startup or recovery to look successful.

Bootstrap effect records carry a versioned list of changed setting-key names,
Created community IDs and the original notification time. They do not contain
setting values, credentials or the source configuration. The API writes these
instructions atomically with the committed configuration; the worker uses its
claimed message without needing private bootstrap audit-table access.

Use matching application and database capability definitions. Old null-payload,
unknown-version or corrupt effects are not silently converted or delivered.
Investigate an undelivered effect through the outbox recovery procedure; do not
edit its payload, lease or receipt, force completion, or replay configuration to
hide the delivery failure. Before upgrading, retain a coherent backup checkpoint
for application/database rollback. Native definition drift remains a startup
failure, not permission for automatic database repair.

---

## Standard Operating Procedure

1. Export or author an allowlisted manifest without secrets.
2. Review the diff and intended tenant scope.
3. Back up the affected database stores (see [Backup, Restore & Upgrade](backup-restore-upgrade.md)).
4. Apply in an isolated staging environment first.
5. Verify updated settings, HAL affordances, public branding, and provider behavior.
6. Promote the reviewed artifact and record its SHA-256 checksum in operational logs.

---

## Tenant Configuration Packages

Tenant administrators can export a tenant configuration package in either
Overrides or Portable view, then upload it for preview and import into an
authorized tenant. Keep its generated authority and sovereign-field omission
metadata intact: the importer validates those declarations. The omission list
describes excluded fields; it contains no credentials and grants no additional
authority.

Committed imports and forward rollbacks can finish cache refresh and
notifications asynchronously. Keep the committed receipt if delivery needs
recovery; retry its existing effect rather than importing the configuration
again. Delivery accepts only the effect message bound to that successful
receipt. Failed imports and mismatched messages cannot publish those effects.

Human imports retain the original publication-policy rows and missing keys
before validating the fresh preview. Applying a selected publication change
must consume that same observation and verify its exact persisted result.
Changed row identities, metadata or other policy inputs reject the whole
attempt; they are not silently adopted as a new baseline. Tenant imports
continue to reject overrides of locked instance publication settings.

Deploy the updated native import contract through the owning migration process
before enabling its consumers. The selected Global import capability uses the
retained-original writer; direct execution of the older raw publication writer
is revoked. The security programme's runtime assurance for this change remains
pending, rather than being established by documentation or source diagnostics.

Instance exports read each selected active tenant through its own isolated
read scope, so the administrator's current tenant does not change the exported
values. Tenant-package exports read only their authorized target. This does not
change the import's atomic commit: fidelity checks remain on the transaction
that owns its configuration changes.

Import snapshots also retain the latest successful bootstrap identity and
metadata through a bounded native read on that same owner. Deploy its updated
snapshot function through the migration process; do not repair missing
capability installation by granting the application direct access to bootstrap
history. Runtime assurance for this repair remains pending.

Portable comparison snapshots resolve effective settings through the import's
own read scope, just as Overrides reads its selected values there. The
administrator's currently selected tenant is unchanged, and does not substitute
for the authorized import target. Instance locks and secret omission still
apply; no extra database grants or configuration variables are needed for this
settings-read correction.

Tenant lock targets are also selected through a bounded native read. A matching
inactive tenant remains visible to the import's eligibility checks; this does
not authorize applying changes to an inactive tenant. The application does not
need direct tenant-table access for this selection.
Resource-change notification timestamps use the same registered clock as the
import receipt. Provenance validation still rejects notifications outside that
receipt's interval; it does not widen the interval to accept inconsistent clocks.

Legal source snapshots include the current editable version, including an
unpublished draft: audience, localized title/summary/Markdown, template
provenance and the fresh-acceptance requirement. Kind names use the manifest
format, such as `TermsOfService`, rather than public route names such as
`terms-of-service`. The imported intent is `ReviewRequired`; review identities,
publication status and acceptance history are not transferred.

Imported legal source is persisted inside the import transaction before fidelity
is checked, but remains invisible to other requests until commit. A failed import
rolls back that source with the other selected changes. Existing unpublished
drafts are not overwritten; a revision starts only from published or retired
state and must pass the target's legal review before publication.

Legal Markdown is restricted text, not an executable template. Keep each locale
within 256 KiB of UTF-8 input, 128 links and 64 total identity-placeholder
occurrences, including placeholders in link labels. Repetition of the same name
counts each time. Identity substitutions are at most 500 UTF-16 characters and
are HTML-encoded once; text inside them is never interpreted as another
placeholder or as markup. Inline nesting is limited to 32 and rendered HTML to
1,048,576 UTF-16 characters. HTML escaping can expand text, so input within its
byte budget can still exceed the output budget.

Over-budget content produces no partial public HTML. If a published document
cannot render within these bounds, its public route reports unavailable.
Shorten the governed source, review it and publish a new version through the
existing legal lifecycle. Remote links are checked as destinations, never fetched
by the renderer. Do not add active template expressions to work around a limit.

Selected changes, consumed session, rollback snapshot, terminal receipt and
deferred-effect marker commit together. Failed apply rolls back the selected
changes; its sanitized failure evidence can be recorded afterward without
committing any of those changes.

Fidelity reads bypass shared settings and branding caches. They can inspect
the import transaction's own changes before commit without exposing those
changes through cache refresh or another reader.

---

## Related Guides & Next Steps

If an import loses its database commit acknowledgement, its outcome is unknown,
not necessarily failed. Do not repeat the import or treat an error response as
proof of rollback. Inspect the existing import session and operation history:
the configuration, consumed session, applied receipt and pending effect may
already be committed together. Recover the existing pending effect through the
outbox worker rather than applying the configuration a second time. Ordinary
and Setup imports coordinate concurrent application work before starting their
transaction; direct database edits outside that protocol can bypass these rules.
Setup application does not automatically replay its transaction. A coordination
release error after an acknowledged commit also does not undo the import:
investigate the existing applied receipt and pending effect rather than retrying.
If acknowledgement and cleanup both fail, the API retains both causes and keeps
the commit outcome classified as unknown; a cleanup error is not rollback proof.

* **[White-Labeling & Branding](../administration-and-branding/white-labeling.md)** — Customize colors, logos, and typography via manifests.
* **[First-Run Administration Guide](../administration-and-branding/admin-guide.md)** — Manage instance and tenant configuration through the web UI.
* **[Secrets Management](secrets.md)** — Separate credentials from declarative configuration manifests.
* **[Backup, Restore & Upgrade](backup-restore-upgrade.md)** — Create complete database snapshots before importing manifests.
