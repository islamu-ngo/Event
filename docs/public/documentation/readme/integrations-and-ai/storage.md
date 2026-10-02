---
description: Choose local or S3-compatible object storage and preserve metadata-backed authorization and recovery.
---

# Storage Providers Architecture

ISLAMU Event implements a clean storage abstraction supporting both **Local Mounted Filesystem** storage and **S3-Compatible Cloud Object Storage** (such as Hetzner Object Storage, self-hosted MinIO, Cloudflare R2, or AWS S3).

---

## 1. Object Authority & Presigned Security

* **Metadata-Backed Storage**: The primary PostgreSQL database owns the metadata record (UUID, owning tenant, mime-type, byte size, authorization rules); the storage provider stores the raw binary bytes.
* **ID-Based Retrieval**: Files are accessed via stable storage-object IDs (`/api/storageobject/{id}/content`), never via raw filesystem paths or raw S3 bucket URLs submitted by users. Public safe-raster images use `/api/storageobject/{id}/public`; resource files use the separately authenticated `/api/eventresource/{id}/content` route.
* **Presigned Download URLs**: For S3-compatible storage, the API generates short-lived, cryptographically signed presigned download URLs only after verifying caller authorization (see [Authorization Guide](../security-and-identity/authorization.md)).

### Origin is not a download URL

Storage keeps optional foreign-origin provenance separately from captured
managed file ownership. Native uploads and generated CSV files have no invented
origin; an imported thumbnail can retain its foreign origin without serving
bytes from that address. A legacy external reference does not become a managed
file automatically.

Use the returned ID-derived `uri` for display and HAL links for permitted
actions. `uri` can be null when no delivery is allowed. It never contains a
provider address or a foreign-origin locator. Ordinary detail/list and upload
responses omit backend provider identity, bucket, key, binding, version, and
origin. Explicit operator diagnostics remain separately scoped.

Federated managed-image links use the deployment's `PUBLIC_BASE_URL` or the
public address established during authorized setup. Event preserves its path
base when producing absolute image links for external readers. Publication
with managed images fails if that address is missing or invalid; Event does
not substitute a provider address or imported origin.

When applying the pre-release provenance cutover, first classify and clear old
locator values deliberately. A generated rename rejects every nonempty old
value rather than guessing whether it is provenance. Blank old values become
null; reapply only confirmed foreign origins after cutover. Follow the ordered
migration procedure supplied with the deployment, not a URL-shape conversion.

---

## 2. Choosing Your Storage Provider

### Profile images: managed files and external sources

An uploaded profile image is selected by its Event storage-object UUID. Event
checks that it belongs to the current tenant and is an active public safe-raster
image, not private/resource-only content or another Actor's assigned image.
Public delivery uses `/api/storageobject/{id}/public`; clients never need the
provider bucket, object key or filesystem location.

An external profile image is explicitly a foreign HTTP(S) URL. Loading it
contacts that external host; Event does not acquire or delete those bytes.
Even a foreign URL that resembles an Event content route remains external.
Replacing or clearing either kind of profile reference does not delete a
previous uploaded file.
An upload whose retirement has already committed cannot be attached as a new
profile image. The failed attachment leaves both the profile and retained
cleanup work unchanged; select another eligible upload rather than retrying
the retired UUID.

Changing a retired upload's metadata back to active does not make it attachable.
Only the verified upload-finalization flow can activate its settled target.
Shared-use and retention checks include hidden or deleted owner records: hiding
an item, rejecting evidence or releasing an answer file does not detach its
storage reference. These checks do not reveal other owners' private identities.
Cleanup must keep its captured target and retry authority after an uncertain
provider response; requesting cleanup is not confirmation that bytes are absent.

Heavy resource redaction detaches the moderated resource before scheduling its
file cleanup. A retained organization evidence document stays stored even when
the resource is redacted. Pending producer work keeps its original target until
that exact write is acknowledged; age alone cannot authorize deletion.

For API integrations, `PATCH /api/user/{id}` accepts a `profileImage` group with
either `profilePictureId` or `externalProfilePictureUri`. Supply neither value
in a present group to clear; omit the group to preserve the image. Supplying both
values, a relative URL, credentials in a URL, or an empty UUID is rejected.
Use the current concurrency stamp in `If-Match`.
Actor/User responses expose `profilePictureStorageObjectId` or
`externalProfilePictureUri` alongside the display URL. The former User
`profileImageKey` field is removed. Existing organization/group backgrounds
remain managed images rather than introducing another external-image setting.

Apply the matching database upgrade before deploying this contract. Old profile
URLs are not proof of ownership: operators must use verified file identity or
explicitly classify a source as external rather than guess from the URL.
The upgrade stops if any old profile URL remains, including an absolute HTTP(S)
URL. Preserve the reviewed file/source selections before approved development
data recreation, then apply them through the new profile API. An empty-profile
database upgrades directly; no automatic URL classification or data clearing runs.

