<!-- ABOUTME: Quarantines the pre-existing SQLite managed-registration row-version creation defect. -->
<!-- ABOUTME: Separates managed Local administrator linkage evidence from registration creation and concurrency. -->

# Managed SQLite row-version creation

Status: OPEN, not repaired by P06 managed Local administrator linkage.

## Evidence

- Base: C04 `ca77595261255f7fd655147f5f3072683edea35d`.
- `ManagedControlPlaneRegistrationConfiguration.cs:30` maps `RowVersion` to `xmin` with `IsRowVersion()`.
- `ManagedTenantProvisioningOperationConfiguration.cs:27` has the same mapping.
- Both configurations and `Schema/PortableRelationalModelPolicy.cs` have an empty diff against C04 as inspected on 2026-09-07.
- The native SQLite linkage fixture uses the real `ExploreDbContext`. Its `OnModelCreating` always applies `PortableRelationalModelPolicy` for the selected provider. The policy supplies no SQLite generator/default for these `uint` row versions. Using the canonical provider composition does not add such a convention.
- Native registration repository creation failed with `SQLite Error 19: 'NOT NULL constraint failed: ie_managed_control_plane_registrations.xmin'`. Evidence: `/tmp/st_01a07c88-native-green.log` (session-local log).
- Operation creation has the same unchanged mapping; its failure was not independently executed in this slice.

## Scope boundary

`ManagedTenantLocalAdministratorLinkageTests` assumes an existing valid managed registration and queued provisioning operation. Two narrow parameterized prerequisite inserts supply initial `xmin = 1`. They do not alter EF concurrency annotations, schema, repository predicates, or runtime authority checks. Native scheduling replay, persisted registration revocation, fresh worker registration reads, operation-generation transitions, credential binding reads, tenant creation, and tenant role grants still execute through production code.

Registration/operation creation and provider-generated row-version concurrency are outside that test slice. Passing linkage tests do not close this defect or prove those concurrency semantics. No failing test is disabled and no production mapping is changed.

## Required separate repair

Design and verify provider-native concurrency for the managed registration and provisioning operation lifecycle, including normal creation and stale-update rejection. Generate any required migrations through the repository's canonical generator. Do not use a constant default alone as evidence of working optimistic concurrency.
