# CI/CD Implementation

`.ci` is the repository-owned CI/CD implementation layer that can be reused across forges.

GitHub-native discovery files stay in their required location:

- GitHub Actions: `.github/workflows/`

All shared scripts, policy validators, local composite actions, and lint rulesets live here so they are reviewed and owned as one CI/CD surface. Codeberg and other mirrors should point their CI/CD settings at this folder when the provider supports custom pipeline locations.

Current layout:

- `.ci/actions/deploy-coolify/` contains the shared GitHub Coolify deployment composite action.
- `.ci/scripts/` contains file-based C# policy and checksum scripts invoked with `dotnet run <script>.cs -- ...` (action pin validation, Dependabot policy, Dockerfile base-image validation, workflow cache policy, deploy workflow contract validation, NuGet vulnerability/license audits, artifact checksums, and PR release-impact validation).
- `.ci/spectral.yaml` contains the project-owned OpenAPI lint ruleset.

Retired provider-adapter manifests (`.ci/providers/`, `.ci/release/`), `validate-release-provider-adapters.cs`, `generate-release-evidence-bundle.cs`, `report-publication-drift.cs`, and the placeholder `release-adapter-preview.yml` / `release-adapter-final.yml` / `release-publish.yml` workflows have been removed in favor of direct offline release verification in `ISLAMU.ReleaseEngineering` and standard signed artifact publication.

Do not make `.github` a symlink to `.ci`: GitHub discovers workflows from `.github/workflows`, and local reusable workflow calls also use that path.
