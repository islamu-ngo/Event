---
description: Run focused project-level tests and choose invariant-oriented evidence for each change.
---

# TUnit Testing Conventions

ISLAMU Event uses **TUnit** as its primary, modern test runner. We prioritize strict test quality over quantity, targeting core business invariants rather than brittle mock assertions.

---

## 1. Focused Developer Loop

During active development, build the affected test project after source changes,
then run one target class using a **TUnit tree-node filter**. Keep pure Domain
checks in memory, without containers or network services:

```bash
# 1. Build the affected project
dotnet build tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release --verbosity quiet

# 2. Run target test class with tree-node filter
dotnet run --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj \
  --configuration Release --no-build -- \
  --treenode-filter "/*/*/EventAddOnLifecycleTests/*" \
  --minimum-expected-tests 12 --report-trx
```

> [!NOTE]
> `--treenode-filter` uses assembly/namespace/class/method segments; VSTest
> `--filter` syntax is not interchangeable. Measure execution time on your machine.

The ordinary Domain suite includes `EventAddOnLifecycleTests` for catalog,
inventory and refund invariants, and `TicketingRecoveryInvariantTests` for
manifest validation, generation/fence advancement and worker-before-sales
recovery. For the latter, substitute its class name and minimum of `7` in the
command above. These cases replace the two standalone mutation projects and
already run through the normal Domain CI lane.

At the phase boundary, use the same selector with the repository test command:

```bash
dotnet test --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj \
  --configuration Release --no-build --verbosity quiet -- \
  --treenode-filter "/*/*/EventAddOnLifecycleTests/*" \
  --minimum-expected-tests 12 --report-trx
```

Check the required named results and unexpected skips as well as the count.
Keep distinct runs with `--results-directory <unique-run-directory>`. A passing
Domain state test establishes deterministic transition/replay behaviour; real
database races, authentication and external effects need their owning integration
lanes. A test-only relocation does not require unrelated provider or browser suites.

The security workflow also checks named execution witnesses declared in the
repository's existing security register. A passing count cannot stand in for a
missing method or variant. Those API test-host results do not attest a database
provider, database role, authentication scheme or distributed hosting profile;
their receipt records these dimensions as `not-attested`. Real provider, browser
and load profiles retain their own results and resource ownership.

### Host-free AppHost composition

Use the `AppHostCompositionContractTests` class in
`Event.Distributed.IntegrationTests` for focused composition changes. It inspects
the real resource registration with explicit inputs and completes without
starting application resources. The fixture keeps its inspection profile private
to that path; its normal creation and startup entry points reject it.

Check the required named cases and their outcomes. Passing model checks proves
registration and the project-owned container customization projection. Image
publication, running services, database authority and browser journeys need their
separate execution lanes. Retain the parsed project, solution, YAML and Compose
checks alongside the model cases.

---

## 2. Invariant-Breaker Testing Strategy

