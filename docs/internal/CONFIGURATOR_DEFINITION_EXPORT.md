# Event-owned Configurator definition authoring

Event maintains a compact public `configurator.yaml`; the separately installed
Configurator CLI admits and compiles it into canonical runtime JSON. The YAML is
author-maintained, not automatically generated or kept fresh by scanning Event.
There is no Event-specific definition exporter in the supported workflow.

## Install the native maintainer workflow

Install the standalone [Configurator CLI package](https://github.com/ISLAMU/Configurator/blob/HEAD/docs/public/packaging.md).
Copy its complete `skills/configurator-integrate/` directory, including routed
resources, into the maintainer's Agent Skills-aware tool's supported skill location.
The same [portable skill](https://github.com/ISLAMU/Configurator/blob/HEAD/skills/configurator-integrate/SKILL.md)
is available in Configurator's repository. No Event-local skill, generator,
compiler wrapper, target runtime dependency or running Event instance is required.

The maintainer directs the agent to inspect Event's public code contracts:
environment declarations, secret classifications, configuration schema,
SettingRegistry allowlists and Wire identity/legal/issuer contracts. Do not
discover configuration by reading private answers, `.env`, connection strings,
key stores or credentials. Missing application-version or output facts require
review, not invented metadata.

## One source and immutable releases

Keep one handwritten source at
`configurator/definitions/islamu-event/configurator.yaml`. Preserve its allocated
family and revision; the existing Event family is
`a4bcfaa6-9979-43e5-b1f5-00d552618cdf`. For a new independent integration,
`definition init` allocates identity once; never invent or regenerate it manually.
The exact application version, authoring schema version and definition revision
are distinct facts.

Use the skill's `resources/authoring.md` and included authoring-v1 schema for
fields and outputs. Review each nested sensitivity leaf before allowing public
defaults. Secret fields contain no values or defaults. Map Event keys and bounded
structures through Configurator's existing closed projection vocabulary.
Source YAML does not change Event's JSON/dotenv artifact formats.

Run the installed help before using the workflow:

```bash
islamu-configurator --help
islamu-configurator definition --help
islamu-configurator definition check --source configurator/definitions/islamu-event/configurator.yaml --machine
```

Confirm command and authoring contracts are v1. Missing commands are prerequisite
failures, not grounds for an Event-specific replacement. For a new integration,
use `definition init --project-id ID --name NAME --application-version VERSION`
with `--output PATH` for its new source. Supply reviewed exact target facts.

For semantic edits to the existing Event source, retain its prior canonical
release and use it as the explicit anchor:

```bash
islamu-configurator definition revise --source configurator/definitions/islamu-event/configurator.yaml --against configurator/definitions/islamu-event/definition.json --machine
islamu-configurator definition check --source configurator/definitions/islamu-event/configurator.yaml --machine
islamu-configurator definition build --source configurator/definitions/islamu-event/configurator.yaml --machine
```

`revise` requires the same family and current revision in the anchor and increments
only for changed canonical content. It preserves source comments; comment-only
changes need no new revision. Check is read-only. Build writes immutable public
JSON to `dist/configurator/revisions/<revision>.json` relative to the source;
an explicit `--output PATH` is also supported. Conflicting existing output rejects.
Never overwrite a previous release or hand-edit generated runtime JSON.
For an unchanged separately located release, check/build support `--against PATH`.
After revision, build creates the new revision rather than treating the old
release as that new revision's anchor.

Review the actual machine result and canonical digest before selecting a built
release as the distributed `definition.json`. Public `release.json` metadata
records the selected release; a receipt, family GUID, source URL or digest is not
publisher authentication, signing or promotion authority. Retained receipts do
not attest that edited YAML was compiled. Record executed commands and results;
never infer current compilation freshness from historical evidence.

## Independent Event fixtures and target acceptance

The delivery directory contains the source and selected public definition/release
metadata. Synthetic inputs, answer workspaces, expected artifacts and catalogue
snapshots belong to
`tests/Event.Wire.Contracts.UnitTests/Fixtures/Configurator/islamu-event/`.
They are test-owned interoperability vectors, not generated onboarding files,
production defaults or approved legal templates. Derive expectations independently
from Event contracts, not from Configurator's composer.

Exercise complete and incomplete synthetic workspaces with the documented
`artifact validate/create` or `env validate/render` commands and compare produced
keys, types and readiness against those expectations. For example:

```bash
islamu-configurator artifact validate --definition configurator/definitions/islamu-event/definition.json --artifact configuration-manifest --input tests/Event.Wire.Contracts.UnitTests/Fixtures/Configurator/islamu-event/answers/configuration-manifest.json --machine
islamu-configurator artifact create --definition configurator/definitions/islamu-event/definition.json --artifact configuration-manifest --input tests/Event.Wire.Contracts.UnitTests/Fixtures/Configurator/islamu-event/answers/configuration-manifest.json --output NEW_MANIFEST.json --machine
```

Use the newly selected canonical definition, declared artifact ID and matching
answer workspace. All answer-derived bytes are restricted; native output is
protected and create-only on supported hosts. Machine success remains
`host-validation-required`. Event's existing parsers and authorized backend
workflows own semantic acceptance, issuer normalization, identity integrity,
secret binding, tenant scope, optimistic concurrency and apply. Configurator
does not create Event identity hashes or confer administrator authority.

Tenant row edits within the declared bounded list change answers, not definition
shape. Changed document vocabulary, bounds, nullability or projection policy
requires a reviewed source edit and deliberate revision. Legal artifacts remain
drafts for host review; preparation does not publish or certify them.

## Evidence and historical cutover

Source review, CLI admission/build, independent fixture comparison and Event host
tests are separate evidence. A check result is not a build result, and a build is
not backend acceptance. No current compilation or test pass is asserted here.
Record actual source/release digests and executed verification in the owning
change; unavailable prerequisites remain explicitly unverified.

The [source cutover inventory](configurator-source-cutover-inventory.md) retains
historical extraction dispositions. It does not define an active exporter path.
See [the architecture boundary](SETUP_ASSISTANT_ARCHITECTURE.md),
[operator guidance](../public/self-hosting/setup-assistant.md) and
the [native CLI contract](https://github.com/ISLAMU/Configurator/blob/HEAD/docs/public/cli.md).
