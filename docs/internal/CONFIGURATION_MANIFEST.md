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

Deferred startup composition registers the native bootstrap applier and deferred
effect strategy without the human mutation/preflight graph. The bootstrap owner
performs its own original-source validation; it does not borrow a human Settings
issuer. `ICoordinatedSettingMutationStore` therefore belongs to full runtime
persistence composition, where its genuine human preparation and selected-data
boundary are available. Immediate runtime import composition retains its existing
preflight, mutation boundaries and effect delivery services.

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

## Moderation Definition Portability Foundation

`ModerationConfigurationDocumentProjection` exports complete non-chat semantic
definitions and thirteen-dimension recipes without source jurisdiction, native
profile IDs, cases, people or publication/approval authority. Strict JSON and
explicit destination mappings feed the existing complete policy compiler.
`CanImport` requires valid definitions, matching recipes and supported selected
support; conversion always proposes a draft, never activation.

`ModerationConfigurationDocumentDiff.Compare` compares a retained preset baseline,
local definitions and incoming definitions. Local-only edits survive; identical
edits need no action; divergent edits to one semantic definition are conflicts,
including recipe changes and removals. Catalogue ordering is not a change.
Profile scope/subject/effect/strategy sets are canonicalized for comparison;
reordering them cannot manufacture a conflict with an independent local edit.
Recipe values and their required base/additive sequence remain exact.
The immutable result names the section and code/revision, not source authority;
comparison does not merge, write or authorize application.

`Merge` uses that complete comparison to preserve local definitions and apply only
nonconflicting incoming additions, replacements and removals to a new immutable
document. One conflict withholds the whole document; unrelated incoming changes
cannot be partially returned. Local-only definitions and original recipe values
survive. The merged document is still authority-neutral: native preview must
recompile the complete result and independently qualify baseline ownership,
destination mappings and current revisions before any coordinated publication.

The scoped `ModerationConfigurationImportAdapter` now has a native-only preview/
validate/export/apply path. Export acquires the same authorized current adopted
graph and strips native coordinates through the complete neutral projection.
Unsupported retained definitions remain exportable for review; export does not
claim destination import eligibility or operational readiness. An authorized
destination with no selected policy returns no document, not a default policy.
Preview acquires current native administrator, tenant, host,
epoch and selected policy entities in a serializable transaction. Its baseline
must be adopted predecessor history, not a supplied document or prepared
candidate. The binding covers caller/target, current heads, inventory, incoming
and merged content, and retained/reserved profile identities. Apply reacquires
that destination and recompiles the complete bound result.

The publication participant requires the coordinator's original serializable
transaction and a clean tracker: earlier sections must flush their own changes
first. It saves its draft graph and qualification head without committing, and
rejecting it rolls back the original transaction, including earlier flushed
sections. Standalone publication keeps its clean-unit ownership contract. An
imported draft does not move the independently active pointer or create
activation evidence.

The source wire contracts now carry an optional typed `moderation` document in
the instance, manifest tenant and tenant-package shapes. The source schema
generator describes the same complete definitions, named vocabularies and
required-nullable recipe members. Wire remains independent of Domain and
Application; its strict codec rejects unknown/private members, duplicate keys,
numeric enums and over-budget input. Both shipped manifest and tenant-package
schema artifacts are now regenerated natively from these complete source shapes.

`ModerationConfigurationExportService` restores independently acquired retained
history without exporting source readiness or native identities. Full-instance
export qualifies native platform administration and explicitly owned tenant
graphs; tenant export reads only the actual authorized request tenant. No tenant
impersonation or provider-session mutation is used. An absent installation or
selection omits optional meaning; malformed selected history fails closed.
`ConfigurationManifestCurrentStateReader` holds one serializable read through
artifact serialization. Import preview and fidelity use its original-transaction
participant methods when the coordinator already owns that transaction. Tenant
previews read a tenant package directly, never a global manifest trimmed afterward.

The artifact/session bridge persists a bounded
`ModerationConfigurationImportReservationSet`: the complete selected instance/
tenant destination set, exact mapped native tenant identities, distinct UUIDv7
operation identities and each destination's adopted baseline, reserved policy/
publication/profile identities and native binding. It stores neither EF entities
nor acquired permission. The approved preview also binds the
reservation digest to artifact, target, selection, mapping, approvals, mode and
expiry. Apply reloads that reservation, independently reacquires native authority
and history, recompiles and compares bindings in the original serializable unit.
Only exact coordinator-owned legal aggregates may flush before native publication;
foreign pending work or stale inputs reject and roll back the whole transaction.
Before the first moderation write, the native owner verifies every original
destination. Shared qualification epochs may advance only by the exact writes
already performed by this fanout; unrelated progress still conflicts. Its
transaction-bound completion rechecks the complete resulting set after operation
and outbox persistence, before the coordinator returns to the owning unit of work.
Existing native fences remain held through commit; this completion is not an EF
commit interceptor. Fidelity compares the actual post-apply artifact's instance
and mapped tenant neutral meanings with their merged proposals. Reservations are
limited to 256 tenants plus one instance definition and 1,100,000 characters.

