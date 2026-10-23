---
description: Bootstrap and export governed configuration without secrets, PII, or application data.
---

# Configuration Manifests

Configuration manifests move governed tenant settings and typed configuration documents across environments. They are specifically **not** database dumps, secret bundles, subject-data exports, or infrastructure snapshots.

---

## What Belongs in a Manifest

Only allowlisted instance/tenant settings and approved typed documents belong in the manifest contract. Typical uses include reproducible policy, [white-labeling presentation tokens](../administration-and-branding/white-labeling.md), email templates, and feature flags that the application explicitly recognizes through the [Administration Console](../administration-and-branding/admin-guide.md).

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
2. Validate schema version, authority signatures, and every included section before committing.
3. Apply outcomes are strictly atomic: if a single section fails validation, the entire manifest is rejected rather than leaving the system in a partially trusted state.

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

---

## Moderation Definitions

The source portability registry now declares `instance.moderation` and
`tenant.moderation` for export, preview, comparison, draft apply and verification.
The instance section belongs only to full manifests; the tenant section also
belongs to tenant packages. The implemented definition boundary
excludes reports, people, sanctions, credentials and source privileges. Imports
require explicit destination mapping and cannot activate a policy by themselves.

Preset comparison preserves local-only changes and flags independent edits to
the same definition, including changed consequences or removals. Reordering
equivalent scope, subject, effect or strategy selections does not erase local
edits or create a conflict. A comparison
does not apply those changes. The definition merge now preserves local-only
changes in a new proposed document and applies independent incoming changes
together. If any definition conflicts, no merged document is returned; there is
no partial import of the other changes. This remains a draft proposal, not an
authorized write.

The native integration now acquires actual destination authority and retained
policy history for preview and complete definition export. Unsupported retained
definitions can be exported for review without declaring them ready to activate;
an authorized destination without a selected policy does not receive an invented
default. It rechecks the bound destination before saving a
complete draft. Draft application participates in the same transaction as the
other imported sections; a rejection rolls back their earlier writes too.
It does not activate the draft or replace an independently active policy.
The source artifact contract now includes optional typed moderation definitions.
The shipped manifest and tenant-package schemas now describe those complete
definitions and consequence recipes; they were regenerated by the native tool.
Full-instance export includes independently authorized instance and tenant
definitions; tenant-package export reads only the authorized tenant. Missing
configuration omits the section, while corrupt selected definitions stop export
rather than silently losing meaning.

For the complete selected destination set, the session bridge retains server-owned reserved identities
and binds them to the approved artifact, target, selections, mappings, approvals
and expiry. A later apply does not reuse permission from preview: it rechecks
current authority and retained policy history in the same transaction as the
other sections. Tenant previews read only their tenant package. Stale approval
or a failed native publication rolls back earlier writes. Whole-instance import
source now resolves each mapped tenant to its active native destination, verifies
all destinations before writing, and applies their drafts atomically with the
instance draft and other sections. Only this import's own writes may advance a
shared qualification revision. A later authority loss or unrelated revision
change rejects the transaction. Fidelity reads the actual resulting artifact;
rollback snapshots retain the original neutral definitions, not permissions.
The server bounds the reservation to 256 tenants plus one instance definition.

Exported artifacts now carry the current legal authoring content too, including
unpublished imported revisions. Their portable intent is Draft: exporting or
reimporting text does not copy the source's legal approval, accountable identity
or publication schedule. Destination review and publication remain separate.
Selected legal content is saved inside the same import transaction before
moderation publication. A legal ownership rejection rolls back earlier imported
sections too; saving legal content does not independently commit it.
Preview compares normalized authoring content, so equivalent locale ordering,
language casing and line endings do not create false differences. Jurisdiction
assumptions and revision summaries cannot currently be stored natively. Preview
marks such a legal section as blocked, and apply rejects it rather than silently
discarding the metadata.

Registry capabilities describe the integrated source, not executed assurance.
They grant neither deletion nor unconditional rollback, and do not authorize
activation or reuse permission from a previous preview.
Whole-instance tenant moderation application source is integrated but untested.
Rollback to a previously absent policy, database/client generation and runtime
assurance remain open.
Do not treat draft export or these source bridges as activation or release
readiness.

---

## Related Guides & Next Steps

* **[White-Labeling & Branding](../administration-and-branding/white-labeling.md)** — Customize colors, logos, and typography via manifests.
* **[First-Run Administration Guide](../administration-and-branding/admin-guide.md)** — Manage instance and tenant configuration through the web UI.
* **[Secrets Management](secrets.md)** — Separate credentials from declarative configuration manifests.
* **[Backup, Restore & Upgrade](backup-restore-upgrade.md)** — Create complete database snapshots before importing manifests.