### Existing files keep their original target

Changing the default local root, S3 bucket or provider affects new reservations,
not existing files. Uploads, generated registration CSVs and imported thumbnails
capture their storage target before writing persistent bytes. Downloads and
cleanup continue using that captured target. Keep old mounts, buckets and retained
credential references available while files or pending cleanup still depend on them.
Local files use application content links; they do not advertise S3 presigned
downloads. S3 downloads retain the saved object version rather than selecting a
newer object at the same key.

Usage includes activated generated CSVs and imported files as well as user
uploads. Replacement and retirement rebuild the affected tenant/provider usage
from retained metadata and upload reservations; cleanup retries do not release
the same charge twice. Hiding or soft-deleting an owner does not itself release
its file charge. A durable cleanup handoff can release the charge while physical
deletion remains pending, so usage totals are not proof of provider absence.

A missing historical binding is an error, not permission to try today's backend.
Before upgrading development data, inventory the original bytes and verify their
target, relative keys and checksums. Use an explicitly reviewed historical mapping
only where that evidence is conclusive; otherwise re-upload from a trusted source.
Recreate a disposable development environment only after approving the data loss.
No automatic target guessing or unbound-row deletion is performed.

Install the matching generated database migrations with the application upgrade.
Do not deploy source changes against the old schema. Preserve pending producer
records with backups: a timeout or absent current object is not proof that an
unacknowledged write can never finish.

For generated registration CSVs, set the connection's workspace to `local` or
`s3_compatible`. Other workspace values are rejected rather than silently choosing
local storage. If disclosure permission expires while a write is in flight, its
captured bytes remain tracked for cleanup and no downloadable artifact is activated.

Configured via `STORAGE_PROVIDER` in [Environment Variables](../configuration-and-operations/environment-variables.md#5-storage-providers-local--cloud-s3):

| Storage Provider | Configuration | Best Fit | Operational Considerations |
|---|---|---|---|
| **`local`** (Default, Compose) | `LOCAL_STORAGE_ROOT_PATH=/app/storage-data/local` | Single-node Docker Compose | Mount a persistent volume for the selected root. |
| **`local`** (Default, Standalone) | `Storage__Local__RootPath` (optional; defaults to `/app/data/storage`) | [Standalone](../self-hosting/docker-standalone.md) | The default lives on the durable `/app/data` volume; persist and back up any override separately. |
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

### Resource Deletion and Provider Recovery

Deleting a resource or applying heavy parent moderation denies new access
before physical cleanup. Ordinary withdrawal remains reversible and keeps its
files. Erasing an uploader removes personal attribution, not shared organizer
materials; unfinished uploads transfer their cleanup responsibility before
their metadata disappears. Independently retained organization evidence is
not deleted by the ordinary resource cleanup path when parent moderation
withdraws the associated resource.

Resource cleanup uses the existing storage reconciliation schedule and its
`Enabled`/`DryRun` controls. The default dry-run does not delete bytes. When
enabled for mutation, committed resource deletions do not depend on the
separate quarantined-file deletion switch or its grace period. Failed cleanup
retains a private retry record; audit expiry and parent removal cannot discard
that responsibility.

Each upload records its original storage target and external secret references.
Changing the current local root, S3 bucket, endpoint or credential binding does
not redirect existing files or pending cleanup. Keep the original namespace and
secret references available. Credentials may rotate at the retained reference,
but removing that reference or changing its selected authority can prevent
reads and cleanup. For local disk relocation, preserve the captured absolute
mount path; changing a setting alone does not move a bound resource.

An interrupted producer can remain pending even if no object is currently
visible. Expiry or cancellation does not prove that an in-flight write cannot
finish later. A late acknowledged write settles its retained cleanup record.
For a lost acknowledgement or unknown version, retain the record and reconcile
the original producer and exact provider version; do not clear it merely because
a timeout elapsed. Missing mounts/buckets and S3 delete markers are not proof
that the required bytes were removed. Restore the original target/reference
before retrying an availability failure.

### External Destination Key Recovery

The API encrypts event-resource destinations with its existing database-backed
Data Protection keyring, scoped to the tenant, resource and protection
version. Keep retired keys while any stored destination ciphertext depends
on them, including withdrawn or archived links. Withdrawal hides a link
without removing its ciphertext; republishing must decrypt it. Retire a
key only after every dependent destination is replaced or removed.
Missing keys fail closed without returning a destination. Backing up the
database together with **unwrapped** key XML does not protect links against
a full database/backup compromise; use the deployment's approved
key-wrapping authority where configured and protect backups accordingly.
BFF cookie-key storage is not an alternative authority for API destination
decryption; Combined hosting retains the API database keyring even when
optional BFF Redis is configured.

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
* Include every captured target, even if it is no longer the default. Bound local
  files must remain reachable at their captured absolute mount path.
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