Current-state artifacts now also include exact-scoped legal authoring definitions
through `LegalConfigurationSnapshotProjection`. Instance export reads only its
independently authorized active-tenant set; tenant export reads only the actual
request tenant. Projection preserves current-version locales, template provenance,
audience and fresh-acceptance meaning as portable Draft intent. It never exports
accountable/reviewer identities, source origin, approval or scheduling permission.
Publication authority remains native-owned. The import coordinator captures the
actual original serializable transaction coordinate through the legal repository,
then flushes only the exact legal graphs returned by the section applier before
moderation acquires its clean-tracker native owner. The flush never commits;
rejected ownership rolls back earlier sections. Wire enum-name legal keys now
resolve directly to the native enum rather than a catalog slug. Legal section
digests use native text/language/template normalization, sort locales ordinally
and compare Draft intent without accountable identity or effective-time proposals.
Nonempty jurisdiction assumptions and nonnull change summaries have no native
storage: their section receives
`configuration_import_legal_metadata_not_supported` in preview, and selected apply
rejects them before legal mutation rather than losing their meaning. Supported
round-trip source is integrated; execution and whole mixed-section assurance
remain deferred.

`ConfigurationPortabilityRegistry` now declares `instance.moderation` and
`tenant.moderation` as portable definition sections with export, preview, diff,
apply and verification source support. The instance descriptor admits only the
manifest; the tenant descriptor also admits a tenant package. Neither descriptor
grants deletion or unconditional rollback. Apply selection uses the same registry
gate as other sections; the complete native reservation and current authority
checks remain independently mandatory. These descriptors do not confer activation.
Whole-instance tenant moderation fanout source is integrated with qualified
per-tenant native ownership; runtime/provider assurance is unrun.
Rollback to an absent native policy is unsupported.
Existing-policy rollback snapshots retain neutral
prior meaning, not reservations or acquired authority. Provider migrations for
the nullable session reservation and complete runtime/rollback assurance remain
open. Schema behavior tests are unrun. All new witnesses remain unrun under the
programme's implementation-first override.

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
- free-form JSON settings are not admitted: JSON entries require an explicit
  `ConfigurationManifestStringArrayDescriptor` in the Application catalog.

### Event-resource policy arrays

Both instance and tenant settings (including `TenantConfigurationPackage`) accept
these four JSON arrays. Their immutable catalog descriptors are shared by
`ConfigurationManifestValidator` and the native schema generator; no shape is
inferred from a default value or added to the Domain setting registry.

| Key | String items |
|---|---|
| `event_resources.enabled_delivery_types` | Exact `EventResourceDeliveryTypeEnum` names: `StoredFile`, `ExternalLink` |
| `event_resources.enabled_audiences` | Exact `EventResourceAudienceKindEnum` names |
| `event_resources.permitted_file_types` | The closed PDF, DOCX, and PPTX MIME constants on `EventResourceGovernancePolicy` |
| `event_resources.external_origins` | Authoritative HTTPS origin strings, validated by the native Domain policy |

The schemas emit `type: array` and string `items`, with ordinal `enum` values for
the first three settings. Origins remain typed strings in the schema: native
validation additionally rejects unsafe or non-authoritative authorities, paths,
credentials, queries, and fragments. Schema validation alone is not a substitute
for native policy validation or coordinated non-widening tenant mutation.

Empty arrays are valid explicit empty permissions, not a request for defaults.
Duplicate items are accepted and collapse to sets in the native policy. Null,
objects, scalar values, and non-string items are rejected. The native parser
accepts only exact enum names, never case-folded, whitespace-padded, numeric, or
comma-combined tokens. Defaults and policy ceilings are unchanged.

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

The publication boundary's existing `RemoveOverride` behavior can clean up a
tenant override whose acquired system row is locked. Its original Settings
issuer now derives that exact Remove effect from the captured tenant Set and
locked source row before issuing the callback-scoped publication proof.
Unlocked rows remain Set; `Reject` remains a locked-policy failure. Instance
owners cannot borrow the cleanup behavior, and the original Set proof does not
retain Remove permission after the child callback ends. Compilation and the
coordinated store still validate the complete effective policy before writing.
Five authentic original-owner/expiry controls are authored and unrun.
Before projecting supplied mutations to owned keys, the publication boundary now
rejects foreign/instance-shaped tenant effects, tenant-shaped instance effects,
invalid kinds and incompatible Set/Remove payload shape. Supplied values still
cannot replace original prepared effects. Four authenticated shape controls are
authored and unrun; duplicate/unowned keys retain the private source rejection.

### Typed documents

| Scope | Key | Schema version | Storage owner |
|---|---|---:|---|
| Instance | `instance.paid_event_policy` | `1` | Existing immutable paid-event-policy aggregate |
| Tenant | `tenant.branding` | `1` | Existing tenant typed-settings document |
| Tenant | `tenant.paid_event_policy` | `1` | Existing immutable paid-event-policy aggregate |

There is no generic instance-document table and no arbitrary JSON document
escape hatch.

If `tenant.branding` is omitted, tenant creation still writes the authoritative
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

Currency codes are authoritative three-letter uppercase monetary codes. Every risk
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

### Original startup authority contract

PostgreSQL ValidateOnly uses the independently attested `validate_source`
entry, not the retained mutation `source` entry. Both use the declared Global
reader and Serializable image, but validation takes neither the manifest
advisory mutex nor source row locks and explicitly rolls back. Its preview
does not issue a mutation capsule. The genuine-origin regression holds that
validation read open at an exact observation event and requires an independent
backend to acquire the manifest mutex immediately.

Bootstrap audit mode and status comparisons use the EF model's provider values,
not the numeric C# enum ordinals. The source predicate, retained snapshot,
mutation shape and failed-history guard must agree with the mapped string
representation. Failed-history writes admit only `Failed`, never an `Applied`
receipt; a representation mismatch must not become an incidental SQL cast error
or weaken that guard. Failure-injection tests derive their status column/literal
from the same relational model and verify actual rollback evidence.

