ABOUTME: Documents provider-neutral storage configuration, API boundaries, UI affordances, and operational impact.
ABOUTME: Covers local-first runtime storage, optional S3-compatible mode, reconciliation, and recovery impact.

# Storage

> **Audience:** Operators | Admins | Contributors
> **Status:** Mixed
> **Owner:** Platform/Ops
> **Last Verified:** 2026-07-29
> **Source Anchors:** `Explore.Infrastructure/Storage/`, `Explore.Infrastructure/StorageObjectDeletionService.cs`, `Explore.Infrastructure/Services/ObjectStorageService.cs`, `Explore.API/Controllers/StorageObjectController.cs`, `Explore.API/Controllers/TenantStorageSettingsController.cs`, `Explore.Blazor.Client/Services/ImageStorageService.cs`, `docs/CONFIGURATION.md`, `docs/SECRETS.md`

Storage is moving to a local-first, provider-neutral model. New upload/read flows use metadata-backed `StorageObject` records and the selected `IFileStorageProvider`; S3-compatible storage remains optional for instances that select and configure it. S3-compatible presigned downloads are ID-bound and are not an upload path.

## What Is Implemented

| Area | Implemented Behavior |
|---|---|
| Runtime storage | Provider-neutral upload sessions and metadata-backed reads are implemented for local-first storage; `ObjectStorageService` supports only ID-authorized S3-compatible presigned downloads and server-side reads. |
| Configuration resolution | `S3ConfigResolver` resolves tenant-aware settings from persisted settings first, then `IConfiguration`, with a five-minute cache. |
| Public reads | `StorageObjectController` streams file content by `StorageObject.Id`; public images use the same metadata reader with public-image visibility checks. Caller-supplied object-key read and presign routes are removed. |
| Authenticated writes | Upload-session creation, content finalization, and cancellation require authentication; storage-object updates/deletes require authentication and `storage_object` resource authorization. Successful finalization creates the storage metadata. |
| Admin settings | Instance admins can read/update provider policy, quotas, max-upload ceilings, delegation lock, usage, and redacted optional S3 settings through instance settings endpoints. Provider test and usage recalculation actions are API-backed. Tenant admins read effective tenant policy, usage, lock state, and redacted optional S3 overrides through `GET /api/tenant/settings/storage`, then patch supplied `policy` or `s3` leaves through `PATCH /api/tenant/settings/storage`. Writes are accepted only when instance delegation is unlocked and values stay within instance ceilings and allowed providers. |
| Blazor client boundary | Browser uploads use BFF upload sessions and proxy streaming. The browser never receives or submits a provider destination URL. Public image/content display resolves from `StorageObject.Id` or existing `/api/storageobject/...` API paths, not raw provider object keys. |
| Blazor admin UI | Instance and tenant storage dashboards consume service models mapped from HAL settings resources. The tenant dashboard autosaves isolated `policy` and `s3` patches only when `_links.edit` is present. Action buttons are driven by `_links` and read-only state, not client-side role checks. |
| Local self-hosting | Docker Compose mounts a durable `local_storage_data` volume for local-first storage by default. MinIO remains optional through the `storage` profile for instances that select S3-compatible storage. Its initializer creates or repairs the sample bucket with anonymous access disabled. |
| Reconciliation | API hosts a dry-run-first reconciliation worker that checks metadata/object drift, reports missing backing objects and local orphan files, and performs quarantine/delete mutations only when explicit policy flags are enabled. |
| Moderation image deletion | Heavy event redaction marks referenced event image metadata as `delete_requested` with the owning event resource id, commits the redaction, then deletes provider objects through `IFileStorageProvider`. Failures leave metadata retryable and do not log object keys, filenames, paths, endpoints, buckets, or raw provider errors. |

The API delete endpoint exists, but `Explore.Blazor.Client/Services/ImageStorageService.cs` still returns `false` from `DeleteImageAsync`. Do not document a completed Blazor client delete flow until that helper is implemented.

## Configuration

