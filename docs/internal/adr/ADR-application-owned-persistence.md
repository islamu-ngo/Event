# Application-owned authority and EF-first persistence

> **Audience:** Contributors | Architects | Operators | AI agents
> **Status:** Mixed
> **Owner:** API
> **Last Verified:** 2026-11-24
> **Source Anchors:** `AGENTS.md`, `docs/internal/ARCHITECTURE.md`, `src/Explore.Application/`, `src/Explore.Domain/`, `src/Explore.Persistence/`, `src/Explore.Persistence/Database/ProviderPrimitives/AdministrativeAuthorityQuery.cs`, `src/Explore.Persistence/Schema/ProviderPrimitives/PostgresAdministrativeAuthorityReadPermissions.cs`

## Decision

Decision status: accepted. Implementation status: SQL-to-EF cutover in progress.

The C# application is the authority for authorization, business rules, workflows,
concurrency coordination and completion. The database is its storage engine, not
a second application with competing authority. This is the Project Steward's
explicit decision, reaffirmed on 2026-10-20.

"API-owned" means the backend's Clean Architecture, not business logic placed in
controllers. Runtime execution follows HTTP/API → Application workflows → Domain
rules → EF Core persistence → database. Background workers use the same application
and domain rules; they do not invent another authorization or completion model.

| Boundary | Responsibility |
| --- | --- |
| API/transport | Authenticate callers, validate transport input, invoke application operations, shape responses |
| Application | Authorize the use case, coordinate concurrency and transactions, complete workflows and durable effects |
| Domain | Express business invariants, validation and state transitions |
| Persistence/provider adapters | Implement storage operations through EF Core and supported provider libraries |
| Database | Supply transactions, foreign keys, uniqueness, constraints and useful tenant isolation |

## Persistence rules

Ordinary reads and writes MUST use C# LINQ/EF Core capabilities where available,
including tracked changes and appropriate set-based operations. EF generates SQL;
the objective is to avoid handwritten business SQL, not to prevent SQL execution.
Provider libraries are the next choice when standard EF cannot express a storage
capability. Any remaining handwritten SQL MUST be a small, documented provider
primitive with a demonstrated capability gap, correctly parameterized values and
separately quoted identifiers. Provider support must be verified, not inferred.

Do not introduce or retain stored procedures, privileged business routines,
business triggers or SQL-owned workflow protocols as the destination architecture.
Schema/RLS/permission installation and genuinely necessary low-level storage or
locking primitives are different from business logic. They MUST NOT decide who
may perform a business action or replace its C# state machine and completion.
Declarative integrity constraints may mirror invariants to reject invalid storage;
they do not orchestrate use cases.

C# concurrency coordination must work across participating application instances,
not only a process-local mutex. Use appropriate transaction isolation, concurrency
tokens, conditional writes, affected-row checks and justified provider primitives.
Keep graph changes, required audit and outbox effects in the original atomic
transaction; handle retries, idempotency and uncertain commits explicitly in C#.
Controllers, repositories and provider adapters do not acquire business-rule
ownership merely because they can execute a statement.

## Permissions and the credential-only compromise scenario

An application purpose needs storage permission to perform the work it is
authorized to manage. Granting that approved purpose its fixed necessary reads
and writes is normal implementation, not automatically a security defect or a
separate approval request. Keep purpose separation, secret isolation and useful
tenant RLS; do not force business logic into privileged routines merely to keep
the authorized application from writing its own data.

A stolen database credential used outside the application can bypass C# rules
and cooperative concurrency protocols within that credential's database reach.
Narrow permissions, constraints and tenant isolation limit damage but do not
establish that every direct edit came through the application. This accepted
residual risk is documented here once. Do not repeatedly reopen it for ordinary
permissions within an approved workflow. Escalate genuinely new trust boundaries,
secret exposure, destructive actions or scope beyond the approved operation.

Runtime is the ordinary request/worker database purpose, generally tenant-bound;
Global is an explicitly selected purpose for authorized instance-wide work. These
are connection/permission boundaries, not user roles or substitutes for application
authorization. A dedicated identity-writing purpose is likewise selected before
its C# lifecycle starts; it does not promote ordinary Runtime or Global.

## Read-only authority verification across Runtime and Global

