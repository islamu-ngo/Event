ABOUTME: Primary entrypoint for authentication, authorization, and trust-boundary documentation.
ABOUTME: Points readers to the maintained security model while preserving documentation index links.

# Security

> **Audience:** Operators | Contributors | AI agents
> **Status:** Mixed
> **Owner:** Security
> **Last Verified:** 2026-08-30
> **Source Anchors:** `docs/SECURITY-MODEL.md`, `src/Explore.Application/Features/ConfigurationManifest/Importing/`, `src/Explore.Application/Features/ConfigurationManifest/Managed/`, `Explore.Blazor/Extensions/YarpProxyExtensions.cs`, `Explore.Blazor/Services/CircuitTokenStore.cs`

The maintained security model lives in [SECURITY-MODEL.md](SECURITY-MODEL.md).

Use that document for the current BFF trust boundaries, token-forwarding model, antiforgery contract, auth diagnostic safety, support-access boundary, upload-session binding, and circuit token lifecycle notes.

Configuration portability is a Tier 1 administration boundary. Upload tokens,
direct-transfer nonces, and destination proofs are header-only capabilities;
responses are private/no-store and bounded by size, rate, and timeout policies.
The authenticated route selects instance or tenant authority, HAL controls UI
affordances, and authorization is rechecked on every write. Apply recomputes the
preview under ordered locks and one serializable transaction. Secrets, PII,
application data, provider bindings, operational state, and deployment topology
are excluded by the closed registry. Direct transfer accepts only public
HTTPS/443 destinations, distinct mutual approvals, digest-bound chunks, and
promotion into the ordinary preview/apply workflow; a signature or transfer
proof never grants configuration authority.

The approved lifecycle-email plan adds dispatch-time privacy authorization without changing SMTP into an authority. Email may target only a current verified tenant member or an authorization-bound same-tenant managed tenant-administrator invitation. Incompatible pre-1.0 delivery ledgers are reset instead of receiving a synthetic legacy authority; inbox notifications and unrelated business/audit/settings data remain preserved. Consent, preference, disclosure, deletion, and supersession may narrow a snapshotted policy before provider handoff, never broaden it.

## Authentication-provider reference visibility

`PostgresLookupCatalogueReadPermissions` owns Runtime reads of the fixed
`AuthenticationProvider` lookup columns: `Id`, `MasterCode`, `FullName` and
`Description`. EF foreign-key preflight uses that reference visibility when
persisting a provider binding. It does not authorize provider selection, account
linking or tenant enrollment, and it grants no provider configuration writes.
Those decisions remain with the original authenticated application operation.

## Tenant parent authority fence

`PostgresTenantMembershipMutationPermissions` owns Runtime `UPDATE(Tenant.Id)`
for the existing `RelationalEntityRowFence` statement
`UPDATE tenants SET id = id WHERE id = ...`. The selected application's
transaction retains the parent fence before membership insertion and disclosure
settlement. The existing API-key parent inventory retains ownership of the
`Tenant.Id` read; counting it as a new membership grant would misclassify an
otherwise valid bootstrap as a partial installation.

The API-key storage manifest recognizes the membership owner's declared read
and update columns only on intersecting relations. Its existing fresh checks
attest the exact Runtime grantee, Migrator grantor, non-grantable column rights,
relation shape and policies alongside the external-enrollment catalogue proof.
Support administration retains its existing membership-or-actor completeness
boundary and its zero Runtime scalar authority on private session and audit
relations. This preserves the supported actor-only profile without coupling
API-key storage to unrelated membership grants.
No installer grant list, required API-key permission or policy is expanded.

The grant permits a column update, rather than enforcing no-op SQL in the
database. A compromised Runtime credential can therefore attempt primary-key
changes; credential isolation and the application's original-owner, tenant,
transaction and concurrency checks remain necessary. No tenant-status write,
provider configuration write, or additional `User` grant is introduced.

## Native external-enrollment graph

`ExternalTenantEnrollmentInventory` declares the fixed Runtime storage needed
to insert a new user, personal actor, provider binding, active tenant membership
and verified identity-email evidence. The production database migrator and
native test fixture invoke the same
`PostgresExternalTenantEnrollmentPermissions` installer. Application handlers
still own authentication, explicit tenant consent and admission; the storage
grant does not select an enrollment execution purpose.

Installation accepts an absent or complete declared addition and refuses a
partial, inherited, table-wide, grantable, PUBLIC or foreign-grantor authority
image. Catalogue owners recognize overlapping declared columns only alongside
the complete package proof. Existing-identity mutation, private PII reads,
identity-claim deletion and role-grant insertion remain outside this addition.
The installer retains its original context, connector, backend and transaction
through savepoint rollback and settlement.

Composed external and worker catalogue proofs are evaluated through
statement-local materialized scalars. Repeated consumers share that statement's
proof, while later statements, transactions and execution owners evaluate it
again. This bounds planner duplication without retaining observed permission
results. Native witnesses cover complete graph commit and rollback, unverified
email, tenant isolation, denied authority drift and same-connector recovery.

Local administrator lifecycle guards recognize this same absent-or-complete
package through one fresh catalogue proof per guard execution. The three local
credential relations retain their blanket denial of Runtime scalar access.
Recognition grants no new privilege and does not waive original-source,
transaction, row, User, trigger or routine checks. Executable lifecycle routines
must reject privilege drift even after an earlier successful readiness check.
