ABOUTME: Operator runbook for backups, restores, upgrades, and rollback decisions.
ABOUTME: Grounds release operations in provider-native database tools, authority isolation, object storage, and migration behavior.

# Backup, Restore, And Upgrade

> **Audience:** Operators
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-08-28
> **Source Anchors:** `docker-compose.yml`, `Event.MigrationService/Worker.cs`, `Explore.API/Program.cs`, `PrivacyErasureStartupGate.cs`, `PrivacyErasureReplayService.cs`, `GlobalLocationPrivacyErasureTests.cs`, `docs/SELF_HOSTING.md`, `docs/CONFIGURATION.md`, `docs/SECRETS.md`

This runbook covers self-hosted deployments using the repository Docker Compose topology. Treat every upgrade as a data operation first and an image rollout second.

## Instance Onboarding State Across Restore

The database is the authority for completed onboarding. Restore a completed
instance and it stays completed; the restored state can't be replayed or
transferred to another identity by editing `INSTANCE_BOOTSTRAP_*` values in the
environment. Raising the generation after completion changes nothing.

Restore a backup taken while onboarding was still pending and the instance
resumes pending, so the configured administrator can sign in and finish.

For post-completion recovery, for example the administrator account is gone,
use normal admin governance inside the product, or restore a backup that
predates the loss. Anything beyond that is a separately approved operator
workstream, not an environment edit.

This is a pre-release breaking change. There is no compatibility reader for
older bootstrap configuration shapes; bring the seven keys to the current
contract before upgrading.

## Payment Restore Inventory And Order

Before restore, enable global stop-sale. Restore as one consistent recovery point: application database tables for payment attempts, paid acceptance snapshots, Checkout dispatch effects, incoming webhook messages/effects, payment reconciliation effects, succeeded observations, secret-binding metadata, and Quartz state; then restore Data Protection keys before exposing BFF sessions or one-time Checkout tickets. Rebind provider and webhook secrets without copying secret values into evidence. Start signed webhook intake and reconciliation first, verify stable due claims and no duplicate provider idempotency identities, then restore support/reads. Clear stop-sale only after reconciliation health is bounded and current acceptance disclosure can be regenerated. Never synthesize acceptance for a historical attempt after restore.

## Ticketing Consistency Manifest And Reopen Order

Before backup, stop sales, pause Quartz, drain bounded active claims, classify
post-handoff work as `Unknown`, and capture one signed/hash-bound manifest per
tenant. The manifest records the release and schema revisions, database
checkpoint, object cutoff, retained key version, authority floor, provider
cursor, durable idempotency floor, worker fence, capability generation, and
credential generation. Store its HMAC outside the database with the matching
`ticketing.recovery_manifest_hmac_key`; never place key material in the
manifest, logs, evidence YAML, or support bundles.

Restore into clean storage in this order:

1. deployment-level maintenance and stop-sale;
2. primary database and durable object cutoff;
3. retained Data Protection and ticketing manifest-signing keys;
4. Quartz tables, inbox/outbox/effect rows, provider cursors, and idempotency facts;
5. manifest validation while runtime stays `RecoveryOnly`;
6. cancellation of pre-restore capabilities and active credential revocation;
7. one durable reissue intent per active admission ticket;
8. authoritative reconciliation of every `Unknown`, poison, and dead-letter effect;
9. reads/support, then workers at the exact new fence, then sales last.

Missing or stale revision, key, cursor, authority, idempotency, or fence facts
keep the deployment closed. Never synthesize them, lower configured floors,
copy a cursor from another tenant, or move authority backward. A replay of the
same tenant/operation/digest returns the existing checkpoint; a changed digest
is a conflict. A restored pre-revocation bearer cannot become valid because
every capability and credential generation must advance before workers reopen.

## Pre-v1 Privacy-erasure Reset Policy

The removed `PrivacyErasure:Durability:Mode` contract has no compatibility
shim or silent translation. Its presence blocks startup. The only supported
transition for this pre-v1 development contract is an operator-managed reset,
and only when all of the following are true:

