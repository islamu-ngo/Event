---
description: Select, bind, rotate, and recover secrets through fail-closed authorities.
---
<!-- ABOUTME: Operator guide for selecting, binding, and rotating external secret authorities. -->
<!-- ABOUTME: Explains fail-closed resolution and credential ownership without exposing secret material. -->

# Secrets Management

Secrets are external authority bindings, not application configuration values to copy between repositories or plaintext files.

The [local SecretKit package bundle](secretkit-local-packages.md) is available
for the upcoming secrets refactor. Vendoring the packages does not change the
authorities or runtime behavior described here.

---

## Approved Secret Authorities

The platform resolves sensitive credentials through one of three approved authorities (configured via `SECRET_PROVIDER`):

* **Environment** (`SECRET_PROVIDER=Environment`): Direct injection of variables from an uncommitted `.env` file or container orchestrator secrets. See [Environment Variables Reference](environment-variables.md).
* **Infisical** (`SECRET_PROVIDER=Infisical`): Centrally managed secret delivery using Infisical Universal Auth. See [Infisical Setup](infisical.md) for the project, folder tree, and machine identity.
* **Shared .NET User Secrets** (`SECRET_PROVIDER=UserSecrets`): Available **strictly in Development and Testing environments** for local developer isolation (see [Local Development](../contributing/local-development.md)).

> [!WARNING]
> User Secrets are strictly prohibited in production and will cause startup failure if selected when `ASPNETCORE_ENVIRONMENT=Production`.

---

## Prohibited Locations

Never store passwords, tokens, connection strings, encryption keys, or setup secrets in:

* Git source code or repository commits.
* [Configuration Manifests](configuration-manifests.md).
* Public container images or Docker build args.
* Application logs, OpenTelemetry traces, or health payloads.
* Public issue trackers or support tickets.

---

## Fail-Closed Authority Status

