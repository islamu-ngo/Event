<!-- ABOUTME: Quarantines an existing MySQL/MariaDB secret-binding uniqueness defect outside Unicode search. -->
<!-- ABOUTME: Preserves tenant-boundary reproduction evidence and the acceptance contract for a separate security-schema repair. -->

# Portable Secret-Binding Uniqueness

Last Updated: 2026-09-07 Europe/Brussels

Status: open, separate security/provider workstream; not part of Unicode normalization. Owner: persistence/security maintainer. Follow the merged PR #38 quarantine rule; do not weaken test keys or absorb this repair into unrelated feature delivery.

## Observed Failure

`SecretBindingProviderContractTests.ProviderPersistsOnlyTenantQualifiedOpaqueMetadata` fails on MySQL and MariaDB while saving two bindings with the same setting key and qualifier but different tenant scopes. PostgreSQL, SQLite and SQL Server pass the same contract. The failure occurs before its concurrent tenant-qualified reads.

`SecretBindingConfiguration` declares a unique `(SettingKey, Qualifier)` index filtered to instance scope and a tenant-scoped unique index including `ScopeId`. On MySQL/MariaDB the physical first index is global, so independent tenants collide. The test and configuration are byte-identical between the Unicode intake revision `3f9b796b4a0aea6fff40baaaf88e07add663e863` and the merged implementation. The configuration last changed in `71423c7df`.

Evidence: five separate structured-provider runs on retained task-owned catalogs, three passed and two failed. Local artifacts are `/home/amir/.cache/agent-tmp/unicode-merge-secretbinding-<provider>.log`; ignored merged evidence preserves exact terminal outcomes. This is a source-parity and real-engine reproduction, not a claim that a separate untouched-base suite was executed. No extra worktree was created because the user explicitly prohibited it.

## Required Repair Contract

- Allow identical setting key/qualifier tuples in different tenant scopes.
- Reject duplicates inside the same tenant and reject duplicate instance-scoped tuples.
- Permit an instance binding and a tenant binding to coexist without weakening tenant-qualified access.
- Preserve opaque metadata storage: no inline credentials, plaintext values or sensitive diagnostic output.
- Reuse existing provider configuration primitives where possible; investigate a portable database-enforced representation before introducing new abstractions.
- Change entities/configuration first and regenerate only affected unapplied development migrations through EF. Never hand-edit migration/snapshot files or reset retained authority stores.
- Preserve the failing existing contract, add only missing uniqueness boundary cases, and verify all five real engines at this separate workstream's exit.

The Unicode location corpus remains independently verified on all five engines. Its passing result does not certify this secret-binding contract, and this finding does not justify restoring legacy Unicode token encoding.