1. The deployment is explicitly confirmed to be pre-v1 and reset-eligible.
2. The operator accepts rebuilding the application database from the current
   generated provider-specific migrations and the dedicated authority storage.
3. A backup or export exists for every value the operator must retain, and the
   operator has verified that the retained artifacts are readable or restorable.
4. The legacy key is removed and
   `PrivacyErasure:Authority:Topology=EmbeddedSqlite|CoLocated|ExternalDatabase` is selected
   explicitly.

If any prerequisite is false or unknown, stop and preserve the database,
containers, volumes, and backups for a separately reviewed forward-migration
or fact-copy plan. Application code and implementation agents never perform
the reset or delete operator resources. A successful EF migration message is
not reset evidence: verify the target database identity, migration history,
`privacy_erasure_authority` tables, and, for an external authority, the approved
functions and function-only runtime grants before starting the API.

## What Must Be Backed Up

| Asset | Compose Anchor | Why It Matters |
|---|---|---|
| Primary application data | Provider database/schema or primary SQLite volume/file | Tenants, events, users, settings, outbox, and Data Protection keys. Record the configured `Database:Schema`; PostgreSQL/SQL Server tables are clean names inside it, while flat providers use `ie_`. |
| Privacy-erasure authority | `EmbeddedSqlite`: dedicated `/app/data/privacy_erasure_authority.db` volume/file; `CoLocated`: primary application database; `ExternalDatabase`: independently operated structured PostgreSQL target | Typed immutable erasure facts and monotonic counter drive replay. Preserve topology-specific backup independence from application restore operations. |
| Keycloak PostgreSQL data | `keycloak-db` volume `keycloak_data` | Realms, clients, roles, users, and login configuration. |
| Object storage | API volume `local_storage_data`; optional `minio` volume `minio_data` or external S3 bucket when selected | Uploaded files, images, and storage-backed assets. |
| Secrets and environment | `.env`, secret-provider project, Keycloak client secrets | Required to recreate the same runtime identity and storage bindings. |
| Release manifest | image tags, commit SHA, migration state, docs version | Required for rollback and audit. |

Do not treat Docker image tags alone as a backup. Database schema and secret-provider state must match the application version being restored.

## Backup Procedure

1. Announce a maintenance window or switch traffic to read-only behavior if available.
2. Record the current release manifest:
   - application image tags or local build commit;
   - `docker compose config --services` output;
   - current `.env` key names without printing secret values;
   - active `DEPLOYMENT_MODE` and optional profiles (`storage`, `authz`).
3. Back up the primary with its provider-native, consistency-safe mechanism:

   | Provider | Backup / restore mechanism |
   |---|---|
   | PostgreSQL | `pg_dump --format=custom` / `pg_restore --exit-on-error` |
   | SQLite | Stop the single writer and use SQLite online `.backup` (or a volume snapshot that includes WAL/SHM consistently); never copy only the live main file. |
   | SQL Server | `BACKUP DATABASE` / `RESTORE DATABASE` to operator-managed backup storage. |
   | MariaDB | `mariadb-dump --single-transaction` / `mariadb` restore. |
   | MySQL | `mysqldump --single-transaction` / `mysql` restore. |

   Include the EF migration history and Data Protection tables. Verify the
   artifact with the provider's restore/validation command, not only a file
   existence check. For PostgreSQL or SQL Server, record and verify the exact
   configured application schema. A full-database backup must contain it; any
   intentionally schema-scoped PostgreSQL backup must also include all required
   application/Data Protection histories and separately governed operational
   schemas. Back up the separate Keycloak database when self-hosted.

