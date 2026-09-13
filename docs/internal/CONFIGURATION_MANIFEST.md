<!-- ABOUTME: End-to-end guide to the instance-wide ConfigurationManifest contract, bootstrap lifecycle, export, and recovery. -->
<!-- ABOUTME: Gives operators, self-hosters, and contributors source-grounded workflows for using and extending the feature safely. -->

# Configuration Manifest

> **Audience:** Operators | Self-hosters | Contributors | Admins | AI agents
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-08-30
> **Source Anchors:** `src/Explore.Application/Features/ConfigurationManifest/`, `src/Explore.Domain/ConfigurationImportOperation.cs`, `src/Explore.Domain/ConfigurationDirectTransferSession.cs`, `src/Explore.Infrastructure/ConfigurationManifest/`, `src/Explore.Persistence/Repositories/ConfigurationImportOperationRepository.cs`, `src/Explore.API/Controllers/ConfigurationImportSessionsControllerBase.cs`, `src/Explore.Blazor.Client/Pages/Admin/Components/ConfigurationPortabilityWorkspace.razor`, `schemas/configuration-manifest-v1alpha2.schema.json`, `schemas/tenant-configuration-package-v1alpha2.schema.json`

The `ConfigurationManifest` is the strict, non-secret contract for Day 0
bootstrap and reviewed Day 2 configuration portability. Tenant administrators
use the separate `TenantConfigurationPackage`; neither artifact grants target
authority from source identities.

## Relationship To Headless Onboarding

The manifest and the Setup Assistant stay identity-free and offline-only. They
never own privilege, and they never own runtime connectivity. Headless
onboarding is separate: the seven `INSTANCE_BOOTSTRAP_*` keys, read from the
environment or the selected secret authority, name the first administrator, and
only a real provider sign-in grants that authority.

Order matters at startup. Migrations and seeding run first, the configuration
manifest applies second, serializable preparation third, and HTTP readiness
last, in both Split and Standalone. A manifest never completes onboarding, and
onboarding never rewrites the manifest.

See [CONFIGURATION.md](CONFIGURATION.md) for the key matrix and
[OPERATIONS.md](OPERATIONS.md) for lifecycle behavior.

## Start Here

Choose the path that matches your task:

