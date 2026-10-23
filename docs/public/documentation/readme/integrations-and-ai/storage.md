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

### File response containment

API file delivery sends a default-deny Content Security Policy with `sandbox`
and `frame-ancestors 'none'`, plus `X-Content-Type-Options: nosniff`. The combined
host uses the same final policy for its API files. Event-resource downloads keep
their safe attachment filename and final access check before bytes are released;
generic storage retains its existing attachment and range behavior.

These browser headers do not replace upload validation or authorization.
Generic storage does not claim the event-document OOXML inspection policy.
Hosted instances also reject retained Local bindings that overlap their public
web root. Changing the active storage root does not exempt old bindings from
that check; use private service-owned storage roots throughout their lifecycle.

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

`DELETE /api/storageobject/{id}` returns `202 Accepted` with the object UUID
when cleanup custody has committed. It does not synchronously erase provider
bytes. In-use, retention-blocked or invalid captured targets return `409` with
a bounded problem code, without other owners' names. Missing eligible metadata
returns `404`; a fresh request cannot authorize from the retained cleanup record.

Use the metadata response's HAL `delete` link to offer retirement. An `edit`
link alone does not permit deletion. A file can remain editable while another
physical reference or retention hold prevents retirement. The link is a current
hint, not a reservation: a new attachment can make a later request return `409`.
Refresh the metadata rather than infer permission from a role or file name.

After acceptance, the existing cleanup worker retries the captured target.
An uncertain provider response retains durable retry custody. An unfinished
upload remains pending until its exact producer acknowledges completion; a
late acknowledgement cannot recreate erased metadata. Do not change provider
keys or infer completion from elapsed time to clear pending work.

Tenant storage administration includes a bounded file list. It checks the
collection and fresh detail response's retirement links, asks for confirmation,
and announces accepted cleanup as pending. A stale in-use, retention or invalid
target response refreshes current metadata; it does not reveal the identity of
another reference owner. Removing a profile or resource attachment is a separate
action and does not itself prove that provider bytes have been erased.

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

## Private Local Custody and Intake Limits

Use a dedicated, persistent private storage directory. Do not put it inside the
host's public assets directory, use the public assets directory as storage, or
expose it through an ingress/static-file alias. Hosted configuration rejects
overlap with the web root. Review historical captured roots too: changing the
default does not move existing files or pending cleanup.

Keep the root and its ancestors under service/operator control. Existing
symlink/reparse roots, ancestors and object paths are rejected, and inventory
does not descend into linked directories. New Unix storage directories are
owner-only (0700), with owner-only temporary objects (0600); retain equivalent
Windows ACLs and review existing permissions. A filesystem on which an adversary
can concurrently replace these paths is not a supported local-storage profile.
Lexical path validation and link checks do not remove that race.

Generic uploads retain their captured per-file limit from reservation through
inspection, before allocating raster bytes. Safe-raster uploads need matching
MIME, extension and complete container framing. Generic document signature checks
are narrower than the governed event-document inspection and are not malware
scanning.

Generated registration CSVs are limited to 64 KiB of final UTF-8 CSV, including
quoted fields, doubled quotes, separators and newlines. Excess content is rejected
before the CSV is materialized or a provider target is acquired. Existing
registration answer files remain quarantined until their audited release;
native file-answer publication stays disabled by default. A manual release is
not a Clean malware verdict, and an external provider's file reference does not
authorize Event to fetch its bytes.

S3-compatible targets are operator-selected administrative configuration. Use
TLS for external providers; an internal HTTP endpoint requires an explicitly
private network. Keep buckets private and verify anonymous denial independently:
successful authenticated storage diagnostics do not prove private bucket policy.
Preserve retained/shared file uses and the original captured namespace during
cleanup or relocation.

## Governed Event Resource Files