4. Back up the retained erasure authority per topology. For
   `EmbeddedSqlite`, stop its only writer and use SQLite `.backup` against
   `/app/data/privacy_erasure_authority.db`, storing the artifact outside both
   the primary and authority volumes. Preserve restrictive permissions on
   restore. For `CoLocated`, include authority rows in the primary backup because
   authority is in the primary database. For `ExternalDatabase`, use an
   authority backup role and a
   PostgreSQL custom-format dump, never the function-only runtime role.

   Record the authority watermark, application watermark, backup timestamp, dump SHA-256, and restore drill identifier in the release manifest. Never record the DSN or opaque owner/location/intent IDs.

5. Back up object storage:
   - for local-first Compose, copy the API `local_storage_data` named volume or the deployment-managed `Storage:Local:RootPath`;
   - for Aspire/local-dev state that must be preserved, copy `storage-data/aspire-local`;
   - for local MinIO/S3-compatible mode, copy the `minio_data` volume or use an S3-compatible sync tool;
   - for external S3, use provider-native versioning or bucket replication.
6. Export secret-provider configuration or capture the exact secret paths and key names used by the release.
7. Store backups outside the host running Docker Compose. Keep the authority
   artifact independent from primary backup retention and for at least as long
   as any restorable primary backup exists.
8. Verify both restores and the authority-over-application replay in a non-production environment before relying on the backups for an upgrade.

## Restore Procedure

Choose the contract before restoring:

- `EmbeddedSqlite` has `restoreReplayProtection=true` only when its dedicated
  file/volume remains outside the primary restore. Never overwrite the authority
  file with a primary backup or embed it inside a primary SQLite volume
  snapshot.
- `CoLocated` has `restoreReplayProtection=false`; authority rows are restored
  with the same restore contract as the primary database.
- `ExternalDatabase` has `restoreReplayProtection=true` as a capability flag,
  conditional on the authority database being outside the application restore
  operation. If both databases or their shared storage snapshot are restored
  together, the guarantee does not apply.

1. Stop application traffic before restoring:

   ```bash
   docker compose down
   ```

2. Restore and verify the retained erasure-authority database first. Never overwrite it with application backup content and never “repair” it by deleting or renumbering intents.
3. Restore the selected primary provider into a clean volume/database using its
   native restore path. It may legitimately contain no checkpoint or a
   checkpoint behind the authority watermark.
4. Restore Keycloak data before starting Keycloak-dependent application services.
5. Restore object storage before user-facing traffic resumes. For local-first Compose, restore `local_storage_data` or the configured `Storage:Local:RootPath`; for S3-compatible mode, restore `minio_data` or the external bucket.
6. Restore matching structured `Database:*` values and role credentials. For
   PostgreSQL and SQL Server, restore the exact `Database:Schema` and its grants;
   selecting another schema points the application at another namespace and is
   not a restore or automatic data move. For MariaDB/MySQL, restore the intended
   database; for SQLite, restore the intended local file. For
   `EmbeddedSqlite`, restore the authority file with its restrictive
   permissions; for `CoLocated`, restore the application database artifact;
   for `ExternalDatabase`, restore its structured PostgreSQL
   settings and credentials.
7. Start dependencies, run `Event.MigrationService` to completion, then start
   the API. Do not start or expose the BFF until the API replay gate succeeds:

   ```bash
   docker compose up -d postgres redis keycloak-db keycloak
   docker compose run --rm event-migrationservice
   docker compose up -d islamu-event-api
   ```

8. Compare the privacy-erasure authority watermark with the application replay checkpoint. A fresh database must replay from zero; an older database must advance from its latest valid checkpoint. Confirm restored PII canaries are erased, local mirror/checkpoint rows converge once, cache invalidation completed, and PII-free outbox work exists before the first successful `/health` response.
9. Start the BFF only after the API is ready, then check `/alive` and `/health` on both hosts.
10. Let the normal idempotent outbox processor drain `LocationPiiErased` and `LocationPrivacyCorrectionRequested`. Inspect failed/dead-letter rows and verify downstream cache/search/index projections before reopening general traffic. No exact-location search index exists in the current release; do not invent an ad hoc rebuild path.
11. Validate login, tenant resolution, event browsing, and file access.

### Privacy-erasure retention and incident recovery