Local storage is the default provider. Its filesystem root is deployment-managed and bound from `Storage:Local:*`; it is never saved as a tenant/admin-editable setting. Optional S3-compatible storage composes non-secret governance with credentials from the selected authority.

| Purpose | Key Shape | Notes |
|---|---|---|
| Local provider root | `Storage:Local:RootPath` / `Storage__Local__RootPath` | Deployment-managed filesystem directory or mounted volume used by the API process. Standalone defaults to `/app/data/storage` within its durable data volume; Compose defaults to `/app/storage-data/local`; Aspire uses `storage-data/aspire-local` under the repository root. Explicit deployment configuration overrides the Standalone default. |
| Local root creation | `Storage:Local:CreateRootIfMissing` / `Storage__Local__CreateRootIfMissing` | Allows the local provider to create the root directory during startup/health checks when the deployment intentionally grants that permission. |
| Persisted settings | `s3.endpoint`, `s3.public_endpoint`, `s3.bucket_name`, `s3.region`, `s3.force_path_style`, `s3.upload_url_expiration_minutes` | Defined by storage setting definitions and surfaced through admin settings. |
| Secrets | `storage.s3.access_key_id`, `storage.s3.secret_access_key` | External-authority bindings only; see [SECRETS.md](SECRETS.md). |
| Tenant delegation | `governance.lock_tenant_storage` | Controls whether tenant-level storage overrides are locked. |

Environment authority reads canonical `STORAGE_S3_ACCESS_KEY_ID` and
`STORAGE_S3_SECRET_ACCESS_KEY`; Infisical reads the matching `/storage` keys.
Neither path maps credentials into .NET configuration or governance settings.
`Storage__Local__*` keys are deployment/runtime configuration only and must not
contain tenant-controlled paths.

### Private S3 Bucket Posture

The optional Compose MinIO initializer enforces `private` anonymous policy after
creating the sample bucket, including when the bucket already exists. Upgrading
from an earlier Compose file is a breaking deployment change: direct anonymous
object URLs stop working. Reapply the initializer to the existing bucket with
`docker compose --profile storage run --rm minio-init`; do not delete or recreate
the bucket or its volume. Operators using an external S3-compatible provider must
enforce the equivalent private bucket policy themselves.

Private provider posture does not remove application-mediated public images.
Clients continue to use the metadata-backed
`/api/storageobject/{id}/public` route, where the application validates active,
safe-raster, public-image metadata before opening provider bytes. General content
continues through its authenticated ID-bound application route. Provider object
keys and bucket URLs are not public delivery contracts.

### Tenant Settings PATCH Contract

Tenant storage administration has one grouped write route: `PATCH /api/tenant/settings/storage`, operation ID `PatchTenantStorageSettings`, generated method `PatchTenantStorageSettingsAsync`. The former `PUT` operation and `UpdateTenantStorageSettingsAsync` client method are removed. This grouped resource is separate from exact-key setting APIs that still use `PUT`.

The request has two optional groups:

| Group | Presence-Aware Leaves |
|---|---|
| `policy` | `provider`, `maxUploadBytes`, `tenantQuotaBytes`, `routes` |
| `s3` | `endpoint`, `publicEndpoint`, `bucketName`, `accessKeyId`, `secretAccessKey`, `region`, `forcePathStyle`, `uploadUrlExpirationMinutes` |

Each leaf uses explicit presence metadata. An omitted group or leaf preserves the stored value. A present clearable string leaf can intentionally clear that value. Ordinary S3 autosave never sends `accessKeyId` or `secretAccessKey`. Credentials rotate only as one coupled pair through the explicit **Update S3 credentials** action, which requires both browser inputs and the resource's HAL `edit` affordance; reads return only redacted configured flags.

The server first requires tenant-admin or instance-admin authority and rejects the whole patch while instance storage delegation is locked. It validates that the request contains an update, merges supplied leaves into the current effective model for provider, quota, route, endpoint, and ceiling validation, then persists only the supplied leaves in one transaction. After a successful transaction it invalidates both the tenant hierarchical-settings cache and the tenant S3 resolver cache. The HAL `edit` relation is the client authority for showing or enabling storage edits, while the server repeats authorization, lock, and validation checks for direct API calls.

