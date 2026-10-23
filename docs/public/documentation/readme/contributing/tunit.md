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

## Related Guides & Next Steps

* **[Local Development Guide](local-development.md)** — Set up your local developer tools.
* **[Clean Architecture Conventions](clean-architecture.md)** — Architectural layer responsibilities.
* **[Authorization & Access Control](../security-and-identity/authorization.md)** — Writing authorization tests.
* **[Privacy Erasure & Anti-Resurrection](../security-and-identity/privacy-erasure.md)** — Testing database restore gates.