Authority retention is bounded by `MaximumBackupHorizon +
AuthorityRetentionSafetyMargin`. Before executing maintenance, prove that no
supported primary backup or replica snapshot is older than that declared
horizon and obtain the complete legal-hold set as PII-free authority sequence
numbers. Run `EvaluateRetentionAsync` first and compare aggregate counts and the
projected floor; never put identifiers in the maintenance evidence. The apply
operation deletes only an expired contiguous prefix, pseudonymizes the first
held fact, and advances the durable floor in the same provider transaction.
Direct SQL deletion or counter edits are unsupported.

A backup with an application checkpoint below the retained floor is no longer
replay-safe. Retention therefore defines the oldest restorable primary state,
not merely the authority database size. Keep at least one verified primary
backup at or above the current floor, plus the newest independently protected
authority backup. Application checkpoints remain append-only evidence. General
correction outbox retention follows the outbox runbook, but failed and
dead-lettered corrections remain until operators reconcile them.

## Keycloak Operation Receipt Recovery

`KeycloakOperationReceipts` is part of the primary application database backup.
It stores only allowlisted change intent, the normalized instance/provider/realm
target, actor or setup-generation binding, digest, lifecycle timestamps and
optimistic concurrency state. It never stores administrator credentials,
provider tokens, runtime client secrets or raw Keycloak responses.

Settled receipts (`Verified`, `PartiallyApplied`, `Conflict`,
`FailedBeforeWrite`, `Cancelled`, or `Expired`) become deletion-eligible 30 days
after `settled_at_utc`. `Applying` and `OutcomeUnknown` are unresolved:

- they have no settlement timestamp and are retained indefinitely;
- they block overlapping operations for the same instance, authority and realm;
- expiry or cancellation does not turn a submitted request into permission to
  send it again;
- only read-only provider reconciliation may settle them.

Restore the primary database and Keycloak from a coordinated backup whenever
possible. After a primary-database-only restore, inspect every unresolved
receipt against Keycloak before allowing another operation. A missing receipt
must not be reconstructed from provider state and a restored receipt must not
be replayed automatically.

Normal startup and restore never import the repository sample realm or run a
Keycloak reconciler. Preserve the restored provider volume and immutable realm,
client and mapper IDs. If a fresh installation has no target realm, complete
setup-time inspection and explicitly provision absent resources; do not make a
sample import or provider-volume reset part of recovery.

Downgrading across the receipt migration is a checked stop. The registered
provider migration SQL generators install a temporary enforced constraint
immediately before the generated table drop. Existing or newly inserted
`Applying`/`OutcomeUnknown` rows make downgrade fail before destruction.
Resolve every uncertain receipt through read-only inspection and retry; never
delete receipts to clear the guard.

MySQL and MariaDB commit DDL implicitly. Interruption after guard installation
but before the table drop can leave
`ck_keycloak_receipts_no_unresolved_downgrade` installed. Keep the application
offline, verify there are no unresolved rows, remove only that known temporary
constraint (`DROP CHECK` on MySQL, `DROP CONSTRAINT` on MariaDB), and rerun the
guarded downgrade. Prefer a forward fix for every retained environment.

If startup replay fails:

1. Keep the API and BFF out of service. A failed process, refused socket, and absent readiness response are the expected safe state.
2. Record only the bounded failure type, release identifier, authority/application watermarks, checkpoint count, and correction counts. Do not record connection details or opaque IDs.
3. For authority unavailability, restore connectivity or the independently verified authority backup, then retry startup.
4. For `stale_restore_below_retained_floor`, keep the latest authority unchanged and restore a verified primary backup whose checkpoint is at or above the floor. If none exists, keep the service offline and escalate as unrecoverable restore-policy drift; rolling the authority backward could lose later erasure evidence.
5. For `checkpoint_ahead_of_authority` or `sequence_gap_detected`, stop and restore a verified authority artifact that preserves the checkpointed fact and every later allocated fact. Never bypass the gate, rewrite the counter, delete a local checkpoint, or synthesize an intent from restored application PII.
6. For application replay failure, restore the application backup into a clean database and overlay the unchanged authority again. The per-intent application transaction is atomic, so restart resumes from the last committed checkpoint without duplicate tombstones or outbox rows.
7. After replay succeeds, verify tombstones, cache invalidation, checkpoint equality, correction rows, and dead letters before starting the BFF or admitting traffic.