`ConfigurationManifestBootstrapOwnerFactory` coordinates mutation in C#: acquire
the enrollment gate, then normalized ordered setting-key session leases, before
opening the original Serializable transaction. Hold that custody through commit
or rollback. Waiting before BEGIN ensures a cooperating successor observes the
previous owner's committed image, rather than retaining an older snapshot.
Native reads do not request SHARE table locks, FOR UPDATE row locks, or a second
native advisory mutex. Fixed column permissions, scope checks, installed-body
attestation, exact before/after effects and affected-row checks remain unchanged.
Global receives function execution, not native-owner membership or broader direct
table permissions.

This is cooperative API coordination, not a database fence against every writer.
Direct database edits outside the C# protocol can race, including inserts of
previously absent settings. Serializable provides a consistent transaction image,
not fresh observations of arbitrary commits; exact-effect checks and constraints
do not promise to detect every such edit. Conflicts abort rather than replaying
an attempt automatically. Operators must use the API for configuration changes.
The native regression checks both retained API mutex custody and absence of the
old table/row fences; concurrency tests observe actual advisory-lock contention
between cooperating owners before asserting create-once/skip-second outcomes.

Bootstrap stores its effect envelope in the same original transaction as the
configuration, operation audit and tenant results. Production startup selects
`DeferredConfigurationManifestEffectDelivery`: it does not enumerate, claim or
settle effects on the bootstrap Global context. The ordinary Runtime outbox
worker admits the persisted operation/effect pair, selects the original
claim-bound Runtime scope, dispatches through `ConfigurationManifestEffectDispatcher`
and settles only the retained claim. A lost claim cannot authorize completion.
Consumer failure leaves durable retry/dead-letter work under the existing outbox
protocol; it must not turn a committed bootstrap into fabricated rollback evidence.

`ConfigurationManifestEffectPayload` is the versioned, key-only delivery contract.
The original C# compiler derives it from the complete Applied receipt and Created
tenant results, then stores it with those rows in the same physical transaction.
Version1 contains only the notification UTC time, changed instance setting-key
names, and Created tenant IDs with their changed setting-key names. It contains
no setting values, document bodies, secrets, source manifest, names or audit text.
Skipped tenants are excluded; a no-op replay does not create another effect.

The dispatcher consumes the exact persisted payload bound to its original claim,
not private operation/result reads or a substitute Global context. .NET10 strict
JSON metadata rejects duplicate/unknown/missing properties; C# additionally checks
the version, size, UTC time, canonical tenant identities/order and closed key
catalogues before any cache invalidation or notification. Applied-operation and
effect-ID admission, exact payload-byte digest, route and live lease checks remain.
The worker no longer re-derives metadata from later audit-row edits: the immutable
atomic payload is its delivery authority. Privileged database rewrites are not an
API workflow and are not repaired or silently normalized.

Manifest payload replacement is denied in both the repository's atomic portable
predicate and the existing native queue primitive, using the persisted route.
Other routes retain their required redaction/scrubbing capabilities. This adds no
private audit grants, schema columns, migration or privileged business routine.
Legacy null payloads and unsupported versions have no fallback. Keep the matching
application and native definitions together; exact body attestation still rejects
stale definitions rather than upgrading them implicitly. A rollback must restore
a coherent application/database checkpoint, not rewrite pending effects by hand.

`AddConfigurationManifestApplication` registers common services only. Startup
registration exclusively selects delivery timing and retains dispatcher/import
services and real human import admission for deferred delivery. Re-registering
common services must not silently restore an immediate claimer. Native origin
tests assert pending operation-linked envelopes, not completed worker delivery;
the genuine PostgreSQL startup-to-worker cohort remains a separate acceptance
requirement. See [Outbox pattern](OUTBOX_PATTERN.md) for worker lifecycle.

The Persistence module registers a scoped concrete
`ConfigurationManifestBootstrapOrigin`; its source-authority interface aliases
that same instance, and the internal executor and owner require the concrete
origin. Origin, executor, owner, real runner and post-migration sequence are
internal sealed Persistence implementations. The private invocation frame is
created by the actual runner and must remain active when the original source is
captured. Application exposes only the runner/sequence/exception contracts and
pure ingestion scanner. Infrastructure supplies reader/options validation and
the real HTTP-context accessor, not an issuer adapter. PostgreSQL migration and native-fixture
hooks install `PostgresConfigurationManifestBootstrapContract` and enroll only
the declared Global login in `event_manifest_bootstrap_global` with inherited
execution and no SET/admin option. This does not expose Global secrets to
MigrationService or grant Runtime owner membership.

These are source hooks, not executed installation receipts. The closed
concrete-origin dependency removes public factory reconstruction with a
substitute interface; authentic counterfeit-source/native regression and full
bootstrap source review remain open. Current descriptor-based file custody
supports Linux only and fails closed on other operating systems; this does not
establish cross-platform startup acceptance.

The bootstrap origination contract selects the closed deployment origin:
the actual post-migration startup invocation, its original `Bootstrap` mode,
selected regular file and exact database/namespace. The reader's SHA-256 digest
identifies bytes; it does not verify signatures or authenticate a signer.
Deployment configuration and filesystem custody therefore carry the operator
trust boundary. A public parsed read-result DTO, configuration selector or
database credential alone cannot issue mutation authority.

The security programme is implementing retained first-source custody and a
separate sealed nonhuman completion for this origin. Its audit actor remains
null rather than a fabricated human or `Guid.Empty`. Fixed manifest-only native
effects must complete on the original non-replaying physical owner using
selected Global credentials, without borrowing human Settings, import,
configured-administrator or setup-ticket authority. This contract is distinct
from authenticated Day 2 import. Runtime assurance remains deferred until the
programme implementation is complete; this section is not a passing receipt.

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
    deterministic key order, a full-file digest, and an authoritative instance-section
   digest that is independent of JSON object insertion order.
