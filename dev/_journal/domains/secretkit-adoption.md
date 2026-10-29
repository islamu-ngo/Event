# SecretKit adoption

This ledger connects the durable integration lessons to their authoritative
owners. The architecture decision remains in
[ADR-secretkit-event-ownership.md](../../../docs/internal/adr/ADR-secretkit-event-ownership.md);
execution receipts belong to the task context, not this ledger.

---

[2026-10-26 Europe/Brussels] — Keep admission, lifetime and effect evidence separate

**Context**: The `secrets-authority` cutover replaced the obsolete Secrets
projects with selected-authority hosting, Infrastructure runtime policy and
Persistence database acquisition. Independently successful provider operations
must not be mistaken for deployment readiness or authorization.

**Symptom / Observation**: Native acquisition alone did not establish safe
publication, cross-scope cache invalidation or truthful cancellation settlement.
An explicit backend credential could survive frontend projection even though
the projector never added it. Compose also needed an independently selected
token policy for every machine identity.

**Root Cause**: These are distinct boundaries. The native client owns its
provider operations; Event owns preparation before and after those calls.
Scoped repositories do not give a shared cache a scoped invalidation index.
Canceling a caller cannot undo a dispatched external write. Skipping projection
does not erase a value already present in the target environment.

**Resolution**: Reserve and drain Event preparations through one selected
process owner. Keep cache indexing and invalidation generations shared while
binding access remains scoped. Retain native revision equality in its outer
owner; expose only qualified process-local comparison tokens to Event.
Acquire write evidence before dispatch and persist its observed disposition
through uncancelable settlement. Historical null evidence remains unknown,
never a fictitious non-dispatch. Mask forbidden target keys explicitly and
project only the selected consumer's lifetime policy.

The owning native Secrets namespace passed 414 cases and the admission slice
passed nine after observed failures. Commands:
`dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet --no-build --no-restore -- --treenode-filter "/*/Explore.Infrastructure.Tests.Secrets/*/*" --minimum-expected-tests 414`
and
`dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet --no-build --no-restore -- --treenode-filter "/*/*/*SecretAuthorityLeastPrivilegeArchitectureTests/*" --minimum-expected-tests 9`.
These bounded results are not provider ACL, replica-convergence or image
certification.

**Why This Matters for Future Work**: Ask separately who admits an operation,
who owns its lifetime, what effect was observed and which consumers are ready.
A successful read, token renewal, configuration mapping or HTTP cancellation
answers only its own question. Owner replacement, another replica or revision
evidence eviction requires fresh review rather than portable approval fiction.

**References**:
- `src/Explore.ServiceDefaults/Secrets/SecretAuthorityAcquisition.cs`
- `src/Explore.ServiceDefaults/Secrets/RuntimeSecretEnvironment.cs`
- `src/Explore.Infrastructure/Secrets/SecretResolverCacheState.cs`
- `src/Explore.Domain/SetupLive/SetupSecretBindingOperation.cs`
- `docs/internal/SECRETS.md`
- `docs/internal/TROUBLESHOOTING.md`
- `dev/_journal/journal.md`

**Promotion Consideration**:
- [x] Architectural ownership is already recorded in the SecretKit ADR.
- [x] Keep the integration lesson here; operator procedures belong in the paired guides.

---

## Build and publish input boundaries

The October 27 finding, "Locked restore includes evaluated build inputs",
in [the journal](../journal.md) records the actual image failures and their
causes: Razor-dependent implicit packages, distinct single-file locks and
host-tool property isolation. Current build instructions and qualification
limits remain in [the technical package guide](../../../docs/internal/SECRETKIT_LOCAL_PACKAGES.md).