### Reconciliation Settings

`StorageReconciliation:*` is validated on startup and defaults to enabled dry-run mode. Dry-run mode reports drift without mutating metadata or backing objects.

| Key | Default | Description |
|---|---:|---|
| `StorageReconciliation:Enabled` | `true` | Enables the API hosted reconciliation loop and its health posture check. |
| `StorageReconciliation:DryRun` | `true` | Counts/report-only mode. Keep enabled until operators review logs, metrics, and backup posture. |
| `StorageReconciliation:InitialDelaySeconds` | `45` | Delay before the first pass after API startup. |
| `StorageReconciliation:PollingIntervalMinutes` | `360` | Delay between reconciliation passes. |
| `StorageReconciliation:BatchSize` | `500` | Maximum metadata rows or local inventory objects processed per pass. |
| `StorageReconciliation:MissingObjectQuarantineGraceHours` | `24` | Active metadata older than this grace window can be quarantined when its backing object is missing. |
| `StorageReconciliation:OrphanFileQuarantineGraceHours` | `24` | Local files older than this grace window can be quarantined when no metadata row exists. |
| `StorageReconciliation:DeleteGraceHours` | `720` | Quarantined/delete-requested metadata older than this window can be physically deleted and soft-deleted. |
| `StorageReconciliation:QuarantineMissingObjects` | `false` | Allows metadata quarantine for missing backing objects, only when `DryRun=false`. |
| `StorageReconciliation:QuarantineOrphanLocalFiles` | `false` | Allows local orphan files to be moved into provider quarantine, only when `DryRun=false`. |
| `StorageReconciliation:DeleteQuarantinedObjects` | `false` | Allows idempotent provider delete plus metadata soft-delete for eligible rows, only when `DryRun=false`. |

Destructive cleanup requires both `DryRun=false` and the specific mutation flag. This prevents a single configuration typo from turning reporting mode into data deletion.

## Upload And Download Flow

1. Browser callers ask the Blazor BFF for an upload session with filename, content type, and expected byte count.
2. The BFF calls the provider-neutral API upload-session endpoint. The API resolves tenant policy, max upload size, provider, quota, and reservation state.
3. The BFF stores only the API upload-session id, owner, content type, expected size, and expiry in distributed cache, then returns an opaque `uploadSessionId` to the browser.
4. Browser uploads send `uploadSessionId`, `contentType`, and `file` to `/bff/storage/upload-proxy`; the proxy rejects raw destination fields, enforces session owner/content type/exact size, and streams bytes to `/api/storageobject/upload-sessions/{id}/content`.
5. The API finalizer replays and validates the bytes against the reserved MIME type, extension, exact size, and full container signature before writing to the server-selected provider and committing active `StorageObject` metadata.
6. Authenticated callers may update or delete existing metadata through the ID-bound API; there is no caller-authored storage-object creation route.
7. Readers use metadata-backed content endpoints (`/api/storageobject/{id}/content`), public image endpoints (`/api/storageobject/{id}/public`), or the ID-based S3-compatible presigned download endpoint depending on the caller path. Public delivery is limited to active safe-raster image metadata; unsafe or non-image content is forced to attachment download, with `nosniff` and a restrictive sandbox policy on streamed responses.

The internal S3 server-side `GetFileStream` helper translates provider 404 responses into `KeyNotFoundException`, which is useful when diagnosing broken metadata-to-object references; browser callers never supply its object key.

Direct object-key read routes are not part of the local-first contract. The removed `file/{fileKey}` and `presigned-url-by-key/{objectKey}` endpoints bypassed metadata visibility and owner checks; clients must carry a `StorageObject.Id` instead of raw provider keys.

## Blazor Client Boundary

