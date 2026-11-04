# Persistence capability assurance

Audience: contributors changing EF storage boundaries and provider primitives.

The semantic persistence audit replaces source fingerprints and directory
exemptions. Its policy grants only exact method/API combinations after a provider
gap, safe identifier/parameter handling, and an executable native test owner have
been reviewed. Moving a business method into a provider folder grants nothing.
The policy initially has no grants; inherited business SQL remains visible until
its owning workflow is replaced. CI-wide enforcement is a final workstream gate.

## Preserved invariants

| Previous source check | Executable replacement and evidence boundary |
|---|---|
| Raw EF and direct ADO regex fixtures and count registry | `PersistenceCapabilityAuditTests` binds actual EF/ADO symbols, aliases, delegates, wrappers, provider overrides and batches. Missing references fail closed. |
| Provider-folder exemptions | Exact method capability policy; moving the same synthetic method between application and provider folders produces the same violation. |
| Provider-name branching and internal API imports | Resolved provider detection and internal API symbols require a storage capability regardless of the caller's directory or aliases. |
| Literal physical table/column/index naming | Mapping methods require a schema capability. `ExploreDbContextModelProviderTests.ConstraintDescriptorsResolveFromEachFinalizedProviderModel` and `PortablePersistenceContractTests` exercise finalized metadata across providers. An approved schema mapping may legitimately name a physical object. |
| Constraint-name literals in exception classification | `RegistrationUniqueConflictClassifierTests` exercises real provider exceptions against metadata-derived names. This checks classification behavior rather than searching source tokens. |
| String arguments to row-fence calls | The compiled row-fence signature accepts mapped property expressions. `AdmissionAuthorityRowFenceProviderTests` executes the primitive with actual mapped keys and tests competing writers. |
| Webhook/federation advisory-lock SQL literals | Raw SQL in every owner is classified semantically. `RelationalNamedLockTests` and native webhook/federation suites retain lock lifetime, cancellation and claim behavior. |
| Source fingerprints and empty temporary registries | Deleted. Counts and hashes of implementation text cannot establish a storage or authorization invariant. |

The synthetic audit tests establish detector behavior. They do not establish
native permissions, transaction correctness, performance or provider parity.
Those remain responsibilities of the owning native suites and the final provider
matrix; no historical test result is promoted into a new candidate's evidence.

## Principal fact projections

`HumanPrincipalSourceProjection` is the shared EF projection for selected
identity-writer and portable principal-source reads. It retains deleted source
candidates, subject-qualified cross-tenant facts and owner-list multiplicity.
`IdentityWriterHumanSourceQueries` keeps the original execution-pair, transaction
and Local lifecycle custody; the portable fence checks its captured transaction
and selection before and after every query. Projection never selects a new
identity context or grants authority.

`AuthoritySourceInvariantTests` guards Application completion against changed
human, tenant, grant and permission facts. `IdentityWriterHumanSourceQueryTests`
and `HumanPrincipalSourceFenceTests` exercise actual SQLite projections and the
ordinary co-located Local boundary. PostgreSQL source contracts remain live
until their dependent source families and replacement permissions migrate;
these SQLite cases do not qualify their retirement or other providers' locks.

## Workflow ownership

Raw persistence diagnostics belong to the actual workflow owner and its complete
caller/writer/installer closure. The implementation ledger tracks identity and
security sources, settings publication, configuration import/provisioning,
notifications and queues, registration/payments, discovery/disclosure, storage,
federation/moderation and privacy settlement separately. No catch-all exception
or source inventory approves these workflows. Retiring a routine also requires
retiring its installer/admission checks and qualifying its C# replacement with
the actual purpose-specific database identity.
