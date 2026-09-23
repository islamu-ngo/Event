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

### Private Bucket Requirement

S3-compatible buckets must deny anonymous object access. The optional Compose
MinIO initializer now enforces a private policy for both new and existing sample
buckets. This is a breaking change for deployments that linked directly to sample
bucket objects: those anonymous URLs no longer work.

After upgrading an existing Compose deployment, preserve the `minio_data` volume
and run:

```bash
docker compose --profile storage run --rm minio-init
```

The command keeps existing objects and reapplies the private posture; do not
delete or recreate the bucket. External S3-compatible providers need the
equivalent private bucket policy configured through their own administration
surface.

Public images remain available through the application-managed
`/api/storageobject/{id}/public` URL. The application checks the stored metadata,
lifecycle, image type, and public-image visibility before reading private provider
bytes. Authenticated files likewise use their ID-based application content route,
not a raw bucket URL or object key.

---

## Governed Event Resource Files

Event resource files use their resource's upload and download actions, not
generic storage-object routes or presigned URLs. The uploader has no exception
to current resource access checks. Provider objects remain private even for a
public audience. PDF/DOCX/PPTX inspection does not provide a malware verdict;
the default policy denies unscanned publication and access. See the
[resource upload and download guide](../events-and-ticketing/README.md#uploading-and-downloading-resource-files)
for the separate workflow and instance-only opt-in.

## Organization Evidence PDF Uploads

With local authorization, an organization administrator can reserve an evidence PDF upload for a pending, active organization participation. Only the account that reserved that tenant-local session can finalize its bytes; tenant-administrator status alone does not transfer ownership. Losing the organization role after reservation does not itself revoke the session. Cancellation, expiry, content validation, quota enforcement and privacy-erasure fences remain authoritative. Retrying a completed upload returns the same stored document without another write.

This repair does not broadly grant storage creation or change the selected authorization provider. Ordinary uploads outside the separate OrganizationTenant and event-resource workflows retain their existing Cerbos authorization for images, documents, attachments and system assets; generic local finalization is not a newly granted right. OrganizationTenant reservation and finalization remain denied with instance or tenant-managed Cerbos until the required typed policies are securely supported. Those unsupported checks stay denied during provider outages or configuration-resolution failure, including for instance-administrator owners; unrelated safe-mode exceptions are unchanged. No new storage credentials or database migration are required for the organization-evidence workflow.

Exact content downloads at `/api/storageobject/{id}/content` use the stored object's tenant, creator, visibility and lifecycle, not a caller's ownership claims. An uploader retains PrivateOwner access after losing an organization role, subject to the selected authorization provider and existing privacy/lifecycle checks. Tenant reviewers can review evidence metadata, but that role alone does not grant another account's PrivateOwner bytes. PDFs remain attachments with sanitized filenames; anonymous public-image access and other storage visibility rules are unchanged. Authorization uses an untracked metadata snapshot; the byte reader reloads the object after the policy decision. Quarantine, deletion, creator erasure or visibility restrictions committed while policy evaluation is pending therefore block the read before storage is opened. During tenant-managed Cerbos transport or configuration failure, owner/public-visibility facts do not confer new emergency access: the existing tenant-authority check still applies, and instance-admin status alone is not a tenant-admin grant. No new generic download or outage-only owner right is granted.

## 3. Disaster Recovery & Backup Integrity

Standalone defaults to `/app/data/storage`, so the `/app/data` persistent volume
retains local uploads with the default database and its Data Protection keys.
Explicit root overrides take precedence and need their own persistent mount and
backup when outside that volume. Before replacing an older container that used
the relative `storage-data/local` default, stop writes, preserve and verify its
bytes, and reconcile the copied root before reopening traffic. Follow the
[Standalone relocation procedure](../self-hosting/docker-standalone.md#relocating-uploads-from-an-earlier-default);
changing a root setting never migrates existing files.

Always back up storage bytes concurrently with the primary database snapshot (see [Backup, Restore & Upgrade](../configuration-and-operations/backup-restore-upgrade.md)):
* Restoring a database without the corresponding storage volume causes broken image links.
* Restoring a storage volume without the database leaves orphaned, unreferenced files.
* After restoring the Compose MinIO volume, rerun `minio-init` before reopening traffic so the existing bucket is private.
* [Configuration Manifests](../configuration-and-operations/configuration-manifests.md) deliberately exclude binary media and do not replace storage volume backups.
* Retain required Data Protection keys and the selected secret authority with the
  protected data. Preserve newer privacy-erasure authority independently rather
  than rolling it back with an older primary database.

---

## Related Guides & Next Steps

* **[Environment Variables Reference](../configuration-and-operations/environment-variables.md#5-storage-providers-local--cloud-s3)** — Configure S3 endpoints, credentials, and bucket settings.
* **[Docker Compose Optional Profiles](../self-hosting/docker-compose.md#optional-service-profiles)** — Run local MinIO S3 object storage.
* **[Backup, Restore & Upgrade Guide](../configuration-and-operations/backup-restore-upgrade.md)** — Automated volume snapshot routines.
* **[Secrets Management](../configuration-and-operations/secrets.md)** — Securely bind S3 access keys.