The Blazor client must treat metadata-backed API URLs as the display contract. `StorageObjectUrlResolver` accepts a storage object `Guid`, an existing `/api/storageobject/...` path, or an absolute application URL whose path is already metadata-backed. It rejects provider object keys such as bucket-relative paths because those bypass storage metadata, lifecycle, and visibility decisions.

Browser-first upload UI uses `/bff/storage/upload-session` and `/bff/storage/upload-proxy`. The BFF keeps the API session binding server-side, rejects raw destination fields, and proxies the bytes to the API finalizer; no direct-provider PUT compatibility path remains.

`StorageImage` is the provider-neutral display component for metadata-backed images. It resolves a stable storage-object id or existing API path through `ImageStorageService`, renders explicit loading/error states, and never requires raw S3/provider object keys. `ImageUpload` accepts caller-provided content-type, accepted-format, and max-size policy inputs, validates browser file metadata before opening the stream, and still relies on `IBrowserFile.OpenReadStream(maxFileSize)` through the storage service for the hard read limit.

The instance storage dashboard reads `HalResourceOfInstanceStorageSettingsDto` through `InstanceOnboardingService` and maps it into `InstanceStorageSettingsModel`. The model exposes `CanUpdate`, `CanTestProvider`, and `CanRecalculateUsage` from HAL link presence. The component uses those flags for save/test/recalculate affordances, shows local provider status without exposing filesystem paths, and shows S3-compatible credential fields only when that provider is selected.

The tenant storage dashboard reads `HalResourceOfTenantStorageSettingsDto` through `TenantStorageSettingsAdminService` and maps it into `TenantStorageSettingsModel`. The UI is read-only when the tenant `edit` link is absent, delegation is locked, or the effective policy says the settings are read-only. Tenant overrides stay bounded by the server-provided effective policy and instance ceilings; server validation remains authoritative.

Autosave keeps `policy` and non-credential `s3` changes separate. Discrete choices save immediately; text and numeric inputs wait 400 ms after typing and flush on blur. A successful credential rotation clears both browser inputs and reloads the redacted configured flags; a failed rotation retains both inputs for an explicit retry. The component cancels superseded autosaves and announces saving, success, and failure through a polite `role="status"` live region. This describes the implemented interaction boundary and does not claim browser visual QA.

## API Surface

| Endpoint Group | Authentication Boundary |
|---|---|
| List/read storage objects | Authenticated, tenant-scoped metadata reads are implemented in `StorageObjectController`. |
| File content/public image/presigned download | File content is authenticated and ID-bound; public images are active safe rasters only; presigned downloads are ID-bound, no-store, and attachment-safe. Arbitrary object-key read/presign endpoints are removed. |
| Upload-session lifecycle (create/content/cancel) | Requires authentication; the API selects the provider and destination, and no upload-URL endpoint is exposed. |
| Update/delete storage object metadata | Requires authentication and `storage_object` resource authorization; metadata creation occurs only during successful upload-session finalization. |
| Instance storage settings read/update/test/recalculate | Instance-admin/setup-secret boundaries are handled by instance settings endpoints. Read responses redact S3 secrets and expose configured flags instead. |
| Tenant storage settings read/patch | Requires tenant-admin or instance-admin authority for the current tenant. `GET /api/tenant/settings/storage` returns effective policy, read-only lock state, usage, and redacted optional S3 overrides. `PATCH /api/tenant/settings/storage` changes only supplied `policy` or `s3` leaves and rejects the whole request while delegation is locked or when merged max-upload, quota, route, provider, or S3 validation fails. |

HAL links are the client source of truth for storage UI affordances. Storage object collection/detail responses expose ID-bound read links (`content`, `public-image`, and, on detail resources, `presigned-download`) only for active objects and expose the upload-session and metadata mutation links (`create-upload-session`, `edit`, `delete`) through server-side authorization metadata. Instance storage settings expose `edit`, `provider-test`, and `recalculate-usage` affordances for authorized instance administrators. Tenant storage settings expose `edit` only when instance delegation allows tenant overrides and the effective policy is not read-only. Link presence controls client affordances, but direct API writes still pass server-side authorization, lock, validation, transaction, and cache-invalidation checks.