Prompt injection is not applicable to this recovery path: startup consumes
typed authority facts and configuration, not natural-language or model-provided
instructions.

## Upgrade Procedure

### Pre-v1 rebaseline boundary

The current five application migration chains were regenerated as one initial
per provider during development. A database carrying a removed development
application history has no supported incremental path to these initials.
Before using the target build:

1. Confirm the database is disposable development state. If it contains data
   that must survive, stop and restore/export through a separately reviewed
   transition; do not stamp the migration history.
2. Back up Data Protection and privacy-erasure authority independently.
3. Recreate only the application database selected by
   `Database__Provider`.
4. Run `Event.MigrationService` twice. The first run applies and seeds; the
   second proves idempotency and no pending provider migration work.
5. Verify the effective namespace: configured schema for PostgreSQL/SQL Server,
   `ie_` tables for SQLite/MariaDB/MySQL.
6. Verify Data Protection and retained authority still use their independent
   history and recovery point.

This reset policy is development-only. It must never be presented as a
production in-place upgrade or as permission to discard encryption keys,
erasure authority, backups, or audit evidence.

1. Read `RELEASE_CHECKLIST.md` and the release notes for migration, config, security, and rollback changes.
2. Take and verify backups before pulling or building new images.
3. Pull or build the target images.
4. Start in a non-production environment with production-like secrets and data shape.
5. Run `Event.MigrationService` with migrator credentials and require exit code
   zero. It selects the provider-specific application and Data Protection
   migration assemblies, migrates configured authority storage, enables SQLite
   WAL where applicable, reapplies external-authority role isolation and
   lifecycle functions idempotently, and seeds. Direct `dotnet ef database
   update` is not complete external-authority deployment evidence because it
   does not run that post-migration contract. Run the service a second time in the
   rehearsal environment to prove idempotency. Do not start deployed API
   replicas until it succeeds.
6. Start the upgraded stack:

   ```bash
   docker compose up -d
   ```

7. Verify health:
   - API `/alive` returns healthy;
   - API `/health` is healthy, or degraded only for an optional dependency that is intentionally disabled;
   - Blazor can reach the API through its configured base URL.
8. Watch logs for migration failures, Keycloak discovery failures, secret-provider failures, storage readiness/reconciliation drift, and Cerbos readiness failures.
9. Keep the previous images and backups until smoke tests pass.

### Webhook redesign migration gate

For webhook redesign schema slices, use a reviewed EF Core idempotent SQL script and a
write-maintenance window. Apply the script with `ON_ERROR_STOP`; never allow a failed SQL
statement to be followed by application startup. The release evidence must record:

1. the exact committed migration used as the upgrade starting point;
2. clean-install and committed-baseline-upgrade semantic schema signatures;
3. representative legacy-row counts, classification counts, timing, and lock samples;
4. a custom-format `pg_dump` restored into a clean database;
5. matching migration history, row count, deterministic data checksum, constraints, and indexes;
6. a successful idempotent reapply on the restored database.

Use a 15-minute write window plus a 15-minute restore buffer for the currently measured
10,000-row fixture. Rehearse with production-like volume before release and enlarge the
window from measured evidence. If the projected run exceeds five minutes or lock sampling
shows waiting writers, stop the rollout and split resumable data backfill from blocking DDL.

Before traffic resumes, restore the verified backup on any failed validation. After traffic
resumes, pause writers and deploy a new additive forward migration. Do not use destructive
rollback for evidence-bearing webhook rows and do not guess provider success during repair.

## Rollback Rules