Event resource files use their resource's upload and download actions, not
generic storage-object routes or presigned URLs. The uploader has no exception
to current resource access checks. Provider objects remain private even for a
public audience. PDF/DOCX/PPTX inspection does not provide a malware verdict;
the default policy denies unscanned publication and access. See the
[resource upload and download guide](../events-and-ticketing/README.md#uploading-and-downloading-resource-files)
for the separate workflow and instance-only opt-in.

### Resource Deletion and Provider Recovery

Relative availability windows using the same schedule anchor require the end
offset to follow the start offset. Distinct anchors use the event/session schedule,
so their offsets need not be numerically ordered; open-ended windows remain valid.
Install the matching generated provider migration before relying on the persisted
constraint. Preserve backups and classify any invalid development rows before
schema admission; do not relax the domain rule or silently rewrite their meaning.

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

## Storage configuration authority

Instance configuration applies complete policy or S3 groups; tenant
configuration changes only supplied leaves. The server retains the original
signed administrator and current grants through database settlement. Tenant
delegation and upload ceilings are checked again within the operation, and a
revoked grant, expired credential or newly restrictive policy rolls back the
whole group. Tenant patches preserve existing locks; instance group writes keep
their existing lock-clearing behavior.

Storage authentication accepts a new ticket or principal wrapper containing the
original validated identity. This does not renew the signed credential's deadline.
Replacing the credential, identity, current grants or resolved request tenant
still rejects the operation; the selected database tenant stays fixed.

Validation and writing now use the same retained first configuration snapshot,
including settings and binding winners that were absent when validation began.
A row inserted or replaced between validation and writing cannot silently become
the new baseline. The server uses a dedicated writer, not a later dashboard
read or a generic setting update; cache activation follows acknowledged commit.

SQLite and PostgreSQL paths are authored. PostgreSQL requires its dedicated
migration-installed configuration contract; missing or altered authority is
rejected rather than repaired at runtime. Other database-provider custody
paths remain unsupported and reject editing.

Setup authority is not storage configuration authority. Provider probes and
accounting recalculation have separate custody requirements. The security
programme has authored configuration regression controls, but executable
database-provider assurance remains pending. Instance S3 cache invalidation now
refreshes all published scopes in the current process and prevents stale
in-progress reads from republishing. Cross-server refresh is not added or
certified by this source change.

## Usage recalculation authority

Usage recalculation now retains the original authenticated administrator through
one atomic database repair. Revoked authority or an expired credential/support
session rejects the whole operation, including counters already repaired and
newly inserted counters. Successful totals are returned only after settlement;
the physical-storage charging and deletion rules remain unchanged.

During unfinished setup, the existing accounting branch requires an ordinary
authenticated request plus the accepted setup secret. It is not anonymous or
setup-secret-only access, and does not confer administrator status. A changed
secret, credential or pending setup state rejects the repair. The source
implementation has 34 authored controls; executable provider/native assurance
is still pending. Provider write probes and cache activation use separate
mechanisms below.

## Provider probe support and cache activation

Write probes keep the original signed administrator or genuine setup request,
current credentials and provider binding through each remote handoff. Database
transactions are not held during remote calls. Observation preflight does not
write objects. S3 cleanup deletes only the version acknowledged for this probe;
an ambiguous write or missing version does not authorize cleanup.

The source implementation covers SQLite and PostgreSQL with versioned S3 probes.
PostgreSQL uses its own migration-installed source fence and retains the original
connection through the action. Changed settings, secret bindings or setup state
invalidate the retained probe; runtime cannot repair missing or altered authority.
This fence conservatively covers whole source tables, so even an unrelated
committed settings change can reject an in-progress probe.

Other database source providers, local write probes and unversioned S3 probes
still fail closed. The 45 original and 59 native/signed controls are authored
but unrun; source integration is not production provider acceptance. A grant
revoked before cleanup rejects that handoff, but revocation cannot atomically
cancel an already in-flight remote request.

Instance S3 cache invalidation now removes all published tenant entries in the
current server process. Tenant invalidation leaves other tenants' entries intact.
In-progress resolution cannot republish stale values after invalidation. Four
additional regression cases are authored and unrun. This change adds no
cross-server broadcast and does not certify executable fleet-wide refresh.

## Organization Evidence PDF Uploads

With local authorization, an organization administrator can reserve an evidence PDF upload for a pending, active organization participation. Only the account that reserved that tenant-local session can finalize its bytes; tenant-administrator status alone does not transfer ownership. Losing the organization role after reservation does not itself revoke the session. Cancellation, expiry, content validation, quota enforcement and privacy-erasure fences remain authoritative. Retrying a completed upload returns the same stored document without another write.

This repair does not broadly grant storage creation or change the selected authorization provider. Ordinary uploads outside the separate OrganizationTenant and event-resource workflows retain their existing Cerbos authorization for images, documents, attachments and system assets; generic local finalization is not a newly granted right. OrganizationTenant reservation and finalization remain denied with instance or tenant-managed Cerbos until the required typed policies are securely supported. Those unsupported checks stay denied during provider outages or configuration-resolution failure, including for instance-administrator owners; unrelated safe-mode exceptions are unchanged. No new storage credentials or database migration are required for the organization-evidence workflow.

Exact content downloads at `/api/storageobject/{id}/content` use the stored object's tenant, creator, visibility and lifecycle, not a caller's ownership claims. An uploader retains PrivateOwner access after losing an organization role, subject to the selected authorization provider and existing privacy/lifecycle checks. Tenant reviewers can review evidence metadata, but that role alone does not grant another account's PrivateOwner bytes. PDFs remain attachments with sanitized filenames; anonymous public-image access and other storage visibility rules are unchanged. Authorization uses an untracked metadata snapshot; the byte reader reloads the object after the policy decision. Quarantine, deletion, creator erasure or visibility restrictions committed while policy evaluation is pending therefore block the read before storage is opened. During tenant-managed Cerbos transport or configuration failure, owner/public-visibility facts do not confer new emergency access: the existing tenant-authority check still applies, and instance-admin status alone is not a tenant-admin grant. No new generic download or outage-only owner right is granted.

## 3. Disaster Recovery & Backup Integrity

### Reconciliation Quarantine Recovery

The security programme is replacing ordinary orphan moves with durable
admission and recovery. Deployment still requires the generated provider
migrations, selected Global execution grants and final programme assurance;
the source implementation is not a completed upgrade receipt.

Keep reconciliation in dry-run while checking a restored database against all
captured local mounts. A file is not orphaned merely because its metadata is
soft-deleted or its upload session completed. Retained producer, session and
deletion records still own bytes, including across overlapping historical
local roots. Never delete those records to make a drift report disappear.

Local identity checks follow the application host, not the database server.
On Windows/macOS, conservative case handling can leave ambiguous files
unquarantined, including non-ASCII paths whose aliases cannot be established.
Treat that as retained custody, not permission to force a move. Keep the
application-host identity mode consistent with the installed database
capability; the upgrade's platform assurance is still pending.

Quarantine effects use a private deterministic destination under
`.quarantine/reconciliation`, identified by the durable quarantine operation.
After an interrupted move, recovery checks that same destination instead of
creating another date-based destination. Changed source attributes, conflicting
destination files or simultaneous source and destination files block the move.
Preserve blocked records and both targets for investigation. Do not manually
reuse their object keys or remove their captured binding.

The retained observation preserves exact file timestamp ticks across database
providers. Completed and blocked outcomes record their terminal time separately
from admission; a blocked outcome does not mean bytes were moved. Changed
database role flags, unexpected execution grants or altered custody policies
fail acquisition rather than activating a broader fallback.

Missing-byte metadata quarantine separately rechecks the original database
target after the provider observation. A storage outage is not proof of missing
bytes; restore the original captured mount or provider access before retrying.

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