Author tests that guard real platform invariants:
* **Domain State Machines**: Invalid transitions, capacity limits, and lifecycle events.
* **Tenant Isolation**: Queries that attempt cross-tenant reads or operate without ambient tenant context (see [Multi-Tenancy](../security-and-identity/multi-tenancy.md)).
* **Security & Fail-Closed Gates**: Unauthenticated write attempts, expired session tokens, or missing [Cerbos PDP](../security-and-identity/authorization.md) policies.
* **Concurrency & Race Conditions**: Optimistic concurrency tokens during ticket purchases or settings edits.
* **Anti-Resurrection Erasure**: Replaying erased user facts against restored databases (see [Disaster Recovery Invariant](../security-and-identity/privacy-erasure.md#the-golden-rule-of-disaster-recovery)).

> [!WARNING]
> Prohibited in tests:
> * Tautological mock mirroring (e.g. asserting `repository.Received(1)` without verifying state).
> * Scraping raw source code or CSS class text.
> * Testing underlying third-party framework behavior (e.g. testing whether EF Core honors cancellation tokens).

---

## 3. Documentation-Only Work

For Markdown or GitBook documentation changes (such as edits in `docs/public/`), do **not** run `dotnet build` or .NET test suites. Verification is strictly scoped to Markdown formatting, link integrity, and schema checks.

---

## CI evidence and real execution

Security execution evidence binds the exact tested commit to registered test
lanes, named passing TRX cases and hashes of retained artifacts. A missing,
skipped, renamed or reused required result fails admission. The local integration
register lists the two API lanes and five existing owned runtime, browser and
load lanes. These names are source expectations, awaiting exact compiled
discovery and successful physical results from the same final candidate;
authoring the registration does not certify the profiles or activate remote CI.
Harness checks and isolated browser-module tests have
their own scope and cannot replace healthy-host journeys. Provider, database
role and authentication remain outside the packet's attested claims.

The `execute-security-execution` producer has compiler and helper verification;
remote workflow activation is held. Native API proof, the copied-report fresh
successor and live managed launch/input evidence remain pending; final execution
of all seven profiles must bind one clean committed candidate. Rebuild for the exact
commit after all source, registration and workflow changes are committed. A
later change requires new evidence; earlier commit results cannot cover it.

The required security job stays present for every supported trigger. Its native
change router can deliberately select a no-op; malformed routing output fails.
Selected runs build both registered test hosts for the exact candidate, then use
the owning producer instead of detached test commands. Only a successful admitted
public directory is uploaded. Failed runs publish no raw TRX, logs or private
temporary directories. External Chromium and the separately admitted k6 binary
remain runner prerequisites and are not redistributed in the evidence artifact.

The hosted security job stops after 360 minutes in total, including build and
setup. Native timeouts cover whole serial batches, with a 330-minute batch cap
and fifteen additional seconds for the parent deadline. API batches keep ten
minutes; each separately executed load case keeps twenty-five minutes for its
twenty-minute regime and lifecycle allowance. Runtime and browser budgets grow
with the actual selected case count, subject to the cap. These are hang limits,
not time reserved for every case: several slow batches can exhaust the overall
job budget. Canceled, missing or incompletely cleaned runs fail admission. The
final all-seven execution must establish that normal operation fits. The job
resolves its explicitly selected Unix socket once and verifies the socket before
exporting it; an unavailable endpoint fails instead of selecting another daemon.

Owning execution uses fresh private temporary directories and discovers the
actual compiled test identities before executing their opaque UIDs. Native TRX,
test names, paths, process output, configuration and credentials stay private.
Upload only its admitted public proof directory containing registered references,
counts, outcomes and whole-artifact digests. Copying an old passing report into a
new directory cannot produce owning evidence; the former harvesting command
rejects such inputs.

Instrumented API runs require an explicit Unix `DOCKER_HOST`, cached prerequisite
images and caller-selected `TESTCONTAINERS_RYUK_DISABLED=true`. Keep the parent
endpoint fixed when child temporary directories change. The producer
checks actual image/container identities and cleanup of known selected resources;
missing observations or cleanup failures reject admission. These are bounded
snapshots, not authenticated creation or continuous daemon-lifecycle proof.
Ordinary tests without ownership context keep their existing fixture behavior.

The bare migration fixture keeps PostgreSQL 18 as its ordinary default and offers
one fixed, pinned PostgreSQL 16 prerequisite profile. That profile requires the
parent-prepared ownership context; instrumented PostgreSQL 18 uses the same
private creation-intent protocol and cached-image requirement. Ordinary
PostgreSQL 18 without ownership inputs remains available. A passing native
capability control proves only its baseline and refusal checks: full public migration, retry safety and
the remaining unexpected-permission controls require their own results.

Release tooling structurally validates the supplied packet and retained artifact
digests under the existing trusted operator boundary. A workflow name, run ID or
URL does not authenticate its CI origin. Maintainers separately check required
GitHub results for the exact final commit before release.

Follow the [browser guide](browser-testing.md) and
[owned load guide](load-testing.md) for their separate runtime prerequisites and
artifact restrictions. Keep browser state, credentials, raw page content and
application/process logs out of uploaded evidence.

---

## Related Guides & Next Steps

* **[Local Development Guide](local-development.md)** — Set up your local developer tools.
* **[Clean Architecture Conventions](clean-architecture.md)** — Architectural layer responsibilities.
* **[Authorization & Access Control](../security-and-identity/authorization.md)** — Writing authorization tests.
* **[Privacy Erasure & Anti-Resurrection](../security-and-identity/privacy-erasure.md)** — Testing database restore gates.