| You need to... | Read |
|---|---|
| Understand what the manifest owns | [Mental model](#mental-model), [Authority boundaries](#authority-boundaries) |
| Create a first manifest | [Minimal manifest](#minimal-valid-manifest), [Practical example](#practical-single-tenant-example) |
| Run a self-hosted bootstrap | [Operator workflow](#operator-workflow) |
| Add tenants later | [Rerun behavior](#bootstrap-state-and-rerun-behavior) |
| Recover from a failure | [Recovery matrix](#recovery-matrix) |
| Export current configuration | [Whole-instance export](#whole-instance-export) |
| Import or roll back configuration | [Day 2 import](#day-2-import-and-tenant-migration), [Recovery matrix](#recovery-matrix) |
| Move tenant configuration | [Day 2 import](#day-2-import-and-tenant-migration), [Section coverage](#section-coverage) |
| Add a setting or document | [Contributor guide](#contributor-guide) |
| Find the owning code and tests | [Implementation map](#implementation-map), [Verification map](#verification-map) |

## Mental Model

Keep these distinctions explicit:

| The manifest is... | The manifest is not... |
|---|---|
| One versioned JSON document for one deployment instance | A separate manifest family for each tenant |
| Day 0 bootstrap plus reviewed Day 2 preview/apply/rollback | Continuous desired-state reconciliation without explicit managed ownership |
| A strict allowlist of non-secret settings and typed documents | A projection of every setting in `SettingRegistry` |
| Atomic instance-plus-tenant initialization | A patch mechanism for existing tenants |
| The same contract in single-tenant and multi-tenant deployments | A deployment-topology selector |
| Exportable as explicit overrides or flattened portable values | A database, secret, application-data, or disaster-recovery backup |

The most important rule is:

> Bootstrap establishes initial state. Day 2 imports always create a bounded
> session, recompute a server-authoritative preview, and apply selected sections
> atomically. A bootstrap rerun never restores historical values over current
> Day 2 state.

## Contract Identity

The public identity is exact and case-sensitive:

| Field | Required value |
|---|---|
| `$schema` | `https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json` |
| `apiVersion` | `configuration.islamu.org/v1alpha2` |
| `kind` | `ConfigurationManifest` |
| Schema artifact | `schemas/configuration-manifest-v1alpha2.schema.json` |
| Media type | `application/vnd.islamu.configuration-manifest.v1alpha2+json` |
| Conventional container path | `/etc/islamu-event/bootstrap/configuration-manifest.json` |

The schema URI is an immutable identifier. It is not currently a promise that
the schema is hosted at that URL. Use the checked-in schema artifact.

## Minimal Valid Manifest

Every map shown below is required even when empty. At least one tenant is
required.

```json
{
  "$schema": "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
  "apiVersion": "configuration.islamu.org/v1alpha2",
  "kind": "ConfigurationManifest",
  "metadata": {
    "name": "primary-instance"
  },
  "spec": {
    "instance": {
      "settings": {},
        "documents": {},
        "legalDocuments": {}
    },
    "tenants": [
      {
        "metadata": {
          "name": "default"
        },
        "spec": {
          "displayName": "Primary Community",
          "settings": {},
          "documents": {},
          "legalDocuments": {}
        }
      }
    ]
  }
}
```

This creates the absent tenant in `Provisioning` status. Tenant activation,
legal-identity readiness, provider setup, and other Day 2 workflows remain
separate.

## Practical Single-Tenant Example

This example configures portable instance defaults and one tenant override. It
contains no secret, provider credential, deployment path, or infrastructure
topology.

```json
{
  "$schema": "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
  "apiVersion": "configuration.islamu.org/v1alpha2",
  "kind": "ConfigurationManifest",
  "metadata": {
    "name": "community-production"
  },
  "spec": {
    "instance": {
      "settings": {
        "branding.display_name": "Community Events",
        "appearance.default_theme_mode": "system",
        "events.require_approval": true,
        "modules.islamic_enabled": true,
        "modules.tech_enabled": true
      },
      "documents": {},
      "legalDocuments": {}
    },
    "tenants": [
      {
        "metadata": {
          "name": "default"
        },
        "spec": {
          "displayName": "Primary Community",
          "settings": {
            "event_reporting.intake_enabled": true,
            "events.require_approval": true,
            "public_experience.mode": "DiscoveryCentric",
            "public_experience.event_catalog_label": "Events"
          },
          "documents": {
            "tenant.branding": {
              "schemaVersion": 1,
              "payload": {
                "displayName": "Primary Community",
                "logoUrl": "https://assets.example.org/logo.svg",
                "faviconUrl": "https://assets.example.org/favicon.svg",
                "customCssUrl": null
              }
            }
          },
          "legalDocuments": {}
        }
      }
    ]
  }
}
```

To configure multiple tenants, add more entries to `spec.tenants`. The reader
validates every entry before any manifest-owned write starts.

## Structural Rules And Limits

| Rule | Implemented limit or behavior |
|---|---|
| File encoding | Strict UTF-8 JSON |
| File size | Maximum `4,194,304` bytes |
| Tenant count | `1..256` |
| Manifest name | `1..100` lowercase ASCII letters/digits with single `-` or `.` separators |
| Tenant name/slug | `1..100` lowercase ASCII letters/digits with single `-` separators |
| Tenant display name | `1..500` characters |
| Unknown members | Rejected at every typed object depth |
| Duplicate JSON members | Rejected recursively before deserialization |
| Comments and trailing commas | Rejected |
| Maximum JSON depth | `16` |
| Maximum JSON tokens | `262,144` |
| Maximum properties per object | `512` |
| Maximum entries per array | `256` |
| Maximum property-name size | `256` UTF-8 bytes |
| Maximum string size | `65,536` UTF-8 bytes |
| Maximum number token | `128` bytes |

Values are not coerced. For example, `"false"` is not accepted where a JSON
boolean is required, and `"System"` is not accepted when the allowed enum value
is `"system"`.

`metadata.export` is optional on operator-authored input. The export subsystem
adds it to downloaded manifests to state whether values were flattened and
which authority was intentionally omitted.

## Authority Boundaries

### Explicit allowlists

The manifest catalogs are independent allowlists:

- instance catalog membership does not imply tenant catalog membership;
- tenant catalog membership does not imply instance catalog membership;
- adding a definition to `SettingRegistry` does not expose it to a manifest;
- a sensitive setting is rejected even before the generic allowlist error;
- free-form JSON settings are not admitted through the scalar catalog.

### Instance settings

All fields are optional, but `spec.instance.settings` itself is required.

| Key | JSON type | Constraints or purpose |
|---|---|---|
| `appearance.default_theme_mode` | string | `dark`, `light`, or `system` |
| `branding.custom_css_url` | string | Maximum 2,048 characters; runtime requires safe HTTPS |
| `branding.display_name` | string | Maximum 200 characters |
| `branding.favicon_url` | string | Maximum 2,048 characters; runtime requires safe HTTPS |
| `branding.logo_url` | string | Maximum 2,048 characters; runtime requires safe HTTPS |
| `events.group_submission_enabled` | boolean | Coordinated publication-policy mutation |
| `events.organization_submission_enabled` | boolean | Coordinated publication-policy mutation |
| `events.require_approval` | boolean | Coordinated publication-policy mutation |
| `events.user_submission_enabled` | boolean | Coordinated publication-policy mutation |
| `footer.lock_tenant_copyright` | boolean | Instance governance lock |
| `footer.lock_tenant_description` | boolean | Instance governance lock |
| `footer.lock_tenant_link_groups` | boolean | Instance governance lock |
| `footer.lock_tenant_social_links` | boolean | Instance governance lock |
| `footer.lock_tenant_template` | boolean | Instance governance lock |
| `groups.self_registration_enabled` | boolean | Instance default |
| `modules.islamic_enabled` | boolean | Instance default |
| `modules.tech_enabled` | boolean | Instance default |
| `organizations.self_registration_enabled` | boolean | Instance default |
| `organizations.tenant_can_omit_verification` | boolean | Instance governance default |
| `organizations.verification_required` | boolean | Instance default |
| `public_experience.event_catalog_label` | string | Maximum 100 characters |
| `public_experience.mode` | string | `DiscoveryCentric` or `OrganizationCentric` |
| `routing.default_public_home_page` | string | `EventList` or `LandingPage` |
| `tenants.self_service_registration` | boolean | Instance tenant-governance default |
| `tenants.white_labeling_enabled` | boolean | Instance tenant-governance default |

Branding URLs must be absolute HTTPS URLs with no embedded credentials, query,
or fragment. Empty strings are accepted as an unset value where the owning
setting permits them.

### Tenant settings

All fields are optional, but each tenant's `settings` map is required.

| Key | JSON type | Constraints or purpose |
|---|---|---|
| `appearance.default_theme_mode` | string | `dark`, `light`, or `system` |
| `event_reporting.intake_enabled` | boolean | Tenant-only reporting-intake authority |
| `events.group_submission_enabled` | boolean | Coordinated publication-policy mutation |
| `events.organization_submission_enabled` | boolean | Coordinated publication-policy mutation |
| `events.require_approval` | boolean | Coordinated publication-policy mutation |
| `events.user_submission_enabled` | boolean | Coordinated publication-policy mutation |
| `groups.self_registration_enabled` | boolean | Tenant override |
| `modules.islamic_enabled` | boolean | Tenant override |
| `modules.tech_enabled` | boolean | Tenant override |
| `organizations.self_registration_enabled` | boolean | Tenant override |
| `organizations.verification_required` | boolean | Tenant override |
| `public_experience.event_catalog_label` | string | Maximum 100 characters |
| `public_experience.mode` | string | `DiscoveryCentric` or `OrganizationCentric` |
| `routing.default_public_home_page` | string | `EventList` or `LandingPage` |
| `tenants.white_labeling_enabled` | boolean | Tenant override |

The publication-policy validator evaluates reporting intake, approval, and
submission settings as a complete proposed state. A combination that disables
reporting intake while leaving unsafe unapproved publication open is rejected.

### Typed documents

| Scope | Key | Schema version | Storage owner |
|---|---|---:|---|
| Instance | `instance.paid_event_policy` | `1` | Existing immutable paid-event-policy aggregate |
| Tenant | `tenant.branding` | `1` | Existing tenant typed-settings document |
| Tenant | `tenant.paid_event_policy` | `1` | Existing immutable paid-event-policy aggregate |

There is no generic instance-document table and no arbitrary JSON document
escape hatch.

If `tenant.branding` is omitted, tenant creation still writes the canonical
branding document using `spec.displayName`. If it is supplied, its nullable
`displayName`, `logoUrl`, `faviconUrl`, and `customCssUrl` fields overlay that
baseline.

### Paid-event policy document

Both paid-policy document keys use this payload shape:

```json
{
  "schemaVersion": 1,
  "payload": {
    "isPaymentsEnabled": true,
    "allowedOrganizerKindIds": [2],
    "requiresLocalVerification": true,
    "allowedCurrencyCodes": ["USD"],
    "defaultCurrencyCode": "USD",
    "refundProtectionIds": [1, 2, 3, 4, 5, 6, 7],
    "currencyRiskLimits": [
      {
        "currencyCode": "USD",
        "perEventSalesCeilingMinor": 10000,
        "perEventSalesCountCeiling": 100,
        "rollingOrganizerSalesCeilingMinor": 50000,
        "rollingOrganizerSalesCountCeiling": 500,
        "rollingOrganizerWindowDays": 30,
        "highValueReviewThresholdMinor": 5000
      }
    ],
    "requiresFirstPaidEventReview": true,
    "farFutureReviewThresholdDays": 90
  }
}
```

Organizer kind IDs are stable Domain IDs:

| ID | Kind |
|---:|---|
| `1` | User |
| `2` | Organization |
| `4` | Group |

All seven refund-protection IDs are currently mandatory:

| ID | Protection |
|---:|---|
| `1` | Organizer cancellation gives a full refund |
| `2` | Material change gives buyer choice or a full refund |
| `3` | Duplicate or incorrect charge gives a full refund |
| `4` | Substantial non-delivery requires a remedy |
| `5` | Buyer-change terms are disclosed subject to law |
| `6` | Card-dispute rights are not waived |
| `7` | Cancelled-event platform amounts are refunded by default |

Currency codes are canonical three-letter uppercase monetary codes. Every risk
limit must reference an allowed currency. Positive money values use integer
minor units.

Tenant policy can only narrow the effective instance policy. It cannot enable
payments, organizer kinds, currencies, risk ceilings, or other authority that
the instance policy does not permit. Callers cannot select an instance policy
revision; preflight binds the current revision internally and checks it again
after locks are held.

### Never manifest-owned

Do not put any of the following into a manifest:

- passwords, API keys, tokens, connection strings, secret references, signing
  keys, encryption keys, or provider credentials;
- database, cache, storage, SMTP, Keycloak, Cerbos, webhook, or deployment
  topology;
- instance operator identity, official status/origin, provider accounts, or
  payment handoff state;
- buyer acceptance, PII, registration answers, event/application data, audit
  payloads, or support evidence;
- sale control, dispute state, liability, negative balances, reconciliation,
  refund execution, or other operational payment state;
- caller-selected tenant IDs, instance IDs, aggregate IDs, or policy revisions.

Secrets continue to come from Infisical or `.env` as documented in
[SECRETS.md](SECRETS.md).

## Source Discovery And Modes

The startup contract uses:

```dotenv
CONFIGURATION_MANIFEST_MODE=Off
CONFIGURATION_MANIFEST_PATH=/etc/islamu-event/bootstrap/configuration-manifest.json
CONFIGURATION_MANIFEST_HOST_DIRECTORY=./deploy/bootstrap
```

`CONFIGURATION_MANIFEST_HOST_DIRECTORY` is Docker Compose/AppHost mounting
input. The application reads `CONFIGURATION_MANIFEST_PATH`; setting a path does
not mount a file.

| Mode | Reader behavior | Manifest-owned database behavior |
|---|---|---|
| `Off` | Does not discover or inspect a file; an invalid configured path is ignored | None |
| `ValidateOnly` | Reads and validates a discovered file | No manifest lock, audit row, outbox row, or configuration write |
| `Bootstrap` | Reads, validates, preflights, and applies | Atomic apply, audit, tenant results, and durable post-commit effect request |

If an explicit path is configured, it must be absolute and a missing file is a
startup failure. If no path is configured, the conventional path is used and
an absent conventional file is a no-op.

`ValidateOnly` is write-free for the manifest feature. The owning migration
process still performs its normal migrations and seeding before manifest
validation, so do not confuse "no manifest writes" with "the host performs no
startup work."

## Filesystem Trust Boundary

The reader accepts one bounded local regular file:

- absolute path when explicitly configured;
- no directory composition;
- no remote URL;
- no symbolic link or reparse point;
- regular file checked before and after the one read;
- read-only mounting recommended;
- readable by the non-root application UID;
- no raw path in safe startup failure output.

For containers, mount the directory read-only at
`/etc/islamu-event/bootstrap`. The repository reserves
`deploy/bootstrap/` for the host-side source.

## End-To-End Bootstrap Flow

The implemented control flow is:

1. The owning host finishes provider migrations and seeding.
2. `ConfigurationManifestStartupRunner` resolves mode and path.
3. `ConfigurationManifestReader` reads at most 4 MiB, checks the filesystem
   boundary, performs strict lexical JSON scanning, and computes the exact-file
   SHA-256 digest.
4. Source-generated JSON deserialization rejects unmapped members.
5. `ConfigurationManifestValidator` checks the envelope, explicit catalogs,
   types, documents, publication safety, and paid-policy narrowing.
6. `ConfigurationManifestCompiler` creates typed instance and tenant plans,
   deterministic key order, a full-file digest, and a canonical instance-section
   digest that is independent of JSON object insertion order.
7. Initial preflight reads bootstrap state, existing tenants, setting/document
   locks, publication state, and paid-policy authority without writes.
8. Bootstrap acquires the instance-manifest lease, sorted instance-resource
   leases, and sorted tenant/resource leases before opening a serializable
   transaction.
9. Preflight runs again inside the fresh transaction while every lease remains
   held.
10. Instance settings and instance paid policy apply first through their
    canonical in-transaction mutation boundaries.
11. Each absent tenant is created in deterministic slug order with its
    directory-operator identity, branding, settings, and optional paid policy.
12. The operation audit, per-tenant results, and payload-free outbox effect are
    committed in the same transaction.
13. Cache invalidation and setting notifications run after commit. In the split
    topology the one-shot owner leaves the effect in the durable general outbox
    for the runtime outbox processor.

No provider HTTP call, email, webhook, or other external side effect belongs in
the manifest transaction.

## Atomicity And Concurrency

The apply path uses one serializable transaction and a deterministic lock
hierarchy:

1. `!configuration-manifest.instance`;
2. sorted instance setting, publication, branding-governance, and paid-policy
   resources;
3. sorted tenant slug, tenant setting, and tenant paid-policy resources.

Locks are acquired before opening the serializable snapshot. This prevents a
waiter from validating against a snapshot taken before the competing writer
committed.

On a pre-commit failure:

- no partial instance setting survives;
- no partial tenant, branding document, tenant setting, or paid policy survives;
- no success audit or effect request survives;
- a bounded failure operation is recorded through a fresh context after
  rollback when the database is available.

If a post-commit cache or notification effect fails, the configuration remains
committed and the outbox item remains retryable. Do not roll back or re-create
the tenant to repair a post-commit effect.

## Bootstrap State And Rerun Behavior

The first successful bootstrap records:

- the full-file SHA-256 digest for operation provenance;
- a normalized SHA-256 digest of only `spec.instance`;
- bootstrap generation `1`;
- scope-qualified changed setting/document key names;
- created/skipped tenant counts and per-tenant results.

The instance-section digest, not the full-file digest, controls later bootstrap
eligibility.

| Situation | Result |
|---|---|
| First valid bootstrap | Apply instance state, create all absent tenants, record bootstrap state |
| Exact same file rerun | Do not reapply instance values; skip every existing tenant |
| Different full file, unchanged `spec.instance`, only existing tenants | Do not reapply instance values; skip tenants wholesale |
| Different full file, unchanged `spec.instance`, with new tenant slugs | Validate new tenants against current Day 2 instance authority and create only absent tenants |
| Changed `spec.instance` after first success | Fail with `configuration_manifest_instance_already_bootstrapped`; write no tenant state |
| Existing tenant has missing manifest-listed values | Still skip the tenant wholesale; do not fill or repair it |
| Day 2 instance policy changed since bootstrap | Keep original instance section unchanged; validate new tenants against the current active policy |
| New tenant conflicts with current locks or policy | Fail complete preflight; create no tenant |

This behavior prevents a startup artifact from becoming a hidden controller
that overwrites administrator changes.

## Startup Ownership

Exactly one process owns manifest application in each topology:

| Topology | Owner | Non-owner behavior |
|---|---|---|
| Docker Compose split | `event-migrationservice` | API receives no manifest environment or mount |
| Aspire `Split` | `Event.MigrationService` | API and Blazor do not own bootstrap |
| Aspire `Standalone` | `Event.Standalone` | Migration project is forced to `Off` for manifest processing |
| Direct standalone image | `Event.Standalone` | No helper process is required |

In all cases, migration/seed work completes before manifest processing, and
manifest processing completes before the owning web host can accept traffic.
A split API waits for successful completion of the one-shot migration service.

## Operator Workflow

### Prepare the source

1. Create `deploy/bootstrap/configuration-manifest.json`.
2. Start from the [minimal manifest](#minimal-valid-manifest).
3. Use a schema-aware editor with
   `schemas/configuration-manifest-v1alpha2.schema.json`.
4. Keep the source in operator-controlled version control or a protected
   configuration repository.
5. Do not put secret values in the file.
6. Make the file read-only and keep the containing directory searchable by the
   container's non-root UID.

The runtime is authoritative for strict UTF-8, duplicate keys, semantic
cross-references, locks, current-state authority, and bootstrap eligibility.

### Docker Compose split deployment

Set:

```dotenv
CONFIGURATION_MANIFEST_HOST_DIRECTORY=./deploy/bootstrap
CONFIGURATION_MANIFEST_PATH=/etc/islamu-event/bootstrap/configuration-manifest.json
CONFIGURATION_MANIFEST_MODE=ValidateOnly
```

Validate Compose interpolation:

```bash
docker compose config --quiet
```

Run the one-shot owner in validation mode:

```bash
docker compose run --rm \
  -e CONFIGURATION_MANIFEST_MODE=ValidateOnly \
  event-migrationservice
```

After validation succeeds, run bootstrap:

```bash
docker compose run --rm \
  -e CONFIGURATION_MANIFEST_MODE=Bootstrap \
  event-migrationservice
```

Then set `CONFIGURATION_MANIFEST_MODE=Off` and start or restart the runtime:

```bash
docker compose up -d
```

The Compose API depends on successful migration-service completion and never
receives the manifest mount.

### Local Aspire

Put the file at `deploy/bootstrap/configuration-manifest.json`, then run:

```bash
CONFIGURATION_MANIFEST_MODE=ValidateOnly \
CONFIGURATION_MANIFEST_HOST_DIRECTORY=./deploy/bootstrap \
aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj
```

Repeat with `CONFIGURATION_MANIFEST_MODE=Bootstrap` after validation. In the
default `Split` topology the migration project owns the manifest. With
`Hosting__Topology=Standalone`, AppHost transfers ownership to
`Event.Standalone` and forces the migration project's manifest mode to `Off`.

### Direct standalone container

Mount the source read-only and pass the canonical in-container path:

```bash
docker run --rm --name islamu-event-standalone \
  --env-file .env \
  --mount source=event_standalone_data,target=/app/data \
  --mount type=bind,src="$PWD/deploy/bootstrap",dst=/etc/islamu-event/bootstrap,readonly \
  -e CONFIGURATION_MANIFEST_MODE=ValidateOnly \
  -e CONFIGURATION_MANIFEST_PATH=/etc/islamu-event/bootstrap/configuration-manifest.json \
  -p 8080:8080 \
  islamu/event-standalone
```

Repeat with `Bootstrap`, then return to `Off` for normal restarts. The
standalone process applies migrations and manifest bootstrap before binding
HTTP.

### Confirm the outcome

Startup logs expose only safe operational facts:

- mode;
- API version;
- operation UUIDv7;
- digest prefix;
- byte length;
- tenant count;
- stable failure code.

They do not expose configuration values or the configured source path.

Persisted operation evidence is append-only:

| Provider family | Operation table | Tenant-result table |
|---|---|---|
| PostgreSQL / SQL Server | `configuration_manifest_operations` | `configuration_manifest_tenant_results` |
| SQLite / MariaDB / MySQL | `ie_configuration_manifest_operations` | `ie_configuration_manifest_tenant_results` |

Do not edit these rows. Use the operation ID and stable code for diagnosis, then
repair the source or authoritative Day 2 state.

After success:

1. set the mode to `Off`;
2. remove the runtime mount if the deployment no longer needs the source;
3. retain the original file securely with deployment records;
4. use Day 2 administration for subsequent configuration changes.

## Whole-Instance Export

### Surfaces

| Surface | Contract |
|---|---|
| API | `GET /api/control-plane/configuration-manifest/export?view=Overrides\|Portable` |
| API operation ID | `ExportConfigurationManifest` |
| Same-origin BFF | `GET /bff/control-plane/configuration-manifest/export?view=Overrides\|Portable` |
| UI | Instance administration configuration-manifest section |

The API has no caller-supplied instance or tenant ID. Trusted server context
selects the current deployment instance, and the export includes every active
tenant.

### Authorization

- authentication is required;
- tenant administrators cannot export the whole instance;
- instance authorization uses `InstanceSetting` `View` plus the explicit
  `configurationManifestExport` fact;
- local and Cerbos authorization consume the same fact;
- an unavailable configured authorization provider fails closed with `503`;
- HAL relations `export-configuration-overrides` and
  `export-configuration-portable` are the UI/BFF affordance authority.

The browser never follows an API URL supplied by HAL and never receives the API
access token. It starts only the fixed same-origin BFF route. The BFF rechecks
HAL, validates the downstream media type and filename, enforces the 4 MiB
limit, and returns a no-store attachment.

### Views

| View | Contents |
|---|---|
| `Overrides` | Explicit stored instance and tenant values in the closed catalogs; stored typed branding; active tenant paid policies when present |
| `Portable` | Effective resolved values for every catalog key, resolved tenant branding, and effective tenant paid policy |

Both views include the active instance paid policy and export metadata stating:

- authority scope is `InstanceAndTenants`;
- sensitive values were omitted;
- sovereign values were omitted;
- portable values were flattened when applicable;
- the fixed sovereign locked-field list.

Exports are deterministic and ordered by tenant slug and catalog key. They are
fully buffered before response bytes are sent. More than 256 active tenants or
more than 4 MiB fails without a partial file.

An export is suitable as a reviewed starting point for another bootstrap. It is
not sufficient to restore users, events, registrations, orders, payments,
secrets, outbox state, audit history, or provider state.

## Day 2 Import And Tenant Migration

Instance administrators import a v1alpha2 `ConfigurationManifest`; tenant
administrators import a v1alpha2 `TenantConfigurationPackage` into the tenant
selected by the authenticated route. Source tenant names and instance metadata
are provenance only and never select target authority.

Public configuration commands and queries execute through scoped native
handlers with authorization outside the business implementation. Tenant
package export and import preview share `ConfigurationManifestCurrentStateReader`
for trusted current-state reads; they do not call the protected whole-instance
export query and therefore do not require instance-administrator permission.
The public whole-instance export query retains its own instance authority.

Bootstrap and the native manifest command share the scoped
`IConfigurationManifestApplier` implementation, `ConfigurationManifestApplier`.
Startup uses that trusted application port directly, without resolving a
concrete native handler or depending on runtime authorization services. The
applier continues to own ordered locks, serializable writes and effect delivery.

`TenantConfigurationPackageSerializer` retains tenant-only authority and emits
the canonical `PaidEventPolicyAuthorityMetadata.SovereignLockedFields` omission
list in both export views. The tenant-package validator requires that same
list; an empty list makes an otherwise valid exported package non-importable.
These metadata names describe excluded authority, not portable credentials or
permission to change sovereign fields.

The administration workspace follows one server-owned state machine:

1. Upload an artifact of at most 4 MiB to create an expiring session. Keep the
   returned access token in the request header only.
2. Select only the section keys returned by the session, supply stable mappings
   and required approval codes, then request preview.
3. Resolve every blocking, warning, external-setup, and legal-review item. A
   stale or expired preview must be refreshed; it cannot be forced through.
4. Apply only through the advertised HAL relation. The server reacquires
   ordered mutation locks, re-exports current target state, and verifies the
   exact preview binding inside one serializable transaction.
5. Retain the receipt. It records selected sections, snapshot availability,
   fidelity digest, omissions, and typed post-commit effect status without
   configuration values.

An apply commits all selected canonical mutations, the protected pre-apply
snapshot, append-only operation evidence, and the payload-free effect outbox or
commits none of them. Cache refresh and other effects may remain `Pending`; the
configuration transaction is not replayed to repair an effect.

Rollback is forward recovery. The receipt advertises a rollback relation only
while its protected snapshot is available. Creating it produces a new import
session; an administrator must preview and apply that session against current
authority. History is never rewritten, and rollback never bypasses a newer
governance lock or paid-policy ceiling.

Tenant migration uses the tenant package export and import surfaces. Creating a
new target tenant remains a separate, explicitly authorized control-plane
action. The clone helper may link those two operations, but it does not grant
source authority, delete the source tenant, or migrate users, events,
registrations, orders, payments, secrets, or operational state.

## Section Coverage

`ConfigurationPortabilityRegistry` is the machine-readable authority for these
statuses. The standard v1alpha2 artifacts currently project only the six
implemented sections; unavailable sections fail closed instead of becoming
silent no-ops.

| Classification | Sections | v1alpha2 behavior |
|---|---|---|
| Supported instance | `instance.settings`, `instance.documents`, `instance.legal_documents` | Export, preview, diff, selected atomic apply, fidelity verification, forward rollback |
| Supported tenant | `tenant.settings`, `tenant.documents`, `tenant.legal_documents` | Whole-instance or tenant-package export, tenant-authorized preview/apply, fidelity verification, forward rollback |
| Declared but not serialized | `tenant.footer`, `tenant.navigation`, `tenant.templates`, `tenant.lookups`, `tenant.custom_property_definitions`, `tenant.localization`, `tenant.registration_policy`, `tenant.modules` | Omitted with `configuration_portability_section_not_serialized`; cannot be selected |
| Governed extension boundary | `extensions` | Base artifacts do not carry extension code; separately signed declarative packs require trusted issuer, license, compatibility, and payload validation |
| Secret/environment authority | `excluded.secrets`, `excluded.provider_bindings`, `excluded.deployment_topology` | Never exported or imported; configure target Infisical/`.env`, provider bindings, and deployment topology separately |
| Private/application authority | `excluded.pii`, `excluded.application_data`, `excluded.operational_state` | Never exported or imported; use privacy, application-data migration, and backup/recovery workflows |

Legal documents carry bounded localized Markdown source and provenance, never
publication or acceptance history. Import creates a draft revision for target
legal review. Raw HTML, remote resources, unsafe links, unresolved required
placeholders, and oversized content fail validation.

Managed ownership is opt-in and separate from ordinary import. Drift-only plans
preserve unmanaged fields; set, delete, relinquish, and takeover actions require
explicit ownership and consent. Scheduled apply requires distinct uploader,
reviewer, and applier identities and a fresh revision. The ordinary
`ReconcileManaged` import mode remains blocked until an approved ownership plan
is integrated, preventing accidental continuous overwrite.

Managed apply windows are durable target-qualified review records. An uploader
creates a schedule from a `PreviewReady` session using the header-only import
capability; a different authenticated reviewer approves it. The ordinary apply
request may then include `managedScheduleId`, and a third actor can apply only
inside the UTC window. Artifact, selected sections, mappings, approvals, mode,
and current target revision are rebound inside the same serializable import
transaction, so stale or mismatched schedules fail before mutation.

Direct transfer is an optional staging protocol, not a trust shortcut. It
requires distinct source and destination approvals, an HTTPS public destination
on port 443, nonce/proof/artifact binding, bounded resumable chunks, expiry,
cancellation, and replay-safe completion. Promotion only creates the ordinary
import session above; preview and apply remain mandatory, and source deletion is
never automatic.

## Recovery Matrix

### Day 2 session and operation recovery

| Condition | Safe action |
|---|---|
| Session or preview expired | Create a new session from the retained source artifact and preview against current target authority |
| Preview is stale | Refresh it; review changed categories, mappings, approvals, and target revision before applying |
| Apply failed | Selected state was rolled back. Keep the failure operation ID, repair the named dependency, and start from a fresh preview |
| Receipt effect is `Pending` or `Processing` | Keep the committed operation; restore outbox processing and observe the same receipt rather than replaying configuration writes |
| Receipt effect is `DeadLettered` | Diagnose the bounded effect failure, repair its dependency, and use the outbox recovery procedure; do not reapply the import |
| Forward rollback relation exists | Create the rollback session, preview the protected snapshot against current authority, and apply it as a new operation |
| Snapshot is unavailable or expired | Do not fabricate or edit history. Use a retained artifact plus current-state review, or restore the database and artifact authority from one consistent recovery point |
| Transfer is interrupted | Resume from the server-reported next offset with the same bounded session; cancel and restart if binding or expiry changed |
| Transfer completed but was not promoted | Promote it once into a normal import session, then preview and apply; never delete source state automatically |

Retain source artifacts and receipts according to the deployment's protected
configuration-record policy. The application retains rollback snapshots only
for the bounded `ConfigurationImportSessionLimits.SnapshotRetention` window;
operators must not treat that window as backup retention.
The platform-owned Quartz job
`configuration-portability-retention-cleanup` runs hourly in bounded batches
and deletes expired encrypted upload bytes, rollback snapshots, and abandoned
direct-transfer chunks while retaining value-minimized session and receipt
evidence.

### Source and ingestion failures

| Code or condition | Meaning | Safe action |
|---|---|---|
| `configuration_manifest_mode_invalid` | Mode is not exact `Off`, `ValidateOnly`, or `Bootstrap` | Correct the case-sensitive mode |
| `configuration_manifest_path_invalid` | Explicit path is not absolute | Use the canonical absolute container path |
| `configuration_manifest_file_missing` | Explicit source does not exist | Restore/mount the intended source or correct the path |
| Convention file absent with no explicit path | No manifest was discovered | No-op by design; configure an explicit path if absence must fail |
| `configuration_manifest_file_unreadable` | Permissions or I/O prevented the read | Fix non-root read access and mount health; do not broaden file contents into logs |
| `configuration_manifest_file_not_regular` | Source is a directory or non-regular object | Mount one regular file |
| `configuration_manifest_file_symlink_not_allowed` | Source is a symlink/reparse point | Mount the real file directly |
| `configuration_manifest_empty` | File has zero bytes | Restore valid JSON |
| `configuration_manifest_too_large` | File exceeds 4 MiB | Remove unsupported content or use Day 2 APIs; do not raise the bound casually |
| `configuration_manifest_json_invalid` | UTF-8 or JSON syntax is invalid | Correct syntax and rerun `ValidateOnly` |
| `configuration_manifest_json_limit_exceeded` | A structural scanner limit was exceeded | Simplify the file within the documented limits |
| `configuration_manifest_duplicate_property` | A duplicate key exists at some depth | Remove the duplicate; do not rely on last-write-wins parsing |

### Contract and authority failures

| Code | Meaning | Safe action |
|---|---|---|
| `configuration_manifest_contract_invalid` | Envelope, required shape, name, or export metadata is invalid | Compare against the checked-in schema and minimal example |
| `configuration_manifest_tenant_duplicate` | Tenant slugs are not ordinally unique | Give each tenant one canonical slug |
| `configuration_manifest_key_not_allowed` | Key is unknown or belongs to another scope | Remove it or use its authoritative Day 2/deployment surface |
| `configuration_manifest_sensitive_key_forbidden` | A secret-bearing key was attempted | Move the secret to Infisical or `.env`; never encode a reference in the manifest |
| `configuration_manifest_value_invalid` | JSON type, enum, length, or URL policy is invalid | Correct the typed value without coercion |
| `configuration_manifest_document_invalid` | Document key, schema version, or payload is invalid | Use the exact typed document contract |
| `configuration_manifest_cross_reference_invalid` | Publication or paid-policy state is internally inconsistent | Correct the complete proposed policy, not one symptom |

### Apply and lifecycle failures

| Code | Meaning | Safe action |
|---|---|---|
| `configuration_manifest_instance_already_bootstrapped` | `spec.instance` changed after successful bootstrap | Restore the original instance section and use Day 2 administration for the intended change |
| `configuration_manifest_bootstrap_state_invalid` | Persisted bootstrap evidence is malformed | Stop; inspect database integrity and restore from a consistent backup |
| `configuration_manifest_setting_locked` | Instance governance locks the requested tenant setting | Remove the tenant override or change the lock through its authoritative Day 2 workflow |
| `configuration_manifest_document_locked` | Tenant branding changes an instance-governed field | Remove the locked branding change |
| `configuration_manifest_paid_policy_unavailable` | No valid active instance paid policy exists | Establish or repair the active instance policy through its authoritative workflow |
| `configuration_manifest_paid_policy_stale` | Policy revision changed during planning/apply | Rerun from `ValidateOnly` against current authority |
| `configuration_manifest_paid_policy_broadening` | Tenant policy exceeds the instance ceiling | Narrow the tenant policy |
| `configuration_manifest_write_conflict` | A canonical mutation boundary rejected a concurrent/stale write | Reload current state, correct the source, and rerun |
| `configuration_manifest_apply_failed` | Transaction failed and no manifest configuration was applied | Use the operation ID and database health evidence; repair the dependency and rerun |

### Post-commit and export failures

| Condition | Safe action |
|---|---|
| Bootstrap committed but startup reports post-commit effect failure | Preserve the committed operation. Restore runtime/outbox processing and let the payload-free effect retry. |
| Failure audit could not be persisted | Treat startup as failed. Use correlation logs and database health evidence, restore database availability, and rerun. |
| `configuration_manifest_export_too_large` / HTTP `413` | Use supported Day 2 APIs or a database backup for recovery. Export emits no partial bytes. |
| Export returns `401` or `403` | Authenticate with instance authority; do not grant tenant admins cross-tenant export |
| Export returns `503 authorization_provider_unavailable` | Restore the configured authorization provider; no local fail-open occurs |
| BFF returns `502` | Downstream file metadata or size was invalid; inspect bounded API/BFF status without copying response secrets |

### Restores and lost source

- Restore the manifest source and bootstrap/audit database state from the same
  deployment lineage.
- Compare the retained source with the recorded digest before accepting traffic.
- If the original instance section is lost, audit intentionally cannot recover
  its values; restore the operator-controlled source or use a reviewed export
  and Day 2 evidence.
- Never synthesize audit rows, change the recorded digest, or force bootstrap
  overwrite.
- An export is not a substitute for the database and secret backup procedures
  in [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md).

## Contributor Guide

### Design rules

Before exposing a new field, answer:

1. Is it non-secret and free of PII?
2. Is it portable across supported deployment topologies?
3. Does a concrete Domain/Application owner already exist?
4. Is there a canonical transaction-aware mutation path?
5. Can it be exported without credentials, operational state, or sovereign
   authority?
6. Is the correct scope instance, tenant, or neither?
7. Does it interact with locks, publication safety, paid-policy ceilings, cache
   invalidation, or tenant creation?
8. Can validation complete before any write?

If any answer is unclear, do not add the field to the catalog.

### Add an approved scalar setting

1. Define or verify the canonical `SettingDefinition` and registry entry in the
   owning settings group.
2. Confirm its scope range includes the intended manifest scope and
   `IsSensitive` is false.
3. Add one explicit entry to `ConfigurationManifestCatalog.InstanceSettings` or
   `.TenantSettings`. Never derive manifest exposure from registry membership.
4. Add a maximum string length when the setting needs one.
5. If `RequiresCoordinatedMutation` is true, route through the existing
   coordinated mutation boundary. Do not downgrade it to a scalar repository
   write.
6. Add failing catalog, wrong-scope, type, sensitive-value, compiler, apply,
   export, and schema-drift tests as appropriate.
7. Regenerate the schema from source.

### Add an approved typed document

1. Start from a real Domain owner, validator, persistence model, and mutation
   boundary.
2. Add a typed payload contract with unmapped-member rejection.
3. Add a scope-tagged `ConfigurationManifestDocumentCatalogEntry`.
4. Extend validator, compiler/preflight, canonical mutation, and export mapping.
5. Preserve instance-before-tenant authority if the document constrains tenant
   state.
6. Extend deterministic schema generation; do not hand-edit the schema.
7. Add transaction, rollback, cross-scope, unsafe-field, export, and provider
   persistence tests.
8. Generate migrations for all affected providers only when the owning
   persistence model actually changes.

Do not introduce a generic JSON document entity to make one manifest feature
easier.

### Change bootstrap behavior

Bootstrap-lifecycle changes are Tier 1 security work. Preserve:

- one owner per topology;
- preflight before writes and again after locks;
- lock acquisition before serializable transaction snapshot;
- instance-before-tenant ordering;
- one transaction for state, audit, tenant results, and outbox;
- same-section rerun protection;
- whole-tenant existing skips;
- safe post-rollback failure recording;
- payload-free post-commit effects.

Use real PostgreSQL collision tests for every shared authority. Tests must
subscribe to explicit gates/events before triggering competitors; do not add
fixed sleeps or timing-based polling.

### Change export behavior

Preserve:

- instance-only authorization and Cerbos/local parity;
- server-selected current instance;
- all-active-tenant entity reads through the named filter bypass;
- closed-catalog output;
- deterministic ordering;
- safe branding revalidation;
- paid-policy narrowing;
- 256-tenant and 4 MiB preflight;
- no-store binary response;
- fixed same-origin BFF route;
- HAL-only UI affordances;
- generated OpenAPI and NSwag ownership.

Tenant-scoped portability uses the distinct `TenantConfigurationPackage`
contract; do not weaken a whole-instance manifest into a caller-selected
partial instance export.

## Implementation Map

| Layer | Responsibility | Primary sources |
|---|---|---|
| Domain | Immutable bootstrap/import evidence, forward rollback, transfer approval, and paid-policy invariants | `src/Explore.Domain/ConfigurationManifestOperation.cs`, `src/Explore.Domain/ConfigurationImportOperation.cs`, `src/Explore.Domain/ConfigurationDirectTransferSession.cs`, `src/Explore.Domain/PaidEventPolicyVersion.cs` |
| Application contract | Exact envelopes, typed documents, legal Markdown, and media identities | `src/Explore.Application/Features/ConfigurationManifest/Contracts/ConfigurationManifestV1Alpha2.cs` |
| Application catalog | Independent explicit instance/tenant allowlists | `src/Explore.Application/Features/ConfigurationManifest/Catalog/ConfigurationManifestCatalog.cs` |
| Validation | Envelope, types, sensitivity, cross-policy checks | `src/Explore.Application/Features/ConfigurationManifest/Validation/ConfigurationManifestValidator.cs` |
| Compilation | Typed plans, deterministic ordering, instance-section digest | `src/Explore.Application/Features/ConfigurationManifest/Compilation/` |
| Preflight | Existing-tenant disposition, lifecycle, locks, current policy | `src/Explore.Application/Features/ConfigurationManifest/Preflight/ConfigurationManifestPreflight.cs` |
| Apply | Lock hierarchy, serializable transaction, canonical boundaries, snapshots, receipts, and effect outbox | `src/Explore.Application/Features/ConfigurationManifest/Application/ConfigurationManifestApplier.cs`, `src/Explore.Application/Features/ConfigurationManifest/Importing/ConfigurationImportApplyService.cs` |
| Export state | Shared trusted snapshots beneath separately authorized instance and tenant operations | `src/Explore.Application/Features/ConfigurationManifest/Application/ConfigurationManifestCurrentStateReader.cs` |
| Persistence | Entity-first repositories, protected artifacts, append-only evidence, isolated failure recorder | `src/Explore.Persistence/Repositories/ConfigurationManifestOperationRepository.cs`, `src/Explore.Persistence/Repositories/ConfigurationImportOperationRepository.cs`, `src/Explore.Persistence/Repositories/ConfigurationImportArtifactStore.cs` |
| Infrastructure | Options, strict reader/scanner, startup runner | `src/Explore.Infrastructure/ConfigurationManifest/` |
| Hosts | One-owner post-migration/pre-traffic ordering | `src/Event.MigrationService/Worker.cs`, `src/Event.Standalone/Program.cs`, `src/Explore.AppHost/AppHost.cs` |
| API/export/import | Authority-scoped export, session, preview, apply, history, rollback, and transfer routes | `src/Explore.API/Controllers/ConfigurationManifestExportsController.cs`, `src/Explore.API/Controllers/ConfigurationImportSessionsControllerBase.cs`, `src/Explore.API/Controllers/ConfigurationDirectTransferController.cs` |
| BFF/client | HAL revalidation, fixed same-origin downloads, and accessible portability workspace | `src/Explore.Blazor/Extensions/BffConfigurationManifestEndpoints.cs`, `src/Explore.Blazor.Client/Pages/Admin/Components/ConfigurationPortabilityWorkspace.razor` |
| Generated schema | Closed deterministic editor/runtime contracts | `eng/configuration-manifest-schema/`, `schemas/configuration-manifest-v1alpha2.schema.json`, `schemas/tenant-configuration-package-v1alpha2.schema.json` |

## Verification Map

During development, run the smallest class that proves the changed seam. Build
the owning project first when necessary, then use TUnit's tree-node filter.

```bash
dotnet run \
  --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj \
  --configuration Release --no-build -- \
  --treenode-filter "/*/*/*ConfigurationManifestValidatorTests/*" \
  --minimum-expected-tests 1 --no-progress
```

| Concern | Test class or project |
|---|---|
| Contract and strict members | `ConfigurationManifestContractTests` |
| Catalog and allowlists | `ConfigurationManifestCatalogTests`, `ConfigurationManifestInstanceAuthorityTests` |
| Semantic validation | `ConfigurationManifestValidatorTests`, `ConfigurationManifestPaidEventPolicyAuthorityTests` |
| Deterministic compilation/digest | `ConfigurationManifestCompilerTests` |
| Lifecycle and current authority | `ConfigurationManifestPreflightTests`, `ApplyConfigurationManifestCommandHandlerTests` |
| Real rollback and rerun | `ConfigurationManifestAtomicPersistenceTests` |
| PostgreSQL lock ordering | `ConfigurationManifestConcurrencyTests`, `ConfigurationManifestCompetingWriterRedTests` |
| Audit/provider parity | `ConfigurationManifestAuditPersistenceTests`, `ConfigurationManifestAuditProviderMigrationTests` |
| Reader and startup | `ConfigurationManifestFileReaderTests`, `ConfigurationManifestStartupRunnerTests`, `ConfigurationManifestStartupCutoverTests` |
| Deployment ownership | `ConfigurationManifestDeploymentContractTests` |
| Export/API authorization | `ExportConfigurationManifestQueryHandlerTests`, `ConfigurationManifestExportControllerTests` |
| BFF boundary | `BffConfigurationManifestEndpointsTests` |
| UI/HAL/accessibility | `ConfigurationManifestExportSectionTests`, `ConfigurationManifestAdministrationAccessibilityTests` |
| Generated schema | `ConfigurationManifestSchemaGenerationTests`, `ConfigurationManifestSchemaArtifactTests` |
| Breaking-name ratchet | `ConfigurationManifestCutoverTests` |

Generate or verify the schema:

```bash
dotnet run \
  --project eng/configuration-manifest-schema/src/ISLAMU.ConfigurationManifest.SchemaGenerator/ISLAMU.ConfigurationManifest.SchemaGenerator.csproj \
  --configuration Release -- \
  --write manifest schemas/configuration-manifest-v1alpha2.schema.json

dotnet run \
  --project eng/configuration-manifest-schema/src/ISLAMU.ConfigurationManifest.SchemaGenerator/ISLAMU.ConfigurationManifest.SchemaGenerator.csproj \
  --configuration Release -- \
  --write tenant-package schemas/tenant-configuration-package-v1alpha2.schema.json

dotnet run \
  --project eng/configuration-manifest-schema/src/ISLAMU.ConfigurationManifest.SchemaGenerator/ISLAMU.ConfigurationManifest.SchemaGenerator.csproj \
  --configuration Release -- \
  --check manifest schemas/configuration-manifest-v1alpha2.schema.json

dotnet run \
  --project eng/configuration-manifest-schema/src/ISLAMU.ConfigurationManifest.SchemaGenerator/ISLAMU.ConfigurationManifest.SchemaGenerator.csproj \
  --configuration Release -- \
  --check tenant-package schemas/tenant-configuration-package-v1alpha2.schema.json
```

Never hand-edit:

- JSON Schema;
- EF migrations or model snapshots;
- OpenAPI;
- API contract inventory;
- generated NSwag client.

For a completed behavioral change, follow the matched intent's minimum project
gates and the layer-bounded policy in [TESTING.md](TESTING.md). Tier 1 or paid
policy changes also require the repository's adversarial, mutation, and review
evidence.

## Deferred Scope

The following behavior is intentionally not implemented:

- automatic managed-reconciliation execution from the standard import route;
- deletion or pruning without an explicit ownership plan and takeover consent;
- YAML;
- remote URL ingestion;
- directory or multi-file composition;
- secret references;
- remote source-to-target discovery or automatic source deletion after direct
  transfer;
- manifest-owned payment sale control, handoff, reconciliation, disputes,
  liability, or refund execution.

Do not imply any of these behaviors from the word "manifest."

## Related Documentation

- [Configuration](CONFIGURATION.md) - runtime configuration sources and exact environment keys.
- [Self-Hosting](SELF_HOSTING.md) - complete deployment topology and first-run setup.
- [Operations](OPERATIONS.md) - startup, health, outbox, and operational ownership.
- [Troubleshooting](TROUBLESHOOTING.md) - broader application incident diagnosis.
- [Secrets](SECRETS.md) - Infisical and `.env` secret ownership.
- [Payments](PAYMENTS.md) - paid-event policy and sovereign payment boundaries.
- [Backup, Restore, And Upgrade](BACKUP_RESTORE_UPGRADE.md) - real recovery and backup procedures.
- [Security Model](SECURITY-MODEL.md) - BFF, authorization, and tenant trust boundaries.
- [Testing](TESTING.md) - TUnit and provider verification policy.
