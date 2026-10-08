# Event-owned Configurator definition export

Status: exporter, immutable identity, environment ownership and reference vectors verified. Source cutover and whole-workstream readiness remain separate parent gates.

## Ownership and immutable identity

The exporter references only Event's Explore.Domain and Event.Wire.Contracts. It has no Event.Setup.Core or sibling Configurator project reference. Event-specific environment catalogue, activation metadata, bounded dotenv parser and value-safe diagnostics are retained in eng/configurator/DefinitionExporter/Environment. Configurator consumes generated data, never an Event assembly or executable extension.

Family a4bcfaa6-9979-43e5-b1f5-00d552618cdf is stable. The current checked profile is revision 4, exact application version 0.0.0-development, 432 fields and five artifacts. Native setup-definition.json, definition.json, public-definition.json and the bundled browser resource contain identical canonical bytes and digest 33661343436560b8004903d6efe569c6e3342d278f3fae6e9df49fad9e0e65bf. The browser restricts answer controls rather than changing definition identity. Only public enum/boolean selections enter its partial template; private answers and arbitrary text stay native-only. No supplied answers, private defaults or private literals are embedded in metadata.

Earlier concurrent implementations produced different digests under one family/revision and introduced removed-project references. Those conflicts were reconciled, not accepted as compatibility exceptions. Revision guards preflight every destination identity before writing any answer or receipt. A custom shape at an existing released revision rejects before creating its output directory.

## Metadata and host validation

EnvironmentCatalogueContract preserves Domain secret-registry parity, sensitivity, canonical keys, Compose interpolation order/requiredness, curated dotenv-template validation and deterministic generated public documentation. Its new output owner is configurator/definitions/islamu-event/environment-catalogue.json. The definition-oriented catalogue.json is separate canonical data. Neither reads secret-provider or deployment values.

Approved defaults are exported only when metadata sensitivity is Public, then converted to the declared scalar type. Sensitive and Secret fields cannot acquire defaults through this path. The independent environment vector includes the host's declared false agent-browser flag, Off manifest mode and one replica, alongside explicit sample ports. A test caught the omitted default vector; the corrected expected data comes from these host declarations, not a recomputed composer oracle.

Profile trustedIssuerCount declares between 1 and 128 canonical indexed issuer fields. It defaults to one; changing the count changes definition shape and requires a new revision. This is finite explicit metadata, never wildcard authority. Host verification still rejects noncanonical indices and duplicate normalized authorities using Wire's OIDC issuer normalizer.

Field/projection data comes from Wire-validated source shapes. Scalars become typed fields and separate answers; objects and arrays become bounded structural projections. Portable settings use the host schema and SettingRegistry allowlist. Structural schema versions, kind identifiers and nulls are literals; non-null operator answer values are not defaults or private literals. Canonicalization uses ordinal object properties, preserved arrays, decimal G29 numbers and System.Text.Json escaping within the closed profile; it is not RFC 8785 certification.

## Workflows and authority

Artifact IDs are environment, configuration-manifest, tenant-package, operator-identity and legal-drafts. Checked answers demonstrate a two-tenant manifest, branding and tenant legal draft, integrity-protected operator identity and bilingual instance legal drafts. They are fictional interoperability vectors, not deployment defaults or approved legal templates.

The checked definition materializes a finite reference shape. For other tenant/document cardinalities or nullability, --export INPUT_DIRECTORY NEW_OUTPUT_DIRECTORY validates supplied host shapes and emits a new bounded definition, answers and receipt. The profile supplies a greater revision when shape changes. There is no target-specific module inside the generic engine and no claim that the host only supports the checked cardinalities.

--prepare-identity DOCUMENT NEW_MANIFEST uses OperatorIdentityManifestJson.Create to compute content and revision hashes from the supplied server-owned UUIDv7 revision. It does not invent, increment or bypass revision preconditions. Editing identity content without recomputing integrity fails host verification.

Legal checks preserve all 29 kind/scope classifications, audiences, Draft/ReviewRequired intent, no publication/accountable-identity authority, bounded provenance and locale uniqueness, and the existing safe Markdown codec. Reviewed-template identifiers remain claims for host review, not approval.

