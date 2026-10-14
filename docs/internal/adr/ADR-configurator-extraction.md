# ADR: External Configurator, Event-owned configuration authority

Status: accepted; destination readiness confirmed before removal of bundled
Event offline tooling.

## Decision and ownership

ISLAMU Configurator owns the definition runtime, CLI, shared presentation,
browser, desktop, terminal and protected native artifact writer. It is installed
separately from Event. Event supplies local, data-only definitions; neither
repository references the other's runtime assemblies. This removes the duplicated
offline engine while keeping Event's authorization and semantic validation in
the application that owns the data.

Event retains its environment declarations and host-wire validators, and
`configurator/definitions/islamu-event/`. Maintainers install Configurator's
portable `configurator-integrate` skill; the agent reads Event's public contracts
and authors one supported compact `configurator.yaml`. It is author-maintained,
not automatically generated or kept fresh. No Event-specific exporter, compiler
wrapper or new target dependency is part of adoption. The standalone CLI's
`definition init/check/revise/build` commands own identity allocation, admission,
deliberate revisions and canonical JSON compilation. Delivery
contains only that source, canonical `definition.json` and `release.json`;
synthetic inputs, answers, expected artifacts and catalogue snapshots belong to
`tests/Event.Wire.Contracts.UnitTests/Fixtures/Configurator/islamu-event/`.
The baseline manifest supplies two-tenant coverage, with separate one- and
three-tenant variations. JSON/dotenv target acceptance remains Event-owned.
Its receipts bind the stable family, revision and canonical definition digest.
Changed canonical definition bytes require a new revision against the prior
release. Retain immutable release history and verify actual machine results and
digests. A retained receipt does not attest that edited YAML was compiled;
synthetic fixtures are test-owned and independent of the compiler.

`Explore.Blazor.Client/Services/SetupLive/` and its focused client tests remain
Event-owned. They transport generated API contracts and server-issued HAL
affordances; moving the offline tools does not enable Configurator live control.
The serial 35-case `SetupLiveAuthoritySecurityTests` API lane retains its
nonempty-count guard, TRX report and optional Cobertura output under the retained
`test_setup` CI owner.

## Preserved semantics

Domain, Application, Wire, Persistence, bootstrap identity selection, environment
secret authority, configuration schemas and backend onboarding/import operations
are not extracted. Configurator success means local preparation, not host
acceptance. Event still validates wire integrity, tenant authority, expected
revision, approval and apply semantics. Legal artifacts remain drafts. Operator
identity preparation preserves its explicit target revision; it does not grant
administrator authority.

The generic CLI takes a local `--definition`, an artifact ID and an answer
workspace. Old `event-setup` families are not aliases. Definitions and answer
workspaces are data, not executable plug-ins. Native and browser definitions
retain the same identity. The complete browser workspace owns answers only in
memory, with explicit consent for secret entry and deferred-secret draft output;
it does not acquire Event authentication or target apply authority.

The semantic-field-path profile is revision 6. `configuration-manifest.tenants` is a
bounded list of typed objects, with ordinal `uniqueBy: ["metadata", "name"]` and
the Wire ceiling of 256 rows. A repeat projection copies each validated whole
row. Row removal therefore removes its settings/documents from output rather
than leaving a fixed tenant behind. Public definition metadata
contains no private defaults or supplied answers. Aggregate Configurator budgets
can reject complex inputs below the host ceiling.

The generated release receipt binds the source URL and canonical digest used in
the README `/open` fragment. Source retrieval precedes answers and cannot confer
publisher trust. The source may move, but changed bytes cannot silently replace
an admitted revision. Operators edit rows in the workspace; maintainers edit the
source vocabulary through the portable skill/native CLI workflow and tests own
independent host vectors. Event remains target acceptance and apply authority.

## Cutover and recovery

Destination packaging, attribution, dependency closure, replacement command
schema and moved behavior passed their parent gate before source removal.
The literal source removal disposition is recorded in
[the historical source cutover inventory](../configurator-source-cutover-inventory.md).
Existing unrelated edits are
outside that scope. The parent owns the operator release fragment, final
verification and commit.

This is a repository/tooling extraction, not a database migration. Recovery is
to use the recorded previous source revision and its matching tool release;
never mix definition revisions, bypass host checks, restore obsolete aliases,
or modify persisted backend state to accommodate a tooling rollback.

## References

- [Event native definition authoring](../CONFIGURATOR_DEFINITION_EXPORT.md)
- [Operator handoff](../../public/self-hosting/setup-assistant.md#external-configurator-handoff)
- [Future live integration](../../../dev/backlog/configurator-live-integration.md)
- [Configurator](https://github.com/ISLAMU/Configurator)
