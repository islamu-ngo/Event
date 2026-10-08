# Event configuration preparation and external runtime boundary

> **Status:** External Configurator source cutover
> **Source Anchors:** `eng/configurator/DefinitionExporter/`, `configurator/definitions/islamu-event/`, `src/Explore.Blazor.Client/Services/SetupLive/`, `src/Explore.Application/Features/ConfigurationManifest/Importing/`, `tests/Event.Architecture.Tests/SetupAssistantArchitectureTests.cs`

## External definition handoff

[ISLAMU Configurator](https://github.com/ISLAMU/Configurator) owns the offline
definition runtime, CLI, presentation, browser, desktop, terminal and protected
native writer. The seven bundled Event product and focused test roots and their
engineering generators/release owners are removed. No compatibility alias or
Event runtime dependency is retained in Configurator.

Event owns `eng/configurator/DefinitionExporter/` and
`configurator/definitions/islamu-event/`. The exporter references only Domain
and Wire; environment catalogue/activation metadata, bounded dotenv handling
and value-safe host diagnostics stay with that owner. Read-only CI checks use
`--check-local` and `--check-environment`, with exporter-owned provenance.
See [the exporter contract](CONFIGURATOR_DEFINITION_EXPORT.md) and
[the ADR](adr/ADR-configurator-extraction.md).

Stable family `a4bcfaa6-9979-43e5-b1f5-00d552618cdf`, revision and canonical digest
bind native and public definitions. The browser restricts answer controls,
not identity. Definitions are data-only metadata; secrets/private answer values
are not executable extensions or public defaults. Shape changes require an
explicit new revision. A local registry/launch selector is not remote authority.

## Ownership and semantic acceptance

| Owner | Responsibility |
|---|---|
| Configurator | Generic definition admission, answer workspace, bounded composition, confidential output, UI and package/native-host evidence |
| Event exporter | Host-owned metadata, safe reference definitions, canonical receipts and actual host-parser vectors |
| Event Domain/Application/Wire/Persistence | Bootstrap, issuer normalization, settings, identity/legal lifecycle, secret authority, tenant scope, revisions, imports and apply |
| Event Blazor client | Generated SetupLive API transport, ephemeral capability custody and HAL affordances |

Local preparation reports `host-validation-required`. Wire verification is not
authenticated backend apply or complete business-policy acceptance. Legal drafts
do not publish, identity preparation does not grant bootstrap/admin authority,
and schema export does not replace tenant state-machine checks. The existing
manifest schema generator, `.env.example` and Compose remain Event-owned.

All answer-derived artifacts are restricted. They require the external
runtime's create-only, owner-only native writer; unsupported hosts fail closed.
The public browser accepts declared public choices only. CLI grammar, the
`islamu-configurator-command/v1` schema, dependency closure and notices belong to
Configurator. Event's `setup-assistant-cli` skill is an external usage pointer.

The relocated adapter is under `Explore.Blazor.Client/Services/SetupLive/` with
focused tests under `Explore.Blazor.Client.Tests/SetupLive/`. It transports
generated contracts and server-authored affordances; neither the retained adapter
nor the backend enables a Configurator live-control workflow. Future integration
is [explicitly deferred](../../dev/backlog/configurator-live-integration.md).

## Live configuration import API

The established authenticated SetupLive API retains its server-owned workflow.
Extraction removes offline source duplication, not HTTP authority.

### Session state

`ConfigurationImportSession` owns:

1. `Uploaded`: protected bounded artifact, target authority, lowercase SHA-256
   digest, byte length, expiry and access-token digest bound to a new session.
2. `PreviewReady`: exact artifact/target revision/selected sections/mapping
   digests, apply mode, approvals and preview expiry bound together.
3. `Consumed`: the same target, token digest and preview binding consume once.
4. `Cancelled` or `Expired`: further preview/apply is rejected.

Default lifetime is 30 minutes, maximum one hour; artifact expiry cannot precede
session expiry. Access-token digests use fixed-time comparison. Wrong target,
invalid token, stale preview, expiry, cancellation and replay fail closed.

SetupLive sessions persist a distinct 64-character authority key derived from
tenant, enrollment, current generation and actor. The tenant mutation lock uses
its canonical tenant key. Ordinary tenant routes cannot match the enrolled
session authority, even with its token. The bridge rechecks enrollment under
its generation fence before using that key. The existing `TargetAuthorityKey`
column remains; source extraction requires no migration.

### HAL and transaction boundary

Clients follow server-authored HAL relations/methods. Link presence is the
action affordance; local role/claim checks are not authorization. Apply revalidates
the preview and current revision inside the server-owned mutation boundary
before committing atomically.

`SetupConfigurationImportsController` retains:

- `POST /api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/configuration-import/sessions`
- `POST .../sessions/{sessionId}/preview`
- `POST .../sessions/{sessionId}/apply`

All require the enrollment capability; preview/apply also require the session
access token. Create advertises preview; apply appears only for an apply-ready
preview. No client invents mutation authority.

## Retained verification

`SetupAssistantArchitectureTests` now verifies Event schemas, all 21 portability
registry entries and exclusions, value-free target/revision bindings, exporter
solution ownership and parsed read-only CI configuration. Portable runtime and
release assertions moved to Configurator's compiled boundary/package suite.
`ConfiguredAdministratorBootstrapArchitectureTests` keeps bootstrap invariants
and now checks the exporter's Domain/Wire-only inward graph.

Wire/generated-contract tests and relocated client security/apply tests remain.
The retained `test_setup` CI step runs the serial 35-case
`SetupLiveAuthoritySecurityTests` class with minimum-count guard, TRX and optional
Cobertura coverage. It is independent of deleted tool tests. No provider matrix
or unrelated suite is implied by this cutover evidence.

## Related documentation

- [Public handoff and command migration](../public/self-hosting/setup-assistant.md)
- [Configuration](CONFIGURATION.md)
- [Self-hosting](SELF_HOSTING.md)
- [CI governance](CI_CD_GOVERNANCE.md)