7. Initial preflight reads bootstrap state, existing tenants, setting/document
   locks, publication state, and paid-policy authority without writes.
8. Bootstrap acquires the instance-manifest lease, sorted instance-resource
   leases, and sorted tenant/resource leases before opening a serializable
   transaction.
9. Preflight runs again inside the fresh transaction while every lease remains
   held.
10. Instance settings and instance paid policy apply first through their
    authoritative in-transaction mutation boundaries.
11. Each absent tenant is created in deterministic slug order with its
    directory-operator identity, branding, settings, and optional paid policy.
12. The operation audit, per-tenant results, and versioned key-only outbox effect are
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
| Docker Compose split | `islamu-event-api`, after successful `event-migrationservice` completion | MigrationService receives neither manifest configuration/mount nor Global credentials |
| Aspire `Split` | `Explore.API`, after `WaitForCompletion(migrations)` | MigrationService and Blazor do not own bootstrap |
| Aspire `Standalone` | `Event.Standalone` | Migration project receives no manifest configuration; shared API startup does not run it twice |
| Direct standalone image | `Event.Standalone` | No helper process is required |

In all cases, migration/seed work completes before manifest processing, and
manifest processing completes before the owning web host can accept traffic.
A split API waits for successful completion of the one-shot migration service.
It then runs manifest startup with its existing selected Global composition
before configured-administrator preparation and HTTP readiness.
`ApiHostCompositionState.OwnsConfigurationManifestStartup` makes that ownership
explicit; Standalone passes false because its outer startup sequence already
performed migration and manifest work. Development API still performs its own
migration/seed sequence first. No Global credential is added to the migration
consumer. Direct API deployment must complete migration externally before
starting with Bootstrap; native capability attestation fails closed otherwise.

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

Start the migration dependency and API owner in validation mode:

```bash
CONFIGURATION_MANIFEST_MODE=ValidateOnly \
  docker compose up -d --build islamu-event-api
docker compose logs islamu-event-api
```

After the API reports validation success, recreate its startup with bootstrap:

```bash
CONFIGURATION_MANIFEST_MODE=Bootstrap \
  docker compose up -d islamu-event-api
docker compose logs islamu-event-api
```

Then set `CONFIGURATION_MANIFEST_MODE=Off` and start or restart the runtime:

```bash
docker compose up -d
```

The Compose API depends on successful migration-service completion and owns
the read-only manifest mount. Validation does not terminate the API host;
after successful bootstrap, returning mode to Off restores ordinary startup.
These commands are the updated workflow, not an executed acceptance receipt.

### Local Aspire

Put the file at `deploy/bootstrap/configuration-manifest.json`, then run:

```bash
CONFIGURATION_MANIFEST_MODE=ValidateOnly \
CONFIGURATION_MANIFEST_HOST_DIRECTORY=./deploy/bootstrap \
aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj
```

Repeat with `CONFIGURATION_MANIFEST_MODE=Bootstrap` after validation. In the
default `Split` topology the API owns the manifest after migration completion. With
`Hosting__Topology=Standalone`, AppHost transfers ownership to
`Event.Standalone`. The migration project has no manifest configuration.

### Direct standalone container

Mount the source read-only and pass the authoritative in-container path:

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
| API | `GET /api/admin/instance/configuration-manifest/export?view=Overrides\|Portable` |
| API operation ID | `ExportConfigurationManifest` |
| Same-origin BFF | `GET /bff/admin/instance/configuration-manifest/export?view=Overrides\|Portable` |
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

The trusted export reader selects the bounded active tenant catalog, then
`ConfigurationManifestTenantReadScopeFactory` acquires a fresh exact Runtime
lease for each selected tenant. Both repositories and hierarchical/typed-document
resolvers are reconstructed inside that scope, so an ambient tenant cannot hide
another selected tenant's values. Scope-local execution facts leave the request
tenant and its existing context unchanged. Tenant-package exports select only
their authorized target instead of reading every tenant's configuration.

Import snapshot reads are a distinct ownership path: `ReadImportSnapshotAsync`
uses the import owner's dependencies so fidelity checks can see uncommitted
authoritative mutations. They must not switch to independent read connections
or split the multi-tenant import transaction. The transaction-enlisted import
write/read capability remains separate from the export-only acquisition port;
neither is enrolled by naming convention in policy mutation command dispatch.

Instance preview and apply enter through `IConfigurationInstanceImportExecutor`.
Its Persistence implementation selects a dedicated sealed-null Global owner
before resolving the private import graph. `ConfigurationImportApplyService`
still owns the single serializable apply transaction; the executor does not
wrap it in another transaction. The request's Runtime lease stays unchanged.
`PostgresConfigurationImportSnapshotContract` supplies fixed catalog setting,
document, and active paid-policy reads on that owner connection. Its policy
restoration preserves stored identity/version and reconstructs validated domain
children instead of creating replacement policy identities.

The snapshot's latest applied bootstrap metadata is read through the fixed
`configuration_import_snapshot_bootstrap(uuid)` function, not a raw Global
table query. `ConfigurationManifestOperationRepository` selects this path only
for the sealed PostgreSQL instance-import owner. It retains the same connection
and transaction, restores the full persisted operation, and preserves ordering
by bootstrap generation, completion time and operation ID. Runtime attests the
function body and authority; installation refuses drift. The snapshot owner has
column-level read privileges while Global receives only function execution.
Runtime assurance for this repair remains pending.

