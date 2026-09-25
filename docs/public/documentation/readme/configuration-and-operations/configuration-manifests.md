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
| `event_resources.external_origins` | Canonical HTTPS origins such as `https://resources.example.org`; no path, trailing slash, credentials, query, or fragment |

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

## Related Guides & Next Steps

* **[White-Labeling & Branding](../administration-and-branding/white-labeling.md)** — Customize colors, logos, and typography via manifests.
* **[First-Run Administration Guide](../administration-and-branding/admin-guide.md)** — Manage instance and tenant configuration through the web UI.
* **[Secrets Management](secrets.md)** — Separate credentials from declarative configuration manifests.
* **[Backup, Restore & Upgrade](backup-restore-upgrade.md)** — Create complete database snapshots before importing manifests.