Rollback depends on whether migrations are reversible:

The `AddRetainedAuthorityLifecycle` migration is forward-only once authority
maintenance has run. Do not apply its generated EF `Down` migration to a live
or compacted authority. Roll back by stopping writes, restoring a matched
pre-upgrade primary backup and independently verified authority backup, and
then starting the previous images. If no matched authority backup exists, keep
the service offline; deleting the floor or reconstructing erased facts from
restored PII is not a recovery path.

The external-secret-authority contraction is also forward-only for secret values.
Generated EF `Down` methods can recreate removed legacy columns, but they cannot
reconstruct deleted ciphertext. Never treat that schema shape as value recovery.
Rollback requires the external authority values needed by the previous images and,
when legacy database state is required, a verified pre-upgrade database restore.

| Situation | Rollback Action |
|---|---|
| No schema/data migration ran | Revert images and restart services. |
| Backward-compatible migration ran | Revert images only if release notes say the old version tolerates the new schema. |
| Destructive or non-reversible migration ran | Restore database/object-storage backups and then revert images. |
| Storage reconciliation mutations ran against the wrong object store | Stop write traffic, disable destructive reconciliation flags, restore database and object-storage backups from the same manifest, then restart with dry-run reconciliation. |
| Secret/key rotation changed runtime identity | Restore matching secret-provider values before restarting old images. Only versioned HMAC/key-ring and explicitly options-driven HTTP consumers support overlap; all other credential families require a coordinated restart. A local acknowledgement is not deployment convergence. |
| Removed pre-v1 development application history is present | Keep the service offline; recreate only disposable application state or perform a separately reviewed export/restore transition. Never stamp the new initial as already applied. |

If release notes do not explicitly state that a rollback is image-only safe, assume a database restore is required.

## Verification Checklist

- [ ] Backups exist for the selected primary provider, authority storage, Keycloak DB, object storage, and secrets.
- [ ] The release manifest records the selected primary database and effective namespace: configured schema for PostgreSQL/SQL Server, local file for SQLite, or database name for MariaDB/MySQL.
- [ ] Restore was tested in non-production.
- [ ] The retained erasure authority was backed up and restored independently, and its watermark/hash are recorded without identifiers or connection details.
- [ ] API startup replay advanced the application checkpoint to the authority high-water mark; `authorityRetainedFloor <= checkpoint <= authorityHighWater`, restored PII canaries are absent, and outbox evidence is present once before BFF startup.
- [ ] Retention dry-run used the complete explicit legal-hold sequence set, and every supported primary backup retained after compaction has a checkpoint at or above the projected floor.
- [ ] `EmbeddedSqlite` restore evidence proves the dedicated authority file was not overwritten by the primary restore and writer replica count remained one.
- [ ] `CoLocated` restore evidence proves app + authority rows were restored and replay catches up correctly from the shared checkpoint.
- [ ] `Event.MigrationService` completed twice against the restored provider before API/BFF startup.
- [ ] `docker compose ps` shows required services running.
- [ ] API `/alive` and `/health` return expected status.
- [ ] Blazor loads and can proxy API requests.
- [ ] Initial admin login works through Keycloak.
- [ ] Object uploads/downloads work with local-first storage or the intentionally selected S3-compatible provider.
- [ ] Storage reconciliation is either dry-run/healthy or intentionally disabled/degraded, and destructive flags match the release plan.
- [ ] Cerbos readiness is healthy when `authz` profile/provider is enabled.
- [ ] Release notes and docs impact are complete.
- [ ] Rotation evidence names one attempt and every declared replica; the previous credential remains valid until convergence, and stale/missing replicas are drained or the rollout is failed closed before revocation.

## Related

- [SELF_HOSTING.md](SELF_HOSTING.md) — runtime topology and environment keys.
- [CONFIGURATION.md](CONFIGURATION.md) — canonical configuration sections.
- [SECRETS.md](SECRETS.md) — secret-provider behavior.
- [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) — release documentation contract.