Before apply acquires tenant locks, `TenantRepository.GetBySlugsAsNoTrackingAsync`
uses the fixed `configuration_import_snapshot_tenant_slugs(uuid,text[])` read on
the sealed PostgreSQL import owner. The request is bounded by the manifest
tenant limit and requires distinct non-empty slugs. This selector includes
inactive matching targets, preserving the original repository semantics so
later eligibility checks can reject them rather than misreporting them as
missing. It does not grant Global direct tenant-table reads or switch owners.
Function source and authority drift reject both execution and reinstallation.

Current legal source is exported under the wire kind names (for example,
`TermsOfService`), not the lower-case public route codes. Snapshots include
the current version's audience, localizations, template provenance and fresh
acceptance requirement, with `ReviewRequired` import intent. They omit review
identities and publication/acceptance history. A published-only query cannot
verify an imported draft or preserve its source in a rollback artifact.

`ConfigurationImportSectionApplier` persists each Domain-created imported draft
or revision through `ILegalDocumentRepository.SaveImportedAsync` before fidelity.
The owning serializable transaction still controls commit or rollback. PostgreSQL
uses `PostgresConfigurationImportLegalMutationContract` on that same connection:
an exact-target aggregate read precedes an append-only write with original
stamp/version/lifecycle comparison. Historical versions/publications stay
unchanged, new source stays `ReviewRequired`, and the importing actor receives
creation/update audit attribution. No published or acceptance state is imported.

`PostgresConfigurationImportReceiptContract` appends terminal instance receipts
and their exact `ConfigurationImportEffectsRequested` messages on the same
serializable owner transaction. It validates the consumed session's preview
digests, snapshot retention and source receipt; PostgreSQL `xmin` proves the
session/snapshot/receipt were written by that transaction. The effect must match
the receipt's effect ID, completion timestamp and public selected-setting
notifications. Failed evidence alone may use a new transaction on the same
selected connection after rollback. No generic Global receipt/outbox write
grant or second success transaction is introduced.

Resource setting notifications use the registered `TimeProvider` passed by
`SelectedEventResourceSettingsWriter` to its selected human writer. Their UTC
timestamps therefore share the import coordinator's clock and remain inside the
receipt interval. A mixed batch retains its original captured `OccurredAt`.
Native timestamp precision normalization and strict notification provenance
validation remain unchanged.

Snapshot resolution requests authoritative reads for both scalar settings
and typed documents: only the requested catalog keys are read, shared caches
are neither consumed nor populated, and fidelity can see the owner's
uncommitted changes without prematurely publishing them.

An export is suitable as a reviewed starting point for another bootstrap. It is
not sufficient to restore users, events, registrations, orders, payments,
secrets, outbox state, audit history, or provider state.

## Day 2 Import And Tenant Migration

Import application owns cooperative coordination before opening its transaction:
`ConfigurationImportCoordination` retains Human then Discovery custody on the
selected context through settlement. Setup acquires those gates before its
enrollment-generation lock; compatible inner calls borrow rather than reacquire.
Failure history is written only after an acknowledged rollback and after owned
gates are released, using the original request credential binding.

Setup apply uses `ISettingMutationLock.ExecuteOrderedGroupsOnceAsync` around
`ISerializableAttemptOwner`, not a replaying lock or unit-of-work entrypoint.
This selects execution behavior only: it does not bind a human-authority scope
or convert internal Setup writers into human mutation writers. Human-only commit
authorities are resolved lazily on the selected human scope before BEGIN; simply
constructing the shared import service never fabricates that scope for Setup.
Setting leases
are all released in reverse order even if one release fails. Generation and
outer coordination cleanup retain the primary exception and cleanup causes.
If commit acknowledgement is lost, the result remains
`HumanMutationOutcomeUnknownException`: do not retry the prepared operation,
persist a contradictory failed receipt, or deliver effects from that attempt.
Committed configuration, consumed session, receipt and outbox remain atomic.
Effect delivery starts only after outer generation and coordination leases end.
After commit is acknowledged, cleanup or receipt-mapping failure is a committed
operation requiring investigation, not an unknown commit or failed apply. Never
append a second failed receipt or replay it to conceal the cleanup failure.
The original EF transaction owner also preserves that distinction during
`DisposeAsync`: `TransactionCommittedCleanupException` means COMMIT returned
success before disposal failed. It is not a retryable provider conflict, even
when its cleanup cause resembles one. If acknowledgement was lost instead,
transaction disposal and later coordination releases retain the original
`HumanMutationOutcomeUnknownException` and every cleanup cause. Import handling
must propagate either classification before its failed-history catch path.

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
the authoritative `PaidEventPolicyAuthorityMetadata.SovereignLockedFields` omission
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

An apply commits all selected authoritative mutations, the protected pre-apply
snapshot, append-only operation evidence, and the typed effect outbox or
commits none of them. Cache refresh and other effects may remain `Pending`; the
configuration transaction is not replayed to repair an effect.

### Original human import publication custody

The genuine import coordinator retains the first resolved mapping target IDs
and the section compiler's first complete publication image before fresh
preview validation. The image includes all fixed policy inputs, stored
identities, absence, value/lock/type metadata and audit fields, not a baseline
sampled later by the writer. Ordered locks close the complete publication key
family before the transaction. Accepted internal
`HumanConfigurationImportPublication` capsules bind the original subject,
operation, target, artifact and exact effects to the actual import issuer.

`ApplyImportInCurrentTransactionAsync` uses the existing publication compiler,
safety evaluator and tenant locked-system Reject behavior with a separate
callback-limited writer admission. It does not borrow ordinary human Settings
preparation. The selected writer compares retained preimages, verifies exact
postimages and records private completion for every accepted slice on the same
context, connection and Serializable transaction. Both callback exit and the
original commit finalizer require those completions. Blocking source reads
finish before terminal credential, support and retained-session time checks.