--verify is a wire/portable-setting/identity/legal/issuer boundary, not authenticated backend apply or complete business-policy acceptance. Artifacts remain generationOnly. Backend onboarding, authorization, secret binding, concurrency, paid-policy decisions and apply authority remain Event-owned. Live capability remains disabled; extraction does not enable administration.

## Reproduction

Run from Event with TMPDIR=$HOME/.cache/agent-tmp:

~~~bash
dotnet build eng/configurator/DefinitionExporter/DefinitionExporter.csproj --configuration Release --verbosity quiet
dotnet run --project eng/configurator/DefinitionExporter/DefinitionExporter.csproj --configuration Release --no-build -- --write-environment
dotnet run --project eng/configurator/DefinitionExporter/DefinitionExporter.csproj --configuration Release --no-build -- --check-environment
dotnet run --project eng/configurator/DefinitionExporter/DefinitionExporter.csproj --configuration Release --no-build -- --write ../Configurator
dotnet run --project eng/configurator/DefinitionExporter/DefinitionExporter.csproj --configuration Release --no-build -- --check ../Configurator
~~~

The original environment checks run before their generated outputs are accepted. --check commands do not mutate files. --export destinations must be new and newly written files use owner-only Unix creation. Do not use real operator data in checked examples.

Compose each answer workspace using the actual compiled Configurator CLI, with an explicit new output file, then pass it to the Event exporter:

~~~bash
dotnet ../Configurator/src/Configurator.Cli/bin/Release/net10.0/Configurator.Cli.dll artifact create --definition configurator/definitions/islamu-event/setup-definition.json --artifact configuration-manifest --input configurator/definitions/islamu-event/answers/configuration-manifest.json --output NEW_MANIFEST.json --machine
dotnet eng/configurator/DefinitionExporter/bin/Release/net10.0/DefinitionExporter.dll --verify configuration-manifest NEW_MANIFEST.json
~~~

Use each declared ID and matching answer workspace; environment uses a .env output. All answer-derived bytes are restricted and local success retains host-validation-required readiness. EVENT_CONFIGURATOR_ARTIFACT_DIRECTORY selects actual CLI-produced files for the wire test slice, rather than checked expected data:

~~~bash
dotnet test --project tests/Event.Wire.Contracts.UnitTests/Event.Wire.Contracts.UnitTests.csproj --configuration Release --verbosity quiet -- --treenode-filter '/*/*/*ConfiguratorDefinitionArtifactTests/*' --minimum-expected-tests 13 --no-progress --maximum-parallel-tests 1
~~~

## Invariant ownership and evidence

Identity ordering, immutable snapshots, UUIDv7, content/revision tampering, malformed/duplicate/unknown/oversized data and value-free failures remain Wire oracles. Manifest/package branding and legal bytes are verified with actual generic output. Generic YAML/dotenv parsing, bounded projections, disclosure-independent validation, diff/coverage and confidential writing remain Configurator responsibilities. Host bootstrap/mail/reserved-key/secret-provider semantics and the retained source schema generator remain Event responsibilities; reference vectors do not replace backend state-machine tests.

Environment registry/Compose/template/document parity moved to the new exporter owner. No Event benchmark hash is relabelled as a Configurator performance claim. Archived source tests are evidence, not claimed executed tests; their behavior must have an explicit cutover disposition.

Executed: exporter Release build exited 0 with zero warnings/errors; environment write/check succeeded for 361 definitions; declaration write/check and actual CLI admission succeeded. Five actual CLI-generated artifacts passed Event host verification. The final wire slice passed 13 cases with zero failures in 6.848 seconds. Generic external-data conformance passed six cases, including exact independent expected bytes and one native/browser registry identity. Browser rendering tests passed 56 cases, including the real external bundle and public-only controls. Native layout and workstream checks are recorded in the parent ledger, not implied by this exporter evidence.

No database/provider suite, backend apply, deployment or signing activation belongs to these checks. JSON/Razor language-server tooling is unavailable and some C# diagnostics timed out; actual compilation and runtime tests are the decisive evidence. Whole-solution cutover remains gated on destination readiness and retained Event architecture checks.
