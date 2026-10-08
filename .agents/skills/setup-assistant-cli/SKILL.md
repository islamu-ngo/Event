---
name: setup-assistant-cli
description: "Load when preparing Event configuration with the separately installed ISLAMU Configurator CLI and Event-owned local definitions; routes to external command documentation, not bundled event-setup tools, terminal UI or backend API design."
type: workflow
enforcement: suggest
priority: high
---

# External Configurator usage pointer

Use [ISLAMU Configurator](https://github.com/ISLAMU/Configurator), installed
separately from Event. Its own
[CLI guide](https://github.com/ISLAMU/Configurator/blob/HEAD/docs/public/cli.md)
and generated `schemas/islamu-configurator-command-v1.schema.json` own executable
grammar and the machine result contract. Event does not maintain a second parser
or command registry. The bundled offline source roots, focused tests and release
targets are removed; install the external package and keep its notices.

## Event workflow

1. Select a trusted local Event definition from
   `configurator/definitions/islamu-event/definition.json`. Check its family,
   revision and digest against the corresponding `release.json`. Definitions
   are data; do not load arbitrary code or fetch a definition implicitly.
2. Choose the declared artifact ID: `environment`, `configuration-manifest`,
   `tenant-package`, `operator-identity` or `legal-drafts`. Prepare a JSON answer
   workspace matching that definition. Reference answers are test vectors,
   not production credentials or a universal reverse importer.
3. Invoke `islamu-configurator` with `--definition`, `--artifact`, `--input` and,
   for publication, `--output`. For example:

   ```bash
   islamu-configurator artifact validate --definition configurator/definitions/islamu-event/definition.json --artifact configuration-manifest --input manifest-answers.json --machine
   islamu-configurator env render --definition configurator/definitions/islamu-event/definition.json --artifact environment --input environment-answers.json --output deployment.env --machine
   ```

4. Branch on `exitCode` and the `islamu-configurator-command/v1` envelope, not
   rendered text. Success reports `host-validation-required`. `--dry-run`
   never writes an artifact or authorizes restricted stdout.
5. Keep answer values and prepared artifacts on the protected native host.
   Never put secrets in process arguments, logs, environment variable names
   or public browser input. Output is create-only and restricted; unsupported
   protected-save hosts fail closed.
6. Submit prepared artifacts only through Event's existing authorized backend
   workflow. Event owns wire integrity, tenant scope, expected revisions,
   approval, legal publication and administrator authority.

Old `event-setup` command families are not aliases. Existing artifacts require
their answer-workspace proof when using `--answers`; the generic CLI cannot infer
lost/transformed answers. Import-semantic definitions require the explicit
`artifact import` revision options described in the external guide. Do not invent
an identity import wrapper or replace the target revision with a source revision.

See [the operator handoff](../../../docs/public/self-hosting/setup-assistant.md#external-configurator-handoff)
and [Event definition ownership](../../../docs/internal/CONFIGURATOR_DEFINITION_EXPORT.md).