Runtime authority is also consumer-specific. API, BFF, Combined and migration
processes require independently scoped identities; a shared project is not a
shared permission grant. AppHost no longer fetches all provider/runtime secrets,
and `local-full` never changes Infisical into Environment. Provider bootstrap,
database and verifier inputs are injected only into their owning resources.
Follow the [consumer isolation cutover](infisical.md#consumer-isolation-cutover)
before removing old values or revoking old grants.

Stored bindings naming retired or foreign folders are rejected before fetching,
even if their logical setting key is valid or a previous value is cached.
Provider replies without the exact requested path, or containing reference
expressions, cannot activate configuration. Independently managed provider ACLs
must enforce the same boundary; code checks do not prove those ACLs exist.

The selected secret provider reports distinct operational states: `Unconfigured`, `Unavailable`, `Unauthorized`, or `Invalid`. If a provider cannot supply a credential, ISLAMU Event **fails closed**: it will never silently downgrade to a weaker fallback source or run with empty security keys.

First-run setup secrets generated in volumes must be retrieved securely and purged after onboarding (see [Setup Secret Recovery](troubleshooting-and-health.md#recipe-6-lost-setup-secret-recovery)).

---

## SMTP Credential Ownership

Email is disabled until explicitly enabled through `email.delivery_enabled`. Credentials
stay in the selected secret authority. Use `MAIL_SMTP_USERNAME` and `MAIL_SMTP_PASSWORD`
for instance SMTP; register tenant-scoped bindings for a tenant's own server. A tenant's
server cannot inherit instance credentials, even if the tenant has no credential bindings.
Without either binding the tenant transport is anonymous; providing only one credential
or failing to resolve a configured credential prevents delivery.

Changing Event SMTP does not configure Keycloak or the account provider's authentication
emails. Manage those providers' credentials and delivery settings with their own controls.

## External Data Protection Rings

Data Protection keys protect cookies, discovery cursors, persisted external
destinations, registration answers, report evidence and other protected values.
They now come exclusively from your selected external secret authority. A
database, Redis snapshot or local key directory alone cannot start a host.
Runtime never generates, writes, revokes or deletes root keys.

### Provision the correct partition

| Process | Prefix | Infisical ring folder | Infisical admission folder |
| --- | --- | --- | --- |
| Split API | `EVENT_DP_API` | `/runtime/api/data-protection` | `/admission/api/data-protection` |
| Split BFF/UI | `EVENT_DP_BFF` | `/runtime/bff/data-protection` | `/admission/bff/data-protection` |
| Combined Standalone | `EVENT_DP_COMBINED` | `/runtime/combined/data-protection` | `/admission/combined/data-protection` |

Each folder contains one value: `<PREFIX>_RING` or `<PREFIX>_ADMISSION`.
Environment uses those same variable names; shared User Secrets uses those names
in `event-shared-secrets`, only in Development/Testing. Infisical identities need
read-only access to exactly their two folders. Do not place extra values or secret
references there. No recursive acquisition or fallback is supported.

Every process also needs `<PREFIX>_ADMISSION_SHA256` and
`<PREFIX>_MINIMUM_GENERATION` injected through process environment from an
independently protected operator deployment record. These are mandatory even for
Infisical and User Secrets. The hash pins the exact approved admission document;
the positive integer is a floor that may only advance. Protect this control from
the administrators/jobs that can restore ring/admission or application backups.
Restoring all four values together would defeat rollback protection. The
application cannot certify that your external control is independently protected.

### Establish complete custody before first admission

The admission document is a separate complete historical-key and revocation
inventory, not a list the candidate ring may invent. Before adopting an existing
deployment, account for every previous API/Combined writer, ring version,
protected-data owner and retained backup. Compare key IDs and descriptor hashes
against that independent custody record. Preserve existing backend discriminator
`islamu-event` and the exact original keys when moving durable backend data.
Split BFF uses `islamu-event:bff:v1`, intentionally invalidating former Split
cookies; communicate sign-out and discard in-flight login flows during cutover.

There is no runtime compatibility reader for the previous database/Redis ring.
An existing installation must export and provision its complete ring through a
controlled offline custody procedure, then independently approve the first
admission pin. The JSON field contract is documented in the repository's internal
`SECRETS.md` and the operator source. Unwrapped framework AES-256-CBC/HMACSHA256
descriptors and explicit per-key revocations are supported. Other encrypted or
custom descriptors require an approved offline conversion, not a runtime
decryptor fallback. If complete historical custody cannot be established, do not
use the fresh-deployment command or claim that durable values are recoverable.

For a genuinely new empty deployment, the operator can explicitly attest that
there is no historical ciphertext. Run the native tool with arguments
`create api --fresh-empty-deployment` (or `bff` / `combined`). The executable is
built from `eng/security/Event.DataProtection.Operator/Event.DataProtection.Operator.csproj`.
It refuses creation when an existing pin/floor is present.

The tool writes a secret-bearing JSON envelope `{ "Ring": ..., "Admission": ... }`
only to redirected stdout. Connect it directly to your private provider intake or
protected process pipeline; do not capture it in CI logs, terminals, shell history,
repository files or deployment artifacts. Safe candidate hash/generation/deadlines
go to stderr. Do not mistake the printed candidate pin for independently protected
approval or a successful provider write. The tool itself makes no provider writes.

### Renewal, overlap and controlled activation

Existing-state operations read that envelope from redirected stdin and require
the current independently injected pin/floor. Commands are:

| Arguments | Effect |
| --- | --- |
| `status <partition>` | Validate pinned history and print bounded metadata only; not live readiness or completeness proof. |
| `renew <partition>` | Preserve material and revocations; advance generation and serving lease. |
| `prepare <partition> <activationUtc>` | Add a future 90-day key; activation is a round-trip `O` UTC timestamp 2–24 hours ahead. Retain current active key. |
| `activate <partition> <keyId>` | Select an already-active prepared key with a forward-generation record. |
| `rotate <partition>` | Add and immediately select a new 90-day key; requires coordinated maintenance/drain. |
| `revoke <partition> <keyId> --invalidate-dependent-values` | Add explicit revocation for a nonactive key; deliberately makes dependent payloads unusable. |

The tool issues a 12-hour serving lease, shortened to active expiry or prepared
activation. Runtime rejects leases over 24 hours. Arrange renewal and coordinated
restarts before lease expiry; there is no background key generation or live reload.
Prepared overlap gives every replica the future material before activation but
does not automatically authorize an active-key switch.

For each change:

1. Retain the latest protected pin/floor and all prior key/admission backups.
   Generate the candidate against that exact pinned history. Review the separate
   custody record and any planned deliberate invalidation.
2. Stop or drain all old replicas before changing admitted state. Publish the ring
   and admission to the selected authority and advance the independently protected
   pin/floor. Partial publication fails closed; it is not a rollback opportunity.
3. Start every replacement with the new expectations. Require every replica's
   `external-data-protection` health result to report the same intended generation
   and admission hash, and exercise its actual cookie/cursor/durable-value flows.
4. Route traffic only after that complete replica acknowledgement. Do not infer
   cluster convergence from one healthy process. Stop failed or stale replicas.

For future-key overlap, perform this procedure for `prepare`, then again at
activation with `activate`. Revocation requires a full drain of old processes:
publishing a revocation does not magically erase their in-memory key caches.
Readiness detects changed/unavailable authority and permanently fences the
probed process until restart. Unprobed processes retain their snapshot until
lease expiry; configure readiness probes and enforce drain centrally.

### Retention and recovery

An expired key is still a decryption key. Expired retained keys decrypt valid
payloads; revoked and missing keys do not. A capability's own expiry still applies.
Persistent destinations, registration answers and report evidence have no
cryptographic expiration, so their keys may be needed indefinitely, including
by backups. Even 180-day unsubscribe tokens are not a universal retention ceiling.

The ring is capped at 16 total XML documents, including revocations, and 32 KiB
of UTF-8 JSON. The admission document is separately capped at 32 KiB. Capacity
overflow stops provisioning/startup; **never remove retained keys to make space**.
There is intentionally no deletion/un-revocation command. A larger representation
or proven offline retirement needs a separately reviewed design.

Restore application data and key material without rolling back the current
independent pin/floor. `renew`/`rotate` can validate pinned historical inventory
after its serving lease expires and produce a forward record; they do not admit
an expired snapshot. A revoked key must remain revoked even when an older database
contains ciphertext that needs it. Missing keys require recovery from external
custody, never fabrication. See [Backup, Restore, and Upgrade](backup-restore-upgrade.md).

Do not delete old database/Redis key state or backups as part of cutover. Keep
them protected and offline until a separately authorized retention decision.

## Secret Rotation Procedure

Plan secret rotations as rolling service restarts. The platform requires process reload to rebind database and encryption credentials:

1. Generate the replacement secret in your selected authority (Environment or Infisical).
2. For database credentials, grant the new password concurrently before rotating.
3. Restart or redeploy the target containers (`event-api`, `event-ui`).
4. Verify `/health` reports all services as `Healthy` (see [Health Check Endpoints](troubleshooting-and-health.md#health-check-endpoints-reference)).
5. Revoke the retired credential in the backing authority.

---

## Related Guides & Next Steps

* **[Environment Variables Reference](environment-variables.md)** — Master catalog of all baseline and secret environment variables.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Inject secrets into production split containers.
* **[Keycloak Authentication](../security-and-identity/authentication.md)** — Securely bind Keycloak client secrets and database passwords.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Diagnose secret provider startup failures.