PostgreSQL instance imports use the fixed native publication `source` and
`write_original` functions. Their expected bodies and execution catalogs are
independently attested; a recomputed comment stamp cannot substitute a changed
body. The old raw `write` is owner-only and no longer executable by the Global
group. The existing migration and native fixture hooks install the updated
contract; no new credential, source registration or fallback factory is needed.
Installation and the ten authored regression cases remain unexecuted under
the programme's deferred-assurance rule. This source integration is separate
from the nonhuman bootstrap origin and grants no general restore capability.

Live-import effect delivery validates the fixed aggregate/event envelope before
loading its receipt, then requires the receipt's `EffectOutboxId` to match the
message and its status to be `Applied` or forward-rollback `RolledBack`.
`ConfigurationImportEffectDelivery.DispatchClaimedAsync` performs these checks
before cache invalidation or persisted notification dispatch. A failed receipt
or mismatched message cannot publish effects. Rejection retains the existing
claim-fenced failure/retry handling; it does not rewrite receipt history or
reapply configuration.

Bootstrap tenant-result replay uses `configuration_manifest_replay_results(uuid)`
on PostgreSQL, not a cross-tenant EF query over the result table. The function
qualifies one applied bootstrap operation, rejects more than 256 results, and
returns only safe result identity/status/key-name metadata; created and skipped
outcomes retain their original meanings. Runtime and Global receive separate
execute-only groups, with no table DML or owner/migrator membership. Portable
providers apply the same operation-status check and explicit result bound.
The existing post-commit dispatcher still owns cache/notification effects;
this reader neither repeats import writes nor changes the caller's tenant lease.

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

### Setup secret-write purpose admission

`SetupSecretBindingWriteRequest` admits only `setup.signing` and
`setup.encryption`. `SecretDefinitionRegistry` declares both as optional,
instance-only Infisical targets in `/setup`; their default provider names are
`SETUP_SIGNING` and `SETUP_ENCRYPTION`. The required registry environment-name
metadata does not enable Environment or User Secrets bindings for these targets.
`SetupSecretBindingWriter` retains its selected-authority, stored binding ID/key,
scope, qualifier, environment, and canonical folder checks before provider work.
Existing API/Combined `/setup` admission applies; BFF access remains denied.
The commitment HMAC definition is a separate server-only purpose, not a
substitute target key. No credential value or fallback authority is introduced.

### Email policy mutation database profiles

The catalogue includes the already-declared instance and tenant email-policy
mutation credentials. `DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_USERNAME` and
`DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_PASSWORD` bind
`Database:EmailPolicyInstanceMutation`; the corresponding
`DATABASE_EMAIL_POLICY_TENANT_MUTATION_*` pair binds
`Database:EmailPolicyTenantMutation`. Their activation is `server-database-config`,
with no safe defaults. Split Compose projects them through the API-only
`database-runtime-env` anchor, preserving the separate selected native mutation
owners rather than borrowing Runtime, Global, or Migrator credentials.

### Operator editor and portable identity authority

Operator save and manifest import retain their actual immutable command.
`OperatorIdentityMutationAuthority` accepts either the original human
administrator with current credential/S/P/route-when-annotated/H/support custody,
or the real setup ticket/secret and unchanged pending state. A human ticket is
not relabeled as setup. Save is unannotated; import retains its original
`InstanceSetting/update` decision. Bootstrap completion's create-only operator
seed remains a separate source and capability.

The actual roots register this authority and `OperatorIdentitySettingsWriter`.
The writer pins the Global context/lease/connection and one Serializable attempt,
verifies the complete setting row, readiness projection and import outbox fields
before final deadline checks, and publishes notifications only after settlement.
Effect time uses the root TimeProvider; PostgreSQL precision is selected before
the capsule freezes. The same instant feeds row, audit and notification metadata.
Credential and support deadlines retain full precision.

The dedicated PostgreSQL editor contract uses existing enrolled Global/Migrator
identities and constrained NOLOGIN roles, not a generic scalar grant or bootstrap
purpose. Parameterized native JSON projection canonicalizes the audit on the
original idle connection before freezing; terminal comparisons remain exact.
Role graph, function/body/ACL/search-path, schema/column/collation and unique-key
attestation rejects drift without repair. Actual migrator and shared fixture
install it after the enrolled Email-delivery-policy contract.

This is source integration: 29 original-source and 43 native/authentication
controls plus migrated fixtures are unrun. Native PostgreSQL setup-secret HTTP
acceptance is not claimed. Other outstanding native-purpose and programme
assurance gates remain open.

### Provider configuration original owners

Authentication and authorization provider configuration retain their actual
setup or administrator commands. The first private observation selects the
original owner before validation and retains rows/absence, metadata, bindings
and participation sources. The human path acquires current principal/policy/support sources and
instance-administrator authority, and validates those sources and credential/
support deadlines at settlement. These commands have no annotated PDP action;
configuration does not synthesize an ordinary Settings route decision.

Setup requires the accepted setup-secret ticket, unchanged request/secret and
the exact pending bootstrap snapshot. Configured startup retains its distinct
declaration/material source, not a fabricated human or setup request.
`ProviderConfigurationMutation` compiles canonical identities, data timestamps,
metadata, values, locks and audit before the physical attempt.
`ProviderConfigurationSettingsWriter` consumes them on the original non-replaying
Serializable unit of work. The existing visitor participation validator hands
only those closed effects to the fixed writer; its ambient EF persistence is
not used for provider configuration. Private completion and full effect/audit
checks are mandatory before settlement. Setup never
gets public human-repository mutation permission. Source/effect checks use the
existing terminal callback collection, which is cleared on settlement/pool return.
Endpoint preflight stays outside source leases; notifications and cache
activation follow successful settlement. This is source integration, not
executed provider or runtime acceptance.

