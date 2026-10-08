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

Event retains `eng/configurator/DefinitionExporter/`, its environment metadata
and host-wire validators, and `configurator/definitions/islamu-event/`.
The exporter references only `Explore.Domain` and `Event.Wire.Contracts`.
Its receipts bind the stable family, revision and canonical definition digest.
Changed definition bytes require a new revision. CI uses `--check-local` and
`--check-environment`, not generators that belong to the extracted engine.

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
workspaces are data, not executable plug-ins. The public browser restricts answer
controls; native and browser definitions retain the same identity.

## Cutover and recovery

Destination packaging, attribution, dependency closure, replacement command
schema and moved behavior passed their parent gate before source removal.
The literal source removal disposition is recorded in
`eng/configurator/source-cutover-inventory.md`. Existing unrelated edits are
outside that scope. The parent owns the operator release fragment, final
verification and commit.

This is a repository/tooling extraction, not a database migration. Recovery is
to use the recorded previous source revision and its matching tool release;
never mix definition revisions, bypass host checks, restore obsolete aliases,
or modify persisted backend state to accommodate a tooling rollback.

## References

- [Event definition export](../CONFIGURATOR_DEFINITION_EXPORT.md)
- [Operator handoff](../../public/self-hosting/setup-assistant.md#external-configurator-handoff)
- [Future live integration](../../../dev/backlog/configurator-live-integration.md)
- [Configurator](https://github.com/ISLAMU/Configurator)
