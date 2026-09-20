---
description: Bootstrap and export governed configuration without secrets, PII, or application data.
---

# Configuration Manifests

Configuration manifests move governed tenant settings and typed configuration documents across environments. They are specifically **not** database dumps, secret bundles, subject-data exports, or infrastructure snapshots.

---

## What Belongs in a Manifest

Only allowlisted instance/tenant settings and approved typed documents belong in the manifest contract. Typical uses include reproducible policy, [white-labeling presentation tokens](../administration-and-branding/white-labeling.md), email templates, and feature flags that the application explicitly recognizes through the [Administration Console](../administration-and-branding/admin-guide.md).

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