SQLite and PostgreSQL source paths are authored. PostgreSQL uses dedicated
roles/functions, independent source/catalog/body attestation and a preserved
`provider_configuration_source` singleton. Exclusion coexists with storage
configuration/probe triggers; runtime drift is denied without repair. Data time
is represented before freezing while original credential/support deadlines keep
full precision. SQL Server/MySQL and other unsupported providers fail closed.
The 64 native/signed cases and nine relocated real-boundary handler scenarios
are unrun; no SQL installation, concurrency or provider acceptance is claimed.

Email disable preview is a database observation on the dedicated Email owner,
not an SMTP connectivity probe. Its scoped source protocol and private effect
issuer are composed in the actual roots. Ordinary Global/Runtime roles cannot
substitute for that purpose. Impact-reader controls now live in API authentication
integration, retaining real Local credential custody and independently unchanged
business state; source acquisition itself requires a write reservation.
Manifest section boundaries still reject Email keys. Plan Email composition
and full executable assurance remain unfinished; PostgreSQL/SQLite
source work is not positive SQL Server/MySQL or external Local acceptance.

The typed instance-tenant Set/Lock/Unlock consumers now prepare their exact Email
effect through IHumanEmailMutationPreparation before invoking the secured writer.
The generic Settings producer is not their owner. Nine authentic typed source/
rollback controls are authored and unrun; plan and other typed governance
composition still require integration.

Pure Email batches also prepare immutable valid effects before BEGIN and acquire
the dedicated issuer, without adding a selected PDP question to the generic
unannotated Settings action.

Combined Email/other or ordinary/specialized non-Email batches now select the distinct `MixedSettings` purpose
before constructing dependencies. The existing Global connection carries the
exact instance or tenant target; it does not borrow Email-only, provider or storage
grants. `HumanEmailMutationPreparation` captures the first original command,
rows/absence, metadata, principal, credential and support sources before handler
validation. `MixedSettingsBatchStore` freezes the accepted typed slices and
requires each actual writer's private completion on one non-replaying Serializable
attempt. BestEffort can skip invalid inputs before compilation; source replacement,
expiry or missing completion rejects the entire accepted group.

`AddHumanMixedSettingsSources` composes the source factory and store. The enrolled
migrator installs `PostgresMixedSettingsContract` after provider/storage contracts;
the native fixture preserves `mixed_settings_source` across reset. Independent
native functions, exclusion and deferred completion reject capability drift and
omitted writes. Notifications and cache activation follow acknowledged settlement;
lost acknowledgment does not authorize replay. SQLite/PostgreSQL source paths are
authored; external Local identity and other providers fail closed. All 72 new cases,
SQL installation and runtime acceptance remain unrun. Complete parent native/source
review remains an explicit programme task.

`MixedSettingsBatchMutation.RequiresCombinedSource` is shared by the original
command scope selector, handler, actual source preparation and source factory.
The non-Email branch covers ordinary keys together with resource, visitor,
publication or location governance keys at Instance/Tenant scope. Pure ordinary
and pure specialized requests retain their respective owners; User scope does
not acquire an administrative combined purpose. The ordinary source compiler
rejects relabeling a combined request as an ordinary-only accepted subset.
The existing source store already freezes and privately completes those typed
slices without requiring an Email effect. Two genuine instance batch controls
exercise ordinary maximum-session creation plus publication approval, including
whole-group rollback after direct post-effect corruption. All controls are unrun.

`IOrdinarySettingsMutationStore` is now scoped in Persistence composition.
Its SQLite source captures full original rows/absence before BEGIN, retains
definition and creation metadata, and privately verifies canonical effects on
the same owner. Failed writer entry poisons the genuine source, preventing a
caught duplicate/dirty-tracker rejection from committing earlier effects.
The separate PostgreSQL native contract and store adapter are now authored.
Source observation supplies insertion IDs and a representable audit time;
all three canonical row writers emit through the opaque native attempt and
advance only from the verified typed effect. Callback admission uses
VerifyAsync, whereas the final source pass uses RequireCompleteAsync before
principal/route/support rereads and final original-human deadline checks.
The declared Migrator and native fixture install the new contract, and reset
retains ordinary_settings_source. SQL Server/MySQL remain refused.
No scalar equality, Serializable transaction, elevated fallback, or the combined
contract substitutes for ordinary native custody. Bounded source review is
complete; installation and runtime acceptance remain pending. The 45 direct
native controls and three original Local API controls remain unrun.

Ordinary PostgreSQL disclosure-setting writes now retain invalidation in the
private `ordinary_settings_attempt`, rather than calling generic discovery
enrollment as the selected service login. The existing caller distinction is
unchanged: instance operations use the declared Global login with no tenant;
tenant and user operations use the declared Runtime login and exact tenant.
`SettingRegistry` and the original disclosure-setting classifier generate the
native key set. Only a canonical changed system or tenant effect adds tenants:
system changes resolve the bounded complete catalog, tenant changes add exactly
their tenant, and user preferences or unrelated keys add none. The retained UUID
array is deduplicated and ordered, with the existing 1024-tenant fanout ceiling.