### Governed resource files

Generic storage routes exclude `event_resource` purpose, ownership or retained
attachment references before metadata projection, counts or provider access.
Uploader ownership does not override this ceiling. Resource sessions use native
resource-update authority and shared finalization; resource downloads use their
own fresh audience decision and never expose presigned URLs. See
[private file intake and delivery](EVENT_RESOURCES.md#private-file-intake-and-delivery)
for inspection binding, version/quota transactions, transport and schema owners.

## Backup And Restore Impact

Object storage is always part of the backup set when users can upload files.

- Back up local object data from the Compose `local_storage_data` volume or the deployment-managed `Storage:Local:RootPath`.
- Standalone stores local bytes at `/app/data/storage` by default alongside its primary database, which includes Data Protection keys. Capture these as coordinated backup units with the selected secret authority; preserve newer privacy-erasure authority independently of any primary rollback. An overridden root needs its own durable mount and backup.
- Back up Aspire development object data from `storage-data/aspire-local` when preserving a local developer environment matters.
- Back up optional S3-compatible object data from `minio_data` when the Compose `storage` profile is enabled, or from the external provider bucket when S3-compatible storage is selected.
- After restoring or adopting an existing sample bucket, rerun `minio-init` to enforce private anonymous policy before reopening application traffic.
- Back up storage secrets and environment configuration with the same release manifest as the database backup.
- Restore object storage before reopening user traffic, then verify representative object metadata resolves to actual objects.
- During rollback, verify the application version still understands the stored `StorageObject` metadata and key layout.

See [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) for the full operational runbook.

### Relocating Earlier Standalone Uploads

The new Standalone default does not move existing bytes. Before replacing an
older container, stop application writes and reconciliation/deletion workers.
Identify its effective local root; an unconfigured older host used
`storage-data/local` relative to its working directory. Preserve that container
until its bytes have been captured. Copy the complete root to the durable
destination without changing relative object keys, compare file counts, sizes
and checksums, and grant the non-root application process access. Do not overwrite
conflicting destination files blindly. Keep the source and coordinated database
backup until recovery has been verified.

Start with the selected root, reconcile metadata against bytes in dry-run mode,
and verify representative authorized downloads before reopening writes or enabling
cleanup mutations. If validation fails, stop the host and repair the copy or
explicitly select the preserved durable root; never delete metadata to hide drift.
The public [Standalone recovery guide](../public/documentation/readme/self-hosting/docker-standalone.md#relocating-uploads-from-an-earlier-default)
owns the operator procedure.

## Reconciliation And Quarantine

The reconciliation worker compares `StorageObject` metadata with provider backing objects:

- active metadata with missing backing objects is reported first, then optionally moved to `quarantined` lifecycle state;
- local provider inventory reports files that are present on disk but absent from metadata, then optionally moves them under the provider quarantine area;
- delete-eligible quarantined or delete-requested metadata can be physically deleted from the selected provider and then soft-deleted in metadata.

The local provider intentionally skips temporary files and existing quarantine files during inventory. Metrics, logs, and health data expose only bounded categories/counts; they do not include object keys, filenames, filesystem paths, tenant IDs, user IDs, endpoints, bucket names, access keys, or secrets.

## Heavy Moderation Image Deletion

Heavy event moderation uses the same provider-neutral delete boundary as normal storage cleanup, but it does not wait for the dry-run-first reconciliation schedule. The event redaction transaction clears event/session/day image foreign keys and marks the affected `StorageObject` rows as `delete_requested` with `OwningResourceKind=event` and the redacted event id. After commit, `StorageObjectDeletionService` loads those rows for the tenant/event, calls the selected `IFileStorageProvider.DeleteAsync`, then soft-deletes the storage metadata when the provider delete succeeds or when metadata has no object key.

If a local or S3-compatible delete fails, the redaction remains committed and public APIs cannot use the image metadata, but the command reports a pending retry failure instead of full success. The rows stay in `delete_requested` so a repeated heavy-redaction command or the reconciliation worker can retry idempotently. Local deletion is idempotent for already-missing files, and S3-compatible deletion issues the provider delete request through the AWS SDK adapter.

Operational evidence for this path must remain bounded. Logs and metrics may include provider name, tenant id, owning resource kind/id, storage object id, outcome, and failure category. They must not include object keys, filenames, filesystem paths, S3 endpoints, bucket names, credentials, raw provider response bodies, or raw exception text.

## Troubleshooting

| Symptom | First Checks |
|---|---|
| Upload session or proxy upload fails | Confirm selected storage provider policy, local provider readiness, and optional S3 settings when the instance selected S3-compatible storage. |
| Reads return missing-object behavior | Check whether `StorageObject` metadata points to a backing object that exists in the selected provider. |
| Local uploads fail or readiness is unhealthy | Verify the API process can create/write to `Storage:Local:RootPath` or the Compose `local_storage_data` volume. Do not expose the host path through admin settings. |
| Reconciliation reports drift but does not mutate | Confirm `StorageReconciliation:DryRun=false` and the specific mutation flag are set; dry-run is the default. Verify backups before enabling destructive flags. |
| Settings update appears ignored | Wait for the resolver cache window or trigger the settings path that invalidates `S3ConfigResolver`; cache invalidation is tenant-scoped when a tenant id is available. |
| Tenant storage settings are read-only | Instance policy has locked tenant storage delegation through `governance.lock_tenant_storage`; an instance administrator must unlock delegation before tenant overrides can be saved. |
| Optional MinIO unavailable | Confirm Docker Compose was started with `--profile storage` and that bucket initialization completed. Local-first storage does not require MinIO. |
| Existing sample-bucket objects remain anonymously readable | Run `docker compose --profile storage run --rm minio-init` with the deployment's configured storage credentials. The idempotent initializer preserves objects and removes anonymous bucket access. |
| Tenant override confusion | Check `governance.lock_tenant_storage` and whether the runtime is single-tenant or multi-tenant. |

The `storage` readiness check and admin connection test both resolve the currently selected `IFileStorageProvider` and return provider-neutral status snapshots. Local mode validates the deployment-managed data root is writable without requiring S3. S3-compatible mode reports unavailable status only when selected and incomplete or unreachable. Readiness payloads expose bounded provider/status/failure-code fields, not filesystem paths, endpoints, bucket names, object keys, access keys, or secrets. Usage recalculation rebuilds used/quarantined object totals from metadata while preserving active reserved-byte counters.

## Metrics

Storage emits provider-neutral OpenTelemetry metrics through the `Explore.Business` meter. The current metric family covers upload-session create/finalize/cancel events, upload bytes, metadata-backed read outcomes and bytes, delete outcomes, quota reservation/release/commit events and bytes, admin provider-test outcomes, and reconciliation run/object outcomes.

Reconciliation counters are `explore.storage.reconciliation_runs` and `explore.storage.reconciliation_objects`. Labels are bounded to mode, provider, category, action, outcome, and failure category.

All storage metric dimensions are bounded to provider, operation, outcome, failure category, and visibility where relevant. They intentionally do not include tenant IDs, user IDs, storage-object IDs, upload-session IDs, object keys, filesystem paths, filenames, endpoints, bucket names, access keys, secrets, raw exception text, or provider response bodies. Use admin APIs and logs for targeted object investigation instead of adding high-cardinality metric labels.

## Related Documentation

- [CONFIGURATION.md](CONFIGURATION.md) - runtime configuration keys.
- [SECRETS.md](SECRETS.md) - secret-provider naming and sensitive value handling.
- [SELF_HOSTING.md](SELF_HOSTING.md) - Compose local storage volume, optional `storage` profile, and MinIO ports.
- [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) - object storage backup and restore runbook.
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - symptom-first operator triage.
