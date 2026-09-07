---
description: Choose local or S3-compatible object storage and preserve metadata-backed authorization and recovery.
---

# Storage Providers Architecture

ISLAMU Event implements a clean storage abstraction supporting both **Local Mounted Filesystem** storage and **S3-Compatible Cloud Object Storage** (such as Hetzner Object Storage, self-hosted MinIO, Cloudflare R2, or AWS S3).

---

## 1. Object Authority & Presigned Security

* **Metadata-Backed Storage**: The primary PostgreSQL database owns the metadata record (UUID, owning tenant, mime-type, byte size, authorization rules); the storage provider stores the raw binary bytes.
* **ID-Based Retrieval**: Files are accessed via authorized storage-object IDs (`/api/storage/{id}`), never via raw filesystem paths or raw S3 bucket URLs submitted by users.
* **Presigned Download URLs**: For S3-compatible storage, the API generates short-lived, cryptographically signed presigned download URLs only after verifying caller authorization (see [Authorization Guide](../security-and-identity/authorization.md)).

---

## 2. Choosing Your Storage Provider

Configured via `STORAGE_PROVIDER` in [Environment Variables](../configuration-and-operations/environment-variables.md#5-storage-providers-local--cloud-s3):

| Storage Provider | Configuration | Best Fit | Operational Considerations |
|---|---|---|---|
| **`local`** (Default) | `STORAGE_LOCAL_ROOTPATH=/app/storage-data/local` | Single-node Docker Compose or [Standalone](../self-hosting/docker-standalone.md) | Requires mounting a persistent Docker volume on the host. |
| **`s3`** | `STORAGE_S3_ENDPOINT`, `STORAGE_S3_BUCKET_NAME`, `STORAGE_S3_ACCESS_KEY_ID`, `STORAGE_S3_SECRET_ACCESS_KEY` | Multi-replica clusters and high-traffic event media | Decouples media storage from application compute nodes. |

> [!TIP]
> To evaluate self-hosted S3 locally, launch Docker Compose with the `storage` profile (`docker compose --profile storage up -d`) to start a co-located **MinIO** container (see [Docker Compose Profiles](../self-hosting/docker-compose.md#optional-service-profiles)).

---

## 3. Disaster Recovery & Backup Integrity

Always back up storage bytes concurrently with the primary database snapshot (see [Backup, Restore & Upgrade](../configuration-and-operations/backup-restore-upgrade.md)):
* Restoring a database without the corresponding storage volume causes broken image links.
* Restoring a storage volume without the database leaves orphaned, unreferenced files.
* [Configuration Manifests](../configuration-and-operations/configuration-manifests.md) deliberately exclude binary media and do not replace storage volume backups.

---

## Related Guides & Next Steps

* **[Environment Variables Reference](../configuration-and-operations/environment-variables.md#5-storage-providers-local--cloud-s3)** — Configure S3 endpoints, credentials, and bucket settings.
* **[Docker Compose Optional Profiles](../self-hosting/docker-compose.md#optional-service-profiles)** — Run local MinIO S3 object storage.
* **[Backup, Restore & Upgrade Guide](../configuration-and-operations/backup-restore-upgrade.md)** — Automated volume snapshot routines.
* **[Secrets Management](../configuration-and-operations/secrets.md)** — Securely bind S3 access keys.