Read-only tenant-scoped workflows that must verify current platform-administrator
or tenant-administrator authority (such as buffered single-tenant configuration
package exports) keep tenant data reads on a tenant-bound `Runtime` connection
and keep authority verification on a dedicated, tenantless `Global` owner.
Do not grant `Runtime` direct reads or PostgreSQL security-barrier views over
platform identity or role-assignment tables (`platform_user_roles`), and do not
acquire write-privileged row locks (`FOR UPDATE` or no-op `UPDATE` row fences)
on read-only paths, where locks generate WAL and contend with authentication and
revocation without protecting a write.

Follow this three-step protocol for buffered read-only administrative exports:

1. **Pre-read `Global` authority check (fail fast)**: Bind an explicitly
   selected tenantless `Global` context to the original resolved human subject
   and validated credential lifetime, and verify live user (`!IsDeleted`), bound
   `UserExternalLogin` (when present), active `Tenant`/`TenantStatus`, and
   persisted `platform.admin` or active `TenantAdmin` authority before reading
   tenant data.
2. **Consistent `Runtime` tenant snapshot**: Read and buffer the target tenant's
   payload inside a single `Serializable` `Runtime` transaction bound to that
   exact tenant (`app.current_tenant_id`), so cross-table tenant configuration
   is mutually consistent without streaming partial output.
3. **Separate post-buffer `Global` recheck and credential custody**: After the
   tenant read finishes buffering—and again after child-scope disposal—re-verify
   the original `HttpContext`/`ClaimsPrincipal`/credential lifetime and run a
   **separate fresh `Global` authority read** before releasing buffered bytes.

Do not run the pre-read and post-buffer `Global` checks inside the same
`Serializable` or `RepeatableRead` transaction once row locks are removed. Under
PostgreSQL Serializable Snapshot Isolation (SSI), a read-only `Serializable`
transaction takes its snapshot at statement 1 and validly serializes *before* a
concurrent revocation transaction; it does not abort with SQLSTATE `40001` when
a role is revoked mid-export. Using two separate fresh `Global` reads (for
example, independent short `ReadCommitted` queries) bracketing the `Serializable`
`Runtime` read actively catches in-flight revocation and credential expiry while
preserving zero write-lock contention and `Runtime` least privilege.

Stay strictly within `Global`'s existing fixed column allowlists so zero new
database grants or PostgreSQL-only views are needed:

- `PostgresSetupLiveStoragePermissions` (`platform_user_roles`, `roles`)
- `PostgresTenantPreferencePermissions` / `PostgresUserTenantNavigationReadPermissions` (`users.id`, `users.is_deleted`)
- `PostgresExternalLoginReadPermissions` (`user_external_logins`)
- `PostgresUserTenantMembershipReadPermissions` (`tenants`, `tenant_statuses`, `tenant_users`)
- `PostgresAdministrativeAuthorityReadPermissions` (`tenant_user_role_grants`
  columns `TenantId`, `TenantUserId`, `RoleId`, `RevokedAt`, paired with
  `ck_tenant_user_role_grants_role_scope` and `Role.RoleScopeId` on `roles`,
  matching `AdministrativeAuthorityQuery`)

Do not select ungranted columns such as `TenantUserRoleGrant.Id` or
`TenantUserRoleGrant.RoleScopeId` on `Global`, and keep `Runtime` repositories
(including tenant-scoped moderation export paths) off `PlatformUserRoles` and
ungranted `TenantUserRoleGrants` columns. Reserve `FOR UPDATE` row fences and
commit-authority locks for mutating workflows.

*Implementation boundary:* `AdministrativeAuthorityQuery` and
`AdministrativeAuthorityRepository` already implement EF-first `Global`
authority reads across providers. Adopting this read-only `Global` owner pattern
for single-tenant configuration package export (and any remaining read-only
exports) is tracked in active implementation work and requires real PostgreSQL
provider verification before claiming runtime completion.

## Transition and verification

Existing native routines and capability attestations describe current mechanisms,
not a mandate to preserve database business authority. This decision supersedes
that architectural assumption in earlier guidance, including native execution
placement in the [discovery decision](ADR-event-discovery-authority.md); its
business invariants and public contracts still need explicit preservation or an
intentional approved change. There is no backward-compatibility requirement.

Replace complete workflows and their callers, owner selection, transactions,
tests and documentation together. Do not silently remove protections or loosen a
test/guard merely to pass a build. Review obsolete database-authority assumptions
against this decision instead of extending them indefinitely or keeping an
obsolete allowlist as the product architecture.

Acceptance requires C# invariant tests, real persistence/concurrency and tenant
isolation tests, generated migrations where the model changes, and proportional
provider verification. This documentation decision does not grant credentials,
change runtime permissions, delete installed routines or certify the SQL-to-EF
cutover as complete.
