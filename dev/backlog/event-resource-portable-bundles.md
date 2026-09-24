# Portable event-resource file bundles

- Owner: Storage and domain.
- Activate when: Operators need bulk backup, export or import beyond existing metadata export and coordinated provider backups.

## Acceptance

- Export only rights-authorized files with a bounded manifest; prohibit archive traversal, oversized entries, secret envelopes and attendee records.
- Reauthorize import against the destination tenant and event, create private drafts by default and enforce owner tuples, quotas and cleanup fences.
- Verify interrupted export/import, duplicate replay and a complete restore of database, bytes and encryption keys without granting historical access.