System disclosure acquisition takes the existing transaction-owned
`discovery-tenant-enrollment` advisory gate before ordinary source exclusion.
The `ordinary_settings_source` singleton also carries `tenant_revision`.
Always-enabled Tenant triggers advance this witness for inserts, deletes,
actual ID changes and truncation, but not no-op ID fences or metadata updates.
The original receipt includes the witness; acquisition updates the same singleton
under Serializable isolation before comparing the receipt. A snapshot predating
the first committed tenant therefore fails rather than treating the catalog as
empty, without opening another connector. A truly empty catalog remains valid.

The existing deferred settlement trigger reloads the current attempt row rather
than trusting an event's historical `NEW` tuple. It requires private completion,
then clears retained data into the existing transaction/backend tombstone before
calling disclosure finalization. This prevents queued or immediate constraint
events from advancing epochs twice. Tenant fences and epoch writes use canonical
tenant order; each affected tenant receives a fresh revision insertion ID,
`disclosure=true`, no expected identity revision and zero identity advancement.
The setting and all disclosure changes commit or roll back together, including
fanout rejection, epoch overflow and missing completion.

Only `event_ordinary_settings_owner` receives the three exact nested EXECUTE
grants on disclosure `dependencies(text,uuid[])`, `fence_tenants(uuid[])` and
`finalize(uuid[],uuid[],boolean[],bigint[],bigint[])`. The disclosure owner still
owns these routines. Both contracts attest the executor allowlist; ordinary
readiness additionally verifies exact routine signatures, result and argument
shapes, owners, security-definer posture, `search_path` and executable bodies
generated from the same source as installation. Restamping a modified body does
not admit it. No service login receives discovery disclosure membership or new
table grants, and traversal's prohibition remains intact.

The managed disclosure scope records a transaction-bound native-owned pending
marker, seals it at Flush and resets it on transaction change or explicit reset.
It rejects mixing this owner with generic disclosure, identity or deletion
accumulation in either order. Non-PostgreSQL ordinary handling is unchanged.
The extended `OrdinarySettingsNativeContractTests` cover atomic global and tenant
invalidation, identity preservation, classification/no-ops, rollback, repeated
settlement, stale empty snapshots, fanout/epoch overflow, nested ACL and helper
drift, Tenant trigger drift and marker lifecycle. These additions are
source-complete only; the parent programme owns executable verification.

Branding preparation resolves its policy-source exclusion contract on the
selected original context before opening the mutation transaction. The prepared
action retains that contract and acquires its exclusion inside the original
transaction before principal, route and support source leases. Resolving the
contract lazily during acquisition violates its selected-owner constructor
precondition; the constructor remains strict.

Session governance locks open owned connections through EF Core rather than
direct ADO.NET, so the tenant connection interceptor initializes the original
Runtime session before native policy admission. Their release balances that
EF-owned open; a pre-opened caller connection remains caller-owned. The native
assignment own-write control also starts with a cold session governance lock,
so bypassing tenant initialization cannot pass source acquisition.

Native principal acquisition retains its actually locked selected Tenant key
for tracked `TenantSettingsDocument` FK checks. This existence proof is bound
to the original human subject, connection and serializable transaction; an
observation, another tenant or a replacement transaction cannot borrow it.
It does not authorize a document action or attest any other private parent.
The support-administration prohibition on ambient Runtime Tenant column
privileges remains intact. Native negative controls cover foreign-tenant,
rolled-back and observation-only attempts.

### Day 2 session and operation recovery

Receipt and history services now prepare the existing Settings `view` action
on their original selected connection before BEGIN. Rollback-session creation
uses the existing `update` action. The single-attempt Serializable owner acquires
complete policy, principal, route and support sources before data access, then
validates original request/credential custody and support deadlines at settlement.
Only after settlement can history or a newly created rollback token be returned.
This observation admission never issues setting-effect/apply proof. Tenant view
preserves the Local instance-admin-or-tenant-admin rule; tenant rollback creation
retains tenant administration. These changes and authentic controls are source
delivery; executable/native assurance remains deferred.

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
| `configuration_manifest_path_invalid` | Explicit path is not absolute | Use the authoritative absolute container path |
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
| `configuration_manifest_tenant_duplicate` | Tenant slugs are not ordinally unique | Give each tenant one authoritative slug |
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
| `configuration_manifest_write_conflict` | A authoritative mutation boundary rejected a concurrent/stale write | Reload current state, correct the source, and rerun |
| `configuration_manifest_apply_failed` | Transaction failed and no manifest configuration was applied | Use the operation ID and database health evidence; repair the dependency and rerun |

### Post-commit and export failures

| Condition | Safe action |
|---|---|
| Bootstrap committed but its effect remains pending | Preserve the committed operation. Restore runtime/outbox processing and let the same versioned effect retry; do not reapply configuration or force completion. |
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
 4. Is there an authoritative transaction-aware mutation path?
5. Can it be exported without credentials, operational state, or sovereign
   authority?
6. Is the correct scope instance, tenant, or neither?
7. Does it interact with locks, publication safety, paid-policy ceilings, cache
   invalidation, or tenant creation?
8. Can validation complete before any write?

If any answer is unclear, do not add the field to the catalog.

### Add an approved scalar setting

1. Define or verify the authoritative `SettingDefinition` and registry entry in the
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
4. Extend validator, compiler/preflight, authoritative mutation, and export mapping.
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
- versioned key-only bootstrap effects, without private audit reads by Runtime.

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
| Apply | Lock hierarchy, serializable transaction, authoritative boundaries, snapshots, receipts, and effect outbox | `src/Explore.Application/Features/ConfigurationManifest/Application/ConfigurationManifestApplier.cs`, `src/Explore.Application/Features/ConfigurationManifest/Importing/ConfigurationImportApplyService.cs` |
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
