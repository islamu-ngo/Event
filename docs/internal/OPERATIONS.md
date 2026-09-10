<!-- ABOUTME: Operational runbook for startup, health, shutdown, and runtime safeguards. -->
<!-- ABOUTME: Captures API, BFF, migration, recovery, and service-default behavior. -->

# Operations

## Anonymous Registration Retention

Anonymous names and answers stop being operationally readable at their original
event-purpose deadline, independently of the physical cleanup schedule. The
existing bounded `RegistrationRetentionCleanupJob` remains the deletion owner;
turning Quartz off delays cleanup, not the read-time privacy boundary.

Participant and ticket presentation, answer aggregation, provider export and
registration-owned downloads enforce the same bound. Export workers exclude
expired values before decryption and check again after preparatory waits, before
handoff. Expiry before handoff is terminal and must not be retried as an ambiguous
provider delivery. Data already lawfully transferred cannot be retracted by this
local policy.

Generated CSV metadata retains its included-content deadline. Application reads
resolve current tenant-qualified submission/order lineage and deny expired or
missing authority, including after source answers are deleted. Physical storage
deletion requires separate proof that no legal hold applies; consent and export
audit evidence remain governed independently.

The sibling registration-answer-file GET and release responses apply the same
filename bound, including after HAL authorization waits. Quarantine/release
authority and immutable release evidence remain separate from filename access.

Queued admission and recovery contacts retain their included deadline in
authenticated metadata around the existing encrypted intent material. A source
PII row disappearing cannot remove that deadline. Handlers check before
decryption and after database waits; channels carry the bound through SMTP
configuration, connection and authentication to the first send. Pre-handoff
expiry is `RetentionExpired`, never a receipt-bearing success or uncertain send.
Admission dispatch treats it as unrecoverable; recovery retains its existing
bounded retry/dead-letter lifecycle, with every replay denied before decryption.
Retained ciphertext and audit evidence are not erased by this decision.

Version 1 remains the current unbounded nonanonymous contact format; version 2
binds a finite contact deadline or the selected account-contact identity to its
Data Protection purpose. Account fallback uses the existing current verified
recipient resolver and does not extend anonymous registration-data retention.
Changing, deleting or unverifying that current account contact cannot revive an
earlier queued recipient. Work whose external send already began retains its
existing accepted/uncertain handling.

Apply the complete primary-context catalog, including the generated
`EmailOptionalSelfHostingIntegration` tail, before this behavior. Its retention
delta adds nullable order and storage-content deadlines, with
no inferred historical backfill or Identity changes. Historical guest PII without
an original bound is unavailable; changing the current event schedule cannot
restore it. Prefer a verified forward correction over removing privacy metadata
to reopen access. Existing user-erasure authority and backup obligations remain.

## Instance Bootstrap Lifecycle

Startup runs in one order, in Split and Standalone alike: migrations and
seeding, then the configuration manifest, then serializable preparation, then
HTTP readiness. Nothing serves traffic before the earlier stages settle.

State rules operators need:

- Pending is healthy. A configured instance waits for the administrator sign-in
  and reports ready while it waits.
- The same generation with identical values converges silently. Restarts are
  safe and produce no new binding.
- A strictly higher generation corrects a pending binding before completion.
- The same or lower generation with changed selectors is drift, and startup
  fails closed rather than guessing which side is right.
- Completion is final. No environment change replays or transfers a completed
  onboarding.

Privilege comes only from a real sign-in carrying the exact provider claim.
Configuration selects; it never authenticates. The BFF follows configured
routing for the current state, so a setup-facing route means the instance is
not complete yet.

Status and reason codes are value-free by design. Logs, health endpoints, and
support evidence never carry the configured subject, DID, email, names, or any
fingerprint.

## Instance Administrator Direct-Database Recovery

`eng/tools/EmergencyAdminProvisioner.cs` is the offline break-glass surface for
an existing exact AT Protocol account binding:

```bash
dotnet run --file eng/tools/EmergencyAdminProvisioner.cs -- \
  --grant-did 'did:plc:replace-with-the-exact-linked-did' \
  --apply
```

Add `--reassign` only when the old platform-administrator grants must be
revoked as part of the same serializable transaction. Without it, recovery is
additive.

The file app resolves the selected structured database secret authority,
configures `ExploreDbContext` through `PrimaryDatabaseConfiguration` and
`PrimaryDatabaseProviderComposition`, and refuses a database with pending
migrations. It uses migrator credentials to reach the schema while selecting
the runtime model role so PostgreSQL/SQL Server schema namespaces and flat
SQLite/MySQL names remain correct.

`EmergencyAdminProvisioningOperation` resolves one global
`UserExternalLogin` by exact AT Protocol provider ID and ordinal DID, verifies
the linked user is active, and verifies all canonical role invariants:
`RoleEnum.Admin`, `platform.admin`, and platform scope. It then converges a
single `PlatformUserRole` under serializable isolation. Optional reassignment
removes every other `platform.admin` grant only after the replacement binding
and canonical role have been verified. The unique
`(UserId, RoleId)` index makes retries and concurrent execution idempotent.
`GrantedBy = null` records that no authenticated actor performed the offline
grant.

Run only while application writers are stopped, from source matching the
deployed revision, after a verified backup. Restart all replicas and establish
a fresh sign-in afterward. Stable output is limited to `granted`,
`reassigned`, `already-present`, `target-not-found`, `role-authority-invalid`,
`database-not-current`, `cancelled`, or `failed`. Exit codes are `0`, `64`,
`65`, `70`, and `130`.

The operation never creates an identity, resolves a handle, grants tenant
authority, mutates `InstanceBootstrapState`, applies migrations, contacts a
PDS, prints database coordinates, or emits IDs, DIDs, PII, secrets, arguments,
or exception text.

## Legal-Identity Readiness And Repair

- Runtime API/Standalone startup validates `Instance:OperatorIdentity` and
  stops on incomplete or malformed public operator facts.
- Tenant activation/reactivation evaluates the exact
  `tenant.directory-operator-identity` document for `Activation`.
- Anonymous settings/shell evaluate `PublicDisclosure`; unavailable identity
  returns non-cacheable `503` with `tenant_identity_unavailable`.
- Paid publication and Checkout activation evaluate `PaidCommerce` before any
  provider handoff.

Repair the authoritative source: environment configuration for instance
identity, the tenant identity admin document for directory identity, organizer
payment onboarding for merchant lineage, or Checkout governance for payment
operations. Never repair by editing a generated migration/snapshot, inserting a
branding fallback, or changing historical acceptance evidence. Development
databases built from the prior unapplied Init migrations must be recreated after
the five provider catalogs are regenerated.

> **Audience:** Operators | Contributors | AI agents
> **Status:** Mixed
> **Owner:** Platform/Ops
> **Last Verified:** 2026-08-15
> **Source Anchors:** `Explore.AppHost/AppHost.cs`, `Explore.API/Program.cs`, `Explore.API/HealthChecks/StorageReadinessHealthCheck.cs`, `Explore.API/HealthChecks/StorageReconciliationHealthCheck.cs`, `Explore.API/Scheduling/MaintenanceSweepJobs.cs`, `Explore.Infrastructure/StorageObjectDeletionService.cs`, `Explore.Infrastructure/Services/Registration/PromotionCodeDigestService.cs`, `Explore.Persistence/Repositories/PromotionManagementRepository.cs`, `Explore.Persistence/Repositories/PromotionRedemptionRepository.cs`, `Explore.ServiceDefaults/`, `docker-compose.yml`, `docs/SELF_HOSTING.md`, `docs/BACKUP_RESTORE_UPGRADE.md`, `docs/TROUBLESHOOTING.md`

This page is the operational reference for implemented runtime behavior. Task procedures should live in dedicated runbooks and be linked from here.

## Paid Checkout Stop-Sale And Reconciliation

`Payments:CheckoutGovernance:ActivationStatus` defaults to `suspended`, which blocks new paid claim and provider dispatch across the deployment until the startup-owned operator profile is complete and explicitly set to `approved`. Once deployment activation is approved, instance operators manage durable tenant-wide or event-specific controls through the private/no-store `/api/tenants/{tenantId}/paid-checkout-governance/sale-control` HAL resource. A first activation or stopped control requires a resume request and approval from a different authenticated operator; every transition appends bounded audit facts. Do not disable the webhook endpoint or reconciliation scheduler: stop-sale preserves signed intake and remedy/recovery paths.

Reconciliation claims at most 50 rows in stable `next_attempt_at/created_at/id` order through `ix_payment_reconciliation_effects_worker_poll`; the PostgreSQL path claims one batch in one command, provider reads occur after claims, and each decision settles separately. `explore.payments.checkout_activation` emits only `allowed|blocked` outcomes and closed `reason_category` values. Logs and metrics never contain buyer contacts, acceptance text, account IDs, or provider payloads.

## Operational Runbooks

