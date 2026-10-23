# Application-owned authority and EF-first persistence

> **Audience:** Contributors | Architects | Operators | AI agents
> **Status:** Mixed
> **Owner:** API
> **Last Verified:** 2026-10-20
> **Source Anchors:** `AGENTS.md`, `docs/internal/ARCHITECTURE.md`, `src/Explore.Application/`, `src/Explore.Domain/`, `src/Explore.Persistence/`

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