| Task | Runbook | Use When |
|---|---|---|
| Install or update a self-hosted stack | [SELF_HOSTING.md](SELF_HOSTING.md) | You need Compose topology, ports, setup secret behavior, Keycloak, MinIO, Cerbos, or reverse-proxy boundaries. |
| Back up, restore, upgrade, or roll back | [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) | You are preparing a release, recovering an environment, or testing disaster recovery. |
| Diagnose repeated symptoms | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | You have a concrete failure such as `401`, `429`, `504`, unhealthy readiness, setup-secret errors, or secret-provider failures. |
| Validate release readiness | [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) | A change affects migrations, configuration, secrets, security, upgrade paths, or operator docs. |
| Prepare, attest, tag, or re-verify a governed release | [RELEASE_RUNBOOK.md](RELEASE_RUNBOOK.md) | You are running `prepare`, `verify-candidate`, `verify-tag`, `verify-main`, `verify-baseline`, opening or deleting a maintenance line, or checking an existing release from its tag alone. |
| Prevent or repair Change-Id collisions | [Release Engineering](../../eng/release/README.md#collision-proof-change-workflow) | You are creating a public change, preflighting a feature range, installing commit hooks, or binding an immutable colliding footer to a replacement fragment. |
| Review privacy-erasure workflow | [PRIVACY_ERASURE.md](PRIVACY_ERASURE.md) | You need the current authority-first erasure flow, replay gate, receipt/status behavior, provider-work fences, cleanup, or operator gaps. |

## Admission Check-In Operations (Phase 21)

This runbook covers online, server-authoritative admission check-in. It is not a ticket roster,
credential recovery, or offline admission procedure. See [Admission And Registration](ADMISSION_AND_REGISTRATION.md#6-phase-21-online-check-in-model)
and [ADR-023](adr/ADR-023-admission-credential-check-in-transfer-recovery.md) for the architecture.

### Normal Authority And Data Flow

| Step | Authority and data boundary | Expected evidence |
|---|---|---|
| 1. Discover work | An authenticated staff member obtains the event-specific `check-in-admissions` HAL relation. | The relation is present only for the authorized event scope. |
| 2. Issue scanner authority | Authorized issuance creates one capability for one exact target and action scope. | The issuing response is the sole plaintext disclosure; later reads are masked. |
| 3. Admit online | Staff or dedicated scanner authority submits the opaque admission value for server validation. | One append-only check-in fact and an updated target-state projection, or a generic rejection. |
| 4. Correct | Authorized staff submits an undo against the exact active fact with a closed reason code. | One linked compensating undo fact; the original check-in remains retained. |
| 5. Observe | Summary and health views expose target/status aggregates and bounded operational state. | No roster, credential, attendee, actor, device, or raw scan data is required for the view. |

Staff and scanner operation are separate. Staff uses normal authenticated event authority; scanner
operation uses only the dedicated scanner authority scoped to one exact target. Do not share a
scanner capability between doors or add a target selector to scanner input.

### Implemented Operational Controls

| Relation | Route | Authority | Durable effect |
|---|---|---|---|
| `admission-check-in-health` | `GET /api/events/{eventId}/admission/check-ins/health?targetId={targetId}` | `event_check_in:view` | Returns bounded target state and database dependency availability. |
| `stop-admission-check-in` | `POST /api/events/{eventId}/admission/check-ins/operations/stop` | `event_check_in:manage` | Sets the target to `Stopped`; Domain admission decisions and new scanner issuance fail closed. |
| `restore-admission-check-in` | `POST /api/events/{eventId}/admission/check-ins/operations/restore` | `event_check_in:manage` | Restores the target to `Active` after authority and dependency checks. |
| `reconcile-admission-check-in` | `POST /api/events/{eventId}/admission/check-ins/operations/reconcile` | `event_check_in:manage` | Records the post-incident reconciliation decision without rewriting check-in facts. |

Mutation bodies carry one exact `TargetId` and a closed reason code: `DeviceLoss`,
`ConnectivityOutage`, `OperatorCorrection`, or `PostIncidentReconciliation`. Each mutation updates
state and appends its PII-free `AuditLog` fact in one EF execution-strategy transaction. Repeated
stop or restore calls are safe and remain independently auditable. Dependency failure never enables
offline validation; health reports unavailable and admission remains fail closed.

### Control And Failure Posture

| Condition | Required posture | Do not do |
|---|---|---|
| One scanner device must stop | Revoke that exact scanner capability and issue a replacement only after containment. Staff and other capability paths remain available when their HAL relations are present. | Do not stop the target unless all admission channels for that target must fail closed. |
| All target admission must stop | Use the target stop control. It blocks staff check-in, scanner check-in, and new scanner-capability issuance for that target until restore. | Do not describe target stop as device-only containment or staff continuity. |
| Scanner capability is revoked or expired | Reject immediately and issue a replacement only after the incident is contained. | Do not extend or reuse the old bearer. |
| Check-in input is invalid or wrong-scoped | Return the generic public rejection; inspect only bounded internal fixed reason categories. | Do not disclose whether a credential, event, target, or capability exists. |
| Limiter is saturated | Return RFC 7807 `429` with the available retry metadata and stop submitting until permitted. | Do not bypass the limiter, increase a queue without review, or locally admit attendees. |
| Connectivity is unavailable | Show the bounded outage state and retain no offline validation or submission queue. | Do not switch to offline validation, cache credentials, or use a local admission ledger. |

Stop, restore, and reconcile are server-authorized operational actions. Operators and clients must
use those controls only when their HAL relations are present; stop/restore/reconcile attempts without
the relation are not an alternate control plane.

### Incident Checklists

#### Lost Scanner Device Or Capability Revocation

1. Revoke the exact scanner capability. Use target stop only when containment requires every
   admission channel for that target to fail closed.
2. Preserve the bounded audit and telemetry window; do not copy bearer material into incident notes.
3. Confirm the revoked capability receives the generic rejection. If the target was not stopped,
   confirm authenticated staff and unrelated scanner capabilities remain available where authorized.
4. Issue a new one-target capability only after the replacement operator/device process is complete.
5. Use the HAL-gated restore control only after validating the new scope and observing normal health.

#### Suspected Credential Or Bearer Compromise

1. Stop new issuance for the affected scope when containment requires it.
2. Revoke the affected scanner capability or admission credential through its authorized lifecycle
   action; never delete check-in facts to invalidate authority.
3. Retain the export-safe audit window and bounded fixed reason category for investigation.
4. Reissue only through the normal one-time issuance path. Confirm that later reads stay masked.
5. Reconcile outstanding target state and restore issuance only when the relevant HAL controls are
   present and the generic rejection rate has returned to the expected range.

#### Mistaken Check-In

1. Find the authorized target-state record through the check-in summary; do not use a roster export
   or raw credential as the correction key.
2. Use the HAL-gated undo action and select `OperatorCorrection`, `DuplicateScan`, `WrongTarget`,
   or `ExceptionalReconciliation`; never enter incident prose.
3. Confirm a compensating undo fact was appended and the target-state summary changed.
4. Do not delete, edit, or recreate the original check-in fact.

#### Queue Saturation Or Rate-Limit Rejections

1. Stop repeated submissions and honor `Retry-After` when present.
2. Inspect only aggregate limiter saturation, queue/backlog, latency, and infrastructure health signals.
3. Reduce intake or stop the affected scanner scope through its HAL control when saturation persists.
4. Restore the scope only after backlog drains and the alert clears; reconcile only server-recorded
   items. No offline queue is retained for replay after an outage.

#### Connectivity Outage, Restore, And Reconciliation

1. Declare admission validation unavailable; do not admit from cached QR, manual notes, or local state.
2. Preserve the outage time window and bounded health/telemetry evidence without credentials or PII.
3. Restore the underlying service path and verify the authorized summary/health surface is available.
4. Use the HAL-gated reconcile action for server-recorded ambiguity only. Do not create retrospective
   check-ins from local scanner memory or a paper list.
5. Restore scanner issuance or scope only after reconciliation is complete and normal online validation
   is confirmed.

Emergency exception admission is **not implemented**. A future exception design must be separately
authenticated, reasoned, append-only, and reconciled later; an outage, rate limit, or missing scanner
capability does not authorize an exception today.

### Fixed-Cardinality Telemetry And Alerts

Admission telemetry uses fixed values only. Metric labels must never contain tenant, event, target,
ticket, credential, capability, actor, user, device, raw scan, route instance, or free-form reason.

| Metric | Type | Allowed dimensions | Alert / operator action |
|---|---|---|---|
| `explore.admission.check_in.duration` | Histogram | `action` (`check_in`/`undo`), `authority_kind` (`staff`/`scanner`), `target_type` (`event`/`day`/`session`), `outcome` (closed vocabulary) | Alert when p95 exceeds 250 ms or p99 exceeds 500 ms at the declared 50-concurrent-request load; reduce intake, inspect aggregate dependency health, then restore only after recovery. |
| `explore.admission.check_in.operations` | Counter | Same closed dimensions as duration | Alert on a sustained rejection anomaly against the declared aggregate baseline; investigate bounded outcome and fixed reason-category aggregates, never individual credentials. |
| `explore.admission.check_in.limiter_rejections` | Counter | `policy`, `authority_kind`, `target_type` | Alert when limiter rejection remains sustained; stop or reduce the affected scope and honor retry metadata. |
| `explore.admission.check_in.backlog` | Gauge | `kind` (`transaction`/`audit`), `target_type` (`event`/`day`/`session`/`unknown`) | Alert when transaction or audit work remains above the configured bounded threshold; stop intake if required and reconcile server-recorded work after drain. |
| `explore.admission.check_in.infrastructure` | Gauge or counter | `dependency_kind`, `status` (`healthy`/`degraded`/`unhealthy`) | Alert immediately for an infrastructure outage; keep admission online-only and follow the outage checklist. |

`outcome`, `policy`, `kind`, `target_type`, `dependency_kind`, and every reason category are closed,
source-defined vocabularies. Dashboard filters and alerts must not add identifier labels through
recording rules or log-to-metric extraction.

### Export-Safe Audit, Retention, And Rollback

| Evidence | Safe retained content | Excluded content |
|---|---|---|
| Export-safe admission fact projection | Timestamp, action, target type, authority kind, stable outcome, bounded fixed reason category | QR/bearer/capability plaintext or digest, ticket/attendee/actor/device identifiers, raw scan input, free-form text. |
| Operational summary | Aggregate target/status counts, backlog, saturation, latency, and health state | Roster rows, credential lookup results, per-user/device data. |
| Incident export | Incident window, affected target type, bounded action/outcome/reason categories, stop/restore/reconcile decision | Credentials, PII, exact identifiers, raw logs, screenshots of scanner input, or unbounded exception text. |

Retain admission facts and the bounded incident evidence under the applicable retention policy; do
not remove facts to make a correction or rollback appear clean. Operational rollback is forward-only:
revoke the affected capability for device-only containment or stop the target for all-channel
containment, restore the online path, reconcile
server-recorded ambiguity, then restore through the applicable HAL gate. Evidence must show the
stop, restore, reconciliation decision, and resulting aggregate state. It must not contain secrets
or personal data. Internal admission facts retain the minimum actor or scanner identity required
for authoritative lineage and compensation, under restricted access and retention controls; those
identifiers are never emitted by the export-safe projection.

## Localization Static Bundle Operations

No-TMS and provider-fallback localization bundles are stored under the API
content root:

```text
{ContentRoot}/App_Data/Localization/Bundles/{code}.json
```

The API writes this path through `IBundleFileWriter` using a temp-file then
rename flow. The bundle health endpoint (`GET /api/admin/localization/bundle-health`)
checks whether the directory can be created and written. Admin import/export
endpoints are authenticated and invalidate the translation resolver cache after
successful writes.

For single-instance deployments, the local path is sufficient. For multi-replica
deployments, mount `App_Data/Localization/Bundles` on a shared persistent volume
or replace `IBundleFileWriter` with a distributed implementation. Without shared
storage, one replica can import a bundle while another keeps serving only its
local embedded/writable state.

Back up writable bundles together with other deployment-owned persistent data.
Embedded bundles still provide defaults after restore, but local operator edits
live only in the writable bundle path.

Operational telemetry for this area is exported by the `Explore.Translation`
meter. Watch `islamu.tms.fallback_activated_total` for connected-provider
degradation and `islamu.localization.static_bundle_operation_total` for static
bundle import/export success or validation failures. Metrics use provider,
language, operation, result, and fallback-reason tags only; translation keys,
bundle contents, and TMS secrets must never be emitted as metric tags.

## Read-Only Doctor Diagnostics

`Explore.Diagnostic` includes a read-only doctor CLI for self-hosting and local-environment preflight checks:

```bash
dotnet run --project src/Explore.Diagnostic/Explore.Diagnostic.csproj -- --root .
```

The doctor prints deterministic `PASS`, `WARN`, and `FAIL` results with remediation links. It exits `0` when all checks are `PASS` or `WARN`, and exits `1` when any check is `FAIL`.

Current checks cover:

- .NET SDK version versus `global.json`;
- Docker and Docker Compose availability;
- Aspire CLI availability;
- Compose service topology and BFF `API_ENDPOINT` alignment;
- structured primary/authority database roots and role-separated credentials;
- presence of operator remediation docs;
- review-first AI tool readiness artifacts, generated inventories, registry tests, and agent hardening docs.

Non-negotiable safety boundary: doctor does **not** repair configuration, generate secrets, start containers, start Aspire, run migrations, seed data, call setup write endpoints, or persist setup state. Use it before running Compose/Aspire or when diagnosing a self-hosting setup, then follow the linked remediation docs for corrective action.

Sensitive values are redacted before output. Do not add checks that print raw connection strings, passwords, setup secrets, bearer tokens, cookies, authorization headers, or secret-provider responses.

## Registration Provider Subscription Lifecycle

`RegistrationProviderSubscriptionLifecycleWorker` polls every 30 seconds and delegates all work to `RegistrationProviderSubscriptionLifecycleService`. The service processes provider subscription renewals and response sweeps with two-minute leases, generation fencing, and bounded metrics (`explore.registration_provider_subscriptions.operations`).

Current Google Forms behavior:

- watches are created or renewed through the pinned Google Forms API origin and are renewed two days before their expected seven-day expiry;
- Pub/Sub callbacks are notify-only and enqueue `registration.provider_response_sweep` after Google OIDC audience/email verification;
- newly provisioned subscription state is marked sweep-due immediately, then normal recovery sweeps run six hours after each successful non-continuation sweep;
- sweeps query responses from the stored checkpoint minus a ten-minute overlap, page up to five pages of 100 responses, persist identifiers-only submission effects before checkpoint settlement, and store an opaque `registration-provider-cursor:` when a continuation batch must run immediately;
- renewal and sweep failures have independent persisted counters, back off exponentially up to 60 minutes, and health/queue surfaces expose only bounded status, lag, generation, timestamps, issue codes, failure category, and queue depth.

Do not treat a green lifecycle worker as live Google proof. Operators must still configure OAuth, Pub/Sub topic IAM, push subscription OIDC audience, and service-account email in their Google Cloud/Workspace tenant. See [Google Forms Pub/Sub Integration](integrations/google-forms-pubsub.md).

## Local Startup Topology (Aspire)

`Explore.AppHost/AppHost.cs` selects local infrastructure from `ISLAMU_ASPIRE_MODE`, normally through `Explore.AppHost/Properties/launchSettings.json`; `Hosting:Topology` separately selects the web-process topology:

| Launch profile | Mode | Started by Aspire |
|---|---|---|
| `https` | `FullLocal` | Compatibility alias for the contributor full-local topology. |
| `local-default` | `DefaultLocal` | Default lightweight local platform: PostgreSQL, Redis cache, RabbitMQ, Mailpit, Keycloak, Cerbos, MinIO, migrations, API, and Blazor. Svix is added only for explicit `Svix`/`Composite` webhook provider selection. |
| `local-full` | `FullLocal` | The default-local resources plus Coop, Osprey, PgAdmin, Prometheus, Grafana, and other heavy extras. Svix is added only for explicit `Svix`/`Composite` webhook provider selection. |
| `local-core` | `LocalDataExternalPlatform` | PostgreSQL `postgres` with app database `islamu_event_db`, Redis `cache`, Mailpit, `Event.MigrationService`, `Explore.API`, and `Explore.Blazor`. Auth, policy, storage, webhooks, and moderation providers come from Infisical/config. |
| `local-lite` | `ExternalInfra` | Mailpit, `Event.MigrationService`, `Explore.API`, and `Explore.Blazor`. All infrastructure comes from Infisical/config. |

Contributor default:

```bash
aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj
```

Install the Aspire CLI first if `aspire` is missing:

```bash
curl -sSL https://aspire.dev/install.sh | bash
```

`AspireRunModeExtensions.Parse` defaults a missing `ISLAMU_ASPIRE_MODE` to `DefaultLocal`, so the contributor path does not require a launch-profile name. `aspire run` is interactive and exits when you press `Ctrl+C`.

### Hosting topology selection

`Hosting:Topology` selects the web-process shape independently of
`ISLAMU_ASPIRE_MODE` (which selects local infrastructure). It accepts only
`Split` and `Standalone`; a missing value is `Split`, and any other value
fails AppHost startup. Use the environment-variable form when launching
Aspire:

```bash
Hosting__Topology=Standalone aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj
```

| `Hosting:Topology` | AppHost web resources | Local browser URLs | Callback and API wiring |
|---|---|---|---|
| `Split` (default) | `Explore.API` and `Explore.Blazor` | API `https://localhost:7039`; BFF `https://localhost:7177` | Blazor references and waits for API readiness; Keycloak callbacks target the BFF endpoint. |
| `Standalone` (opt-in) | `Event.Standalone` only | Combined UI and `/api/*`: `https://localhost:7180`; HTTP via `WithHttpEndpoint(name: "http")` (dynamic/non-guaranteed) | The combined host waits for migrations and shared infrastructure directly; Keycloak callbacks target its one browser endpoint and `/api/*` uses the in-process bridge, not YARP. |

The three application composition roots are `Explore.API`, `Explore.Blazor`, and `Event.Standalone`; AppHost orchestrates the selected set. `/api/*` contract behavior remains unchanged between topologies. API routes, HAL boundaries, rate limits, and version parsing remain API-owned and stable; Standalone only swaps transport from out-of-process YARP forwarding to in-process bridge forwarding.

The Combined in-process bridge is still the BFF/API trust boundary: it
sanitizes browser headers and reconstructs the server-held bearer request before
the API pipeline authorizes it.

AppHost publishes dynamic/non-guaranteed internal HTTP via `WithHttpEndpoint(name: "http")`; HTTPS remains `https://localhost:7180`. Direct `Event.Standalone` launch profiles reserve `http://localhost:5180` (and `https://localhost:7180` for the HTTPS profile).

`CONTROL_PLANE_PUBLIC_ORIGIN` remains the public admin-host input in both
topologies. AppHost forwards it to the API/combined host and sets
`Bff__AdminHosts__0` on the selected BFF surface. Set it to the browser-facing
admin origin when testing an explicit admin host; it is not inferred from an
Aspire endpoint.

Foreground isolated run for repeatable infrastructure launch proof:

```bash
aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj --isolated
```

For concurrent worktrees or repeated infrastructure proofs, isolate the Aspire run and discover ports from Aspire resource metadata in another shell while the foreground run is alive. Interactive Keycloak OIDC login is intentionally unsupported in `--isolated` runs: isolation randomizes the BFF port, while the realm uses exact callback URIs. Use one of the named non-isolated profiles for authentication testing.

```bash
aspire ps --format Json
aspire describe explore-api --apphost src/Explore.AppHost/Explore.AppHost.csproj --format Json
aspire describe mailpit --apphost src/Explore.AppHost/Explore.AppHost.csproj --format Json
```

Detached Aspire commands remain useful for CLI lifecycle investigation, but they are not the current canonical launch proof path for this workspace. Official Aspire CLI documentation says `aspire start` starts an AppHost in the background and leaves it inspectable with `aspire ps`, `aspire describe`, `aspire logs`, and `aspire stop`. On 2026-07-04, local Aspire CLI `13.4.6` repeatedly returned detached startup JSON after AppHost readiness, then the AppHost process disappeared and `aspire ps --format Json` returned `[]`. If that reproduces, use the foreground `aspire run --isolated` path above and inspect the detached child log under `~/.aspire/logs/`.

Maintainer modes:

```bash
ISLAMU_ASPIRE_MODE=FullLocal aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj
ISLAMU_ASPIRE_MODE=LocalDataExternalPlatform aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj
ISLAMU_ASPIRE_MODE=ExternalInfra aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj
```

Launch profiles remain available through `dotnet run` for IDEs and compatibility:

```bash
dotnet run --project src/Explore.AppHost/Explore.AppHost.csproj --launch-profile local-full
dotnet run --project src/Explore.AppHost/Explore.AppHost.csproj --launch-profile local-default
dotnet run --project src/Explore.AppHost/Explore.AppHost.csproj --launch-profile local-core
dotnet run --project src/Explore.AppHost/Explore.AppHost.csproj --launch-profile local-lite
```

`local-full` uses persistent container lifetimes and named volumes for heavy stateful resources so local database, Keycloak, MinIO, RabbitMQ, PgAdmin, and observability state survive AppHost restarts. Non-isolated `Split` publishes API HTTPS on `https://localhost:7039` and Blazor HTTPS on `https://localhost:7177`; non-isolated `Standalone` publishes its combined HTTPS endpoint on `https://localhost:7180`. Internal HTTP endpoints remain dynamically allocated for Aspire service discovery. Isolated runs publish dynamic localhost ports, so use `aspire describe <resource> --format Table` instead of hardcoding resource endpoints. Local Keycloak initialization derives exact login, web-origin, and logout values from the selected BFF or combined-host HTTP/HTTPS ports, so OIDC remains usable without wildcard callbacks.

PgAdmin is available as the `pgadmin` browser resource in `local-full`. AppHost injects its local access configuration; inspect the running resource when troubleshooting authentication. It imports the PostgreSQL servers from `Explore.AppHost/Config/pgadmin/servers.json`; inside PgAdmin, use container-network hosts `postgres`, `cerbos-db`, `svix-postgres`, and `coop-postgres` on port `5432`, not Aspire dashboard endpoint strings such as `tcp://localhost:35305`. The Cerbos, Svix, and Coop server entries use `Explore.AppHost/Config/pgadmin/pgpass`; the app `postgres` server may require its Aspire-generated connection details.

To reset only the local app database while keeping the persistent Postgres container/volume, connect to the `postgres` server and drop/recreate `islamu_event_db`. The Aspire resource alias is `islamu-event-db` because resource names cannot contain underscores. Do not delete the `islamu-event-postgres-data` volume unless you also want to rotate the generated Postgres credentials and lose every database in that server.

Secret and connection priority:

- `local-full` uses the explicit Environment authority, clears Infisical bootstrap identifiers, and forwards only canonical environment names. Optional local services require their credentials in `.env`; AppHost does not generate or hard-code them.
- AppHost injects structured `Database__*` fields and only the credential role
  required by each process. Raw application connection strings are not a
  deployment input.
- Mailpit SMTP is local in every Aspire profile. Non-isolated runs use configured development ports; isolated runs use Aspire-assigned dynamic ports. Development seeding may refresh only non-secret `email.*` governance. SMTP credentials remain in the selected authority and have no alias or database fallback.
- Self-hosted local Keycloak may also be configured to use Mailpit or shared SMTP for Keycloak realm email. That is Keycloak realm SMTP plumbing, not product Basic Dispatch configuration: identity lifecycle emails still come from Keycloak and do not create `EmailDispatchOutbox` rows.
- Explicit structured `Database:*` values are authoritative. Infisical loads
  primary database configuration directly from `/database` with `DATABASE_*` keys.
- `local-core` and `local-lite` are maintainer modes and honor the explicitly
  selected authority. Infisical bootstrap credentials come only from process
  environment variables (including `.env` values loaded by AppHost); .NET User
  Secrets are unsupported and cannot supply or override them. Select
  `SECRET_PROVIDER=Environment` for environment-only local debugging.

Keycloak local infrastructure imports the repository realm export from `docker/keycloak/realm-export.json`. Aspire mounts that file into `/opt/keycloak/data/import/realm-export.json` and starts Keycloak with `--import-realm`; Docker Compose mounts the same file and then runs `keycloak-init` to synchronize the confidential Blazor client secret plus managed realm/client security settings. The export contains no client secret. Aspire sets `KC_HTTP_RELATIVE_PATH=/auth`, so its management readiness probe is `/auth/health/ready`. Keycloak skips startup import when the realm already exists in the persistent database; `keycloak-init` repairs the managed policy/client fields, while a disposable database reset is still required for unrelated export-only changes.

Startup dependencies are explicit:

1. Local data profiles create PostgreSQL and Redis first.
2. `local-full` creates platform infrastructure, including CockroachDB before Phase Two Keycloak and Cerbos PostgreSQL before Cerbos.
3. `Event.MigrationService` runs in every profile. Local data profiles inject
   structured PostgreSQL migrator fields; `local-lite` resolves the selected
   provider from structured external configuration.
4. In `Split`, `Explore.API` waits for migration completion, local data/cache, and `local-full` platform resources when those resources exist.
5. In `Split`, `Explore.Blazor` waits for API readiness and receives API service discovery through Aspire.
6. In `Standalone`, `Event.Standalone` waits directly for migration completion and the same selected infrastructure; it owns API startup, workers, health endpoints, shutdown state, and the BFF/UI endpoint once.
7. Dedicated admin hosts use the selected BFF surface and generated API client boundary.

Topology selection changes only local AppHost resource composition; it does not
roll back schemas or data. To return to the supported default, stop the
Standalone AppHost run, relaunch without `Hosting__Topology` (or set
`Hosting__Topology=Split`), then verify the selected `/health` endpoint and
Keycloak callback origin before accepting traffic. Do not run provider or
migration rollback as a topology-switch shortcut.

Current limitation: this is an Aspire development topology. The repository
`docker-compose.yml` remains the Split API + BFF deployment, and there is no
standalone Compose descriptor or packaged one-container runtime yet. SQLite is
not automatically selected by this topology; keep the explicit structured
database provider configuration and the existing SQLite single-writer rules.

The three application composition roots keep one route contract: API calls use `/api/...` and non-URL API versioning (`Accept`, `?api-version=`, or `X-Api-Version`), never `/api/v1/...` (see [the support matrix](ARCHITECTURE.md#hosting-topology)). This applies equally after a topology rollback to the Split default.

The Blazor BFF resolves the API through Aspire service discovery (`services__explore-api__https__0` / `services__explore-api__http__0`) or `ExploreApi:BaseUrl`. Compose uses `API_ENDPOINT`, defaulting to the internal `islamu-event-api:8080` service. Do not hardcode the Compose/API host port into AppHost documentation.

Aspire local development uses the local filesystem storage provider by default. Optional MinIO uses non-secret `storage.*` governance plus credentials supplied through `.env` under Environment authority; AppHost does not define credential defaults. AppHost sets `Storage:Local:RootPath` to `storage-data/aspire-local` and keeps `StorageReconciliation:DryRun=true`. With `WEBHOOKS_PROVIDER=Local`, AppHost omits Svix resources. Explicit `Svix` or `Composite` selection adds the pinned self-hosted resources, while tokens remain sourced from the selected external authority.

Cerbos local infrastructure uses the repository `cerbos/` folder as its source of truth. Aspire and Docker Compose mount `cerbos/config/.cerbos.yaml` into the Cerbos container, mount `cerbos/policies/` read-only for derived roles, policies, and `_schemas`, and initialize the local Cerbos PostgreSQL store from `cerbos/init/cerbos-schema.sql`. The local Cerbos PostgreSQL container uses the Postgres 18 parent data mount (`/var/lib/postgresql`) rather than the legacy direct `data` mount. Do not copy policy files into container images for local development; update the repo-owned `cerbos/` tree and restart or sync the local Cerbos service.

Osprey starts in `local-full` from `ghcr.io/roostorg/osprey/osprey-coordinator:latest` and exposes coordinator ports `19950`/`19951`. Aspire also starts `osprey-kafka`, creates the `osprey.actions_input` topic, and points the coordinator at `osprey-kafka:29092`, because the coordinator's action consumer defaults to Kafka. The API's `Reporting:Osprey` HTTP adapter remains disabled until a compatible HTTP facade endpoint is configured.

Webhook operations:

- `Local` works without Svix and is the default self-hosted outgoing provider.
- Local delivery claims durable PostgreSQL work directly and introduces no webhook-specific Redis, Kafka, CDC, or reverse-proxy dependency. Other platform features may still use resources shown in the selected Aspire profile.
- `Svix` and `Composite` require `webhooks.svix.auth_token` to resolve server-side. In local-full this is seeded from `WEBHOOKS_SVIX_AUTH_TOKEN`.
- `webhook-local-delivery` readiness reports LocalProvider queue backlog and stale sending leases.
- `webhook-svix-provider` readiness rejects unknown or zero-evidence provider tuples before resolving secrets and reports only the safe deployment kind, versioned conformance evidence/count, exact-lookup availability, provider selection, and secret-resolution booleans. It never exposes tokens, secret refs, or provider URLs.
- Self-hosted v1.96.1 does not return the request-hash message tag through list/get, so exact automated reconciliation is disabled for that profile; response-loss ambiguity routes to manual reconciliation.
- Incoming Coop, Osprey, and Svix operational callbacks do not depend on the outgoing provider mode.

See [WEBHOOKS.md](WEBHOOKS.md) and [INTEGRATIONS.md](INTEGRATIONS.md) for provider switching, signatures, and callback rules.

## Location Address Governance Migration Topology

Address governance belongs to the current application initial, not an incremental
legacy backfill. Four application migration assemblies cover five engines, with
MySQL and MariaDB sharing their catalog. Each initial owns that baseline;
the complete application catalog also includes the email-optional integration
tail described below. Neither stage reinterprets older rows or supplies a version-0 search
compatibility path. Promotion never infers provider/manual provenance, creator,
organization, address, or coordinates.

The exact generated heads and lifecycle checks are maintained once in
[Development Application Migration Rebaseline](#development-application-migration-rebaseline).

A deployment applies only its selected provider assembly through `Event.MigrationService`; never apply
multiple provider chains to one database or hand-edit a migration, designer, or snapshot.

### Mandatory development reset

Select an explicitly disposable application catalog, or preserve a matching backup,
before recreating it from the selected regenerated initial. Identify and preserve
independent Identity, Data Protection and privacy-authority histories first; sharing
a server, schema topology or file does not authorize deleting those authorities.
Do not point a rebaselined assembly at an old database or synthesize migration-history
rows. Run `Event.MigrationService` twice against the recreated application target and
require both runs to exit zero; the second run is the idempotency check.

Do not delete an entire shared server volume when only the application
database requires recreation. If an unapplied development migration is wrong,
fix the model or generation source and use EF CLI to remove and regenerate
only the affected provider artifact. Never patch generated output.

### Retained PostgreSQL data

Back up PostgreSQL and rehearse its upgrade against production-shaped retained data. After migration,
record bounded counts of quarantined rows without emitting tenant, location, actor, organization,
address, postcode, coordinate, or derived-key values. Review exact address data only through a
restricted private management path; do not export it to logs, telemetry, CSV, provider requests, or
shared caches.

An operator may promote a row only after truthful scope and provenance review. `UnknownLegacy` remains
unknown after approval. If reliable evidence requires a different source or owner, use the governed
authoritative write/replacement workflow rather than ad-hoc SQL. Never add heuristic backfills,
automatic approval jobs, compatibility readers, inferred creator/organization assignments, or an
address-text rule that widens visibility. Run `Event.MigrationService` a second time before starting API
replicas, then verify quarantined rows remain absent and an explicitly approved row is reusable only
inside its tenant.

## API Startup Behavior

### Configuration-manifest bootstrap runbook

Configuration bootstrap has exactly one topology owner:
`Event.MigrationService` in split deployments and `Event.Standalone` in
combined deployments. The owner completes generated provider migrations and
seeding before it reads `CONFIGURATION_MANIFEST_PATH` when
`CONFIGURATION_MANIFEST_MODE` is `ValidateOnly` or `Bootstrap`. API replicas
never own manifest application. See
[Configuration Manifest](CONFIGURATION_MANIFEST.md) for the full contract,
topology matrix, operator workflow, and contributor guide.

Use this sequence:

1. Validate the file against
   `schemas/configuration-manifest-v1alpha2.schema.json` and mount it read-only
   at `/etc/islamu-event/bootstrap/configuration-manifest.json`.
2. Start the topology owner with `ValidateOnly`. It completes its normal
   migration/seed phase, then performs bounded file, UTF-8 JSON, contract,
   catalog, complete-state, authority, and paid-policy narrowing checks without
   manifest-owned writes.
3. Restart the same topology owner with `Bootstrap`. In split deployments this
   is `Event.MigrationService`; in combined deployments it is
   `Event.Standalone`.
4. Confirm the instance-section digest, operation audit, per-tenant results,
   and durable effect state. Instance state is applied before tenants inside
   one serializable lock-ordered transaction.
5. Disable bootstrap or remove the mounted file after success. A repeated file
   is not desired-state reconciliation: an already-applied instance section
   must match its digest, existing tenant results are wholesale skips, and only
   absent tenants may be added under the unchanged instance section.

On any validation, concurrency, or persistence failure, fix the source and run
`ValidateOnly` again; do not patch audit or result rows. Because this is a
development breaking cutover, databases containing the removed unapplied
tenant-shaped bootstrap model must be reset or recreated before rehearsal
rather than migrated through compatibility aliases. See
[Troubleshooting](TROUBLESHOOTING.md) for digest mismatch, unsafe path, oversize file,
provider availability, and durable-effect recovery codes.

Instance administrators may export an Overrides or Portable view from the
control plane. Export is capped at 4 MiB and intentionally omits secrets,
credentials, topology, PII, payment execution, and application data. Treat it
as bootstrap configuration, not as a database, secret, or disaster-recovery
backup.

### Configuration portability Day 2 runbook

Use the instance or tenant administration workspace for reviewed migration;
do not place Day 2 artifacts in the startup bootstrap path. Upload creates a
private, no-store session with a header-only access token and bounded expiry.
The server returns the selectable section keys, computes the diff from current
target authority, and advertises apply through HAL only when no blocking or
external-setup item remains and required approvals are present.

Apply reacquires the configuration lock hierarchy and recomputes the preview
inside one serializable transaction. The operation receipt is the authority for
fidelity, named omissions, snapshot availability, and post-commit effect state.
For managed automation, create and independently approve a target-scoped apply
schedule, then supply its identifier on the normal apply request. The server
enforces distinct uploader/reviewer/applier actors, the UTC apply window, and
the fresh preview revision in that same transaction; it never stores the
header capability in the schedule.
Use it as follows:

| Receipt/session state | Operator action |
|---|---|
| Expired or stale preview | Upload again or refresh; review the new target revision and approvals |
| Failed apply | No selected section committed. Repair the stable failure condition and create a fresh preview |
| `Pending` / `Processing` effect | Restore outbox workers or dependencies; do not replay the import |
| `DeadLettered` effect | Follow outbox recovery using the operation ID; configuration remains committed |
| Snapshot available | Create the advertised forward-rollback session, preview it against current authority, then apply |
| Snapshot unavailable | Use a retained artifact and current-state review, or restore all authorities from one consistent backup point |

Rollback snapshots have bounded application retention and are not backups.
Retain source artifacts, receipts, and legal review evidence under the
deployment's protected records policy. Tenant packages never carry target
authority, secrets, users, events, orders, payments, provider state, or backup
data. Configure omitted environment and provider requirements on the target
before apply.

Quartz registers `configuration-portability-retention-cleanup` as an hourly,
non-overlapping bounded sweep. It expires open import sessions and removes
expired encrypted upload, snapshot, and transfer-chunk payloads; digest-only
session and append-only receipt evidence remain available for support.

Direct transfer is opt-in staging only. The destination must be a public HTTPS
origin on port 443; distinct source and destination actors approve a
nonce/proof/artifact-bound, expiring session. Resume only from the reported
offset. Completion is replay-safe, promotion creates an ordinary import
session, and source deletion is never part of the protocol.

In deployed environments, `Event.MigrationService` owns the primary application
and Data Protection schemas. It binds `Database:Migrator`, selects the closed
provider switch, applies pending migrations, enables SQLite WAL when selected,
applies PostgreSQL-only model constraints when selected, migrates configured
privacy-erasure authority storage, runs idempotent seeding, and exits. A nonzero
exit blocks API rollout. Run it twice in deployment rehearsal to prove there is
no pending work on the second pass.

The API binds `Database:Runtime`. Development retains application migration and
seed convenience; production/staging do not. The API owns only the Quartz
scheduler schema, which is applied as idempotent DDL rather than an EF Core
migration and works on every supported primary provider, including SQLite.

SQLite transaction-owned named locks retain their owning connection separately
from the transaction object. Commit, rollback, closed/disposed-connection cleanup,
and reaping completed owners before connection reuse release the process semaphore.
This prevents implicit rollback from stranding later payment or configuration work
without releasing a lock while its owning transaction is still active. SQLite
remains a single-instance deployment; server-provider locking is unchanged.

| Provider | Application migrations | Data Protection migrations | Namespace/history |
|---|---|---|---|
| PostgreSQL | `Explore.Persistence` | `Explore.Persistence` | Configured schema (default `islamu_event`) with separate histories |
| SQLite | `Explore.Persistence.Migrations.Sqlite` | `Explore.Persistence.DataProtection.Migrations.Sqlite` | Fixed `ie_` table prefix and prefixed histories |
| SQL Server | `Explore.Persistence.Migrations.SqlServer` | `Explore.Persistence.DataProtection.Migrations.SqlServer` | Configured schema (default `islamu_event`) with separate histories |
| MariaDB | `Explore.Persistence.Migrations.MySql` (shared with MySQL) | `Explore.Persistence.DataProtection.Migrations.MySql` (shared with MySQL) | Fixed `ie_` table prefix and prefixed histories |
| MySQL | `Explore.Persistence.Migrations.MySql` | `Explore.Persistence.DataProtection.Migrations.MySql` | Fixed `ie_` table prefix and prefixed histories |

`EmbeddedSqlite` authority uses its dedicated local file and authority schema
owner. `ExternalDatabase` uses the authority context's PostgreSQL migrations
and `__EFPrivacyErasureAuthorityMigrationsHistory`. Neither authority topology
shares the primary database.

Development catalog reseeding is provider-aware. Relational providers use
set-based cleanup such as `ExecuteDeleteAsync` and bounded SQL where needed;
non-relational test providers materialize and remove tracked rows because EF
Core's in-memory provider cannot translate relational set-based delete
operations. Do not copy the in-memory fallback into production cleanup jobs.

When creating EF Core migrations, target the provider's owning migration
project. Generate Data Protection and application migrations separately.

Migration files and model snapshots are generated artifacts. Never patch them manually. If generated output is incorrect, fix the entity/configuration, `DbContext`, lookup seeding, or migration-generation extension; remove the unapplied development migration and run `dotnet ef migrations add` again. Applied or merged migrations remain immutable and require a newly generated corrective migration.

PostgreSQL remains in `Explore.Persistence`; SQLite, SQL Server, and MySQL use
the dedicated projects listed above. MariaDB stays a distinct runtime provider
with its own server-flavor and version dialect, but it owns no migration
project: it resolves the MySQL application and Data Protection migration
assemblies, so there is no MariaDB-specific migration project or migration
command. Use the matching design-time factory, remove only an
unapplied development migration with `dotnet ef migrations remove`, then
regenerate with `dotnet ef migrations add`. Never patch generated migration,
designer, or snapshot files.

### Full development migration reset and regeneration

Run these commands from the repository root. They use the current `src/`
project paths and cover every generated catalog:

- four `ExploreDbContext` application catalogs (PostgreSQL, SQLite, SQL Server,
  and the MySQL catalog that MariaDB also uses);
- four `DataProtectionKeyContext` catalogs on the same provider set;
- external PostgreSQL, co-located PostgreSQL, and embedded SQLite
  privacy-erasure authority catalogs.

This procedure is development-only. Confirm the corresponding databases have
been deleted or are disposable before removing migration history. Do not use
it after a migration has shipped or been applied to retained data.

The EF CLI process must receive the same secret-backed configuration as the
migrator role. Starting the `local-core` Aspire profile does **not** inject its
child-process environment into a separate terminal. Launch the shell or each
command through the repository's Infisical-backed environment first. For the
shared local User Secrets authority, populate the complete structured database
contract in `src/Explore.Secrets`, then export
`SECRET_PROVIDER=UserSecrets` and `DOTNET_ENVIRONMENT=Development` in the EF shell.
User Secrets is rejected when that environment is absent or Production, and store
changes require restarting the EF command. The commands below deliberately override
only provider-selection and non-secret design-time fields; they never embed
usernames, passwords, hosts, or connection strings. Use the Environment/Infisical
workflow below for multi-provider regeneration when per-command provider overrides
are required.

Define the project paths once:

```bash
PERSISTENCE=src/Explore.Persistence/Explore.Persistence.csproj

APP_SQLITE=src/Explore.Persistence.Migrations.Sqlite/Explore.Persistence.Migrations.Sqlite.csproj
APP_SQLSERVER=src/Explore.Persistence.Migrations.SqlServer/Explore.Persistence.Migrations.SqlServer.csproj
APP_MYSQL=src/Explore.Persistence.Migrations.MySql/Explore.Persistence.Migrations.MySql.csproj

DP_SQLITE=src/Explore.Persistence.DataProtection.Migrations.Sqlite/Explore.Persistence.DataProtection.Migrations.Sqlite.csproj
DP_SQLSERVER=src/Explore.Persistence.DataProtection.Migrations.SqlServer/Explore.Persistence.DataProtection.Migrations.SqlServer.csproj
DP_MYSQL=src/Explore.Persistence.DataProtection.Migrations.MySql/Explore.Persistence.DataProtection.Migrations.MySql.csproj

AUTHORITY_SQLITE=src/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite.csproj

# SQLite rejects server-only fields. This array masks any PostgreSQL values
# inherited from the Infisical-backed shell while preserving other variables.
SQLITE_DESIGN_TIME_ENV=(
  "Database__Provider=Sqlite"
  "Database__Migrator__Database=$PWD/.artifacts/islamu-event-migrations.db"
  "Database__Host="
  "Database__Port="
  "Database__Username="
  "Database__Password="
  "Database__TlsMode=Prefer"
  "Database__TrustServerCertificate=false"
  "Database__ServerFlavor="
  "Database__ServerVersion="
  "Database__Migrator__Host="
  "Database__Migrator__Port="
  "Database__Migrator__Username="
  "Database__Migrator__Password="
  "Database__Migrator__TlsMode=Prefer"
  "Database__Migrator__TrustServerCertificate=false"
  "Database__Migrator__ServerFlavor="
  "Database__Migrator__ServerVersion="
  "DATABASE_PROVIDER=Sqlite"
  "DATABASE_NAME=$PWD/.artifacts/islamu-event-migrations.db"
  "DATABASE_HOST="
  "DATABASE_PORT="
  "DATABASE_USERNAME="
  "DATABASE_PASSWORD="
  "DATABASE_TLS_MODE=Prefer"
  "DATABASE_TRUST_SERVER_CERTIFICATE=false"
  "DATABASE_SERVER_VERSION="
  "DATABASE_MIGRATOR_HOST="
  "DATABASE_MIGRATOR_PORT="
  "DATABASE_MIGRATOR_USERNAME="
  "DATABASE_MIGRATOR_PASSWORD="
  "DATABASE_MIGRATOR_TLSMODE=Prefer"
  "DATABASE_MIGRATOR_TRUSTSERVERCERTIFICATE=false"
  "DATABASE_MIGRATOR_SERVERFLAVOR="
  "DATABASE_MIGRATOR_SERVERVERSION="
)
```

#### Remove the current generated histories

`dotnet ef migrations remove --force` can roll back an applied migration and
therefore is not an artifact-only deletion command. Use plain `migrations remove`
against a verified empty disposable target for an unapplied initial. The clean development baseline has one `Init` migration
per catalog, so run each command once. A dedicated provider project must be its
own startup project while its existing snapshot is removed; otherwise EF can
load the context but fail to discover that provider's snapshot. Stop
immediately on an unexpected error; do not hand-delete a designer or snapshot
to work around it.

Application catalogs:

```bash
env Database__Provider=PostgreSql \
  dotnet ef migrations remove \
  --context ExploreDbContext \
  --project "$PERSISTENCE" \
  --startup-project "$PERSISTENCE"

env "${SQLITE_DESIGN_TIME_ENV[@]}" \
  dotnet ef migrations remove \
  --context ExploreDbContext \
  --project "$APP_SQLITE" \
  --startup-project "$APP_SQLITE"

env Database__Provider=SqlServer Database__Port=1433 \
  dotnet ef migrations remove \
  --context ExploreDbContext \
  --project "$APP_SQLSERVER" \
  --startup-project "$APP_SQLSERVER"

env Database__Provider=MySql Database__Port=3306 \
  Database__ServerFlavor=MySql Database__ServerVersion=8.4 \
  dotnet ef migrations remove \
  --context ExploreDbContext \
  --project "$APP_MYSQL" \
  --startup-project "$APP_MYSQL"
```

Data Protection catalogs:

```bash
env Database__Provider=PostgreSql \
  dotnet ef migrations remove --force \
  --context DataProtectionKeyContext \
  --project "$PERSISTENCE" \
  --startup-project "$PERSISTENCE"

env "${SQLITE_DESIGN_TIME_ENV[@]}" \
  dotnet ef migrations remove --force \
  --context DataProtectionKeyContext \
  --project "$DP_SQLITE" \
  --startup-project "$DP_SQLITE"

env Database__Provider=SqlServer Database__Port=1433 \
  dotnet ef migrations remove --force \
  --context DataProtectionKeyContext \
  --project "$DP_SQLSERVER" \
  --startup-project "$DP_SQLSERVER"

env Database__Provider=MySql Database__Port=3306 \
  Database__ServerFlavor=MySql Database__ServerVersion=8.4 \
  dotnet ef migrations remove --force \
  --context DataProtectionKeyContext \
  --project "$DP_MYSQL" \
  --startup-project "$DP_MYSQL"
```

Retained privacy-erasure authority catalogs:

```bash
dotnet ef migrations remove --force \
  --context PrivacyErasureAuthorityDbContext \
  --project "$PERSISTENCE" \
  --startup-project "$PERSISTENCE"

env Database__Provider=PostgreSql \
  dotnet ef migrations remove --force \
  --context CoLocatedPrivacyErasureAuthorityDbContext \
  --project "$PERSISTENCE" \
  --startup-project "$PERSISTENCE"

env PrivacyErasureAuthorityEmbedded__Path="$PWD/.artifacts/privacy-erasure-authority-migrations.db" \
  dotnet ef migrations remove --force \
  --context EmbeddedPrivacyErasureAuthorityDbContext \
  --project "$AUTHORITY_SQLITE" \
  --startup-project "$AUTHORITY_SQLITE"
```

#### Generate one clean initial migration per catalog

After every snapshot has been removed, clean the design-time output and stage
each now-empty dedicated migrations assembly beside the `Explore.Persistence`
startup assembly. This prevents a stale pre-reset snapshot DLL from producing
an empty or differential migration. The add commands use `--no-build` so every
catalog compares its current model against the deliberately empty staged
assembly:

```bash
dotnet clean "$PERSISTENCE" --configuration Debug --verbosity quiet

MIGRATION_PROJECTS=(
  "$APP_SQLITE"
  "$APP_SQLSERVER"
  "$APP_MYSQL"
  "$DP_SQLITE"
  "$DP_SQLSERVER"
  "$DP_MYSQL"
  "$AUTHORITY_SQLITE"
)

for project in "${MIGRATION_PROJECTS[@]}"; do
  dotnet clean "$project" --configuration Debug --verbosity quiet
  dotnet build "$project" \
    --configuration Debug \
    --verbosity quiet \
    --output src/Explore.Persistence/bin/Debug/net10.0
done
```

Application catalogs:

```bash
env Database__Provider=PostgreSql \
  dotnet ef migrations add Init \
  --context ExploreDbContext \
  --project "$PERSISTENCE" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations \
  --no-build

env "${SQLITE_DESIGN_TIME_ENV[@]}" \
  dotnet ef migrations add Init \
  --context ExploreDbContext \
  --project "$APP_SQLITE" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations \
  --no-build

env Database__Provider=SqlServer Database__Port=1433 \
  dotnet ef migrations add Init \
  --context ExploreDbContext \
  --project "$APP_SQLSERVER" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations \
  --no-build

env Database__Provider=MySql Database__Port=3306 \
  Database__ServerFlavor=MySql Database__ServerVersion=8.4 \
  dotnet ef migrations add Init \
  --context ExploreDbContext \
  --project "$APP_MYSQL" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations \
  --no-build
```

Data Protection catalogs:

```bash
env Database__Provider=PostgreSql \
  dotnet ef migrations add Init \
  --context DataProtectionKeyContext \
  --project "$PERSISTENCE" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations/DataProtection \
  --no-build

env "${SQLITE_DESIGN_TIME_ENV[@]}" \
  dotnet ef migrations add Init \
  --context DataProtectionKeyContext \
  --project "$DP_SQLITE" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations \
  --no-build

env Database__Provider=SqlServer Database__Port=1433 \
  dotnet ef migrations add Init \
  --context DataProtectionKeyContext \
  --project "$DP_SQLSERVER" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations \
  --no-build

env Database__Provider=MySql Database__Port=3306 \
  Database__ServerFlavor=MySql Database__ServerVersion=8.4 \
  dotnet ef migrations add Init \
  --context DataProtectionKeyContext \
  --project "$DP_MYSQL" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations \
  --no-build
```

Retained privacy-erasure authority catalogs:

```bash
dotnet ef migrations add Init \
  --context PrivacyErasureAuthorityDbContext \
  --project "$PERSISTENCE" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations/PrivacyErasureAuthority \
  --no-build

env Database__Provider=PostgreSql \
  dotnet ef migrations add Init \
  --context CoLocatedPrivacyErasureAuthorityDbContext \
  --project "$PERSISTENCE" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations/CoLocatedPrivacyErasureAuthority \
  --no-build

env PrivacyErasureAuthorityEmbedded__Path="$PWD/.artifacts/privacy-erasure-authority-migrations.db" \
  dotnet ef migrations add Init \
  --context EmbeddedPrivacyErasureAuthorityDbContext \
  --project "$AUTHORITY_SQLITE" \
  --startup-project "$PERSISTENCE" \
  --output-dir Migrations \
  --no-build
```

After generation, inspect every generated `Up`, `Down`, designer, and snapshot
without editing them. Then run the migration ownership/model tests, apply each
catalog to an empty database, roll it back to `0`, reapply it, and require no
pending model changes. Run the selected provider's `Event.MigrationService`
twice against the recreated database; the second successful run proves
idempotency.

ASP.NET Core Data Protection protects BFF authentication/setup cookies,
antiforgery state and other payloads, but its persistence depends on the host.
`AddApiHostServices` uses `DataProtectionKeyContext` in the primary database;
the migration owner applies that context's schema. Combined Standalone retains
this registration when no Redis cache connection is configured. Its keys live
in the primary SQLite database, not `/app/data/dataprotection-keys/`.

`AddBffDataProtection` uses application name `islamu-event`. With
`ConnectionStrings:cache`, it selects Redis key
`islamu-event:data-protection-keys`; shipped Split Compose supplies this connection
to the separate UI and persists Redis in `redis_data`. Without Redis the extension
leaves the existing key-store registration intact. A separate UI host does not
automatically inherit the API's database registration.

`DataProtectionKeyStoreHealthCheck` reports `store=redis` when a multiplexer is
available, probes Redis and reports `keyRingPresent`. A reachable Redis with no
keyring still returns Healthy; this check does not prove recovery of prior keys.
Without Redis it reports `store=local` and performs a protect/unprotect roundtrip
through the active provider. That label does not establish filesystem storage or
restart durability, and the check does not query `DataProtectionKeyContext`.
Probe exceptions are Unhealthy with bounded failure type, not key material.

Preserve the actual database/Redis key stores, selected signing-secret authority,
Identity state and media in coordinated backups. Keep retained erasure authority
outside primary rollback. Lost keys can invalidate protected payloads, while
preserved keys alone do not guarantee session validity, crash recovery or a
successful restore. Diagnose unexpected post-restart cookie invalidation against
the selected store and current identity authority before browser storage. See
[SELF_HOSTING.md](SELF_HOSTING.md#persistent-keys-and-backup-boundaries) for the
composition contract and the [public backup runbook](../public/documentation/readme/configuration-and-operations/backup-restore-upgrade.md)
for operator procedures.

Event/session lifecycle migration notes:

- `20260623101543_AddEventSessionStatusAndNullableSchedule` adds the `event_session_statuses` lookup, backfills existing `event_sessions` to `DRAFT`, and makes session schedule/local projection columns nullable so draft sessions do not need fake times.
- The room-overlap GiST exclusion constraint intentionally ignores rows where `start_time` or `end_time` is null. Do not "fix" that predicate back to all rows; unscheduled drafts must not create unbounded overlap ranges.
- Rollback is development-only and not data-preserving for unscheduled draft rows. Before downgrading past this migration, either delete/resolve unscheduled draft sessions or schedule them through the normal command path so non-null schedule columns can be restored safely.
- Public outputs must continue to use the published/scheduled session gate. Operator backfills or imports may create structurally valid draft rows, but publication and session publish readiness remain Application-layer checks.

### Privacy-erasure startup replay

`Explore.API` resolves `IPrivacyErasureReplayService` after application migrations and seeding but before `app.Run()` in both authority topologies. The gate reads the newest immutable application checkpoint, verifies it against the retained authority, and reapplies every intent not covered by the current compiled policy version. A fresh application database starts at sequence zero. Sequence gaps, checkpoint mismatch, authority unavailability, or replay failure block startup.

Startup is closed when the authority is unavailable, the local checkpoint cannot be matched to the retained authority sequence, the next sequence is missing, replay is cancelled, the application transaction fails, or synchronous cache invalidation fails. On every restart with an existing checkpoint, replay verifies that checkpoint against the authority and re-invalidates the cached privacy-erasure surface before reading later facts. This makes a post-commit cache outage retryable instead of letting a durable checkpoint hide stale distributed-cache state. The API never reaches host start in those cases, so health, outbox processing, and worker services remain unavailable until replay succeeds. This ordering relies on the Generic Host boundary: `Build()` creates the host, while `Run`/`Start` starts its hosted services, including the HTTP server.

Required authority configuration is secret. Never log the connection details, retained opaque identifiers, or provider exception text. The API emits only the bounded exception type when the gate fails.

Operator evidence uses two read-only database sessions because the authority is deliberately outside the application restore set. Compare the retained authority sequence, the local checkpoint sequence, and the presence of correction/outbox convergence evidence before declaring the gate complete. The authority role remains function-only.

Before exposing readiness, require the local checkpoint to match the retained authority, restored canaries to be tombstoned, and correction outbox evidence to exist. Replay invalidates application cache state synchronously. Once the gate succeeds and the host starts, the normal idempotent outbox processor drains correction rows; inspect failed and dead-lettered rows before declaring external projections converged.

For local orchestration, `aspire start --isolated --apphost src/Explore.AppHost/Explore.AppHost.csproj` waits for a stable AppHost state and surfaces early startup failures; inspect with `aspire describe` and `aspire logs explore-api`. Do not infer success from an “app starting” log: prove the socket and the two database watermarks. See the official [Aspire `start` command](https://aspire.dev/reference/cli/commands/aspire-start/).

## Health and Metrics Endpoints

Via `Explore.ServiceDefaults` + app-specific checks:

- `/health`: readiness (`ready`-tag checks only)
- `/alive`: liveness (`live`-tag checks only)
- `/metrics`: Prometheus scraping endpoint

Status-code contract:

- `Healthy` / `Degraded` -> `200`
- `Unhealthy` -> `503`

Readiness interpretation:

| Check | Host | Healthy | Degraded | Unhealthy |
|---|---|---|---|---|
| `shutdown` | API, Blazor, Control Plane BFF | Process is accepting traffic | Not used | Graceful shutdown is active; remove from load balancer |
| `database` | API, Blazor | EF Core can reach the configured primary provider | Not used | Database unavailable or migration/runtime connectivity failed |
| `data-protection-keys` | Blazor, Standalone | Redis probe succeeds (inspect `keyRingPresent` separately), or the active non-Redis provider completes protect/unprotect | Not used | Active key-store probe or protect/unprotect fails; this check does not prove prior-key recovery |
| `distributed-cache` | API, Blazor, Control Plane BFF | Effective cache round-trip works | Configured Redis fell back to in-memory cache | Effective cache round-trip failed |
| `oidc-discovery` | API, Blazor, Control Plane BFF | OIDC metadata valid, or OIDC is not configured | Not used | Configured OIDC metadata endpoint is unreachable or invalid |
| `atproto-authentication` | Blazor | AT Protocol login is disabled, or local prerequisites and the signed transient-store create/read/consume probe pass | ATProto is unavailable but explicit Local Identity or Keycloak is primary | ATProto is primary (or primary authority is unknown) and a prerequisite/probe fails |
| `smtp` | API | Instance delivery is disabled without a transport probe, or its configured SMTP connection/authentication succeeds | Instance delivery is enabled but its capability is unavailable, or SMTP connection/authentication fails or times out | Required capability/diagnostic resolution throws, or health-check composition fails; the registration retains an `Unhealthy` fallback |
| `email-dispatch` | API | Worker is intentionally disabled (`Enabled=false` or `Mode=Disabled`), or the selected trigger is enabled and outbox counts are below warning thresholds | `Quartz` mode selected while the scheduler is disabled; operator pause or backlog, stale-processing, unknown, dead-letter, or age thresholds require attention | Outbox/database status cannot be read or health-check composition fails; invalid dispatch/scheduler options still fail startup |
| `email-dispatch-retention-cleanup` | API | Retention cleanup is enabled in redaction or dry-run mode | Cleanup is intentionally disabled | Invalid retention options fail startup |
| `email-dispatch-rabbitmq` | API | RabbitMQ Dispatch Mode is disabled, or enabled and topology can be declared | Not used | RabbitMQ mode is enabled but the broker/topology is unreachable or invalid |
| `queue-drains` | API | Scheduler-owned IntegrationSync, incoming-webhook, bulk-replay, optional provider-publication, and PDS lanes are enabled as configured and below aggregate thresholds | A required lane is disabled or any enabled lane reaches its bounded due, stale, ambiguous, unknown, executing, or dead-letter threshold | The bounded aggregate database query fails |
| `web-push-dispatch` | API | Web Push is disabled, or enabled with bounded backlog/retry/lease/failure counts | Due, stale-processing, or terminal-failure counts crossed configured warning thresholds | Invalid VAPID/worker settings fail startup |
| `idempotency-cleanup` | API | Expired idempotency cleanup is enabled in delete or dry-run mode | Cleanup is intentionally disabled | Invalid cleanup options fail startup |
| `ai-retention-cleanup` | API | AI retention cleanup is enabled in redaction or dry-run mode | Cleanup is intentionally disabled | Invalid cleanup options fail startup |
| `storage` | API | Selected storage provider is available. Local mode verifies the API-owned data root is writable; S3-compatible mode verifies bucket reachability only when selected. | Not used | Selected provider cannot be resolved, selected local root is not writable, or selected S3-compatible storage is missing/unreachable |
| `storage-reconciliation` | API | Storage reconciliation worker is enabled in dry-run or mutation mode | Reconciliation is intentionally disabled | Invalid reconciliation options fail startup |
| `payment-reconciliation` | API | Payment due/parked/configuration-blocked/duplicate-success counts are below attention thresholds | Due work reaches 100, or any parked, configuration-blocked, or duplicate-succeeded-order condition exists | The bounded payment readiness query fails |
| `ai-provider` | API | AI provider integration is disabled, the deterministic fake provider is enabled in `Development`/`Testing`, or OpenAI Responses/OpenAI-compatible/Anthropic/Anthropic-compatible/Azure OpenAI settings are valid | Not used | AI provider is enabled but no runnable provider is configured, a production-like host selects the forbidden fake provider, or provider endpoint/settings fail egress validation |
| `cerbos` | API | Local provider mode is selected, or configured Cerbos PDP passes gRPC health | Not used | Instance `authorization.provider` is `cerbos` and the PDP is missing or unreachable |
| `atproto-jetstream` | API | Ingestion is dormant because no tenant capability is enabled, or capability plus public/curated exact-collection subscription is ready | Not used | Capability readiness cannot be resolved |
| `privacy-erasure` | API | Authority is caught up and no provider work is `Unknown` or dead-lettered | Replay lag or provider reconciliation/dead-letter attention is present | Authority/checkpoint diagnostics cannot be read |
| `event-location-privacy-review` | API | EventLocation privacy remediation backlog is at or below `LocationPrivacy:Observability:ReviewQueueDegradedThreshold` (default 50) | Backlog exceeds the threshold, so erased or tightened venues are still awaiting organizer remediation | The bounded aggregate backlog count cannot be read |
| `islamu-event-api` | Blazor, Control Plane BFF | BFF can reach API readiness endpoint | Not used | API readiness endpoint is unavailable or unhealthy |
| `secret_provider` | API, Blazor, Control Plane BFF | Secret backend path is healthy | Secret backend has transient failures within the configured threshold | Secret backend crossed the unhealthy threshold |

Operational rules:

- Privacy-erasure readiness exposes only topology, restore capability, aggregate high-water/floor values, bounded replay reason, replay-caught-up state, and aggregate due/unknown/dead-letter counts. It never exposes intent IDs, subject IDs, provider targets, endpoints, payloads, credentials, connection details, or exception text.
- Privacy-erasure cleanup is finite and bounded: receipt hashes and provider locators expire through the scheduled dry-run-capable cleanup, while authority maintenance separately evaluates or atomically compacts an expired contiguous prefix. Authority compaction requires the caller to provide the complete explicit PII-free legal-hold sequence set; held rows are pseudonymized and the floor never advances past the first held row in that pass.
- Unknown provider work is not self-healing. Explicit reconciliation may move it to completed or retry-scheduled state, and dead-lettered work stays operator attention.
- Payment readiness exposes only aggregate `due`, `unknown`, `parked`, `configurationBlocked`, `duplicateSucceededOrders`, bounded `code`, and `oldestDueAtUtc` fields. It never exposes tenant/order/attempt/provider object IDs, account IDs, request IDs, URLs, PII, or secrets.

- Point load balancer readiness checks at `/health` and liveness checks at `/alive`.
- Treat `Degraded` as deployable only when the affected dependency is optional for the deployment mode and the response body clearly identifies the dependency.
- Treat `Unhealthy` as non-deployable for rolling updates; fix the dependency or intentionally switch the related feature/provider off.
- Treat `data-protection-keys` unhealthy as an active key-store incident. Check the actual host registration: shipped Split UI uses Redis, while default Combined Standalone uses its primary database. Do not restore an invented filesystem keyring or assume Healthy proves the previous keys survived. Preserve independent erasure authority and current credential revocations during recovery.
- SMTP is optional for core readiness. `SmtpHealthCheck` resolves instance delivery capability with a `null` tenant scope before probing. Disabled delivery is `Healthy` without SMTP network I/O; enabled but unavailable capability is `Degraded`. Configured SMTP connection/authentication failures and network timeouts are also `Degraded`, so `/health` remains HTTP 200 when core checks are healthy. `SmtpEmailService.TestConnectionAsync` explicitly resolves instance transport; it does not probe the ambient tenant's SMTP server.
- `IEmailConnectionTester` returns ordinary network/authentication outcomes as `EmailResult`. Exceptions escaping capability or diagnostic resolution remain `Unhealthy`, including required authority/database failures; health-check composition failures retain the same fail-closed fallback. The SMTP registration keeps its five-second timeout. Health output uses bounded codes and safe metadata, never transport `Message`, `ErrorMessage`, or exception details. Database, authorization, privacy-authority, and signing-key readiness failures are not weakened by optional email.
- Instance Cerbos readiness follows authorization fail-closed semantics: if the operator selected `authorization.provider=cerbos`, an unreachable PDP makes `/health` unhealthy rather than silently falling back to local RBAC.
- Local authorization mode skips Cerbos readiness, so self-hosted/local deployments do not need a Cerbos PDP unless explicitly selected.
- Basic Email Dispatch Mode skips RabbitMQ readiness entirely. A self-hosted deployment can send registration confirmation email with API + PostgreSQL + configured SMTP only. The default trigger is the Quartz `email-dispatch-drain` job; the hosted service mode is a fallback over the same drain service. The `email-dispatch` readiness payload also reports safe aggregate outbox counts for due dispatch backlog, retry-scheduled rows, stale processing leases, and dead-letter rows.
- Web Push readiness is healthy while `WebPush:Enabled=false`. When enabled, `web-push-dispatch` exposes only bounded aggregate dispatch counts and thresholds; it never exposes subscription endpoints, browser keys, VAPID material, tenant IDs, payloads, or provider bodies. Push-service `404`/`410` outcomes deactivate stale subscriptions transactionally, while retryable `429`/`5xx` outcomes remain bounded by the dispatch TTL and maximum attempts.
- The control-plane operations endpoint includes a `moderation-reporting` status card for managed reporting routing. It reports aggregate-only provider sync metrics (`pending-sync`, `stuck-pending-sync`, `failed-sync`, `disabled-sync`, `ignored-sync`) and active-tenant lock impact metrics (`reporting-locked-tenants`, `reporting-unlocked-tenants`, `osprey-locked-tenants`, `coop-locked-tenants`). `Reporting:Health:StuckProviderSyncMinutes` defaults to `120`; `Reporting:Health:FailedProviderSyncWarningThreshold` defaults to `1`. These metrics are safe for operators and must not include tenant identifiers, report identifiers, provider URLs, API keys, webhook secrets, correlation IDs, provider payloads, or raw provider errors.
- RabbitMQ Dispatch Mode is optional transport infrastructure. When `EmailDispatchRabbitMq:Enabled=false`, the `email-dispatch-rabbitmq` check is healthy without opening a broker connection. When enabled, missing broker connectivity or failed topology declaration is unhealthy because the operator explicitly selected RabbitMQ transport.
- Idempotency cleanup is an optional operational worker over the PostgreSQL replay cache. `Degraded` means cleanup is intentionally disabled; stale rows remain ignored for replay but are not physically deleted until cleanup is re-enabled.
- AI retention cleanup is an optional operational worker over tenant-owned AI assistant history. `Degraded` means cleanup is intentionally disabled; expired conversations remain readable until cleanup is re-enabled. Dry-run is healthy and records counts without redaction.
- Registration retention cleanup starts five minutes after API startup and then runs daily. It visits active tenants and deletes at most 500 expired answers, sensitive answer values, order PII rows, and participant PII rows per tenant per pass using each row's immutable `RetentionUntil` deadline. `LegalHold` has no automatic deadline. Consent current/history rows and contact-export audits are deliberately outside this cleanup and remain evidentiary records. Failures are logged for the next daily attempt without logging attendee values.
- Storage readiness follows the selected instance storage policy. Local-first deployments do not need S3 for `/health`; S3 configuration is probed only when `s3_compatible` is the selected provider. The readiness payload exposes bounded provider/status/failure-code fields and does not include filesystem paths, endpoints, bucket names, access keys, object keys, or secrets.
- Storage reconciliation is dry-run-first. `StorageReconciliation:DryRun=true` reports drift without metadata or provider mutations. Destructive cleanup requires `DryRun=false` plus a specific mutation flag such as `DeleteQuarantinedObjects=true`; health and logs expose bounded settings/counts only.
- Heavy event moderation image deletion is a post-commit provider operation. Redaction commits first, affected image metadata stays unavailable with `delete_requested`, and provider failures return a pending retry result instead of full moderation success. Retry by repeating the heavy-redaction command after fixing provider readiness, or let reconciliation handle eligible delete-requested rows when destructive reconciliation is intentionally enabled. Logs and metrics for this path must not include object keys, filenames, filesystem paths, S3 endpoints, bucket names, credentials, raw provider response bodies, or raw exception text.
- AI provider readiness is intentionally configuration-first. `AiProvider:Enabled=false` is healthy-disabled. If enabled, unsupported providers, missing required provider endpoint/key/model values, local/private endpoints without explicit opt-in, Azure OpenAI non-HTTPS endpoints, embedded endpoint credentials, query strings, or fragments make readiness unhealthy before chat/send is broadly enabled. The readiness payload exposes only bounded booleans and provider/status labels, not endpoint URLs, API keys, model IDs, prompts, responses, provider request IDs, or raw provider errors.

### Photon Geocoding Readiness And Change Control

`GEOCODING_PROVIDER=None` is healthy and must issue zero HTTP requests. With
Photon enabled, readiness performs one bounded, query-free `GET /status`
request. It never executes `/api`, retries a health probe, parses provider
records, or emits the configured endpoint. Unavailable, rate-limited, invalid,
or timed-out Photon readiness is `Degraded`; process liveness remains healthy so
local governed suggestions continue to operate.

The check is registered as `geocoding` on the standard readiness surface with
the bounded tags `ready`, `geocoding`, `provider`, and `infrastructure`. Its
data contains only `provider=photon` plus one state category:
`disabled`, `configured`, `invalid_configuration`, `limited`, `timeout`, or
`unreachable`. Caller cancellation propagates; the configured timeout degrades
without retry. Response bodies are never read.

Activation requires the operator deployment manifest listed in
[SELF_HOSTING.md](SELF_HOSTING.md). Keep release/image and dataset checksums,
capacity evidence, attribution obligations, refresh/rollback instructions, and
restore evidence in that operator-controlled artifact. Do not duplicate those
records as application options, secrets, health data, or telemetry dimensions.
Use
[`docs/examples/photon-deployment-manifest.example.yaml`](examples/photon-deployment-manifest.example.yaml)
as the machine-readable starting shape and attach the populated manifest to the
activation change record.

For a provider incident:

1. Set `GEOCODING_PROVIDER=None` and restart the API to stop outbound calls.
2. Confirm local address suggestions and manual create/PATCH remain available.
3. Preserve only bounded provider outcome, retry count, and latency-bucket
   telemetry; do not capture query, URI, address, coordinates, records, tokens,
   tenant, organization, actor, or upstream response bodies.
4. Repair or roll back the operator deployment, validate `/status`, benchmark,
   and update the activation/change record before re-enabling Photon.

## Deployment Protection and Evidence

GitHub Actions deploys use the `staging` and `production` environments. Configure environment rules in GitHub repository settings, not in application runtime configuration. Code scanning is owned by the `CodeQL Advanced` workflow; keep GitHub CodeQL default setup disabled so advanced SARIF uploads are accepted:

Tier 0-2 implementation phases produce machine-consumed evidence before phase
closeout. Evidence names and executes the owning invariant-breaker scenarios:
real concurrency, provider behavior, tenant isolation, fail-closed
authorization, privacy erasure, and zero-PII telemetry as applicable. TUnit
runs on Microsoft.Testing.Platform; use `--treenode-filter` for focused
execution and `--minimum-expected-tests` so an empty selector fails. Do not
create linked wrapper projects, lower behavior to interaction assertions, or
use a mutation score as merge evidence. Store anonymized MAD output as
structured YAML with specialist proposals, concrete invariant-breaker tests,
weighted votes, and no unresolved critical finding. Architecture evidence
contracts validate structured fields, executable behavior, and sentinel values
rather than source text, prose, or prompt wording.

- `production` should require reviewer approval and restrict deployments to `main` and version tags.
- `staging` should use environment-scoped secrets and can deploy automatically from `develop` unless the release process requires review.
- Store Coolify webhook URLs and bearer tokens as environment secrets. Do not print webhook URLs or tokens in workflow logs.

See [CI_CD_GOVERNANCE.md](CI_CD_GOVERNANCE.md) for the required/advisory gate matrix, branch-protection settings, and artifact retention policy. Test lanes that require Docker-backed providers, including Mailpit SMTP evidence, stay explicit runtime evidence rather than hidden prerequisites of the fast build/test gate.

Deploy jobs call the existing Coolify webhook contract through the local `.ci/actions/deploy-coolify` composite action with timeout, retry, redacted error output, transport-failure summaries, and explicit HTTP status validation. Before calling the action, the deploy workflow downloads retained `container-build-*` evidence and resolves the component's expected immutable image tag and digest with `.ci/scripts/resolve-deploy-image-evidence.cs`. The action writes deployment summaries and uploads deployment evidence for 90 days through the caller workflow. Production deploys require configured `PRODUCTION_API_URL` and `PRODUCTION_UI_URL` values for components being deployed; missing production smoke URLs block the Coolify webhook call. When a smoke URL is configured, the action requires both `/alive` and `/health` to return `200` with a bounded retry budget before reporting success. Staging smoke URLs remain optional, but configured staging URLs use the same checks. The reusable container build verifies the pushed GHCR digest's artifact attestation before deploy jobs can invoke Coolify.

Set the GitHub Environment or Repository variable `DEPLOYMENT_FREEZE=true` to block Coolify webhook calls during a deployment freeze. Manual `workflow_dispatch` runs can provide `override_reason` for urgent security releases; the local deploy action records the override reason in deployment evidence before it calls Coolify. Push-triggered deploys do not have an override reason and are blocked while the freeze variable is active.

### Deployment Image Source of Truth

Container builds publish mutable convenience tags (`latest` for production and `develop` for staging), full-commit immutable tags (`sha-${GITHUB_SHA}` for production and `dev-${GITHUB_SHA}` for staging), digest evidence, immutable tag promotion evidence, SBOM/provenance attestations, image scan artifacts, and attestation verification evidence for the pushed GHCR digest. GitHub artifact attestations are the selected SLSA-compatible provenance evidence path: the reusable build verifies the SLSA provenance predicate for the pushed GHCR digest with `gh attestation verify` before dependent deploy jobs can start. ATCR currently uses the scoped `ATCR_PASSWORD` environment secret because public ATCR docs document ATProto OAuth/DPoP, the Docker credential helper/device flow, short-lived registry JWTs behind that helper, and `docker login` with an ATProto app password, but not a GitHub Actions OIDC federation path for CI pushes. Rotate `ATCR_PASSWORD` at least every 90 days and replace it with documented GitHub OIDC or another non-interactive short-lived credential path when ATCR supports that model. Deployable Dockerfiles pin .NET runtime and SDK base images with tag-plus-digest references; Dependabot Docker update PRs are the expected path for refreshing those base digests.

Production must not rely on `latest` as the source of truth once deployment promotion is complete.

Decision path:

1. Preferred: configure Coolify Docker Image resources to deploy explicit image digests. Public Coolify v4.x source/UI evidence supports Docker Image hash input and normalizes SHA-256 references to `image@sha256:<digest>`; ISLAMU still needs live Coolify application configuration/deployment evidence proving the resource consumed the expected digest.
2. Fallback: configure Coolify to deploy immutable full-commit tags (`sha-${GITHUB_SHA}` for production and `dev-${GITHUB_SHA}` for staging) until live digest consumption is configured and proven. The reusable container build records those primary-registry tag references and verifies that each resolves to the built digest before deployment jobs can start; the deploy workflow resolves the retained promotion artifact again and records the expected immutable image tag plus expected image digest in deployment evidence.
3. Temporary risk: mutable tags remain convenience aliases and must not be used as the release source of truth while live Coolify digest/tag consumption proof is being collected.

Do not remove digest/SBOM/provenance evidence even if Coolify temporarily consumes immutable tags rather than digests.

## Graceful Shutdown Contract

API and Blazor include shutdown-aware checks for rolling deployments.

API specifics:

- grace window: 25 seconds
- during shutdown, health checks become unhealthy so load balancers stop routing traffic

## Request Protection Defaults

API runtime protections include:

### Rate Limiting (`RateLimiting` config section)

| Policy | Mechanism | Partition Key | Defaults |
|---|---|---|---|
| `global` | Token bucket | Successfully authenticated API key ID when present, otherwise remote IP | 200 tokens, replenish 40/10s. Localhost exempt for anonymous/IP traffic |
| `authenticated` | Sliding window | API key ID when present, otherwise `User.Identity.Name` | 200 requests/60s, 4 segments |
| `write` | Fixed window | API key ID when present, otherwise `User.Identity.Name` | 30 requests/60s |
| `PublicIngestion` | Fixed window | IP | 60 requests/60s |
| `setup_secret` | Fixed window | IP | 5 requests/60s |
| `AnalyticsRelay` | Fixed window | IP | 120 requests/60s |
| `AiAssistant` | Fixed window | API key ID when present, otherwise authenticated user ID | 12 sends/60s |
| `EventOpenGraphImage` | Concurrency | Fixed `EventOpenGraphImage` key shared by the API process | 2 concurrent renders, queue 0 |

**Rejection**: `429 Too Many Requests` with RFC 6585 ProblemDetails, `Retry-After` when available, plus `X-RateLimit-Limit` and `X-RateLimit-Remaining` headers. Successfully authenticated API keys are throttled per key ID; no-key, malformed, invalid, revoked, or inactive API-key traffic remains in the anonymous/IP partition. External API-key authentication metrics use bounded `outcome`, `tenant_id`, and `owner_type` tags only, never raw keys, secrets, or request paths.

**Testing override**: All rate limiters are replaced with `NoLimiter` in `Testing` environment unless a specific integration factory opts back into the global limiter to assert `429`, `Retry-After`, and per-key/IP partition behavior.

**Config keys** (all under `RateLimiting` section):
- `Global:TokenLimit`, `Global:ReplenishmentPeriodSeconds`, `Global:TokensPerPeriod`
- `Authenticated:PermitLimit`, `Authenticated:WindowSeconds`, `Authenticated:SegmentsPerWindow`
- `Write:PermitLimit`, `Write:WindowSeconds`
- `PublicIngestion:PermitLimit`, `PublicIngestion:WindowSeconds`
- `SetupSecret:PermitLimit`, `SetupSecret:WindowSeconds`
- `AnalyticsRelay:PermitLimit`, `AnalyticsRelay:WindowSeconds`
- `AiAssistant:PermitLimit`, `AiAssistant:WindowSeconds`
- `EventOpenGraphImage:ConcurrencyLimit`

`EventOpenGraphImage` is a process-wide render ceiling within each API process. All Open Graph image requests share the fixed partition, and the zero-length queue rejects excess work immediately with `429`. Raise the concurrency limit carefully after observing CPU and memory use; each API replica has its own ceiling.

AI assistant send requests have layered abuse controls:
- API rate limiting uses the `AiAssistant` policy before the request reaches MediatR.
- Application handlers enforce `ai_assistant.daily_message_limit`, `ai_assistant.daily_tenant_message_limit`, and `ai_assistant.concurrent_run_limit` before provider calls.
- Idempotency replay is evaluated before quota checks so successful retries do not consume additional provider calls.
- AI run cancellation is persisted through the authenticated cancel-run API for queued/in-progress runs only. Run-status HAL exposes `cancel-run` only while a run is cancellable; completed runs return safe conflict ProblemDetails. The send-message pipeline already passes the request `CancellationToken` into provider calls, but cross-request provider abort orchestration is not a scheduler/registry feature yet.
- AI run progress uses authenticated polling, not streaming. `SendAiMessage` returns the run-status route and clients poll `GET /api/ai/assistant/conversations/{conversationId}/runs/{runId}` until the run reaches a terminal state. `ai_assistant.streaming_enabled` remains reserved and disabled until a future slice hardens streaming transport, proxy buffering, request cancellation, timeout behavior, auth, logging, and non-streaming fallback.
- 429 and quota ProblemDetails must not include prompts, model responses, selected reference content, provider request IDs, endpoint URLs, API keys, or raw provider errors.

MCP adapter operations are opt-out at startup:
- The MCP adapter endpoint is mapped by default through the startup ceiling `Mcp:Enabled=true` at `/mcp`. Set `Mcp:Enabled=false` only when the endpoint must be unmapped at startup.
- Runtime governance then resolves `mcp.enabled` through the instance/tenant settings cascade. Effective exposure is `Mcp:Enabled && resolved(mcp.enabled)`, so instance administrators can turn the adapter off without changing endpoint path/stateless startup posture.
- The adapter exposes a bounded readiness check named `mcp-adapter`, read-only registry discovery, anonymous-safe public event reads (`search_public_events`, `get_public_event`, `get_public_event_program_summary`, `list_public_event_sessions`), authenticated event-management reads (`list_my_events`, `get_event_creation_context`, `get_event_publish_readiness`, program/custom-property/registration/team/template/sync contexts), the authenticated `event_management_context` resource template, first-class registry-projected `propose_*` tools, safe AI conversation resources, and event-management confirmation prompts. `list_my_events` derives the principal from the authenticated request and delegates to `GetMyEventsRequest`; it does not accept a caller-supplied user id. `get_event_creation_context` delegates to `GetEventCreationContextRequest` and returns bounded tenant policy flags plus publisher options without MCP-side role/claim inference or internal role IDs. `event_management_context` delegates detail visibility to `GetEventDetailsRequest`, materializes REST HAL through the event resource assembler, and derives edit/delete/publish/publish-readiness/add-session/session-create-context availability from `_links`. `get_event_publish_readiness` also materializes REST HAL first and calls `GetEventPublishReadinessRequest` only when `_links` contains `publish-readiness`; otherwise it returns a bounded `not_found` or `not_available` descriptor. Phase 5 read tools use the same authenticated event-management policy, MediatR queries, bounded descriptors, and event HAL/domain-authority gates before returning program/session, custom-property, registration, team, template, or sync context data. Mutating MCP tools persist proposed actions through MediatR and require the normal product/API confirmation path before side effects occur.
- The selected product transport is API-hosted stateless Streamable HTTP. Keep `Mcp:Stateless=true`; no MCP session affinity should be required for API replicas.
- Do not add `WithStdioServerTransport()` to the product API host. `stdio` remains a local/developer diagnostic transport and needs a separate host/runbook decision before use.
- `Mcp:EnableLegacySse=true` is the default startup ceiling for future governance only; `mcp.enable_legacy_sse` records runtime intent, but current runtime legacy SSE remains unavailable because the official SDK legacy mode requires stateful in-memory sessions and weaker request backpressure than Streamable HTTP.
- Instance administrators can lock tenant MCP overrides with `governance.lock_tenant_mcp` and `governance.lock_tenant_mcp_legacy_sse`. Multi-tenant tenant administrators can override only unlocked values; single-tenant deployments use the existing single-tenant bypass semantics.
- Keep MCP SDK registration explicit. The API host uses explicit `WithTools<T>()`, `WithResources<T>()`, `WithPrompts<T>()`, and registry-projected tool options instead of assembly scanning. This keeps startup behavior reviewable and avoids avoidable Native AOT/reflection risk; Native AOT publication is not supported until a dedicated publish profile and verification gate exist.
- MCP is API-key-first for external clients. The endpoint is mapped anonymously so SDK authorization filters can expose only explicitly anonymous-safe registry discovery and public event reads without credentials; scoped tools/resources/prompts carry scope-aware policies and require a valid bearer session or non-empty `X-API-Key` principal. API keys need `mcp:read` for generic MCP read resources, `mcp:read` plus event read-equivalent scope authority for protected event-management reads such as `list_my_events`, `get_event_creation_context`, `get_event_publish_readiness`, and `event_management_context`, and `mcp:propose` for proposal tools/prompts; no key, a blank `X-API-Key` header, invalid keys, revoked keys, or valid keys without the required MCP/domain scope combination can see only anonymous-safe capabilities. Requests that send both `Authorization` and a non-empty `X-API-Key` return a redacted bad-request response. When rate limiting is enabled, valid MCP API keys are partitioned by key ID while anonymous/blank/invalid/revoked MCP requests are partitioned by remote IP and still return normal `429` responses without echoing credentials.
- MCP tools must be registry-backed and mutating tools must use the proposal/confirmation path; MCP must not mutate repositories directly.
- Production MCP exposure must use the same trusted HTTPS boundary as the API. Local `curl -k` is acceptable only for developer certificate troubleshooting and must not appear in production runbooks, support evidence, or client configuration.
- MCP health, logs, metrics, traces, and errors must not include prompts, selected reference content, tool payloads, provider responses, provider endpoint URLs, API keys, tenant IDs, user IDs, or raw provider exceptions. Health reports only safe effective-state booleans such as `startupEnabled`, `runtimeEnabled`, and legacy-SSE requested/enabled state. Bounded MCP telemetry uses `Explore.Mcp` as both ActivitySource and Meter name with allow-listed tool/outcome/failure-code tags only.

MCP recovery and operator actions:
- If `mcp-adapter` is degraded because MCP is disabled, confirm that startup `Mcp:Enabled=false` or runtime `mcp.enabled=false` was intentional.
- If MCP was enabled at startup but must be rolled back immediately, set runtime `mcp.enabled=false` from instance governance. Requests to the mapped MCP path return `404` while the rest of the API remains available.
- If MCP must be unmapped entirely, set `Mcp:Enabled=false` and restart the API. Inspect only bounded startup/configuration errors. Do not capture prompts, payloads, provider responses, API keys, endpoint URLs, tenant IDs, or raw MCP request bodies in support tickets.
- If an MCP API key or captured client configuration is suspected leaked, revoke the key first, verify invalid/revoked-key traffic only sees anonymous-safe discovery and remains IP-partitioned, then create a new least-privilege key for the specific operator smoke scenario.
- If external agents report mutation failures, inspect the returned failure code from the projected `propose_*` tool or generic `propose_ai_tool_action`, then inspect the normal AI conversation/proposed-action API state. Do not bypass the confirmation flow or write repositories directly.
- If a client requires legacy SSE, treat that as a new architecture decision. Startup and runtime governance can record intent, but the current adapter intentionally supports stateless Streamable HTTP only and reports `legacySseRuntimeEnabled=false`.
- If a deployment requests Native AOT for the API host, treat MCP as unverified until a dedicated `dotnet publish` profile proves the SDK, explicit static registrations, and registry-projected dynamic tools all survive trimming/AOT without losing schema metadata.

MCP local debugging, Inspector, and redacted contract smoke:
- The full local debug and client-smoke runbook is [MCP_DEBUGGING.md](MCP_DEBUGGING.md). It includes Debug-build startup, redacted `.vscode/mcp.json`/`.mcp.json` templates, Inspector, GitHub Copilot Agent Mode, JSON-RPC fallback, and compatibility gates.
- First run the deterministic replay report. This is the CI-safe contract check and uses no live provider credentials:
  `dotnet run --project src/Explore.Diagnostic/Explore.Diagnostic.csproj --configuration Release --no-restore -- ai-replay-report --output /tmp/explore-ai-replay-mcp-inspector`
- Use MCP Inspector only for manual local/staging smoke against fake or disposable data. Current MCP docs start Inspector with `npx -y @modelcontextprotocol/inspector`; connect it to the API's Streamable HTTP URL, for example `https://<redacted-host>/mcp`.
- Configure `X-API-Key: <redacted-api-key>` for normal scoped machine smoke, leave credentials blank for anonymous-safe discovery, or use `Authorization: Bearer <redacted-token>` only for user-delegated local smoke. For multi-tenant routing, use the same trusted tenant binding as normal API traffic, such as host/subdomain routing or `X-Tenant-Slug: <redacted-tenant-slug>`. Do not mix bearer and API-key credentials in one request.
- Discovery checklist: initialize the connection, then list tools, resources, resource templates, and prompts. Anonymous or invalid-key tool surface is `list_ai_tool_contracts`, `search_public_events`, `get_public_event`, `get_public_event_program_summary`, and `list_public_event_sessions` only; a valid key with `mcp:read` plus event read-equivalent scope can also expose protected event-management reads such as `list_my_events`, `get_event_creation_context`, `get_event_publish_readiness`, `event_management_context`, and the Phase 5 program/custom-property/registration/team/template/sync context tools. Generic MCP read resources such as `ai_conversations` still require MCP read scope, while proposal tools/prompts require `mcp:propose`.
- Safe call checklist: call `list_ai_tool_contracts`; optionally call `search_public_events`, `get_public_event`, `get_public_event_program_summary`, or `list_public_event_sessions` for published public event data only; optionally call protected reads such as `get_event_publish_readiness` only for disposable events where REST HAL currently exposes `publish-readiness`; optionally call one representative Phase 5 context tool only for disposable event data; optionally call `propose_create_event_draft` or `propose_update_event_draft` only against a disposable test conversation with fixture data. Stop after a proposed action is returned. Do not call confirm/reject API endpoints from Inspector, do not write repositories, and do not assert that an event was created.
- Redaction checklist: retain only scenario codes, pass/fail status, redacted endpoint path, redacted auth mode, and bounded failure categories. Do not retain Inspector screenshots, browser storage, exports, proxy logs, prompts, selected-reference content, raw tool payloads, provider responses, tenant/user identifiers, bearer/API-key values, model IDs, raw MCP request/response bodies, or raw exceptions.
- If a command-line HTTP smoke is needed instead of Inspector, use the same redaction rules and send JSON-RPC methods such as `tools/list`, `resources/list`, `resources/templates/list`, and `prompts/list` with `ProtocolVersion: 2025-06-18`, `Accept: application/json, text/event-stream`, and `Content-Type: application/json`. Do not paste real credentials or response bodies into tickets.
- Automated MCP protocol coverage lives in `McpProtocolContractTests`, `EventManagementMcpPublicReadTests`, and `EventManagementMcpAuthenticatedReadTests`: it exercises `initialize`, discovery lists, registry discovery calls, public event list/detail/program/session parity, authenticated my-events/creation-context/publish-readiness reads, HAL-derived event-management context parity, generic/projected proposal calls, disabled endpoint behavior, and redaction failure paths through `WebApplicationFactory` without Inspector, Copilot, live providers, product confirmation calls, or database side effects beyond disposable proposed-action fixtures.
- Review-first MCP debug readiness is covered by `McpDebugReadinessDoctorCheck`. The doctor only checks docs/tests/ignore-rule presence and redacted content markers; it does not start servers, call live endpoints, generate tokens, persist config, run migrations, or print secrets.

MCP protocol/client compatibility reviews:
- Run this review before upgrading `ModelContextProtocol.AspNetCore`, changing MCP headers/protocol versions, or supporting a client that requests behavior outside the current stateless Streamable HTTP surface.
- Current allowed capability posture is intentionally small: anonymous-safe registry discovery, anonymous-safe public event list/detail/program/session reads backed by MediatR queries plus a `Published` + `Public` MCP gate, authenticated event-management reads/resources backed by MediatR and REST HAL affordances including HAL-gated publish readiness, registry-projected proposal tools, and no server-to-client requests. Stateless mode means no `Mcp-Session-Id`, no API session affinity, no legacy SSE runtime transport, no resource subscriptions, and no sampling/elicitation/roots/server-initiated notifications.
- Treat these as ADR-gated changes: stateful sessions, session migration/resumability, GET/DELETE MCP endpoints, legacy SSE, sampling, elicitation, roots, completions, progress notifications, tool/resource/prompt list-changed notifications, dynamic non-registry tool changes, client-specific compatibility shims, or any use of SDK annotations as authorization authority.
- Review checklist: read the SDK/protocol release notes, compare generated MCP tool/resource/prompt surface, rerun focused MCP API tests including `McpProtocolContractTests`, rerun `ai-replay-report`, repeat the redacted Inspector checklist when endpoint behavior changes, and verify docs/configuration still say default `/mcp`, stateless, API-key-first, and proposal-first.
- Compatibility evidence matrix: Inspector and VS Code/Copilot smoke are manual and redacted; WebApplicationFactory JSON-RPC tests are CI-safe; official C# SDK client transport remains a future upgrade target when it fits the in-memory test host; curl is a local fallback only.
- Evidence may include package version, protocol version, scenario codes, pass/fail status, and bounded failure categories only. Do not preserve raw MCP request/response bodies, prompts, payload JSON, provider data, tenant/user identifiers, endpoint URLs, bearer/API-key values, model IDs, or raw exceptions.
- Rollback posture: if a client requires unsupported stateful/SSE/server-to-client behavior, keep `Mcp:Enabled=false` for that deployment or keep the current minimal surface unchanged until the ADR, implementation, tests, self-hosting docs, and redaction runbook are complete.

Advisory AI evaluation reports:
- Generate deterministic ATCR evaluation evidence with:
  `dotnet run --project src/Explore.Diagnostic/Explore.Diagnostic.csproj --configuration Release -- ai-eval-report --output artifacts/ai-evaluation`
- The report is intentionally advisory and non-gating. It covers tool proposal correctness, refusal/safety behavior, prompt-injection resistance, selected-reference groundedness metadata, MCP proposal-flow posture, and event-draft regression using local fake/deterministic checks, so normal CI and operator smoke tests do not require live AI provider credentials or model calls.
- JSON and Markdown artifacts must stay redacted. They may include scenario codes, dimensions, status, summaries, and recommendations, but not prompts, provider responses, selected-reference content beyond deterministic fixture labels, raw tool payloads, tenant/user identifiers, endpoint URLs, API keys, model secrets, or raw exceptions.
- Treat report drift as trend evidence first. Do not promote model-scored or live-provider evaluation checks to hard CI gates until cost, cache stability, false-positive behavior, and provider volatility are reviewed.

Fake/replay AI usability reports:
- Generate deterministic assistant/MCP proposal-flow evidence with:
  `dotnet run --project src/Explore.Diagnostic/Explore.Diagnostic.csproj --configuration Release -- ai-replay-report --output artifacts/ai-replay`
- The report is suitable for normal CI because it uses local fake/replay checks only. It validates assistant rail catalog + plan-preview readiness, MCP Inspector discovery checklist posture, projected MCP tool selection, MCP proposal-first/confirmation-required behavior, missing-HAL blocking, and safe recovery metadata without live provider credentials, screenshots with user content, or database writes.
- The command exits non-zero if a replay scenario fails, a live-provider credential path is used, content-bearing artifacts are detected, or database side effects are detected.
- JSON and Markdown artifacts may include scenario codes, failure classes, pass rates, redacted diagnostics, and artifact paths. They must not include prompts, provider responses, selected-reference content, raw tool payloads, tenant/user identifiers, endpoint URLs, API keys, model secrets, or raw exception bodies.

### Request Timeouts (`RequestTimeouts` config section)

| Policy | Default | Applied To |
|---|---|---|
| `Default` | 30 seconds | Standard operations |
| `Lookup` | 10 seconds | Lookup/fast queries |
| `Complex` | 60 seconds | Complex queries, exports |

Timeout expiry: `504 Gateway Timeout`.

### Response Compression

- Brotli + Gzip at `CompressionLevel.Fastest`, enabled for HTTPS.
- Additional MIME types: `application/json`, `application/hal+json`.

### ETag / Conditional Requests

- SHA256-based weak ETags on JSON/HAL responses.
- Client sends `If-None-Match` → API returns `304 Not Modified` when content unchanged.
- Saves bandwidth for repeat requests.

### Correlation ID

- `CorrelationIdMiddleware` reads `X-Correlation-ID` or `X-Request-ID` from incoming request.
- Uses `HttpContext.TraceIdentifier` when both request headers are absent.
- Pushes to Serilog `LogContext` as `CorrelationId` property for structured log correlation.

### Forwarded Header Trust

- `Explore.API` and the Split BFF use ASP.NET Core forwarded-header middleware with explicit `ForwardedHeadersTrust` configuration.
- The API and BFF default to loopback-only trust. For non-loopback ingress, set the matching `API_FORWARDED_HEADERS_*` or `BFF_FORWARDED_HEADERS_*` proxy/network value to the exact boundary; Compose and Aspire map these values without trusting a Docker bridge wholesale.
- The BFF accepts trusted `X-Forwarded-For` and `X-Forwarded-Proto` only. It never consumes `X-Forwarded-Host`.
- Operators must configure trusted reverse-proxy IPs or CIDR networks before relying on API `X-Forwarded-Host` for custom-domain or subdomain tenant resolution.
- Malformed, trust-all, overlong, or unbounded configuration fails startup. Headers from untrusted direct clients are ignored.

### Security Headers

Added to every response by `SecurityHeadersMiddleware`:
- `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`
- `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`
- Non-GET responses additionally receive `Cache-Control: no-store` and `Pragma: no-cache`.

### EventLocation Privacy Metrics (OpenTelemetry)

Meter `Explore.EventLocationPrivacy` reports how venue disclosure behaves in production without ever
naming a tenant, event, venue, or requester. Every dimension is a closed vocabulary.

- `event_location_privacy_disclosures_total` (`purpose` = `public`/`attendee`/`management`, `state` = `hidden`/`to_be_announced`/`available`/`private_venue`/`unavailable`/`needs_privacy_review`) — one increment per evaluated disclosure. A rising `hidden`/`unavailable` share on the public surface usually means governance was tightened or a venue lost its PII.
- `event_location_privacy_corrections_total` (`event_type`, `status` = `success`/`retry`/`dead_letter`) — durable location-privacy correction dispatches. `event_type` is a compile-time outbox constant, never operator input.
- `event_location_privacy_review_queue_depth` — gauge of live EventLocations still flagged `NeedsPrivacyReview`, refreshed whenever the `event-location-privacy-review` readiness check is scraped. Before the first probe the gauge reports nothing at all, so an unscraped instance is never mistaken for an empty queue.

Alert on sustained `dead_letter` corrections and on review-queue depth above the configured threshold.
Do not build dashboards that join these series to tenant or event identifiers; the labels deliberately
do not carry them.

### Business Metrics (OpenTelemetry)

Meter `Explore.Business` exposes source-defined business counters. Counter names and tags are not uniform across all events; check the metric-specific tags before building dashboards.

AI provider tracing uses the `Explore.Ai.Provider` activity source. Provider spans are platform-owned and redacted; they intentionally do not use SDK GenAI middleware because prompts, responses, tool payloads, provider endpoints, model IDs, provider request IDs, tenant/user IDs, API keys, and raw provider errors must not be exported.

Current counters include:

Payment reconciliation currently uses the bounded `/health` projection and structured aggregate job log rather than dedicated `Explore.Business` counters. Alert on `payment-reconciliation` Degraded/Unhealthy and the scheduler's bounded claimed/succeeded/nonterminal/unknown/parked/stale counts; do not derive labels from order, account, provider object, or request identifiers.

### Refund Reconciliation And Campaign Recovery

Readiness exposes `refund-reconciliation` with aggregate `pending`, `unknown`, `requiresAction`, `failed`, `campaignsRequiringOperator`, `disputesDueSoon`, `disputesDueWithin72Hours`, `disputesOverdue`, and `oldestNonTerminalAtUtc` facts only. `failed` covers definitive failures observed within the last 24 hours; older terminal history does not keep readiness degraded forever. It degrades immediately for ambiguity, provider action, recent definitive failure, operator-required campaigns, any open dispute deadline within 72 hours or overdue, or non-terminal work older than 15 minutes. The `explore.refunds.operations` and `explore.refunds.campaign_operations` counters admit only closed operation/kind/status/outcome labels; tenant, event, order, payment, refund, amount, provider request, and personal-data labels are forbidden.

Alert and recovery policy:

- page immediately for any `unknown`, account restriction/configuration failure, `requiresAction`, or `campaignsRequiringOperator` value above zero;
- warn when non-terminal work reaches 10 minutes and page at the 15-minute readiness threshold;
- warn when a completed campaign has non-zero failed/unknown/operator counters or its generated count does not reach a closed buyer outcome;
- monitor pending balance by database aggregation grouped by bounded currency as a value, never by adding amount or identifiers as metric labels;
- treat dispute response deadlines inside 72 hours as urgent and inside 24 hours as paging; provider dispute responses remain external-provider operations and webhook observations remain authoritative locally;
- stop new refund initiation before recovery, preserve campaign/refund/outbox rows, inspect original-account routing and outbox dead letters, then use the campaign resource's `resume-refund-campaign` action. Resume requeues the existing provider-blocked attempt with its stable idempotency key; never set `Succeeded` manually;
- communicate `Pending`, `RequiresAction`, or `Unknown` verbatim to buyers. Say `Refunded` only after provider-proven success. If cancellation races with capture, allow payment reconciliation to settle, then let the stable campaign key create the refund exactly once.

- `explore.events.created` (`tenant_id`, `event_type`)
- `explore.events.published` (`tenant_id`)
- `explore.registrations.created` (`tenant_id`)
- `explore.organizations.created` (`tenant_id`)
- `explore.authorization.decisions` (`resource`, `action`, `result`)
- `explore.support_access.lifecycle_events` (`event_type`, `mode`, `outcome`, `failure_category`) — support-access start/stop/expire/revoke/force-stop decisions; labels intentionally exclude session IDs, actor/user IDs, tenant IDs, ticket references, reason text, and raw exception text.
- `explore.support_access.request_audits` (`event_type`, `outcome`, `persistence_outcome`, `failure_category`) — support-access per-request audit persistence outcomes; labels intentionally exclude route paths, resource IDs, session IDs, actor/user IDs, ticket references, request payloads, and raw storage errors.
- `explore.support_access.session_validation_denials` (`reason`, `mode`) — forwarded support-session validation denials such as kill-switch or write-mode shutdown; labels intentionally exclude forwarded session IDs, actor IDs, tenant IDs, and request headers.
- `explore.support_access.authorization_boundary_denials` (`reason`, `mode`, `action_class`) — runtime support-access boundary denials such as inactive forwarded sessions, read-only write attempts, missing tenant context, and cross-tenant mismatches; labels intentionally exclude resource IDs, session IDs, actor IDs, and tenant IDs.
- `event_role_assignment.changed` (`operation`, `outcome`, `role`)
- `explore.email_dispatch.attempts` (`outcome`, `failure_category`) — SMTP provider-handoff outcomes with closed-vocabulary labels; labels intentionally exclude tenant identity, recipient, subject, body, provider message ID, and raw error text.
- `explore.email_dispatch.operational_outcomes` (`outcome`, `reason`) — bounded eligibility-skip and SMTP-rate-deferral outcomes that occur without provider handoff.
- `explore.email_dispatch.tenant_backlog` (`sample_rank`) — active backlog samples ranked within the bounded health sample; no tenant identifier is exported.
- `explore.email_dispatch.oldest_pending_age` — oldest active due-row age in seconds without labels.
- `explore.email_dispatch.optional_reminder_deferral` — current persisted optional-reminder deferral state (`0` or `1`) as an observable gauge without labels.
- `explore.queue_drains.health_checks` (`job_name`, `outcome`) — bounded per-lane readiness outcomes using only canonical scheduled-job names and `healthy`, `degraded`, `disabled`, or `unhealthy`.
- `explore.queue_drains.backlog` (`job_name`) and `explore.queue_drains.stale_work` (`job_name`) — tenant-free aggregate queue counts; labels never include tenant, row, user, provider, endpoint, or payload identity.
- `explore.email_dispatch.rabbitmq.publishes` (`outcome`, `failure_category`) — optional RabbitMQ pointer-publish outcomes with closed-vocabulary labels; labels intentionally exclude tenant, recipient, subject, body, provider message ID, raw broker error text, and connection strings.
- `explore.email_dispatch.rabbitmq.consumes` (`outcome`, `failure_category`) — manual-ack RabbitMQ delivery outcomes with closed-vocabulary labels; labels intentionally exclude tenant, recipient, subject, body, provider message ID, publish event ID, delivery tag, raw broker error text, and connection strings.
- `explore.notifications.fanout_runs` (`tenant_id`, `fanout_kind`, `outcome`) — notification fanout run outcomes; labels intentionally exclude event IDs, actor IDs, subscriber IDs, notification IDs, event titles, and deduplication keys.
- `explore.notifications.fanout_subscribers` (`tenant_id`, `fanout_kind`, `outcome`) — aggregate subscriber decisions for notification fanout; labels intentionally exclude event IDs, actor IDs, subscriber IDs, notification IDs, event titles, and deduplication keys.
- `explore.event_reports.submissions` (`tenant_id`, `outcome`, `failure_category`) — event-report intake outcomes; labels intentionally exclude reporter text, reporter IP/User-Agent values or hashes, event titles, slugs, URLs, report IDs, and raw validation/provider errors.
- `explore.event_reports.workflow_actions` (`tenant_id`, `action`, `outcome`, `failure_category`) — moderation report triage/assign/decide/execute outcomes; labels intentionally exclude report IDs, case IDs, decision IDs, moderator IDs, reporter evidence, safe notes, and raw errors.
- `explore.event_reports.provider_syncs` (`tenant_id`, `provider`, `outcome`, `failure_category`) — report provider sync outcomes for local/Osprey/Coop/composite paths; labels intentionally exclude provider URLs, credentials, external case/signal IDs, payload bodies, reporter evidence, and raw provider errors.
- `explore.event_reports.provider_callbacks` (`tenant_id`, `provider`, `outcome`, `failure_category`) — provider callback outcomes; labels intentionally exclude callback bodies, signatures, provider decision IDs, provider message IDs, report IDs, event IDs, case IDs, reporter evidence, and raw parse/auth errors. Anonymous public-ingestion callbacks such as Svix operational webhooks use the default tenant tag even when a verified payload contains a tenant identifier.
- `explore.webhooks.messages_created` (`event_type`, `provider`, `outcome`) — canonical outgoing webhook message creation outcomes; labels intentionally exclude tenant/resource IDs, payloads, aggregate titles/slugs/URLs, endpoint URLs, and secrets.
- `explore.webhooks.delivery_attempts` (`event_type`, `outcome`, `failure_category`) — LocalProvider delivery attempt outcomes; labels intentionally exclude tenant/resource IDs, endpoint URLs, request payloads, response bodies, headers, and raw transport errors.
- `explore.webhooks.delivery_success` (`event_type`) — LocalProvider successful delivery count without tenant/resource identity.
- `explore.webhooks.delivery_failure` (`event_type`, `outcome`, `failure_category`) — LocalProvider failed delivery count with bounded failure categories only.
- `explore.webhooks.endpoint_disabled` (`failure_category`) — legacy endpoint auto-pause transition count; labels intentionally exclude tenant/resource IDs, endpoint URLs, and secrets.
- `explore.webhooks.manual_retries` (`event_type`, `outcome`, `failure_category`) — manual retry scheduling outcomes; labels intentionally exclude tenant, message, endpoint, and payload identity.
- `explore.webhooks.provider_publish_failure` (`event_type`, `provider`, `failure_category`) — outgoing provider handoff failures before provider-owned fanout; labels intentionally exclude tenant/resource IDs, provider message IDs, endpoint URLs, payloads, secrets, and raw provider errors.
- `explore.webhooks.claim_lag` (`provider`, `operation`) — claim-lag histogram for Local delivery and self-hosted Svix publication/reconciliation.
- `explore.webhooks.processing_outcomes` (`provider`, `operation`, `outcome`) — durable claim and settlement outcomes from the closed enum-backed telemetry vocabulary.
- `explore.webhooks.retries_scheduled` (`provider`, `operation`) and `explore.webhooks.dead_letters` (`provider`, `operation`) — automatic retry and terminal dead-letter transitions.
- `explore.webhooks.publication_unknown_age` (`provider`) and `explore.webhooks.manual_reconciliations` (`provider`) — uncertain publication age observations and operator-owned reconciliation transitions.
- `explore.webhooks.endpoint_auto_pauses` (`provider`) — counts only the transition into automatic pause, not later failures while already paused.
- `explore.webhooks.provider_health_checks` (`provider`, `outcome`) — independent Local, Svix, and Coop-effect readiness observations.
- `explore.webhooks.retention.cleanup_runs` (`mode`, `outcome`) and `explore.webhooks.retention.cleanup_items` (`mode`, `data_kind`) — cleanup pass and bounded evidence-category counts; unknown input collapses to `unknown`.
- `explore.ai.provider.health_checks` (`provider`, `status`, `reason`) — AI provider readiness outcomes; labels intentionally exclude endpoint URLs, API keys, model IDs, prompts, responses, provider request IDs, tenant/user IDs, and raw errors.
- `explore.ai.provider.requests` (`provider`, `outcome`, `failure_category`) — AI provider call outcomes; labels intentionally exclude tenant/user prompt content, selected reference content, raw tool payloads, model IDs, endpoint URLs, API keys, provider request IDs, and raw provider errors.
- `explore.ai.provider.request_duration` (`provider`, `outcome`, `failure_category`) — AI provider request duration histogram in seconds; labels intentionally use the same bounded dimensions as provider request counters.
- `explore.ai.provider.token_usage` (`provider`, `token_type`) — AI provider token usage histogram for input/output/total tokens; labels intentionally exclude prompts, responses, model IDs, endpoints, provider request IDs, tenant/user IDs, and provider errors.
- `explore.ai.provider.proposed_actions` (`provider`, `action_kind`) — aggregate count of provider-returned proposed actions; labels intentionally include only bounded action kinds such as `create_event_draft`, not raw tool arguments or proposal payloads.
- `explore.ai.retention.cleanup_runs` (`mode`, `outcome`) — scheduled AI retention cleanup pass outcomes in `dry_run` or `redact` mode; labels intentionally exclude tenant IDs, prompts, responses, provider IDs, and tool payloads.
- `explore.ai.retention.cleanup_rows` (`mode`, `category`) — bounded aggregate row counts for eligible/redacted AI retention cleanup categories; labels intentionally exclude tenant IDs and content-bearing identifiers.
- `explore.registration_providers.management_actions` (`action`, `outcome`) — bounded registration-provider management action outcomes such as reconciliation, manual import, retry, and resolve; labels intentionally exclude tenant IDs, event IDs, binding IDs, provider submission IDs, URLs, answers, PII, secret refs, raw payloads, and raw provider errors.
- `explore.storage.upload_sessions` (`provider`, `operation`, `outcome`, `failure_category`) — provider-neutral upload session create/finalize/cancel outcomes; labels intentionally exclude tenant IDs, user IDs, upload-session IDs, filenames, object keys, paths, endpoints, bucket names, access keys, secrets, and raw exception text.
- `explore.storage.upload_bytes` (`provider`, `outcome`, `failure_category`) — upload byte histogram for accepted/attempted provider writes; labels are bounded to provider and outcome categories.
- `explore.storage.reads` (`provider`, `outcome`, `failure_category`, `visibility`) — metadata-backed storage read outcomes after lifecycle and visibility checks; labels intentionally exclude storage-object IDs, object keys, paths, filenames, tenant IDs, user IDs, and raw provider errors.
- `explore.storage.read_bytes` (`provider`, `outcome`, `visibility`) — read byte histogram for successful metadata-backed provider reads.
- `explore.storage.deletes` (`provider`, `outcome`, `failure_category`) — provider-neutral blob delete plus metadata delete outcomes; labels intentionally exclude storage-object IDs, object keys, paths, filenames, endpoints, bucket names, and raw provider errors.
- `explore.storage.quota_reservations` (`provider`, `operation`, `outcome`, `failure_category`) — quota reserve/release/commit outcomes around upload sessions.
- `explore.storage.quota_bytes` (`provider`, `operation`, `outcome`) — byte histogram for quota reserve/release/commit operations.
- `explore.storage.reconciliation_runs` (`mode`, `outcome`, `failure_category`) — storage drift scan outcomes; labels intentionally exclude tenant IDs, storage-object IDs, object keys, filenames, paths, endpoints, bucket names, and raw provider errors.
- `explore.storage.reconciliation_objects` (`provider`, `category`, `action`, `outcome`, `failure_category`) — aggregate object decisions from reconciliation scans; labels are bounded to provider/category/action/outcome and intentionally exclude identifiers, paths, object keys, filenames, and secrets.
- `explore.storage.provider_tests` (`provider`, `outcome`, `failure_category`) — admin storage provider test outcomes; labels intentionally exclude local filesystem roots, S3 endpoints, bucket names, access keys, secrets, and raw exception text.

### Incoming Coop Effect Operations

`POST /api/integrations/moderation/coop/callback` acknowledges a valid decision callback only after the retained inbox row and unique effect pointer commit. Execution is asynchronous: the pointer worker uses a fenced renewable lease, loads the retained callback, invokes `ProcessCoopDecisionCallbackCommand`, and completes the pointer with an applied-effect receipt only after command success.

Operator sequence:

1. Check `/health/webhooks/coop-effects`. `Degraded` means processing is disabled, the due backlog reached `EffectBacklogWarningThreshold`, or stale leases reached `EffectStaleLeaseWarningThreshold`; `Unhealthy` means the PostgreSQL readiness query failed. The payload contains aggregate counts and settings only.
2. Query `GET /api/admin/incoming-webhook-effects/status?tenantId={tenantId}&limit=50` with an authorized operator identity. Inspect status, generation/fence, attempts, next-attempt/lease/terminal timestamps, and bounded failure evidence. Callback bytes, hashes, provider decision IDs, headers, and raw exceptions are deliberately unavailable.
3. Fix the underlying permanent condition before redrive. Follow the item HAL `redrive` relation only when present, then POST its `expectedProcessingGeneration` and a bounded operator reason. A stale generation, non-dead-lettered row, expired replay window, or missing retained payload fails closed.
4. Confirm the processing generation advanced, an audit event was appended, and the row later becomes `Completed`. Do not edit pointer status, fences, or receipts directly in PostgreSQL.

Incident controls:

- Set `Webhooks:IncomingProcessing:Enabled=false` and restart API replicas to pause the incoming/effect background loops. Intake remains durable. Re-enable only after checking accumulated backlog and database capacity.
- A cancelled or crashed worker leaves its active lease for fenced expiry recovery. Never manually clear a token or reuse a stale claim; a recovered claim receives a new token and higher fence.
- Retention cleanup cannot clear retained callback bytes while an effect is pending, failed, or processing. Completed/dead-lettered pointers permit payload cleanup only after the inbox payload-retention timestamp and replay window have expired. After cleanup, redrive is intentionally unavailable.
- Alert on `explore.webhooks.processing_outcomes{provider="coop",operation="incoming_effect"}`, `explore.webhooks.retries_scheduled`, `explore.webhooks.dead_letters`, and `explore.webhooks.provider_health_checks`. These labels are closed and PII-free; logs include bounded failure type/category only.
- Back up the retained inbox, effect pointer, applied-effect receipt, and webhook audit tables together. Restoring only part of this relationship can remove replay evidence or cause a settled command to appear pending.

### Registration Provider Operations

Registration-provider callbacks reuse the incoming webhook ledger with effect kind `registration.provider_submission`. The callback route acknowledges every non-oversize outcome with `202 Accepted`; operators diagnose completion through event-scoped provider health and reconciliation, not HTTP callback status.

Operator sequence:

1. Open Studio at `/studio/events/{eventId}/integrations` only when the event HAL exposes `manage-registration-channels` or `view-registration-provider-health`.
2. Check provider health rows for connection validity, callback age class, drift class, reconciliation lag, parked queue depth, and capability codes. The surface intentionally contains no answers, attendee PII, raw provider payloads, URLs, or secret refs.
3. Use HAL `poll` for reconciliation, `manual-import` for bounded storage/source metadata, and item `retry`/`resolve` only when the queue resource emits them. Retry requires retained effect identity and current processing generation; receipt conflicts and event/binding mismatches fail closed.
4. For browser embeds, verify the connection approved origin. The BFF emits a per-route CSP `frame-src` for the descriptor origin and rejects arbitrary iframe input; iframe navigation is display-only, so use status polling for completion.

### Development Application Migration Rebaseline

All development application-provider chains are rebaselined at PostgreSQL
`20260906223112_Init`, SQLite `20260906223113_Init`, SQL Server
`20260906223115_Init`, and the shared MySQL/MariaDB catalog
`20260906223116_Init`. These generated initials contain both complete Unicode
location search fields and relational ATProto transient authentication storage.
Existing development application
databases must be recreated; incremental upgrade from the former development
chains is intentionally unsupported. Data Protection and retained
privacy-erasure authority keep their independent histories and must not be
reset with the application catalog. All migration and snapshot files are
generated artifacts and must never be patched by hand.

The email-optional integration retains these initials and adds one natively
generated `EmailOptionalSelfHostingIntegration` tail to each application catalog.
It replaces the seven unapplied feature stages for credential operations,
administrator bootstrap, email controls, Local lifecycle operations, challenge
quotas, guest status, and anonymous retention. The final model retains all of
those effects alongside the upstream Unicode and relational ATProto baseline.
The separate external Identity Init and three feature migrations are unchanged;
Data Protection and privacy-authority catalogs are not consolidated.

The generated integration heads are PostgreSQL
`20260908213348_EmailOptionalSelfHostingIntegration`, SQLite
`20260908213357_EmailOptionalSelfHostingIntegration`, SQL Server
`20260908213409_EmailOptionalSelfHostingIntegration`, and shared MySQL/MariaDB
`20260908213419_EmailOptionalSelfHostingIntegration`.

This approved development consolidation removes six intermediate rollback
destinations. The generated Down boundary is the entire application feature back
to its retained upstream Init, not selective credential/email/retention rollback.
It is not a retained-data recovery strategy: it drops receipts, control metadata
and deadlines, and restoring the old bootstrap provider constraint can fail when
a Local bootstrap row remains. Use forward correction or a tested matching
backup/binary recovery while preserving newer independent erasure facts.

Do not rewrite history-table rows to make a database with retired Init or feature
IDs appear current. Such development stores require an explicitly disposable
rebuild or the matching recovery procedure. When applying the new tail to a
retained database already at the supported Init, check for duplicate nonnull
normalized Local emails before the unique-index transition; the migration does
not select a winner or deduplicate identities. Existing policy revisions start
at zero; absent guest/privacy deadlines remain null, with no capability or
historical PII-window backfill.

Verify a provider change through the generated lifecycle before starting an
application host:

1. Apply the provider's complete application catalog to an empty database and
   require exact ordered equality between available and applied migration IDs.
2. Run the idempotent application again. Roll back to zero only in the
   explicitly disposable generated development lifecycle lane.
3. Reapply the complete catalog and run `has-pending-model-changes`.
4. Repeat the independent Data Protection lifecycle.
5. For PostgreSQL retained authority, verify the standalone and co-located
   generated histories independently; SQLite embedded authority has its own
   history.
6. Run the real provider behavior and lock contracts documented in
   [TESTING.md](TESTING.md#persistence-hardening-verification).

If apply, rollback, or pending-model verification fails, preserve the generated
SQL and provider logs without credentials or parameter values. Fix the entity,
configuration, provider primitive, or migration generator; then regenerate the
affected unapplied development migration. Never repair a generated migration or model
snapshot manually. Recreate only the disposable application database selected
for the development lane—do not delete Data Protection or retained-authority
catalogs as collateral recovery.

### Location Unicode Runtime Compatibility

Location search now stores complete Unicode NFC → invariant uppercase → NFC values
(revision 2), replacing scalar-token text. SQL Server uses `nvarchar(2000)`;
MySQL/MariaDB use `utf8mb4` binary collation. The original name/address limit is
500 UTF-16 code units; derived output is bounded separately at 2,000. Invalid
input fails before mutation, including SQLite writes. See [DOMAIN.md](DOMAIN.md#location-unicode-search-text)
for matching and privacy semantics.

For this pre-release migration rebaseline, stop application writers and select a
disposable application catalog or take a matching backup before recreating it.
Never delete a shared database volume. Preserve the independent Identity,
Data Protection, and retained privacy-authority histories; identify their topology
before resetting an application schema/file. Run `Event.MigrationService` twice
against the new application target, require exit 0 both times, verify the selected
application migration ID and no pending model changes, and complete the five-engine
Unicode/authority/erasure corpus before allowing readers or writers to resume.

For later SDK/runtime, ICU, NLS, operating-system, or globalization-profile upgrades:

1. Pin the candidate runtime and deployment globalization profile. Compare focused
   Unicode semantics and authorized match membership against the currently deployed
   profile; a passing build or unchanged revision number is insufficient.
2. Stop all old-profile readers and writers. Run an explicitly authorized current-key
   rebuild through the aggregate lifecycle, or recreate a disposable development
   application catalog. There is no automatic rebuild endpoint or rolling mixed-profile
   writer support. Never repair erased PII, bypass authority, or export raw derived keys.
3. Re-run the focused corpus and provider-local repeat/limited ordering checks before
   reopening traffic. Record profile, generated migration IDs, result counts, and
   query-plan/timing observations without text parameters or connection strings.

Recovery requires the matching binary/profile plus its matching backup, or recreation
of the selected disposable application catalog. Reverting a commit alone cannot
restore deleted data or a previous normalization profile.

### Promotion Code Operations

Promotion lookup is keyed, versioned, and fail-closed. `Promotions:CodeLookup:ActiveKeyVersion` selects the `v{version}` instance binding for new publish and organizer code-rotation writes. Application reads query the distinct key versions used by active codes in the event/catalog scope and compute candidates with every corresponding qualified key; there is no secret-source fallback.

HMAC trust-root rotation runbook:

1. Generate at least 32 random bytes, encode them as standard Base64, store them at a distinct external source coordinate, and provision a new instance `promotions.code_lookup_hmac_key` binding under the next qualifier without altering the old binding or its source value.
2. Change `Promotions:CodeLookup:ActiveKeyVersion` to that positive version and restart API replicas. New publish and organizer code-rotation writes now pin the new version.
3. Exercise one controlled create/apply path. Observe only success/failure and masked display labels; never put the key, raw promotion code, lookup digest, binding coordinates, tenant/event IDs, or internal code IDs in logs, tickets, screenshots, metrics, health output, or support artifacts.
4. Query authoritative administrative/database state for active code rows grouped by `LookupKeyVersion`. Remove an old qualified binding only after its active count is zero. Overwriting an existing version's key destroys lookup compatibility and is not rotation.

Organizer code rotation and definition revocation are different controls. `rotate-code` retires the currently active code row, creates a replacement under the active HMAC-key version, and returns the replacement plaintext once; subsequent management reads remain masked. `revoke` has no meaningful request body or caller timestamp: the server `TimeProvider` records an immediate decision that blocks new redemption. It does not rewrite previously accepted orders, reservations, or pricing snapshots. Operators should use the exact HAL action exposed by Studio and must not edit code, digest, active, retirement, reservation, or redemption rows directly.

Failure behavior is intentionally bounded. An invalid attendee code, ineligible/expired/revoked definition, exhausted limit, or conflicting reservation produces the same generic unavailable outcome. A missing qualified key, invalid Base64, or fewer than 32 decoded bytes prevents the keyed operation rather than falling back or exposing comparison detail. Restore the exact qualified binding or roll the configured active version back to a still-provisioned key; do not regenerate a value under an existing qualifier.

Checkout displays the server snapshots for pre-discount organizer amount, promotion discount, post-discount organizer amount, platform fee, voluntary contribution, and final total separately. When the final total is zero and the order resource emits `finalize`, authenticated or capability-scoped guest checkout finalizes through the registration-order lifecycle without a payment-provider call. A positive total uses the durable Phase 18 start/status/Checkout/reconciliation contract and never treats browser return as success.

### Payment Checkout And Reconciliation Runbook

1. Check `/health` for `payment-reconciliation`. `configurationBlocked` normally means `PublicBaseUrl`, Stripe mode/secrets, provider identity, or organizer connection state is incomplete; correct configuration before retrying user checkout.
2. Confirm `payment-reconciliation-drain` is scheduled every 30 seconds and inspect its aggregate claimed/succeeded/nonterminal/unknown/parked/stale log fields. Do not query or publish provider/account/order identifiers in general diagnostics.
3. For Split topology, verify Redis independently. General cache degradation may fall back to memory, but payment checkout-ticket issue/consume deliberately fails closed. Existing provider attempts still reconcile in the API.
4. For `Unknown`, preserve the attempt and exact persisted idempotency identity. Restore provider/API connectivity and let the drain retrieve authoritative Checkout and PaymentIntent state; never create a replacement blindly or mark success from a browser return.
5. For `parked`, `duplicateSucceededOrders`, or money/identity mismatch codes, stop new paid sales for the affected scope and investigate authoritative database plus Stripe Dashboard evidence under controlled access. Do not edit payment rows or emit raw provider payloads into support artifacts.
6. To roll back new sales, disable paid publication/Checkout creation while leaving signed webhook intake and the reconciliation job running until retained attempts settle. Free-event finalization remains provider-free.

### Support Access Operations

Support access is an operator-governed, actor-preserving session model. Browser clients never own support authority; the BFF forwards only a server-owned `X-Support-Access-Session-Id`, and the API revalidates the persisted session, actor, tenant, expiry, mode, and governance settings on each forwarded request.

Operational controls:

- Kill switch: set `support_access.enabled=false` to deny new starts and reject existing forwarded support-access use. Existing BFF stored session references become inert because API validation fails closed.
- Write shutdown: set `support_access.allow_write_mode=false` to reject new write sessions and reject existing forwarded write sessions.
- Emergency revocation: use the support-access force-stop endpoint/action for active sessions. Revocation writes a durable audit event attributed to the operator who force-stopped the session.
- Retention and backup: support-access session and audit-event tables are security evidence. Do not include them in ephemeral cleanup jobs; include them in the normal database backup/restore plan and define retention through governance before purging historical evidence.

Alert-worthy structured logs:

| Event | Signal |
|---|---|
| Write-capable session started | Warning from `StartSupportAccessSessionCommandHandler`; investigate ticket/reference and expiry. |
| Force-stop completed | Warning from `ForceStopSupportAccessSessionCommandHandler`; this is an emergency revocation path. |
| Kill switch denied forwarded use | Warning from `SupportAccessSessionService` plus `explore.support_access.session_validation_denials{reason="support_access_disabled"}`. |
| Cross-tenant mismatch denied | Warning from `RuntimeAuthorizationProvider` plus `explore.support_access.authorization_boundary_denials{reason="support_access_target_tenant_mismatch"}`. |
| Audit persistence failed | Warning from `SupportAccessAuditMiddleware` plus `explore.support_access.request_audits{persistence_outcome="failed"}`. |

Trace tags on active support-access requests use bounded support context such as `support_access.active`, `support_access.mode`, `support_access.allows_writes`, and `support_access.was_forwarded`. They intentionally do not carry ticket text or reason text.

### Notification Fanout Operations

Two fanout paths currently coexist. The legacy event-published actor-subscription path remains an internal outbox side effect:

1. The event publish command writes an internal `EventPublishedNotificationFanoutRequested` outbox row in the same transaction as the event status change.
2. `OutboxProcessor` claims pending rows and calls `CompositeOutboxMessageDispatcher`.
3. The composite dispatcher routes `EventPublishedNotificationFanoutRequested` to `EventPublishedNotificationFanoutService`; retired external `EventPublished` broker rows fail closed as unknown outbox event types.
4. The fanout service creates or resumes `NotificationFanoutRun`, scans active organization/group actor subscriptions for active tenant-local users, skips existing `Notification.DeduplicationKey` values, creates durable in-app notification rows, and marks the run completed or failed.

The recipient-occurrence path handles attendee lifecycle fanout:

1. A business mutation persists one immutable `NotificationFanoutOccurrence` and its generic-outbox pointer in the mutation transaction.
2. Pointer handoff creates the corresponding pending `NotificationFanoutRun`; handoff is never suppressed by backlog pressure.
3. `NotificationFanoutProcessor` acquires a fair round under the global PostgreSQL claim lock. Global and per-tenant active limits are rechecked before each exact claim under global → tenant → event-precedence → occurrence lock order.
4. Every claim runs in a fresh dependency-injection scope through `NotificationFanoutPageProcessor`. The processor renews the fenced lease, reads a deterministic attendee page, atomically materializes recipient notification/delivery/email work, and advances the compound timestamp/user cursor only after the page commits.
5. A crash leaves the lease and last committed cursor durable. After expiry, another replica advances token, fence, and generation and resumes without skipping the uncommitted page.

Optional-reminder backpressure is durable and cross-replica. Under the global claim lock, the repository counts active non-reminder occurrences and updates the singleton `notification_fanout_processor_states` hysteresis row. At the high watermark, reminder claims stop while core work remains eligible; at or below the low watermark, reminders resume. Reminder occurrences/runs are retained and are never marked superseded merely because the queue is pressured.

Operator signals:

| Signal | Meaning |
|---|---|
| `explore.notifications.fanout_runs` | Legacy run outcomes by a closed fanout-kind/outcome vocabulary. Tenant IDs are not metric labels. |
| `explore.notifications.fanout_subscribers` | Aggregate processed, notification-created, and duplicate-skipped subscriber decisions. |
| `explore.notifications.fanout_processor.claims` | Recipient-occurrence claimed, completed, stale-claim, lease-contention, capacity-deferred, unavailable, and failed counts. |
| `explore.notifications.fanout_processor.recipients` | Aggregate processed and notification-created recipient counts without recipient or tenant labels. |
| `explore.notifications.fanout_processor.*` gauges | Due/core/reminder occurrences, active/expired claims, processed recipients for unfinished runs, superseded occurrences, oldest due age, and durable reminder deferral. |
| `notification-fanout` readiness | Safe aggregate counts and thresholds; degraded for disabled processing, expired claims, excessive due backlog, or excessive oldest-due age. |
| `NotificationFanoutRun` rows | Durable worker cursor/count/status state for source event/actor/kind tuples. |
| `NotificationFanoutOccurrence` rows | Immutable occurrence snapshots plus pending/superseded business authority. |
| `notification_fanout_processor_states` | Cross-replica optional-reminder hysteresis authority. |
| General outbox dead-letter rows | Internal fanout messages that exceeded retry policy and need inspection/replay decisions. |
| Structured fanout logs | Aggregate round/failure messages only; do not include tenant/event/occurrence/run/recipient IDs, template payloads, addresses, titles, bodies, or deduplication keys. |

Fanout is at-least-once. Operators should treat the occurrence/run fence, recipient intent uniqueness, delivery uniqueness, email outbox uniqueness, and `Notification.DeduplicationKey` as duplicate-prevention authorities rather than inferring success from logs. `remainingOccurrenceCount` means due plus active occurrences; the system intentionally does not run a full recipient-audience count merely for health reporting.

### Notification SSE Refresh Operations

`GET /api/notification/stream` is a long-lived authenticated HTTP response that sends one-way SSE refresh hints to the browser notification bell.

Operational expectations:

| Concern | Guidance |
|---|---|
| Delivery truth | SSE is only a refresh hint. Durable `Notification` rows and authenticated list/detail/unread APIs remain the source of truth. |
| Payload safety | Hints include unread count, unread flag, bounded reason, and timestamp only. Do not include notification body, title, entity IDs, user IDs, deduplication keys, or PII. |
| Authentication | Browser `EventSource` uses same-origin cookies through the BFF/API boundary. Do not rely on custom request headers for the stream. |
| Proxy behavior | Do not buffer `text/event-stream`. The API sets `X-Accel-Buffering: no`; reverse proxies should preserve streaming responses. |
| Compression | Do not add `text/event-stream` to response compression MIME types. Compression can delay SSE frames through buffering. |
| Reconnect | Browser `EventSource` reconnects automatically. The endpoint emits SSE IDs and a reconnect interval; polling remains the fallback. |
| Shutdown | The endpoint honors request cancellation. Long-lived streams should end when the client disconnects or the host shuts down. |

No additional configuration keys were added for SSE refresh hints in this implementation slice.

### Basic Email Dispatch Operations

#### Delivery policy revocation

SMTP administration uses a read-only impact preview followed by a separately
authorized disable command. `PreviewEmailDeliveryDisableQueryHandler` resolves
current persisted administrator grants and reads effective policy through
`EmailDeliveryDisableImpactReader`. The preview compares the actual instance/tenant
policy with the proposed disable; independently enabled tenant-owned transports
are excluded from an instance disable's affected scopes.

`EmailDeliveryDisableTokenService` uses existing ASP.NET Core Data Protection with
a separate purpose and five-minute lifetime. Its protected digest binds the actor,
target, revision, lock state and ordered affected-scope revisions; no preview token
is persisted. `DisableEmailDeliveryCommandHandler` rechecks authority and impact
under the ordered SMTP mutation lock and serializable unit of work, then delegates
the confirmed mutation to `IEmailDeliverySettingsWriter`. Setting, revision and
optional-work suppression commit together; cache notifications follow commit.
Generic single, batch and reset writes cannot bypass deliberate disable, and
configuration manifests continue to exclude SMTP policy.

The API publishes `disable-preview` and `disable` HAL relations and private,
no-store responses. A stale revision, changed impact or invalid confirmation cannot
mutate policy. Preserve SMTP values, delegation locks, operator pauses and rate
state when disabling. A handoff admitted before disable commits may still finish;
the policy fence prevents new admission, not recall of an accepted message.
Optional historical work stays suppressed after re-enable. Required work resumes
only from eligible capability parks; operator holds and `Unknown` acceptance
remain separately controlled. Retired `TenantAdministratorInvitation` rows always
take the existing transactional skip/redaction path, even after administrator
authority and SMTP capability return.

The native `EmailDeliveryControl` migrations add primary-store policy/source
revisions, suppression watermarks and typed park provenance with constraints.
They do not change external Identity schemas. Apply the generated migration for
the selected primary provider; never hand-edit migrations or snapshots. Reverting
these migrations removes fence metadata and is not a queue-replay recovery action.
Keep application code and its generated schema together during rollback.

Registration confirmation email is handled as a durable side effect:

1. The registration command creates an `EmailDispatchOutbox` row in the same primary-database transaction as registration state.
2. The Quartz `email-dispatch-drain` job triggers the shared drain service on every supported primary provider, including SQLite. `EmailDispatchProcessor:Mode=HostedService` remains available as a scheduler-free timer over the same service.
3. Batch, Quartz, hosted-service, and RabbitMQ pointer paths enter the same atomic claim operation. Provider-specific database locking applies the instance drain pause, fair tenant rounds, required-work priority, paused-tenant exclusion, and global/per-tenant `Processing` ceilings without incrementing `AttemptCount`. Dispatch-time eligibility rechecks the instance pause to close the claim-to-provider race.
4. The conditional eligibility transition rechecks current authorization, consent, preference, and verified address, then reserves both persisted SMTP buckets against the database clock. Rate deferral releases the lease without creating an attempt, receipt, or provider fence.
5. An admitted transition atomically decrements the global and tenant buckets, increments `AttemptCount`, and creates the processing receipt plus `provider_handoff_started` attempt fence before SMTP I/O.
6. SMTP is called through `IEmailService`; handlers and controllers do not send SMTP, publish RabbitMQ, or schedule Quartz jobs directly.
7. Provider acceptance, provider failure, and acceptance reconciliation use tenant/outbox/lease/attempt-fenced transactions to align `EmailDispatchOutbox`, `EmailDispatchAttempt`, `EmailDispatchReceipt`, and `NotificationDelivery`.
8. The Quartz `email-dispatch-recovery-scan` job returns stale unfenced claims to `RetryScheduled` and marks only fenced or partially fenced provider uncertainty `Unknown`; the hosted-service fallback runs the same recovery scan before each drain loop.

When an absolute public base URL is configured through `PublicBaseUrl`, `App:PublicBaseUrl`, or `Application:PublicBaseUrl`, categorized lifecycle messages include `List-Unsubscribe`, `List-Unsubscribe-Post: List-Unsubscribe=One-Click`, and a visible unsubscribe URL appended to the plain text and HTML bodies. Public launch deployments should configure the public base URL; otherwise the dispatch path still sends allowed email, but it cannot emit absolute unsubscribe links.

Operator signals:

| Signal | Meaning |
|---|---|
| `email-dispatch` health check | Selected dispatch mode, safe settings, persisted optional-reminder deferral, and active non-paused aggregate counts including due, retry, stale processing, unknown, parked, and dead-lettered rows. Parked rows are informational; unknown rows degrade at the configured threshold. |
| `explore.email_dispatch.attempts` | Provider-handoff outcome counter for sent, unknown, retry-scheduled, and dead-lettered attempts. Closed-vocabulary labels omit tenant identity. |
| `explore.email_dispatch.operational_outcomes` | Eligibility-skip and SMTP-rate-deferral counter. These outcomes do not claim provider I/O occurred. |
| Scheduler status endpoint | Optional instance-admin-only, read-only scheduler internals at `Scheduler:Quartz:StatusEndpointPath`. It is disabled by default and is not the product/operator source of truth for email delivery state. |
| Scheduler administration API and admin UI | Optional instance-admin surface at `/api/admin/scheduler`, rendered by the **Background Scheduler** section under Instance Settings. Enabled with `Scheduler:Quartz:AdminApiEnabled`; read-only until `Scheduler:Quartz:AdminApiReadOnly=false`. Works in both split and standalone topologies. |
| Quartz.NET dashboard | Optional upstream dashboard at `Scheduler:Quartz:DashboardPath` (default `/quartz`), available only in the combined `Event.Standalone` host. Enabled with `Scheduler:Quartz:DashboardEnabled`. |
| Structured drain logs | Include dispatch/outbox IDs, tenant IDs, outcomes, retry delay, and normalized failure category; do not include bodies, recipients, subjects, secrets, provider message IDs, or raw SMTP error text. |

Timeout-like SMTP outcomes are recorded as `Unknown` instead of blind retry. Use the HAL `reconcile` action only after provider evidence supports `Delivered` or `NotDelivered`; the transaction aligns outbox, current attempt, receipt, and notification delivery. Generic replay excludes `Unknown`, while `resolve-without-replay` remains the explicit unresolved/abandon path. Skipped rows are terminal.

Crash-window recovery follows the durable handoff evidence. If a node dies after claim but before `provider_handoff_started`, the scan clears the exact lease and schedules an immediate safe retry with `processing_lease_released`; no SMTP attempt was consumed. If the current attempt has the provider fence, or a processing receipt shows a partial fence, recovery atomically marks the outbox/current attempt/receipt/delivery graph `Unknown` with `processing_lease_expired`. Operators must reconcile fenced uncertainty before replay. Recovery selects bounded rows with `FOR UPDATE SKIP LOCKED` and never infers SMTP success from scheduler or RabbitMQ state.

The durable `EmailDispatchOutbox` drain is the single retry authority. Expected SMTP/provider outcomes are caught by the drain service and persisted in `EmailDispatchOutbox`; only unexpected infrastructure failures bubble to the scheduler as failed job executions. One-off reminder triggers are deliberately not retried by the scheduler: a failed wake-up leaves the outbox row due, and the next `email-dispatch-drain` pass picks it up.

Quartz operational state lives in the primary application database under the `Scheduler:Quartz:TablePrefix` prefix (default `QRTZ_`). These are raw ADO tables created by embedded, idempotent DDL, not EF Core migrations, so there is no second `DbContext` and no second migration chain. The same table set works on PostgreSQL, SQLite, SQL Server, MariaDB, and MySQL.

Status-endpoint protection is enforced twice: authorization middleware challenges or forbids on the configured path before any scheduler state is read, and the mapped endpoint additionally requires the instance-admin policy. If `Scheduler:Quartz:StatusEndpointEnabled=false`, the route is not exposed at all.

#### Scheduler administration surfaces

Three independent operator surfaces exist over the same scheduler; all are disabled by default.

`InstanceSchedulerSection` handles generated-client read errors (including HTTP
429 and 503) by clearing stale overview/job data and displaying a retry message.
Manual Refresh reloads the overview and job list without issuing a scheduler
mutation. Existing rate limits and HAL control authorization remain authoritative.

- **Status endpoint** — one read-only JSON document for scripted checks. No UI.
- **Administration API and admin UI** (`Scheduler:Quartz:AdminApiEnabled`) — the portable surface, available in both
  split and standalone topologies. It is a normal versioned HAL controller under `/api/admin/scheduler`, authorized as
  instance-setting `View` for reads and `Update` for controls, and rendered by the **Background Scheduler** section in
  Instance Settings. Operators get scheduler lifecycle state, the job table with trigger states and next/previous fire
  times, and pause/resume/run-now actions. `Scheduler:Quartz:AdminApiReadOnly` defaults to `true`: control links are
  withheld from HAL and mutating requests are refused before the scheduler is touched, so the UI cannot offer a control
  that the API would then reject. When the API is disabled every route answers `404` and the settings section does not
  appear at all, because the client discovers the section from the served resource rather than from local claims.
- **Quartz.NET dashboard** (`Scheduler:Quartz:DashboardEnabled`) — the upstream Blazor dashboard, mounted only by the
  combined `Event.Standalone` host, where Razor components and the scheduler share a process. The split-mode API host
  has no Razor infrastructure and ignores the flag. It is mounted self-contained under its own root component, so it
  does not participate in the application's client router, and its paths sit outside the API-owned route set and
  therefore authenticate through the Blazor cookie pipeline rather than the bearer API bridge.

Two recovery actions exist alongside the lifecycle controls, and they appear only for the state they repair. A job
whose triggers have entered the scheduler's error state offers **clear error state**, which returns those triggers to
normal firing — nothing else clears that state, so a job left in it stops running silently. A currently executing job
offers **request cancellation**, which signals the running job's cancellation token; this is cooperative, so a job
that does not observe cancellation continues to completion. Either action reports
`scheduler_action_not_applicable` when the job's state moved on before the request arrived, rather than reporting a
success that changed nothing.

Pausing the scheduler moves it to standby rather than shutting it down: running jobs finish, no further triggers fire,
and the scheduler can be resumed in-process. A shutdown scheduler would need a host restart, turning a routine pause
into an outage. Pausing an individual job pauses its triggers without removing its schedule, and triggering a job runs
it once immediately while leaving its schedule untouched.

Changing anything in this section is governed by the **`schedule-background-work`** intent in
[`.agents/contract/intents.yaml`](../../.agents/contract/intents.yaml). It answers the contract's eight
questions for scheduler work and encodes the invariants that have already caused defects here: Quartz types
stay inside `Explore.API`, scheduler payloads stay pointer-only, scheduler tables are raw ADO rather than EF
migrations, scheduler DDL is never destructive, cron expressions use Quartz's `?` day rule, and a missing
optional column degrades silently rather than loudly.

The scheduler job catalog is Application-owned through `IScheduledJobRegistry`. Current implemented jobs are:

| Job | Schedule type | Payload | Source of truth |
|---|---|---|---|
| `email-dispatch-drain` | Cron `*/10 * * * * ?` (every 10 seconds) | None | `EmailDispatchOutbox` pending/retry state |
| `email-dispatch-recovery-scan` | Cron `0 */1 * * * ?` (every minute) | None | Stale `EmailDispatchOutbox` processing leases |
| `event-reminder-dispatch` | One-off time trigger | Pointer-only IDs | Pre-persisted `EmailDispatchOutbox` row |
| `idempotency-cleanup` | Interval, `IdempotencyCleanup:PollingIntervalMinutes` | None | Expired `idempotency_records` |
| `atproto-transient-cleanup` | Interval, fixed 1 minute | None | Expired ATProto transient records and assertion-replay claims; remains enabled when ATProto login is disabled |
| `ai-retention-cleanup` | Interval, `AiRetentionCleanup:PollingIntervalMinutes` | None | Per-tenant `ai_assistant.retention_days` |
| `email-dispatch-retention-cleanup` | Interval, `EmailDispatchRetention:PollingIntervalMinutes` | None | Email dispatch content retention horizon |
| `webhook-retention-cleanup` | Interval, `WebhookRetention:PollingIntervalMinutes` | None | Webhook message/attempt retention horizon |
| `registration-retention-cleanup` | Interval, fixed 1 day | None | Immutable per-tenant registration retention deadlines |
| `storage-reconciliation` | Interval, `StorageReconciliation:PollingIntervalMinutes` | None | Storage object state vs. provider |
| `privacy-erasure-credential-cleanup` | Interval, `PrivacyErasure:ProviderPollingInterval` | None | Expired provider credentials/locators |
| `organizer-payment-readiness-reconciliation` | Interval, `OrganizerPaymentReadinessReconciliation:PollingIntervalSeconds` | None | Stale organizer payment connections |
| `payment-reconciliation-drain` | Cron `*/30 * * * * ?` (every 30 seconds) | None | Durable Checkout dispatch and payment-reconciliation effects |
| `fair-return-orchestration` | Cron `*/15 * * * * ?` (every 15 seconds) | Optional effect UUID only | Durable replacement-settlement and refund-intent effects |
| `inventory-hold-expiry` | One-off time trigger, per order | Pointer-only IDs | The order's earliest `RegistrationInventoryHold.ExpiresAt` |
| `inventory-hold-expiry-reconciliation` | Cron `0 */5 * * * ?` (every 5 minutes) | None | Expired active holds and hold-expiry recovery targets |
| `registration-finalization-drain` | Cron `*/10 * * * * ?` (every 10 seconds) | None | Durable registration-finalization effect claims |
| `integration-sync-drain` | Interval, `IntegrationSyncProcessor:PollingIntervalSeconds` | None | Tenant-bound integration synchronization and ambiguity parking |
| `local-webhook-delivery-drain` | Interval, `WebhookDeliveryProcessor:PollingIntervalSeconds` | None | Stale recovery then Local-provider delivery |
| `incoming-webhook-intake-drain` | Interval, `Webhooks:IncomingProcessing:PollIntervalSeconds` | None | Verified incoming webhook claims |
| `incoming-webhook-effect-drain` | Interval, `Webhooks:IncomingProcessing:PollIntervalSeconds` | None | Durable incoming effect pointers |
| `webhook-bulk-replay-drain` | Interval, `WebhookBulkReplay:PollingIntervalSeconds` | None | Bounded queued bulk replay |
| `webhook-provider-publication-drain` | Interval, `WebhookProviderPublicationProcessor:PollingIntervalSeconds` | None | Provider publication then reconciliation |
| `pds-sync-drain` | Interval, `Atproto:PdsSync:PollingIntervalSeconds` | None | Fenced AT Protocol PDS delivery |

Planned-only jobs are `dead-letter-summary`, `waitlist-promotion-scan`, and `tenant-maintenance-scan`. General outbox remains the explicit hosted-service exception and has no Quartz catalog identity.

`payment-reconciliation-drain` performs a dispatch/reconcile/dispatch pass. Missing or invalid `PublicBaseUrl` defers only new Checkout handoff; provider reconciliation still runs. Keep the scheduler enabled after disabling paid sales so retained attempts and late signed evidence can settle.

`fair-return-orchestration` claims at most 10,000 effects per pass using stable cursor order and bounded round-robin tenant selection. Its Quartz payload may contain only `effect_id`; payment, refund, participant, provider payload, and bearer data remain durable and are never scheduled. The health check publishes fixed pending, processing, unknown, dead-letter, and oldest-age fields. Keep this job running after stopping new waitlist allocation so ambiguous replacement payments and refunds can reconcile forward.

For an IntegrationSync row reported as ambiguous by `/health` under `queue-drains`, establish provider evidence before acting. Use the tenant-authenticated `POST /api/integrations/listmonk/queue/{outboxId}/resolve` endpoint with an opaque incident/evidence reference. `ConfirmAccepted` settles without replay; `RetryDefinitelyNotAccepted` schedules a retry only after proof the provider did not accept the POST; `DeadLetter` preserves the terminal refusal. Never select retry from timeout or response-loss evidence alone.

#### Upgrade note — maintenance sweeps moved to the scheduler

The eight maintenance sweeps above previously ran as in-process `BackgroundService` timer loops. They now run as
Quartz jobs. **No configuration key changed**: each sweep still reads the same section, the same `Enabled`
flag, and the same interval value, so an existing `appsettings` or environment configuration keeps working
unchanged.

What changes for operators:

- **Log lines.** Each sweep previously logged its own start/stop and per-loop messages. Completion is now
  logged uniformly as `Scheduled job {JobName} completed.` with `JobName` set to the identifier in the table
  above. Alerts or log queries that matched the old per-worker text must be repointed at `JobName`.
- **Disabled sweeps are absent, not idle.** A sweep whose `Enabled` flag is false is no longer registered with
  the scheduler at all, so it does not appear in the scheduler status endpoint. Previously it started and
  immediately returned.
- **Schedule state now survives restarts.** Trigger state lives in the `QRTZ_` tables, so a restart resumes the
  existing cadence instead of restarting every interval from zero. Missed occurrences during downtime collapse
  into a single next run rather than replaying one pass per skipped interval.
- **`Scheduler:Quartz:Enabled=false` now also disables these sweeps.** They are scheduler jobs, so turning the
  scheduler off turns them off. Operators who disable the scheduler must confirm they intend retention and
  reconciliation to stop.
- **Clustering.** With `Scheduler:Quartz:ClusteringEnabled=true`, each sweep runs on exactly one node instead of
  on every node, which removes the duplicate-work that the old per-process loops caused in multi-node
  deployments.

`OutboxProcessor` remains deliberately hosted. Queue-driven webhook, integration-sync, and PDS cadence now
runs under Quartz; their fencing, retry, tenant, ambiguity, and settlement semantics remain in the same
scheduler-neutral services and durable repositories.

#### Upgrade note — registration finalization drain moved to the scheduler

`RegistrationFinalizationWorker` was a `BackgroundService` polling every 10 seconds. It is now the
`registration-finalization-drain` cron job on the same 10-second cadence. **Only the timer moved**: the job
sends the identical `DrainRegistrationFinalizationEffectsCommand`, so the fenced claim, batch size (100), and
lease duration (60s) are unchanged, and `[DisallowConcurrentExecution]` preserves the old loop's guarantee
that a slow pass delays the next rather than overlapping it.

The one operator-visible change is the claim's lease owner, which is now
`registration-finalization-drain-job` instead of `registration-finalization-worker`. Queries or dashboards
matching the old owner string must be repointed. This worker was chosen as the first drain migration
precisely because its loop carried no logic of its own; the remaining queue drains keep their loops until
this pattern has run in production.

#### Scheduled job telemetry

Every scheduled job — existing, migrated, or added later — is observed by one `IJobListener`
(`SchedulerTelemetryJobListener`) rather than by per-job logging:

- `explore.scheduler.job_executions` — counter labelled `job_name`, `job_group`, and `outcome`
  (`succeeded` / `failed` / `vetoed`).
- `explore.scheduler.job_duration` — histogram of execution seconds labelled `job_name` and `job_group`.

`job_name` is collapsed to the `ScheduledJobNames` catalog; anything else reports as `other`, so an ad-hoc job
cannot grow metric cardinality without bound. Labels deliberately carry **no tenant identity and no payload
values** — a job's pointer identifies a tenant and an aggregate, and metric labels are exported and retained
far more widely than logs. A `vetoed` execution never ran, and is counted separately so a trigger listener
suppressing a job cannot look like that job running healthily.

Every listener method is exception-contained: Quartz documents that an unhandled listener exception can
disrupt the scheduling cycle, so a telemetry fault degrades to a missing metric rather than to a scheduler
that silently stops firing every job in the process.

#### Inventory-hold expiry — deadline plus sweep

Registration capacity holds used to be released by a worker that polled every 60 seconds, so held inventory
could stay unsellable for up to a minute past its expiry. That is now two jobs, and both are required:

- **`inventory-hold-expiry`** is a one-off trigger registered when an order is created with holds, due at the
  order's earliest hold expiry. It releases that one order's due holds and runs lifecycle recovery. It gives
  punctuality — capacity returns to sale at the deadline rather than on the next poll.
- **`inventory-hold-expiry-reconciliation`** is the correctness guarantee. It sweeps expired active holds
  *and* hold-expiry recovery targets, catching three cases the trigger structurally cannot: holds that
  pre-date the deployment and so have no registered deadline, deadlines lost with their scheduler row, and
  orders that need lifecycle recovery after an interrupted expiry and never had a hold deadline at all.

Because the trigger handles the punctual case, the sweep runs every five minutes rather than every minute.
**Do not remove the sweep on the grounds that deadlines are precise** — precision and coverage are different
properties, and only the sweep provides the second.

Deadline registration is deliberately best-effort: it happens after the order-creation transaction commits,
and a scheduler failure is logged and swallowed rather than failing the order, because the sweep still covers
the order. Deadlines are keyed per order and withdrawn when an order reaches a terminal state (`Confirmed`,
`Rejected`, `Expired`, `Cancelled`), which is what keeps `QRTZ_TRIGGERS` from accumulating one dead row per
completed order. An orphaned deadline that does survive is harmless: it fires once, finds no due hold, and
stops.

### Lifecycle-Email Operations

The selected primary database remains the email delivery ledger. Parent-aware content retention is implemented by the `email-dispatch-retention-cleanup` Quartz job (`EmailDispatchRetentionCleanupJob`): it runs bounded transactional passes, supports dry-run, and records only counts and cutoff timestamps in summary logs. Per-tenant failure warnings retain only the exception type, never the exception object or provider message. Failed tenants remain counted, cancellation still propagates, and this diagnostic boundary does not change transaction or retry behavior.

- Sent and skipped content redacts after the configured 180-day default; attempt and receipt free text/provider IDs follow the selected parent in the same transaction.
- Dead-lettered, `Unknown`, and parked replay material remains until its explicit resolution timestamp, then follows the same retention clock. `ContentRedactedAt` permanently removes replay authority.
- A `Purged` tenant is eligible immediately. Non-sent work and related delivery/receipt state become typed `tenant_deleted` skips before only non-PII ledger metadata remains.
- Run with `EmailDispatchRetention:DryRun=true` first when changing retention policy. Compare the bounded eligible count, then restore mutating mode. Repeated passes are idempotent because already-redacted parents are excluded.

The lifecycle dispatch foundation now processes bounded batches with fair tenant rounds, cross-replica concurrency ceilings, persisted global/per-tenant SMTP buckets, required-work priority, and persisted high/low optional-reminder hysteresis. Rate deferral occurs before attempt/fence creation and therefore consumes no SMTP attempt budget.

Implemented operator controls now include authenticated HAL-gated instance pause/resume, bounded global SMTP-rate override/clear, tenant suppression, replay/park/resolve, two-outcome `Unknown` reconciliation, and retention dry-run. Remaining lifecycle release requirements are:

- expose fanout oldest-pending age, progress, and lease-contention telemetry without recipient PII;

Eligibility and the occurrence/version fence are checked in the conditional provider-handoff transition. Before that transition, cancellation, consent withdrawal, preference, deletion, and supersession can skip work. After it, in-flight cancellation is not promised; I/O/protocol/process/persistence uncertainty settles as `Unknown` and is never automatically resent.

### Optional RabbitMQ Dispatch Operations

RabbitMQ Dispatch Mode is optional transport infrastructure over the same primary-database-owned `EmailDispatchOutbox` state machine. It declares RabbitMQ topology, publishes pointer-only `EmailDispatchPointer` messages with mandatory routing and publisher confirmations, exposes `email-dispatch-rabbitmq` readiness, wires the local Aspire `messaging` resource, and can run manual-ack dispatch and DLQ replay workers when explicitly enabled. It does **not** replace Basic Dispatch Mode; API + the selected primary database + SMTP remains sufficient when RabbitMQ is disabled.

Operator signals:

| Signal | Meaning |
|---|---|
| `email-dispatch-rabbitmq` health check | Disabled mode is healthy and independent; enabled mode proves broker connectivity and topology declaration. |
| `explore.email_dispatch.rabbitmq.publishes` | Outcome counter for disabled, confirmed, returned, nacked, failed, and timeout publish attempts. |
| `explore.email_dispatch.rabbitmq.consumes` | Manual-ack dispatch and DLQ replay delivery counter with closed-vocabulary `outcome` and `failure_category` tags only; tenant identity is omitted. |
| Structured RabbitMQ transport logs | Include dispatch IDs, tenant IDs, topology names, outcomes, and normalized failure categories; do not include recipient addresses, subjects, bodies, provider message IDs, raw broker errors, or AMQP connection strings. |

The RabbitMQ payload is a pointer contract only: tenant ID, stable `PublishEventId`, dispatch kind, source IDs, and optional event/registration/user IDs. Email body, subject, recipient, reply-to, SMTP settings, provider message IDs, and raw provider errors remain out of broker payloads and logs.

Manual-ack dispatch consumption is bounded by `EmailDispatchRabbitMq:PrefetchCount`; ACKs are sent only after `IEmailDispatchDrainService.ProcessSingleAsync(...)` returns a durable PostgreSQL-backed outcome. Malformed or missing pointers are rejected to the queue's DLX/DLQ path, while unexpected transient failures are NACKed with requeue.

DLQ replay is opt-in with `EmailDispatchRabbitMq:DeadLetterReplayEnabled=true`. The replay worker consumes the DLQ with bounded `DeadLetterReplayPrefetchCount`, validates tenant/publish-event/event metadata against the database row, resets replayable durable rows before republishing, parks unsafe messages to the parking queue, and ACKs the original DLQ delivery only after replay or parking publish succeeds. Missing parking topology makes `email-dispatch-rabbitmq` unhealthy because topology declaration is part of the enabled RabbitMQ readiness check.

Operational verification commands:

```bash
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=Email]" --minimum-expected-tests 1
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=RabbitMQ]" --minimum-expected-tests 1
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=Email]" --minimum-expected-tests 1
```

Use the first command for SMTP/Mailpit/Basic Dispatch runtime evidence, the second for optional RabbitMQ topology/publish/consumer/DLQ evidence, and the third for API health, scheduler wrapper, and HAL-gated operator contract evidence.

### Keycloak Identity Lifecycle Email Operations

Keycloak account emails are not part of Basic Email Dispatch. The runtime path for ISLAMU-initiated Keycloak lifecycle messages is:

1. Application code calls `IAccountAuthorityLifecycleEmailService` for email verification, password reset, email update verification, or a future required-action workflow.
2. The Infrastructure Keycloak adapter records a local `NotificationIntent` plus account-authority delegation audit when enabled and configured.
3. The adapter asks Keycloak Admin REST to run `execute-actions-email` for the required action (`VERIFY_EMAIL`, `UPDATE_PASSWORD`, or `UPDATE_EMAIL`).
4. Keycloak generates the action token/link, renders the Keycloak email theme, and sends through the Keycloak realm SMTP provider.

Operational boundaries:

| Concern | Guidance |
|---|---|
| Source of truth | Keycloak is the source of truth for required-action tokens, rendered templates, and provider-side delivery. Local delegation audit proves ISLAMU requested the action; it is not delivery state. |
| Product dispatch separation | Do not inspect `email-dispatch` health, `EmailDispatchOutbox`, RabbitMQ dispatch queues, or Quartz jobs for Keycloak identity email delivery. Use Keycloak realm SMTP/theme settings and Keycloak logs. |
| `keycloak.smtp_mode` | Operational policy label only. `managed` means SMTP is provider-managed outside this deployment. Self-hosted/shared-SMTP modes may configure Keycloak realm SMTP from deployment credentials, but ownership remains Keycloak. |
| Local Mailpit | Local Keycloak can point its realm SMTP at Mailpit for developer inspection. This is separate from product Basic Dispatch Mailpit settings and must not be documented as a production default. |
| Theme sync | Keycloak email templates live in the Keycloak `email` theme type. A future `keycloak.theme_sync_enabled` automation may apply theme assets, but rendered content and sending stay Keycloak-owned. |
| Development theme cache | During theme work, disable Keycloak theme/template caches with the documented Keycloak flags such as `--spi-theme--static-max-age=-1`, `--spi-theme--cache-themes=false`, and `--spi-theme--cache-templates=false`. Do not carry those settings into production guidance without an explicit operator decision. |
| Redaction | Logs, metrics, local results, and delegation audit must not include admin tokens, provider secrets, raw Keycloak response bodies, action tokens, rendered subjects/bodies, SMTP passwords, or theme output. |

If a Keycloak identity email fails, diagnose in this order: Keycloak lifecycle email options and safe URL policy, admin-token acquisition, Keycloak Admin REST status code, realm SMTP configuration, email theme/template availability, then Keycloak server logs. Do not replay through product `EmailDispatchOutbox`.

## Cerbos PDP Operations

### Storage And Package Topology

Static policies, schemas, and derived roles live in repo-root `cerbos/policies/`; native policy tests live in `cerbos/tests/`. The application publishes the bundled package through `IPolicyPackageService` instead of generating ad-hoc role policies at runtime.

`Cerbos:PolicyPackagePath` (environment variable `CERBOS__POLICYPACKAGEPATH`) points the API at the policy package directory. Container deployments should either bundle `cerbos/` into the API image or mount the policy folder read-only, for example `./cerbos/policies:/app/cerbos/policies:ro` with `CERBOS__POLICYPACKAGEPATH=/app/cerbos/policies`. Aspire local development sets `Cerbos__PolicyPackagePath` for `explore-api` to repo-root `cerbos/policies`; direct `dotnet run` also falls back from the default relative `cerbos/policies` path to repo-root policies when launched from a project subdirectory. If the folder is missing, download endpoints return safe `503 ProblemDetails` without host paths.

Package delivery paths:

| Path | Trigger | Notes |
|---|---|---|
| GitHub Actions production publish | `Cerbos Policy Validation` on `push` to `main` after policy validation succeeds | Production CI/CD path. Uses the `production` GitHub Environment approval gate, digest-pinned `cerbosctl`, and repository secrets `CERBOS_SERVER`, `CERBOS_USERNAME`, `CERBOS_PASSWORD`, plus optional `CERBOS_CA_CERT_PEM`. Uploads `_schemas` before policies and retains `cerbos-policy-publish-evidence`. |
| Docker Compose one-shot sync | `docker compose --profile authz run --rm cerbos-policy-sync` | Recommended self-hosting path. Starts the `authz` profile with `cerbos-db`, maps `.env` `CERBOS_ADMIN_USERNAME` to the Cerbos server's `CERBOS_ADMIN_USER` variable, uses `CERBOS_ADMIN_PASSWORD`, recursively uploads policies and `_schemas`, then requests store reload. Set `CERBOS_ADMIN_PASSWORD_HASH` to the hash matching `CERBOS_ADMIN_PASSWORD` before using Admin API sync. |
| Coolify external Cerbos | [CERBOS_COOLIFY.md](CERBOS_COOLIFY.md) | Separate PDP deployment path for Coolify Docker Image resources. Uses PostgreSQL-backed policy storage, a mounted `conf.yaml`, gRPC h2c routing, and manual or CI `cerbosctl` upload from this repo's `cerbos/policies/` tree. |
| Zero-touch boot sync | API startup when complete instance Admin API config exists | Skips safely when endpoint or credentials are incomplete. |
| Setup/Admin UI sync | Operator-triggered setup or admin action | Advanced path shown only when server-side Admin API credentials are already configured; the browser does not collect Cerbos Admin API passwords. Returns safe issue codes for missing config, auth failure, unavailable/rejected package, reload failure, or unknown package status. |
| Manual ZIP fallback | Setup/Admin download endpoint | Always visible in onboarding, including Local RBAC mode. Exports the same bundled package for `cerbosctl put policy --recursive .` and `cerbosctl put schema --recursive _schemas` when Admin API sync is unavailable or intentionally disabled. |

Runtime authorization checks use the PDP gRPC endpoint. Package sync/status uses the Admin API endpoint and credentials. Do not treat a healthy Admin API as proof that runtime PDP checks are healthy, or vice versa.

### Admin API Configuration (`Cerbos:AdminApi` And BYO Admin API)

| Key | Type | Description |
|---|---|---|
| `Endpoint` / `Endpoints` | `string` / `List<string>` | Instance Admin API target(s) for package upload/status/reload. |
| `AdminUsername` | `string` | Basic Auth username; secret-bearing and redacted from reads/logs. |
| `AdminPassword` | `string` | Basic Auth password; secret-bearing and redacted from reads/logs. Docker Compose uses `CERBOS_ADMIN_PASSWORD` for `cerbosctl` and `CERBOS_ADMIN_PASSWORD_HASH` for the Cerbos server config. |
| `Cerbos:PolicyPackagePath` | `string` | API-local path to bundled or mounted `cerbos/policies`; use `CERBOS__POLICYPACKAGEPATH=/app/cerbos/policies` in containers. |
| BYO custom Admin API endpoint/credentials | tenant governance/secret settings | Optional per-tenant package target, preserved even when the tenant custom PDP endpoint is blank. |

Non-local Admin API/PDP endpoints must use safe TLS-capable URLs. Unsafe endpoint changes are rejected before provider settings are persisted. Runtime failure logs must not include raw endpoints, credentials, JWTs/tokens, response bodies, or exception objects/messages.

### Monitoring

| Signal | Meaning |
|---|---|
| Cerbos readiness health check | Follows fail-closed semantics when instance Cerbos mode is active; local mode skips PDP readiness. |
| Package status issue code | Distinguishes Admin API not configured, auth failure, Admin API unavailable/rejected package, reload failure, generic publish failure, and Cerbos package-status unknown. |
| BYO safe-mode log | Tenant BYO failure activated provider-instance fallback safe mode; non-instance-admin decisions deny. |
| Policy revision unknown | Runtime authorization continues through the gRPC PDP. Check the privileged package-status endpoint for operational drift diagnostics, then restore Admin API read access if an observation is required. |
| Runtime failure type metadata | Safe diagnostic context; no raw endpoints, credentials, JWTs, response bodies, or exception messages. |

### Incident Triage (Cerbos)

1. Check the runtime PDP health and the app Cerbos readiness endpoint.
2. Verify instance `Cerbos:GrpcEndpoint` for runtime checks and `Cerbos:AdminApi:*` for package operations.
3. For BYO tenants, verify `cerbos.mode`, `cerbos.custom_endpoint`, and optional custom Admin API endpoint/credentials. There is no failure-mode setting; BYO outages always fail closed.
4. If `cerbos.mode=custom_endpoint` has a blank PDP endpoint, runtime authorization activates safe mode; configure the PDP endpoint or explicitly switch the tenant back to instance mode after confirming policy intent.
5. For package sync failures, inspect the safe issue code before retrying. Prefer `docker compose --profile authz run --rm cerbos-policy-sync` for self-hosted Compose deployments after confirming `CERBOS_ADMIN_PASSWORD_HASH` matches `CERBOS_ADMIN_PASSWORD`, or use setup/admin manual ZIP download plus `cerbosctl put policy --recursive .` and `cerbosctl put schema --recursive _schemas` when Admin API sync is unavailable.
6. For missing HAL affordances, confirm the link was not denied by server-side authorization before debugging route generation.

## Setup Secret Lifecycle

Instance bootstrap uses `ISetupSecretProvider`:

- if setup mode is active and no env secret exists, API keeps validation fail-closed with an internal random fallback and logs only safe configuration guidance;
- onboarding endpoints in BFF (`/bff/setup-secret*`) validate and synchronize secret state;
- setup status returns client-safe state labels (`Environment`, `Generated`, `Locked`, `Unavailable`) and operator guidance without exposing raw secrets;
- API setup authority remains active until onboarding completes and calls `Lock()`; it does not expire relative to process startup;
- BFF setup sessions and protected cookies use a 30-minute rolling inactivity timeout. Successful status and synchronization calls refresh the session; expiry returns the operator to `/setup` with a local return URL and does not require an API restart.

Setup-secret-gated API onboarding endpoints use the dedicated `SetupSecret` rate-limit policy. Invalid setup secrets return RFC 7807 `403 Forbidden` with code `forbidden`; setup-secret endpoints called after bootstrap completion return RFC 7807 `410 Gone` with code `setup_already_completed`; rate-limit rejection returns RFC 7807 `429 Too Many Requests`.

The convention-first launch path is Setup Secret → Admin Auth → Site Profile → Preflight → Launch. Preflight readiness data is non-sensitive and separates launch blockers from operational warnings.

### Terminal Setup Target

`Event.SetupAssistant.Terminal` is the only human terminal interface. It uses
the audited `ISLAMU.Terminal.Gui` `2.4.17-islamu.1` package and the shared
CommunityToolkit presentation workspace; `Event.SetupAssistant.Cli` remains
noninteractive and has no `tui` command or console fallback. Launch the target
from an interactive terminal with no arguments. Redirected input, output, or
error fails with `interactive-terminal-required` before secret state is created.
Below 40 columns by 17 rows the target hides every input/action and shows only
a resize instruction; it never leaves clipped controls active.

The target creates a new `.env.setup` by default and never overwrites an
existing file. Secret entry is masked, clipboard paste is refused, and the
buffer is cleared on completion, cancellation, signal, disposal, or failure.
Protected output currently requires Linux, macOS, or FreeBSD owner-only `0600`
file semantics; other operating systems fail closed with
`protected-output-unavailable`. There is no alternate renderer.

## External API Key Operations

External API keys are long-lived credentials for non-interactive callers. Operational guarantees differ from interactive JWT flows in three places: rate-limit partitioning, quota enforcement, and usage-metadata freshness.

### Rate-Limit Partitioning

- When an API-key principal is present on `HttpContext.User`, the `global` and `authenticated` rate-limit policies partition on `api-key:{keyId}` instead of remote IP or user ID.
- Partitioning guarantees that one key's burst does not starve other keys sharing the same egress IP.
- Per-key limits use the same token-bucket configuration as `authenticated` (200 requests / 60s sliding). Per-key write limits match `write` (30 requests / 60s fixed).
- Anonymous and JWT callers retain their existing partition keys (IP and user ID respectively); no behavior change for those paths.

### Clustered Deployment Semantics

Three persistence tiers behave differently in multi-node deployments:

| Tier | Storage | Cluster Behavior | Mitigation |
|---|---|---|---|
| Rate limits | In-process `PartitionedRateLimiter` | **Node-local** — N nodes give N× the advertised limit | Deploy a Redis-backed limiter or an ingress-tier limit when strict global enforcement is required |
| Quota credits | PostgreSQL `ExternalApiKeyQuota` table | **Cluster-safe** — atomic `INSERT ... ON CONFLICT` + `UPDATE ... WHERE credits_used + amount <= limit` with row-level lock | No action required |
| Usage metadata | In-memory write-through to `LastUsedAt` / `LastUsedIp` | **Eventually consistent** — 5-minute in-memory throttle per key; races between nodes are acceptable | No action required; metadata is informational, not security-critical |

### Revocation Semantics

- Revocation sets `ExternalApiKeyStatusId` to `Revoked`. The change takes effect on the **next** authentication attempt — in-flight requests already past the auth handler complete normally.
- Cache invalidation for the key row is immediate (HybridCache `RemoveAsync`); no stale reads beyond the auth handler's first lookup.
- Revoked keys emit `explore.external_api_keys.revoked` business metrics tagged with `tenant_id` and `owner_type`.

### Usage Reporting

- Tenant admins call `GET /api/ExternalApiKey/usage-report?from=&to=` and receive a report scoped to their tenant.
- Instance admins call the same endpoint and receive a platform-wide report — optionally narrowed via `tenantId` query parameter.
- Reports are aggregated from request counts + last-used timestamps; no raw request logs are surfaced (privacy boundary).

### Observability

External API-key business counters use the `explore.external_api_keys.*` prefix. Most lifecycle counters include `tenant_id` and `owner_type`; authentication attempts add `outcome`, and throttle events add `policy`.

- `explore.external_api_keys.created`
- `explore.external_api_keys.revoked`
- `explore.external_api_keys.policy_updated`
- `explore.external_api_keys.rotated` (metric exists for future/overlap workflows; do not infer a public rotate endpoint from this metric alone)
- `explore.external_api_keys.authentication_attempts` (+ `outcome` tag: `success` / `invalid` / `inactive` / `expired` / `tenant_mismatch` / `empty_header`)
- `explore.external_api_keys.throttled` (+ `policy` tag)

Structured logs on the auth handler include `key_id` and outcome only — never the secret segment.

## Analytics Operational Contract

Analytics is optional infrastructure. Provider failures must never block normal product flows.

Current operational expectations:

- `NullAnalyticsProvider` is the safe baseline for disabled deployments.
- Runtime provider selection is tenant-aware and settings-driven through `AnalyticsConfigResolver`.
- Provider/network failures are logged and swallowed so command/query flows continue.
- Self-hosted operators may need first-party proxying or custom endpoints via `analytics.endpoint_url` to reduce CSP or ad-blocker loss.
- Browser relay fallback is available at `POST /api/a/t` for operators who cannot or do not want to load vendor analytics scripts in the browser.

Current provider capability tiers:

| Provider | Current operational tier | Notes |
|---|---|---|
| `none` | Disabled | First-class no-op mode |
| `plausible` | Lightweight web analytics | Pageviews and custom events only; no identify/group semantics by design |
| `posthog` | Rich product analytics | Richest current provider: identify, groups, and feature flags |
| `rybbit` | Browser-richer / validate before broad rollout | Current server code is track/pageview-focused; official docs justify browser identify but not server/group/flag parity |
| `rudderstack` | Advanced pipeline / validate before broad rollout | Full event-spec surface in code, but better understood as a CDP/router than a first-party analytics backend |

Self-hoster deployment tiers:

| Tier | Typical operator posture | Expected analytics mode | Operational focus |
|---|---|---|---|
| Tier 0 | Privacy-first / no analytics | `none` | Zero breakage, zero analytics dependency |
| Tier 1 | Lightweight self-hosting | `plausible`-style web analytics | Simple setup, low overhead, pageview/custom event tracking |
| Tier 2 | Product analytics | `posthog`, `rybbit`, or `rudderstack` | Richer events, optional identity semantics, stronger operator validation |
| Tier 3 | Accuracy-sensitive/self-hosted proxy | First-party proxied analytics | Reverse proxy, CSP, and blocker mitigation guidance |

Incident triage for analytics-related issues:

1. Check whether `analytics.enabled` is actually `true` for the affected tenant.
2. Check resolved `analytics.provider` and `analytics.endpoint_url` values.
3. Check browser blocking conditions first for client-side analytics: CSP, ad blockers, reverse proxy pathing.
4. Confirm failures are isolated to analytics logging and not leaking into user-facing requests.

Transport mode quick guide:

| Mode | Browser behavior | Typical operator use | Requirements |
|---|---|---|---|
| `direct` | Loads provider script from vendor/provider host | Fastest setup, cloud-hosted or permissive CSP | Public API/site key, provider host allowed by CSP/network |
| `proxy` | Loads provider script and ingest through a first-party reverse proxy | Self-hosted deployments that want better blocker resistance and simpler CSP | Reverse proxy for script + ingest paths, forwarded host/proto headers, stable first-party path |
| `relay` | No vendor script in browser; client posts pageview/custom events to `/api/a/t` | Strict CSP, privacy-sensitive, or heavily blocked environments | API reachable from browser, provider configured server-side, no public API key required |

Reverse proxy and CSP notes:

- Avoid blocker-friendly path names such as `/analytics`, `/tracking`, or `/stats` when fronting third-party providers; use opaque first-party paths instead.
- Preserve `Host`, `X-Forwarded-Proto`, and `X-Forwarded-For` correctly so provider proxies and relay rate limiting see the intended origin/protocol.
- Keep proxied analytics endpoints on HTTPS; mixed-content browser failures look like random analytics drops.
- `direct` and `proxy` modes need CSP allowances for the chosen script/connect sources; `relay` mode can keep CSP tighter because the browser only talks to the application origin.
- The relay endpoint has its own `AnalyticsRelay` fixed-window rate limit in addition to normal API protections.

Bootstrap and failure behavior:

- Disabled analytics or provider `none` results in a clean no-op bridge.
- `relay` mode initializes even when `analytics.api_key` is empty.
- Script load failures in `direct` or `proxy` mode degrade to a no-op adapter rather than breaking the page.
- Relay/browser failures must be treated as observational loss only; user-facing navigation and commands continue normally.

Client vs server event responsibilities:

- Browser-side analytics is for pageviews and low-risk interaction telemetry only.
- Server-side analytics is for business events that must come from authoritative handlers or workflows.
- Do not emit the same business action from both client and server unless the event is deliberately modeled as correlated-but-distinct.
- Treat browser pageviews as navigation context and server events as domain facts.

Provider-capability caution:

- Do not promise semantic parity across providers just because the interface shape is shared.
- `PostHog` is the only currently validated provider in this repo that supports rich product-analytics behavior plus feature flags.
- `Plausible` should be positioned as the privacy-friendly lightweight tier.
- `Rybbit` should stay in a validated-with-caution tier until official docs justify server-side parity beyond browser events.
- `RudderStack` should be documented as an advanced event pipeline option, especially for operators who want to forward data onward, not as a direct substitute for PostHog dashboards/flags.

Analytics rollout and incident runbook:

1. **Enable / disable**
   - Use `analytics.enabled = false` or provider `none` for a clean global or tenant-level kill switch.
   - For urgent incident response, short-circuit the proxy/relay path to `204` rather than redeploying clients.
2. **Provider switch**
   - Validate the target provider in a staging tenant first.
   - Keep event names and property semantics stable across the switch; do not promise unsupported provider parity.
   - Prefer a short dual-run window only when operators explicitly need migration confidence.
3. **Proxy / relay verification**
   - Confirm the reverse proxy preserves `Host`, `X-Forwarded-Proto`, and `X-Forwarded-For`.
   - Verify CSP permits the required `script-src` / `connect-src` entries for `direct` or `proxy` mode.
   - Verify `relay` mode can reach `/api/a/t` from the browser without a public API key.
4. **Blocked or missing data**
   - Check CSP reports, browser network failures, and ad-blocker interference before suspecting provider code.
   - If traffic is heavily blocked, move to `proxy` or `relay` mode before expanding provider-specific debugging.
5. **Deferred reliability work**
   - Buffered/outbox analytics delivery is intentionally deferred to a follow-up milestone.
   - Current guidance is best-effort delivery for browser analytics and handler-driven best-effort server events; introduce an outbox only if operators need stronger guarantees for business-critical analytics.

### Cookie Consent & Privacy Governance

The platform provides a consent framework; each instance operator is the data controller responsible for their own legal compliance.

Global kill switch:

- `analytics.global_disable_client_tracking = true` immediately disables all browser-side analytics across all tenants.
- Use this for urgent legal/privacy incidents without editing individual tenant settings.
- Server-side analytics continues normally; this only affects browser SDK initialization.

Provider consent matrix:

| Provider | Cookieless Mode | Banner Required | Decline Behavior |
|---|---|---|---|
| `none` | N/A | No | N/A |
| `plausible` | Always cookieless | No | N/A |
| `rybbit` | Always cookieless | No | N/A |
| `posthog` (`cookieless_mode=always`) | Always cookieless | No | N/A |
| `posthog` (`cookieless_mode=on_reject`) | Cookieless after decline | Yes | Configurable: `disable` or `cookieless` (default: `cookieless`) |
| `posthog` (`cookieless_mode=off`) | Consent-managed | Yes | Disable (no cookieless fallback) |
| `rudderstack` | Full consent (v1) | Yes | Disable (full consent required) |

PostHog privacy defaults for self-hosters:

- `posthog_session_replay`: disabled by default. Requires explicit consent if enabled.
- `posthog_autocapture`: disabled by default. When enabled, captures click/input interactions.
- `posthog_heatmaps`: disabled by default. Collects pointer position data.
- `posthog_person_profiles`: `identified_only` by default. Set to `never` for fully anonymous analytics.
- Admin UI shows contextual warnings when enabling features that expand the consent surface.

Consent cookie operational notes:

- Consent cookies are tenant-scoped (`explore_cc_{stableShortKey}` where `stableShortKey` is the first 8 hex chars of the tenant's immutable GUID) — never shared across tenants. Cookie scope is per effective public host (`SameSite=Lax`, `Secure`, `path=/`).
- Default lifetime is 180 days (configurable via `analytics.consent_cookie_lifetime_days`).
- Cookie values are minimal (`accepted`/`declined` only) — not identity artifacts.
- The consent cookie is classified as strictly necessary and does not require its own consent.
- A persistent "Cookie Settings" link in the footer allows users to withdraw consent at any time.

Admin settings management:

- `GET /api/InstanceOnboarding/analytics-governance` returns current governance settings plus computed advisory fields.
- `PUT /api/InstanceOnboarding/analytics-governance` updates all 10 consent/privacy governance keys.
- Auto-computation is advisory: the resolver suggests recommended settings but does not silently overwrite operator choices.
- The resolver returns `ResolveReasons` (e.g., `GlobalKillSwitch`, `ProviderInherentlyCookieless`, `PosthogCookielessOnReject`, `CookieBannerEnabledByOperator`) for diagnostics and admin UX. These are surfaced in the admin settings panel but never exposed in public-facing DTOs.
- Save-time validation rejects illegal combinations (e.g., `Cookieless` decline behavior for providers that don't support cookieless mode, out-of-range cookie lifetime) and returns warnings for suboptimal but allowed configurations (e.g., PostHog features enabled on a non-PostHog provider, session replay with always-cookieless mode).

## Single-Tenant Endpoint Exposure

`BlockInSingleTenantAttribute` behavior:

- in single-tenant mode with hiding enabled, guarded endpoints return `404` (hidden from discovery).

`RequireMultiTenantAttribute` behavior:

- returns `403` with a clear error payload when feature requires multi-tenant mode.

## Localization Operational Runbooks

### Runbook: TMS Provider Is Down
1. Check Grafana `islamu_tms_fallback_activated_total` — if > 0 in 5m, confirm TMS outage.
2. Check container logs for `[LOCALIZATION] TMS ExportTranslations failed` entries.
3. **Immediate mitigation**: Flip `localization.force_offline_mode` to `true` via Admin UI → Localization → Kill-switches → "Save & Apply Kill-switches Now".
4. Investigate TMS provider status (Tolgee dashboard, Weblate status page).
5. When TMS is restored: disable force-offline, verify live translations resume.

### Runbook: Bundle File Lost / Corrupted
1. Navigate to Admin UI → Localization → Offline Bundle Export.
2. Click "Export {LANG}" for each affected language.
3. Verify file appears in `App_Data/Localization/Bundles/{lang}.json`.
4. If export fails, check the health banner — writable path may not be available.

### Runbook: Writable Path Health Banner Red
1. Check deployment topology: single-instance vs multi-replica.
2. Verify `App_Data/Localization/Bundles/` directory exists and has write permissions.
3. For multi-replica without shared storage: this is expected — see `dev/backlog/distributed-bundle-file-writer.md`.
4. For single-instance: check filesystem permissions, disk space.
5. Escalate to SRE if distributed bundle writer is needed.

### Runbook: TMS Credential Replacement
1. Open the Admin UI localization provider settings for the affected instance or tenant.
2. Replace the TMS API credential through the configured settings or secret-provider path.
3. Save the settings and use the provider test action, if available, to verify the new credential.
4. If the credential is managed by an external secret provider, rotate it in that provider first and then refresh or restart the application according to the secret-provider runbook.
5. Keep the old credential active only for the minimum overlap window required by the external TMS provider.

### Localization Metrics & Alerts

| Metric | Alert Threshold | Description |
|--------|----------------|-------------|
| `islamu_tms_fallback_activated_total` | > 0 in 5m | TMS provider failed; page on-call |
| `islamu_translation_fetch_duration_seconds` | p99 > 5s | TMS latency degradation |
| `islamu_translation_fetch_total{result="error"}` | > 10 in 5m | Repeated fetch failures |

### AT Protocol Operations

AT Protocol event federation is disabled by governance by default. Before enabling `federation.atproto_events_enabled`, verify migrations are current and the Jetstream worker bounds match [CONFIGURATION.md](CONFIGURATION.md); leave `AllowedDids` empty for public exact-collection discovery or configure it for curated ingress. Inbound discovery does not require ATProto authentication. For outbound publication, verify the OAuth health check is ready; each owner must still opt in through `federation.atproto_publish_my_events`.

`Explore.API` hosts Quartz `pds-sync-drain` and the Jetstream subscriber. The job invokes one Infrastructure drain pass every 5 seconds by default, claims at most 20 rows with 90-second fenced leases, and processes at most 10 concurrently in fresh scopes. `AtprotoJetstreamSubscriber` opens one capability-aware global stream, renews its 60-second lease every 20 seconds, and cancels the stream immediately when renewal is fenced or fails. Jetstream never opens per-tenant streams.

Operational signals are intentionally bounded:

| Signal | Meaning |
|---|---|
| `atproto.authentication.operations` | Count of readiness/challenge/callback/bridge/refresh/revoke outcomes with bounded `operation` and `outcome` tags. |
| `atproto.authentication.duration` | Matching authentication duration histogram in seconds. |
| `atproto.jetstream.envelopes` | Jetstream connection, replay, fencing, materialization, quarantine, and lease outcomes; the optional `collection` tag is normalized to `event`, `rsvp`, or `unsupported`. |
| `atproto-authentication` health check | Local prerequisites plus a signed synthetic transient-store create/read/consume probe; two-second deadline, no retry/hedging, ten-second completed-result cache, stable failure codes only. |
| `explore.atproto.transient.operations` | Private operation count; only fixed `operation`, verified `purpose`, and `outcome` labels described below. |
| `explore.atproto.transient.cleanup_runs` | Completed/failed cleanup passes; `outcome=succeeded` or `failed`. |
| `explore.atproto.transient.cleanup_rows` | Rows deleted by completed passes; `store=transients` or `assertions`. Failed partial-pass work is not reported as a completed row total. |
| `atproto-jetstream` health check | API readiness for capability resolution and public or DID-curated exact-collection subscription; dormant disabled capability is also healthy. |
| `pds-sync-drain` structured logs | Aggregate claimed/delivered/failed/claim-lost counts only; provider response bodies, OAuth material, DIDs, record keys, and payloads must not be logged. |

Transient instruments use the existing `Explore.Business` meter. Operation
labels are closed to `create`, `read`, `consume`, `probe`, or `unknown`;
purpose labels are `oauth_state`, `tenant_handoff`, `health_probe`, or
`unknown`, taken only from successfully verified authentication context.
Outcomes are `succeeded`, `not_found`, `conflict`, `rate_limited`, `rejected`,
`unavailable`, or `cancelled`. Never label or log tenant/user identifiers,
locators or their digests, assertions, proof cookies, ciphertext, or keys.

`BffProviderReadinessService` checks local configuration/key/adapter prerequisites
before its cached result. Concurrent cache misses share one probe per BFF
singleton through a cancellation-aware gate; the two-second budget includes
gate waiting. A cancelled waiter neither cancels the active probe nor replaces
its result. Coalescing is per instance, not distributed; account for aggregate
replica probes and login operations when sizing the existing instance-wide
transient admission limit. Its `ApiBackedAtprotoTransientStore` posts the sole
body `{"purpose":"health_probe"}` to the signed private
`/api/auth/atproto/transient/probe` endpoint. The Application command generates
tenantless random non-secret data with a thirty-second expiry, proves creation,
read-back, and conditional consumption, then returns an empty `204`. No caller
tenant, locator, or payload is accepted, and the browser proxy denies this
route. Probe failures reduce to `state_store_unavailable`; they do not disclose
database errors. This does not certify PDS/discovery availability or durable
session operations. Disabled ATProto is Healthy; an unavailable ATProto primary
is Unhealthy (`503`), while optional ATProto with explicit Local Identity or
Keycloak primary is Degraded (`200`). Other checks can still fail readiness;
`/alive` is independent of the probe.

`AtprotoTransientCleanupJob` calls one `AtprotoTransientCleanupService` pass
every minute, starting after sixty seconds, with
`DisallowConcurrentExecution` and no job payload. Each pass captures one
Unix-millisecond time and deletes at most five 500-row batches per table,
stopping on a short batch: at most ten delete calls and 5,000 rows in total.
Repositories select fixed expired identities before a parameterized,
non-retrying deletion. A lost delete acknowledgement fails the pass instead
of letting a provider retry select another batch; the next scheduled pass
resumes cleanup. Failed passes can have committed partial work.
There is no 24-hour idempotency grace. The owned job remains registered when
ATProto is disabled, but `Scheduler:Quartz:Enabled=false` stops it along with
other scheduler work. Successful passes use the canonical log event
`Scheduled job {JobName} completed.` with `JobName=atproto-transient-cleanup`.

Keep every BFF/API host within five seconds of trusted UTC and monitor clock
synchronization. This permits at most ten seconds of pairwise clock difference.
The transient cutoff is the captured time; the replay cutoff is that time minus
10,000 milliseconds. Replay claims retain their original acceptance expiry
(assertion expiry plus five seconds), so the extra retention never widens
assertion admission. An ahead cleanup host therefore cannot delete a claim
while a supported behind verifier still accepts it. If a host exceeds the
clock bound, restore synchronization before returning it to authentication
traffic; the fixed margin does not protect arbitrary clock drift.

For a store outage, restore API/database access, migrations, and the shared
OAuth signing authority; wait beyond the ten-second readiness cache, then
start a fresh login. Never replay an uncertain consume. For cleanup failures,
restore scheduler/database access and watch failed-run counts and subsequent
completed-pass row counts. Backlogs above the per-pass cap drain over later
passes; do not extend expiry or manually recycle locators. Reads and consumes
reject expired rows independently of deletion, including synthetic leftovers
from failed probes. Replay claims are retained through their acceptance expiry,
including five seconds of skew. Active-row cleanup does not erase backup
copies; apply operator backup retention separately. Preserve and share the
existing BFF Data Protection keys across restarts/replicas as described in
[ADR-014](adr/ADR-014-atproto-session-trust-bridge.md); loss of required keys
fails closed and requires new login flows, not a memory fallback.

For delayed or failed publication, keep the local event authoritative. Inspect the newest non-superseded `PdsSyncOutbox` row for the tenant/event, verify capability, consent, linked session, and public-location eligibility, then follow the stable recovery guidance in [TROUBLESHOOTING.md](TROUBLESHOOTING.md). Do not create a PDS record manually and do not replay by changing the stable record key.

## Ticketing Recovery Operator Matrix

Ticketing restore starts fail-closed in `RecoveryOnly`. New sales, transfer,
waitlist allocation, add-on fulfillment, and ordinary queue dispatch remain
closed until the consistency manifest matches the configured release/schema,
retained key inventory, authority floor, provider cursor, durable idempotency
floor, and worker fence. The configured manifest-signing key is a server-only
Infisical/environment secret; health and operator output expose only the
canonical key reference and retained integer versions.

The ordered controls are:

| Current state | Allowed action | Result / refusal |
|---|---|---|
| Any non-failed state | `StopSales` | Advances the worker fence and returns to `RecoveryOnly`. |
| `WorkersOpen` / `SalesOpen` | `PauseWorkers` | Quartz enters standby and durable state returns to `AuthorityRotated`. |
| `RecoveryOnly` | `Reconcile` | Validates the exact manifest, cancels pre-restore capabilities, revokes active credentials, writes one digest-free reissue intent per ticket, and marks in-flight provider work `Unknown`. |
| `Unknown` effect | `ResolveUnknown` | Operator chooses retry only after authoritative provider evidence; the expected processing fence must match. |
| `Unknown` effect | `DeadLetter` | Parks the effect with append-only failure evidence; blind replay and direct SQL are forbidden. |
| `AuthorityRotated` | `ReopenWorkers` | Requires the exact new worker fence; Quartz resumes only after durable acceptance. |
| `WorkersOpen` | `ReopenSales` | Refused while any unknown provider effect or pending credential reissue remains. Sales open last. |

`ticketing-recovery` readiness emits only:
`status`, `pending_reissues`, `ambiguous_effects`, `dead_lettered_effects`,
`poison_effects`, and `oldest_due_age_seconds`. It is unhealthy for a failed
recovery or oldest due age at least 120 seconds; degraded for recovery-only,
unknown/dead-letter/poison work, backlog at the configured threshold, or age at
least 60 seconds. Never add tenant, event, order, ticket, actor, amount,
provider object, capability, digest, or exception text to this signal.

Reference recovery targets are RPO <=15 minutes and RTO <=60 minutes.
Configuration can declare stricter values but cannot claim looser values as
production-ready. SQLite supports one application replica. Server-database
multi-replica deployments require shared primary state and clustered Quartz.
Production-like timed restore and takeover evidence remains an external release
gate; fixture success is not that evidence.

## Incident Triage Quick Checks

1. Check `/health` and `/alive`.
2. Check MigrationService logs for primary, Data Protection, authority, or seeding failures; check API logs for runtime-provider validation and scheduler schema failures.
3. Check rate-limit/timeouts if clients receive `429` or `504`.
4. Check tenant resolution and deployment mode (`deployment.mode`) if tenant-scoped behavior is wrong.
5. Check setup-secret mode if onboarding is blocked.
6. Check `islamu_tms_fallback_activated_total` if localization is degraded — flip force-offline if needed.

## AI Agent Operational Context

AI-agent workflow rules are not runtime operations. Keep them in [../../AGENTS.md](../../AGENTS.md) and [the context-engineering contract](../../.agents/CONTEXT_ENGINEERING.md) so operators do not have to scan agent tooling while diagnosing production behavior.

The only repository workflow utility is the read-only `eng/agent-workflow`
guard. Use `validate-intents .agents/contract/intents.yaml` for YAML syntax and
`validate-commit -- git commit ... -- <literal-files>` to reject broad commit
pathspecs. It owns no state and never executes Git.

```bash
dotnet run --project eng/agent-workflow/src/ISLAMU.AgentWorkflow/ISLAMU.AgentWorkflow.csproj -- validate-intents .agents/contract/intents.yaml
dotnet run --project eng/agent-workflow/src/ISLAMU.AgentWorkflow/ISLAMU.AgentWorkflow.csproj -- validate-commit -- git commit --only -m "message" -- src/ExactFile.cs docs/ExactFile.md
```


## Planned Capacity Work

Partitioning is not implemented. Treat partitioning notes as future capacity planning only; do not document partitioned-table behavior as a current operator contract. Revisit this when tenant-scoped or append-only tables approach sizes where normal indexing and query-filter pruning no longer meet SLOs. This is an intentional architecture decision recorded in [ADR-009](adr/ADR-009-postgresql-partitioning-deferral.md): the product stays simple for Tier 1/Tier 2 self-hosters, while Tier 3 operators get clear activation thresholds and a runbook path before PostgreSQL partitioning becomes production behavior.

## Data Lifecycle And Retention Matrix

This matrix is the Phase 6 source of truth for high-growth operational data. It records current behavior and target policy. Unless a row explicitly says cleanup is implemented, no automated retention job currently exists.

Context7 research notes:

- PostgreSQL declarative range partitioning works best when the partition key appears in query and retention predicates. Partition pruning is driven by partition-key constraints, and old data can be removed operationally by detaching or dropping old partitions.
- EF Core migrations can use `migrationBuilder.Sql(...)` for provider-specific database features that EF does not model directly. PostgreSQL partitioning, partition attachment, and concurrent partition maintenance should therefore be introduced through explicit migration SQL/runbooks, not hidden inside normal entity configuration.

Lifecycle classes:

| Class | Meaning | Cleanup posture |
|---|---|---|
| Compliance evidence | Security, admin, audit, or consent evidence | Retain by default; purge only through documented operator retention policy and legal-hold checks. |
| Durable side-effect ledger | Outbox intent, attempts, receipts, and delivery evidence | Completed rows may age out after operator-safe windows; unresolved rows stay until parked/replayed/resolved. |
| User-facing operational state | User inbox or active workflow state | Keep active rows; archive/delete only after user/admin lifecycle rules are explicit. |
| Rebuildable projection/cache | Derived from canonical tables | Safe to rebuild; cleanup should be tied to source deletion or projection rebuild/drain semantics. |
| Ephemeral safety cache | Short-lived duplicate/retry protection | Delete after expiry plus a small clock-skew buffer. |
| External mirror/index | Copy of another system or object-store metadata | Retention follows source/integration policy; never assume local rows can be dropped without reconciliation. |

| Table family | Lifecycle class | Source of truth / owner | Current cleanup | Target default retention | Partitioning trigger and shape | Phase 6 follow-up |
|---|---|---|---|---|---|---|
| `audit_logs` | Compliance evidence | EF audit writes in `ExploreDbContext` | No automated cleanup | 7 years by default for production/self-hosted compliance, configurable only with legal-hold support | Consider monthly `Timestamp` range partitions when table exceeds 100M rows total, 10M rows per tenant, or time-range audit queries miss SLOs | Add retention settings, export-before-purge runbook, and legal-hold guard before any delete job |
| `configuration_change_logs`, `tenant_lifecycle_logs` | Compliance evidence | Admin/governance and tenant lifecycle workflows | No automated cleanup | 7 years by default; tenant lifecycle logs retained for tenant lifetime plus retention window | Consider yearly or monthly `Timestamp`/`TransitionedAt` range partitions only after audit-log partitioning patterns are proven | Keep append-only; add operator export and retention policy before cleanup |
| `event_contact_share_exports`, `event_contact_share_export_items` | Compliance evidence with PII snapshots | Contact-share export workflow | No automated cleanup | 3 years by default, or longer where operator policy requires consent/export evidence | Consider monthly `CreatedAt` range partitions if export volume becomes large; item rows must stay co-located by export lifecycle | Add policy-controlled purge that preserves aggregate counts/audit evidence while deleting email snapshots when retention expires |
| `notifications` | User-facing operational state | Notification handlers/repository | Soft delete/archive only through user workflows; no age cleanup | Keep unread/unsnoozed rows; retain read or archived rows for 365 days by default after last update | Consider monthly `CreatedAt` range partitions when inbox queries exceed index-only performance or table exceeds 50M rows | Add tenant/user-scoped notification retention job with opt-out for compliance notification types |
| `outbox_messages`, `pds_sync_outbox`, `policy_change_outbox` | Durable side-effect ledger | Transactional outbox processors | Processors update status; no completed-row cleanup | Completed rows: 30 days. Failed/dead-lettered rows: retain until operator resolution, then 90 days | Consider monthly `CreatedAt` range partitions when completed rows dominate scans; worker indexes must keep pending/retry rows hot | Add cleanup that deletes only completed/resolved rows and never deletes pending, processing, retry, failed, or dead-letter rows |
| `email_dispatch_outbox` | Durable side-effect ledger with email PII snapshots | Registration/email dispatch state machine | Implemented: bounded/dry-runnable redaction after sent/skipped or explicitly resolved retention cutoff; purged tenants are immediate; `ContentRedactedAt` blocks replay | Sent rows: 180 days. Dead-lettered/unknown/parked rows: retain until operator resolution, then 180 days | Consider monthly `CreatedAt` range partitions when dispatch history exceeds 25M rows or status polling slows | Monitor bounded cleanup duration/counts and add a dedicated readiness signal if operational evidence requires one |
| `email_dispatch_attempts`, `email_dispatch_receipts` | Durable side-effect ledger | Email dispatch drain/consumer idempotency | Implemented: free-text errors and provider IDs redact transactionally with the selected parent; typed outcomes and timestamps remain | Attempts/receipts follow parent retention; failed/unknown evidence stays while parent is unresolved | Partition only with parent strategy; independent partitioning risks expensive parent/child maintenance | Keep parent-aware regression coverage in the persistence gate |
| `ai_conversations`, `ai_messages`, `ai_runs`, `ai_conversation_references`, `ai_proposed_actions`, `ai_tool_executions` | User-facing operational state with provider/prompt sensitivity | AI assistant conversation, proposal, and confirmed-tool audit flows | Implemented: the `ai-retention-cleanup` Quartz job (`AiRetentionCleanupJob`) iterates active tenants, binds tenant context, resolves each tenant's `ai_assistant.retention_days`, supports `AiRetentionCleanup:DryRun`, redacts message content/action payload/reference summaries/failure messages/tool failure messages, and soft-deletes expired conversation shells through tenant-filtered repository cleanup. | 30 days by default via `ai_assistant.retention_days`, tenant-configurable through governance settings | Do not partition initially; cleanup predicates use tenant plus conversation age and should stay index-backed until AI history volume proves otherwise | Monitor `ai-retention-cleanup` readiness and `explore.ai.retention.*` metrics before broad history enablement; never log prompt content, action payloads, provider responses, or model secrets |
| `idempotency_records` | Ephemeral safety cache | `IdempotencyMiddleware` / `IIdempotencyRepository` | Implemented: reads ignore expired rows, and the `idempotency-cleanup` Quartz job (`IdempotencyCleanupJob`) deletes rows older than `ExpiresAt + IdempotencyCleanup:ExpirationGraceHours` in bounded batches; dry-run is available | Delete after `ExpiresAt + 24h` safety buffer by default | Do not partition initially; TTL delete by `ExpiresAt` should be enough unless write volume is extreme | Monitor `idempotency-cleanup` readiness and cleanup metrics; revisit only if delete volume or index bloat threatens SLOs |
| `atproto_transient_records`, `atproto_transient_assertion_replays` | Ephemeral authentication state and replay protection | Private signed ATProto transient bridge | Implemented: `atproto-transient-cleanup` deletes at most five 500-row batches per table each minute; reads/consumes enforce expiry independently | State at most 10 minutes, handoff at most 2 minutes, synthetic probe 30 seconds; replay claims through assertion acceptance expiry including 5-second skew, then 10-second cleanup retention without extending validity; no 24-hour grace | No partitioning initially; indexed Unix-millisecond expiry supports bounded deletion | Keep cleanup running after disabling login; monitor fixed operation/cleanup counters; active-row deletion does not erase retained backups |
| `custom_property_projection_dirty_scope` | Rebuildable projection/cache backlog | Projection rebuild/drain coordination | Drained rows remain; pending rows are quota-bounded | Pending rows stay until drained; drained rows retained 7 days for diagnostics | No partitioning initially; the table is quota-bounded per tenant | Add drained-row cleanup and metrics for deleted/drained/pending counts |
| `event_custom_property_projections`, `event_session_custom_property_projections` | Rebuildable projection/cache | Projection updaters from Layer 3 values | Rebuild and source deletes replace/remove rows; no age cleanup | No independent age retention; rows live while source values and exposure rules require them | Consider tenant/hash or event-date-adjacent strategy only after projection query SLOs require it; range partitioning by `UpdatedAt` is not useful for most lookup predicates | Keep rebuild-first recovery; add periodic consistency checks before partitioning |
| `external_api_key_quotas` | Operational accounting ledger | External API key quota service | Cascade delete when key is physically deleted; no age cleanup | 24 monthly periods by default for usage reporting | Do not partition initially; one row per key per period should stay small | Add retention by `PeriodEnd` with tenant/admin reporting guardrails |
| `atproto_records`, `indexed_dids`, `sync_states` | External mirror/index | Federation indexer/PDS sync | No automated cleanup | Retain while the indexed actor/record is active or until federation reconciliation marks it stale | Consider partitioning only if indexer query patterns become time-based; current keys are DID/collection/record oriented | Define federation stale-record reconciliation before cleanup |
| `storage_objects` | External mirror/index with blob lifecycle risk | Storage metadata plus external object store | No automated cleanup | Retain metadata while owning domain reference exists; orphan candidates require quarantine before object deletion | Do not partition initially; metadata cleanup depends on object ownership graph, not time alone | Add orphan detector, quarantine window, and blob-delete idempotency before any purge |

Retention implementation rules:

1. Cleanup jobs must be tenant-aware unless they are explicitly instance/system scoped.
2. Cleanup jobs must be dry-run capable before destructive mode is enabled.
3. Metrics must use bounded dimensions only: table family, lifecycle class, outcome, tenant ID when tenant-scoped, and failure category. Do not tag raw entity IDs, emails, setting keys, custom-property keys, subjects, provider message IDs, or exception text.
4. Hard deletion of compliance evidence requires an operator-visible retention policy, legal-hold check, and audit summary.
5. Partitioning work must include migration/runbook rollback behavior. Detaching a partition for archival is preferred over immediate destructive dropping when evidence value is uncertain.

### Partitioning Decision And Activation Runbook

Current decision: PostgreSQL partitioning is deferred. [ADR-009](adr/ADR-009-postgresql-partitioning-deferral.md) is the durable source of truth. Do not add partitioned tables, partition-maintenance workers, or generated partition migrations until an operator need or load-test result crosses the activation gates below.

Decision rationale:

- Tier 1 self-hosters should not inherit high-scale database maintenance before they need it.
- Existing Phase 6 work now has lifecycle classification and one low-risk cleanup implementation for ephemeral idempotency rows.
- PostgreSQL partitioning changes insert routing, migration operations, backup/restore expectations, and retention procedures.
- EF Core does not model PostgreSQL partition lifecycle directly; partition DDL belongs in explicit SQL migrations and runbooks.

Activation gates:

| Gate | Default trigger | Evidence required |
|---|---:|---|
| Total table size | Candidate table exceeds the matrix threshold, for example `audit_logs` over 100M rows or email dispatch history over 25M rows | Database statistics, index bloat report, and table growth trend |
| Tenant concentration | One tenant exceeds the per-tenant threshold for a candidate table, for example `audit_logs` over 10M rows | Tenant-scoped count query and operator impact assessment |
| Query SLO pressure | Normal indexes and query-filter pruning miss production SLOs for time-range or worker scans | Query plans with timing before/after index tuning |
| Retention pressure | Deleting or archiving old rows creates unacceptable locks, vacuum debt, or maintenance windows | Retention dry-run timings and maintenance logs |
| Backup/restore pressure | Backup, restore, or export windows exceed operator objectives because of one append-heavy table family | Backup/restore timing evidence and recovery objective |

Candidate order:

1. `audit_logs`: first candidate only after legal-hold/export posture exists. Use monthly `Timestamp` range partitions because the table is append-only and naturally queried by time.
2. Completed outbox ledgers: consider only after completed/resolved cleanup exists. Keep pending, processing, retry, failed, and dead-letter rows in hot indexes and never partition them in a way that hides unresolved work from operators.
3. `event_contact_share_exports`: consider after PII-aware purge/export policy exists. Partition export items only with the parent export lifecycle.
4. `notifications`: consider only after read/archive retention rules are implemented and compliance notification categories are protected.
5. `email_dispatch_outbox` plus attempts/receipts: defer until parent-aware redaction/retention exists. Independent child partitioning is not allowed because attempts and receipts must follow parent evidence semantics.

Required implementation package before partitioning becomes current behavior:

1. A decision record naming the table family, partition key, partition interval, retention policy, and rollback plan.
2. An explicit PostgreSQL migration using `migrationBuilder.Sql(...)` or an approved migration extension. Do not hide partition DDL in entity configuration.
3. A preflight script that checks existing data fits the proposed partition bounds and reports rows that would fail routing.
4. A partition creation/attachment runbook. New partitions must be created before writes reach their date range.
5. A detach/archive/drop runbook. Detach before destructive drop when evidence value is uncertain.
6. Integration tests proving insert routing, partition-bound rejection, expected query predicates, and rollback/finalize behavior where feasible.
7. Backup/restore documentation covering parent and child partition tables.

Rollback posture:

- Prefer `DETACH PARTITION` over immediate drop for evidence-bearing tables.
- Do not implement destructive `Down()` behavior that silently loses retained evidence.
- If partitioning is disabled or rolled back, operators must have a tested path to keep accepting new writes without data loss.
- Retention cleanup and legal-hold checks must continue to operate by lifecycle class, not by partition name alone.

### Idempotency Cleanup Operations

The `idempotency-cleanup` readiness check reports the current cleanup posture:

- `Healthy` when cleanup is enabled in delete mode or dry-run mode.
- `Degraded` when cleanup is intentionally disabled.

The worker is explicitly instance/system-scoped because `idempotency_records` are an ephemeral replay cache. It does not delete protected audit, dead-letter, email-dispatch, notification, export, or source-of-truth rows. Use `IdempotencyCleanup:DryRun=true` before first enabling destructive cleanup in an environment, then watch logs and `Explore.Business` metrics:

| Metric | Bounded tags | Meaning |
|---|---|---|
| `explore.idempotency.cleanup_runs` | `mode`, `outcome` | One cleanup attempt in `dry_run` or `delete` mode, with `succeeded` or `failed` outcome. |
| `explore.idempotency.cleanup_rows` | `mode`, `outcome` | Eligible row count in dry-run mode or deleted row count in delete mode. |

Metric tags and logs intentionally exclude raw idempotency keys, request paths, response bodies, tenant IDs, and exception text.

### AI Retention Cleanup Operations

The `ai-retention-cleanup` readiness check reports the current AI retention cleanup posture:

- `Healthy` when cleanup is enabled in redaction mode or dry-run mode.
- `Degraded` when cleanup is intentionally disabled.

The worker is tenant-scoped by design. Each pass reads active tenant lookups, sets tenant context for one tenant at a time, resolves that tenant's `ai_assistant.retention_days`, and invokes the tenant-filtered retention cleanup primitive. It does not disable tenant filters and it does not log tenant IDs, prompt content, provider responses, selected references, proposed-action payloads, API keys, model IDs, or raw provider exceptions.

Use `AiRetentionCleanup:DryRun=true` before first enabling destructive redaction in an environment, then watch logs and `Explore.Business` metrics:

| Metric | Bounded tags | Meaning |
|---|---|---|
| `explore.ai.retention.cleanup_runs` | `mode`, `outcome` | One all-tenant cleanup pass in `dry_run` or `redact` mode, with `succeeded`, `partial_failure`, or `failed` outcome. |
| `explore.ai.retention.cleanup_rows` | `mode`, `category` | Aggregate eligible/redacted row counts for bounded categories such as `eligible_conversations`, `redacted_messages`, and `redacted_proposed_actions`. |

If a pass reports partial failure, inspect bounded worker logs and health configuration first. Do not enable verbose logging that prints prompt text, provider responses, tool payloads, reference summaries, or tenant identifiers.

---

## Custom Property Projections (Milestone D)

### What is it?

Custom property projections are denormalized, read-optimized rows derived from Layer 3 EAV runtime values. They keep discovery/search/filter query paths out of the raw normalized EAV joins.

**Source of truth:** `event_custom_property_definitions` + `event_custom_property_values` (and session equivalents).

**Projection tables:** `event_custom_property_projections`, `event_session_custom_property_projections`.

**Coordination tables:** `custom_property_projection_status` (rebuild tracking), `custom_property_projection_dirty_scope` (skip-on-contention backlog).

### What can go wrong?

| Symptom | Likely Cause | Recovery |
|---|---|---|
| Search/filter results are stale | Projection rows not updated after value write | Rebuild projection for the tenant |
| Projection rows missing for an event | Event created while rebuild was in progress; inline write skipped | Drain dirty scopes, or rebuild single event |
| Rebuild hangs | Advisory lock contention or long-running transaction | Check `custom_property_projection_status` for `Rebuilding` state; if stuck >10min, investigate PostgreSQL advisory lock waits |
| Dirty-scope backlog growing | Frequent rebuilds causing inline writers to skip | Drain dirty scopes; consider reducing rebuild frequency |
| Governance report shows stale data | Counts based on runtime definitions, not projection | No action needed; governance report reads from definitions, not projections |

### How to inspect

**Projection status:**
```
GET /api/admin/custom-property-projections/status?tenantId={tenantId}
```
Returns: `State` (Idle/Rebuilding/Failed), `LastRebuildStartedAt`, `LastRebuildCompletedAt`, `RowsProcessed`, `RowsFailed`, `LastErrorMessage`, `PendingDirtyScopeCount`, `OperationalState`, `RequiresOperatorAction`, and `RecommendedAction`.

`OperationalState` is intentionally bounded for dashboarding:

| State | Meaning | First action |
|---|---|---|
| `healthy` | Projection is idle and no dirty-scope backlog is pending | No action |
| `dirty_backlog_pending` | Inline writers skipped during rebuild contention and queued dirty scopes | Drain dirty scopes or run a tenant rebuild |
| `rebuilding` | Rebuild is currently active and not yet stale | Monitor until completion |
| `rebuild_stale` | Rebuild has been active for more than 10 minutes | Investigate PostgreSQL advisory-lock waits and worker health |
| `failed` | Last rebuild failed | Inspect `LastErrorMessage`, fix the root cause, then rebuild |

**Dirty-scope backlog:**
```
GET /api/admin/custom-property-projections/dirty-scopes?tenantId={tenantId}&projectionName=event_custom_property_projection
```
Returns: pending (un-drained) dirty-scope rows with creation timestamps and reasons.

**Projection rows for a specific event:**
```
GET /api/admin/custom-property-projections/events/{eventId}?exposureCeiling=Public
```
Use `exposureCeiling` when inspecting rows for public/export/moderation analysis. Public callers and generated-client consumers must not read raw projection rows without a ceiling.

**Projection rows for a specific event session:**
```
GET /api/admin/custom-property-projections/sessions/{eventSessionId}?exposureCeiling=Public
```

**Governance report (Rule 12):**
```
GET /api/admin/custom-property-definitions/governance-report?tenantId={tenantId}&scope=Event
```
Returns: all active Layer 3 definitions with flags, instance counts, and `PromotionRecommendation`.

### How to recover

**Full tenant rebuild (event projections):**
```
POST /api/admin/custom-property-projections/rebuild
Body: { "tenantId": "{tenantId}" }
```
Acquires advisory lock, rebuilds all projection rows, drains pending dirty scopes on completion. If lock is not acquired (another rebuild running), returns immediately with `lockAcquired: false`.

**Single event rebuild:**
```
POST /api/admin/custom-property-projections/rebuild-single-event
Body: { "eventId": "{eventId}" }
```
Refreshes all projection rows for one event inside a transaction.

**Drain dirty scopes without rebuild:**
```
POST /api/admin/custom-property-projections/drain-dirty-scopes
Body: { "tenantId": "{tenantId}", "projectionName": "event_custom_property_projection" }
```
Processes pending dirty-scope rows without triggering a full rebuild. Idempotent — returns `drainedCount: 0` if no pending rows.

**Session equivalents:** Replace `/rebuild` with `/sessions/rebuild`, `/rebuild-single-event` with `/sessions/rebuild-single`, etc.

### Concurrency model

- **Advisory locks:** Rebuild acquires a PostgreSQL advisory lock keyed on `fnv1a(projectionName), fnv1a(tenantId)`. Only one rebuild runs per projection per tenant.
- **Skip-on-contention:** Inline writers (triggered by value/definition changes) attempt the same lock. If contended (rebuild in progress), they upsert a `custom_property_projection_dirty_scope` row instead of blocking.
- **Drain-on-completion:** The rebuild worker drains all pending dirty scopes after completing its scan, so the skip window is bounded.
- **ConcurrencyStamp:** All mutable EAV entities carry an EF Core `ConcurrencyStamp` (`Guid`). `DbUpdateConcurrencyException` is translated to HTTP 409 with `code: concurrent_update`.
- **Quota errors:** Business quota breaches return HTTP 422 with `code: quota_exceeded`, `quotaKey`, `limit`, `scope`, and optional `actual`/`attempted` fields.
- **Admin purge:** Hard purge is separate from normal delete. It is admin-only, writes an audit summary, and is blocked when historical values, projection rows, audit references, or sync provenance exist.

### Metrics

Use these meters for custom-property projection and lifecycle dashboards:

| Meter | Metric | Safe dimensions |
|---|---|---|
| `Explore.Projections` | `explore.projections.rebuild_total` | `tenant_id`, `projection_type`, `lock_acquired` |
| `Explore.Projections` | `explore.projections.rebuild_failures_total` | `tenant_id`, `projection_type`, `lock_acquired` |
| `Explore.Projections` | `explore.projections.rebuild_duration_seconds` | `tenant_id`, `projection_type`, `lock_acquired` |
| `Explore.Projections` | `explore.projections.drain_total` | `tenant_id`, `projection_type` |
| `Explore.Projections` | `explore.projections.drained_scopes_total` | `tenant_id`, `projection_type` |
| `Explore.Projections` | `explore.projections.dirty_scope_skips_total` | `tenant_id`, `projection_type`, `operation`, `reason` |
| `Explore.Projections` | `explore.projections.quota_exceeded_total` | `tenant_id`, `projection_type`, `quota_key`, `scope` |
| `Explore.Business` | `explore.custom_properties.purge_decisions` | `tenant_id`, `scope`, `outcome`, `blocker_category` |

Do not add raw custom-property `Namespace`, `Key`, display names, event IDs, session IDs, or purge reasons as metric dimensions. Those values are high-cardinality and may expose tenant-specific semantics. Use admin API responses for targeted inspection instead.

### Hard limits

| Setting Key | Default | Platform Max |
|---|---|---|
| `custom_properties.max_definitions_per_tenant_per_entity_scope` | 500 | 5000 |
| `custom_properties.max_definitions_per_event` | 100 | 1000 |
| `custom_properties.max_definitions_per_event_session` | 50 | 500 |
| `custom_properties.max_options_per_definition` | 200 | 2000 |
| `custom_properties.max_multi_value_rows_per_value` | 20 | 200 |
| `custom_properties.projection_rebuild_batch_size` | 500 | 5000 |
| `custom_properties.max_dirty_scope_pending_per_tenant` | 10000 | 100000 |

### Governance report (Rule 12)

The governance report surfaces Layer 3 custom property definitions that may be candidates for promotion to Layer 2 (typed schema) or Layer 1 (universal core), using the Atlassian 4-question framework:

| Recommendation | Trigger |
|---|---|
| `None` | No search/filter/moderation/analytics flags set |
| `ConsiderProjectionFirst` | `IsSearchable` or `IsFilterable` is true |
| `ConsiderLayer2Promotion` | `IsModerationRelevant` or `IsAnalyticsRelevant` is true |
| `ConsiderLayer1Promotion` | `IsModerationRelevant` AND (`IsSearchable` or `IsFilterable`) AND used by ≥30% of tenant's events |

Review quarterly. Promotion is an operational decision, not an automated action.

## Configuration-manifest paid-policy operations

Before deploying a manifest containing `instance.paid_event_policy` or
`tenant.paid_event_policy`:

1. review the current instance paid-event policy through the authorized
   administration API and decide whether the manifest is the first bootstrap
   or a same-section rerun after Day 2 changes;
2. include only fields admitted by the governed typed documents; tenant policy
   must narrow the instance policy and callers never provide revision authority;
3. do not add
   provider, operator, buyer, refund-execution, or sale-control data;
4. validate against
   `schemas/configuration-manifest-v1alpha2.schema.json`;
5. mount the immutable file through the documented manifest path and run the
   one-shot migration/startup owner;
6. verify the scope-qualified operation audit and the instance/tenant
   paid-policy HAL resources
   before admitting traffic.

The startup transaction acquires the manifest lock, sorted instance-resource
locks, then sorted tenant/resource locks. It replays preflight against current
authority inside the serializable transaction. First bootstrap applies the
instance policy before tenant narrowing and records the resulting revision;
same-section reruns never reapply historical instance policy and bind new
tenants to the fresh active revision. A concurrent revision, policy collision,
invalid narrowing, or later audit/outbox write rolls back instance settings,
policy, tenant creation, tenant settings/documents, and operation evidence
together.

Do not retry by deleting payment, sale-control, checkout, or reconciliation
rows. Correct an invalid narrowing and rerun the unchanged instance section. A
changed post-bootstrap instance section fails closed and must be handled
through authenticated Day 2 administration or a complete disposable-
development reset. An already-created tenant is skipped wholesale; subsequent
policy changes use authenticated paid-policy administration. Provider handoff
and stop/review workflows remain independent runtime authorities and are never
replayed from a manifest.

Operational logs and metrics may record stable failure codes, operation IDs,
tenant IDs, lock outcomes, and revision numbers. Never record manifest payloads,
provider credentials, buyer acceptance data, payment references, email
addresses, or other PII.

## Event Add-On Operational Contract

Event-bound add-ons use six tenant-qualified persistence concepts:

- versioned catalog and catalog item;
- immutable registration-order add-on line;
- finite inventory allocation;
- fulfillment outcome;
- refund allocation.

Inventory mutation follows the canonical
`catalog-item > order > line > inventory > fulfillment > refund` fence order.
The PostgreSQL one-winner test uses independent contexts and an exact event
barrier; it contains no sleeps or timing-based pass condition. Serialization
failures may retry the entire transaction, so all IDs and timestamps are
created before entering retryable work.

Operational interpretation:

- `InsufficientInventory` means no line/allocation/effect survived;
- fulfillment replay returns the existing outcome and never delivers twice;
- `allocated_pending_provider` is a local refund allocation, not provider
  success;
- `provider_failed` reopens refundable quantity without releasing stock;
- `provider_confirmed_inventory_release_pending` preserves confirmed provider
  truth while recovery repairs the stock-release anomaly;
- add-on refund allocation, canonical `RefundAttempt`, and dispatch outbox
  commit atomically under the payment-capacity and add-on fence order;
- a local refund allocation never releases stock; only a later
  provider-confirmed refund transition may return add-on stock, and it never
  returns ticket capacity;
- tenant mismatch and missing resources converge on generic unavailable
  responses;
- BFF responses are private/no-store, write routes require cookie
  authentication plus antiforgery, and raw bearer tokens never reach the
  browser.

Troubleshooting must inspect stable operation IDs, outcome codes, and counts.
Never log capability headers, buyer identity, item descriptions, fulfillment
notes, refund payloads, or whole HAL resources. No add-on row, endpoint, job,
or recovery path has authority over admission tables.

---

## Local Identity Operations

The architecture and trust boundaries are defined in
[Authentication](AUTHENTICATION.md).

### Secret and topology preflight

For Local Identity, verify that:

* `AUTHENTICATION_PROVIDER=local`, or no deployment override is present;
* `AUTHENTICATION_LOCAL_JWT_KEY` resolves through the selected secret authority
  and Base64-decodes to at least 256 bits;
* `IDENTITY_DATABASE_TOPOLOGY` is `colocated` or `external`;
* external topology has distinct runtime and migrator credentials and the
  provider-specific connection settings resolve successfully.

Never print the JWT key, passwords, connection strings, Local credential
requests, or whole token claims.

### External Identity migration verification

Generate migrations only through EF tooling. Never edit a migration or snapshot
by hand. For each provider, select `ExternalIdentityDbContext`, the matching
migration project, and its provider-specific design-time startup project. The
generated migration must carry
`[DbContext(typeof(ExternalIdentityDbContext))]`.

Before deployment, run `dotnet ef migrations has-pending-model-changes` for:

* `Explore.Persistence` (PostgreSQL);
* `Explore.Persistence.Migrations.Sqlite`;
* `Explore.Persistence.Migrations.SqlServer`;
* `Explore.Persistence.Migrations.MySql`.

`Event.MigrationService` loads `/database/identity`, registers the external
context only when external topology is selected, applies its migrations first,
then runs the normal application post-migration sequence. A Local Identity
migration failure fails startup; it is never downgraded to colocated storage.
`Event.Standalone` invokes the same provider composition through a short-lived
migrator context before its in-process application migration and continues to
use separately registered runtime credentials for Identity stores.

### Runtime diagnosis

Use stable codes and bounded metadata:

* missing or invalid Local JWT material must fail startup or token issuance;
* invalid primary-provider IDs and configuration read failures block login;
* repeated password failures increment Identity access-failure state and
  eventually return a generic locked/unavailable result;
* registration synchronization failure returns no browser session;
* browser responses and logs must never contain the raw access token or
  password;
* provider switching must hide the previous provider from new-login discovery
  while old sessions continue through their original scheme.
