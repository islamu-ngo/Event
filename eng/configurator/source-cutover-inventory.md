# Event source cutover inventory

Status: destination readiness confirmed; this literal source allowlist was
removed on develop. It is a disposition record, not a wildcard cleanup command.
Unrelated edits were preserved. There are 201 literal deletions and 33 other
authored changed/created paths below. The parent owns final checks, release
fragment and commit.

## Product and focused test roots

Removed authored files from these exact seven product roots and seven focused
test roots after the destination gate. No other `src/` or `tests/` root is included.

```text
src/Event.Setup.Core/
src/Event.Setup.Artifacts/
src/Event.SetupAssistant/
src/Event.SetupAssistant.Browser/
src/Event.SetupAssistant.Desktop/
src/Event.SetupAssistant.Cli/
src/Event.SetupAssistant.Terminal/
tests/Event.Setup.Core.Tests/
tests/Event.Setup.Artifacts.Tests/
tests/Event.SetupAssistant.Tests/
tests/Event.SetupAssistant.Browser.Tests/
tests/Event.SetupAssistant.Desktop.Tests/
tests/Event.SetupAssistant.Cli.Tests/
tests/Event.SetupAssistant.Terminal.Tests/
```

The nested `src/Event.SetupAssistant/SetupLive/` project was already removed in
phase 9.2; it is not a second source owner to recreate or delete.

## Literal tracked product files

```text
src/Event.Setup.Artifacts/Event.Setup.Artifacts.csproj
src/Event.Setup.Artifacts/ProtectedArtifactWriter.cs
src/Event.Setup.Artifacts/UnixProtectedArtifactWriter.cs
src/Event.Setup.Artifacts/WindowsProtectedArtifactWriter.cs
src/Event.Setup.Artifacts/packages.lock.json
src/Event.Setup.Core/ArtifactDigest.cs
src/Event.Setup.Core/Artifacts/SetupArtifactSensitivity.cs
src/Event.Setup.Core/Composition/SetupCompositionCompiler.cs
src/Event.Setup.Core/Composition/SetupCompositionContracts.cs
src/Event.Setup.Core/Composition/SetupCompositionDirectoryReader.cs
src/Event.Setup.Core/Composition/SetupCompositionLimits.cs
src/Event.Setup.Core/Composition/SetupCompositionNormalizer.cs
src/Event.Setup.Core/Composition/SetupCompositionScaleProfile.cs
src/Event.Setup.Core/Composition/SetupCompositionYamlParser.cs
src/Event.Setup.Core/Dotenv/DotenvCodec.cs
src/Event.Setup.Core/Dotenv/DotenvComposer.cs
src/Event.Setup.Core/Dotenv/DotenvContracts.cs
src/Event.Setup.Core/Dotenv/DotenvReadiness.cs
src/Event.Setup.Core/Environment/EnvironmentActivationExpression.cs
src/Event.Setup.Core/Environment/EnvironmentCatalogue.cs
src/Event.Setup.Core/Environment/EnvironmentCatalogueContracts.cs
src/Event.Setup.Core/Environment/IdentityCorrelationEnvironment.cs
src/Event.Setup.Core/Environment/PlatformEnvironmentCatalogue.cs
src/Event.Setup.Core/Environment/PlatformEnvironmentMetadata.cs
src/Event.Setup.Core/Event.Setup.Core.csproj
src/Event.Setup.Core/Legal/OfflineLegalDraft.cs
src/Event.Setup.Core/Portability/OfflinePortabilityContracts.cs
src/Event.Setup.Core/Portability/OfflinePortabilityWorkflow.cs
src/Event.Setup.Core/Portability/OperatorIdentityManifestCodec.cs
src/Event.Setup.Core/Secrets/LocalSecretGenerator.cs
src/Event.Setup.Core/SetupComparison.cs
src/Event.Setup.Core/SetupDiagnostic.cs
src/Event.Setup.Core/SetupMetadata.cs
src/Event.Setup.Core/SetupReadiness.cs
src/Event.Setup.Core/SetupWorkflow.cs
src/Event.Setup.Core/packages.lock.json
src/Event.SetupAssistant.Browser/App.razor
src/Event.SetupAssistant.Browser/BrowserPublicCatalogue.cs
src/Event.SetupAssistant.Browser/BrowserPublicManifest.cs
src/Event.SetupAssistant.Browser/Event.SetupAssistant.Browser.csproj
src/Event.SetupAssistant.Browser/Pages/Catalogue.razor
src/Event.SetupAssistant.Browser/Pages/ManifestPreview.razor
src/Event.SetupAssistant.Browser/Program.cs
src/Event.SetupAssistant.Browser/Shared/MainLayout.razor
src/Event.SetupAssistant.Browser/_Imports.razor
src/Event.SetupAssistant.Browser/packages.lock.json
src/Event.SetupAssistant.Browser/wwwroot/css/setup.css
src/Event.SetupAssistant.Browser/wwwroot/index.html
src/Event.SetupAssistant.Cli/Commands/CatalogueCommands.cs
src/Event.SetupAssistant.Cli/Commands/DoctorCommands.cs
src/Event.SetupAssistant.Cli/Commands/EnvironmentCommands.cs
src/Event.SetupAssistant.Cli/Commands/LegalCommands.cs
src/Event.SetupAssistant.Cli/Commands/ManifestCommands.cs
src/Event.SetupAssistant.Cli/Commands/PortabilityCommands.cs
src/Event.SetupAssistant.Cli/Commands/SetupCliCommandSettings.cs
src/Event.SetupAssistant.Cli/Commands/TenantPackageCommands.cs
src/Event.SetupAssistant.Cli/Event.SetupAssistant.Cli.csproj
src/Event.SetupAssistant.Cli/Program.cs
src/Event.SetupAssistant.Cli/Properties/AssemblyInfo.cs
src/Event.SetupAssistant.Cli/SetupCliApplication.cs
src/Event.SetupAssistant.Cli/SetupCliArgumentPreflight.cs
src/Event.SetupAssistant.Cli/SetupCliCatalogueEnvironmentHandlers.cs
src/Event.SetupAssistant.Cli/SetupCliCommand.cs
src/Event.SetupAssistant.Cli/SetupCliCommandRegistry.cs
src/Event.SetupAssistant.Cli/SetupCliCommandSchemaMetadata.cs
src/Event.SetupAssistant.Cli/SetupCliContracts.cs
src/Event.SetupAssistant.Cli/SetupCliHumanOutput.cs
src/Event.SetupAssistant.Cli/SetupCliMachineOutput.cs
src/Event.SetupAssistant.Cli/SetupCliPortabilityHandlers.cs
src/Event.SetupAssistant.Cli/packages.lock.json
src/Event.SetupAssistant.Desktop/App.axaml
src/Event.SetupAssistant.Desktop/App.axaml.cs
src/Event.SetupAssistant.Desktop/Event.SetupAssistant.Desktop.csproj
src/Event.SetupAssistant.Desktop/Program.cs
src/Event.SetupAssistant.Desktop/Properties/AssemblyInfo.cs
src/Event.SetupAssistant.Desktop/ViewModels/SetupDesktopShellViewModel.cs
src/Event.SetupAssistant.Desktop/Views/EnvironmentView.axaml
src/Event.SetupAssistant.Desktop/Views/EnvironmentView.axaml.cs
src/Event.SetupAssistant.Desktop/Views/IdentityDraftView.axaml
src/Event.SetupAssistant.Desktop/Views/IdentityDraftView.axaml.cs
src/Event.SetupAssistant.Desktop/Views/MainWindow.axaml
src/Event.SetupAssistant.Desktop/Views/MainWindow.axaml.cs
src/Event.SetupAssistant.Desktop/Views/ManifestView.axaml
src/Event.SetupAssistant.Desktop/Views/ManifestView.axaml.cs
src/Event.SetupAssistant.Desktop/packages.lock.json
src/Event.SetupAssistant.Terminal/Event.SetupAssistant.Terminal.csproj
src/Event.SetupAssistant.Terminal/Program.cs
src/Event.SetupAssistant.Terminal/Properties/AssemblyInfo.cs
src/Event.SetupAssistant.Terminal/Resources/SetupTerminalText.ar.resx
src/Event.SetupAssistant.Terminal/Resources/SetupTerminalText.resx
src/Event.SetupAssistant.Terminal/SetupSecretTextField.cs
src/Event.SetupAssistant.Terminal/SetupTerminalApplication.cs
src/Event.SetupAssistant.Terminal/SetupTerminalArtifactOperation.cs
src/Event.SetupAssistant.Terminal/SetupTerminalProgram.cs
src/Event.SetupAssistant.Terminal/SetupTerminalSecretBuffer.cs
src/Event.SetupAssistant.Terminal/SetupTerminalSignalScope.cs
src/Event.SetupAssistant.Terminal/SetupTerminalText.cs
src/Event.SetupAssistant.Terminal/SetupTerminalWindow.cs
src/Event.SetupAssistant.Terminal/Views/CatalogueView.cs
src/Event.SetupAssistant.Terminal/Views/IdentityDraftView.cs
src/Event.SetupAssistant.Terminal/Views/LegalDraftView.cs
src/Event.SetupAssistant.Terminal/Views/ManifestView.cs
src/Event.SetupAssistant.Terminal/packages.lock.json
src/Event.SetupAssistant/Event.SetupAssistant.csproj
src/Event.SetupAssistant/Presentation/SetupOperationGeneration.cs
src/Event.SetupAssistant/Presentation/SetupPresentationContracts.cs
src/Event.SetupAssistant/Presentation/SetupPresentationSession.cs
src/Event.SetupAssistant/Presentation/SetupPresentationWorkspace.cs
src/Event.SetupAssistant/Presentation/SetupWorkspaceId.cs
src/Event.SetupAssistant/packages.lock.json
```

## Literal tracked focused test files

```text
tests/Event.Setup.Artifacts.Tests/Event.Setup.Artifacts.Tests.csproj
tests/Event.Setup.Artifacts.Tests/ProtectedArtifactWriterTests.cs
tests/Event.Setup.Artifacts.Tests/packages.lock.json
tests/Event.Setup.Core.Tests/EmailOptionalSetupTests.cs
tests/Event.Setup.Core.Tests/Environment/DotenvContractTests.cs
tests/Event.Setup.Core.Tests/Environment/EnvironmentCatalogueInvariantTests.cs
tests/Event.Setup.Core.Tests/Environment/EnvironmentContractExpectedVectors.cs
tests/Event.Setup.Core.Tests/Environment/EnvironmentContractRuntime.cs
tests/Event.Setup.Core.Tests/Environment/EnvironmentInvariantVerifier.cs
tests/Event.Setup.Core.Tests/Environment/EnvironmentMachineConfiguration.cs
tests/Event.Setup.Core.Tests/Environment/IdentityCorrelationEnvironmentTests.cs
tests/Event.Setup.Core.Tests/Environment/ReservedFingerprintEnvironmentTests.cs
tests/Event.Setup.Core.Tests/Event.Setup.Core.Tests.csproj
tests/Event.Setup.Core.Tests/GlobalUsings.cs
tests/Event.Setup.Core.Tests/Portability/OfflinePortabilityWorkflowTests.cs
tests/Event.Setup.Core.Tests/Portability/OperatorIdentityManifestTests.cs
tests/Event.Setup.Core.Tests/Program.cs
tests/Event.Setup.Core.Tests/SetupArtifactSensitivityTests.cs
tests/Event.Setup.Core.Tests/SetupCompositionInvariantTests.cs
tests/Event.Setup.Core.Tests/SetupCompositionScaleTests.cs
tests/Event.Setup.Core.Tests/SetupCompositionTestContract.cs
tests/Event.Setup.Core.Tests/SetupCoreArchitectureTests.cs
tests/Event.Setup.Core.Tests/packages.lock.json
tests/Event.SetupAssistant.Browser.Tests/BrowserManifestReadLifecycleTests.cs
tests/Event.SetupAssistant.Browser.Tests/BrowserOfflineWorkflowTests.cs
tests/Event.SetupAssistant.Browser.Tests/BrowserReleaseContractTests.cs
tests/Event.SetupAssistant.Browser.Tests/BrowserSecretBoundaryTests.cs
tests/Event.SetupAssistant.Browser.Tests/Event.SetupAssistant.Browser.Tests.csproj
tests/Event.SetupAssistant.Browser.Tests/GlobalUsings.cs
tests/Event.SetupAssistant.Browser.Tests/Program.cs
tests/Event.SetupAssistant.Browser.Tests/packages.lock.json
tests/Event.SetupAssistant.Cli.Tests/Event.SetupAssistant.Cli.Tests.csproj
tests/Event.SetupAssistant.Cli.Tests/GlobalUsings.cs
tests/Event.SetupAssistant.Cli.Tests/Program.cs
tests/Event.SetupAssistant.Cli.Tests/SetupCliApplicationTests.cs
tests/Event.SetupAssistant.Cli.Tests/SetupCliContractSpecification.cs
tests/Event.SetupAssistant.Cli.Tests/SetupCliContractTests.cs
tests/Event.SetupAssistant.Cli.Tests/SetupCliMachineContractVerifier.cs
tests/Event.SetupAssistant.Cli.Tests/SetupCliOperatorIdentityTests.cs
tests/Event.SetupAssistant.Cli.Tests/SetupCliProgramTests.cs
tests/Event.SetupAssistant.Cli.Tests/packages.lock.json
tests/Event.SetupAssistant.Desktop.Tests/DesktopApplicationTests.cs
tests/Event.SetupAssistant.Desktop.Tests/DesktopProtectedWriteInvariantTests.cs
tests/Event.SetupAssistant.Desktop.Tests/Event.SetupAssistant.Desktop.Tests.csproj
tests/Event.SetupAssistant.Desktop.Tests/GlobalUsings.cs
tests/Event.SetupAssistant.Desktop.Tests/Program.cs
tests/Event.SetupAssistant.Desktop.Tests/packages.lock.json
tests/Event.SetupAssistant.Terminal.Tests/Event.SetupAssistant.Terminal.Tests.csproj
tests/Event.SetupAssistant.Terminal.Tests/GlobalUsings.cs
tests/Event.SetupAssistant.Terminal.Tests/Program.cs
tests/Event.SetupAssistant.Terminal.Tests/SetupTerminalGuiAdapterTests.cs
tests/Event.SetupAssistant.Terminal.Tests/SetupTerminalOfflineWorkflowTests.cs
tests/Event.SetupAssistant.Terminal.Tests/SetupTerminalSecurityTests.cs
tests/Event.SetupAssistant.Terminal.Tests/packages.lock.json
tests/Event.SetupAssistant.Tests/Event.SetupAssistant.Tests.csproj
tests/Event.SetupAssistant.Tests/GlobalUsings.cs
tests/Event.SetupAssistant.Tests/Program.cs
tests/Event.SetupAssistant.Tests/SetupPresentationModelContract.cs
tests/Event.SetupAssistant.Tests/SetupPresentationModelTests.cs
tests/Event.SetupAssistant.Tests/packages.lock.json
```

## Literal obsolete engineering files

The complete `eng/setup-assistant/` root has this tracked inventory. Event
environment ownership already lives in the retained exporter; its metadata and
checks must not be removed with the old generator.

```text
eng/setup-assistant/EnvironmentCatalogueGenerator/EnvironmentCatalogueGenerator.csproj
eng/setup-assistant/EnvironmentCatalogueGenerator/Program.cs
eng/setup-assistant/EnvironmentCatalogueGenerator/packages.lock.json
eng/setup-assistant/GenerateSetupAssistantRatchets.cs
eng/setup-assistant/GenerateSetupCompositionScaleProfiles.cs
eng/setup-assistant/SetupAssistant.Release.proj
eng/setup-assistant/SetupCliCommandSchemaGenerator/Program.cs
eng/setup-assistant/SetupCliCommandSchemaGenerator/SetupCliCommandSchemaGenerator.csproj
eng/setup-assistant/SetupCliCommandSchemaGenerator/packages.lock.json
eng/setup-assistant/ValidateSetupAssistantSkill.cs
eng/setup-assistant/generated/browser-release-capabilities.json
eng/setup-assistant/generated/composition-scale-profiles.json
eng/setup-assistant/generated/environment-catalogue.json
eng/setup-assistant/generated/frozen-contract-baseline.json
eng/setup-assistant/generated/setup-live-release-capabilities.json
eng/setup-assistant/probes/communitytoolkit/CommunityToolkitProbe.csproj
eng/setup-assistant/probes/communitytoolkit/CommunityToolkitProbeViewModel.cs
eng/setup-assistant/probes/communitytoolkit/packages.lock.json
eng/setup-assistant/probes/dependency-injection/DependencyInjectionProbe.csproj
eng/setup-assistant/probes/dependency-injection/DependencyInjectionProbeComposition.cs
eng/setup-assistant/probes/dependency-injection/packages.lock.json
eng/setup-assistant/probes/yamldotnet/Program.cs
eng/setup-assistant/probes/yamldotnet/YamlDotNetProbe.csproj
eng/setup-assistant/probes/yamldotnet/packages.lock.json
```

## Additional disposition

The following literal obsolete owners were removed after destination compiled
boundary/release guards and generic command schema replacement were accepted.

```text
tests/Event.Architecture.Tests/SetupAssistantReleaseTests.cs
schemas/event-setup-command-v1.schema.json
eng/release/dependencies/terminal-gui/BuildTerminalGuiPackage.sh
eng/release/dependencies/terminal-gui/VerifyTerminalGuiPackage.cs
eng/release/dependencies/terminal-gui/feed/ISLAMU.Terminal.Gui.2.4.17-islamu.1.nupkg
eng/release/dependencies/terminal-gui/probe/TerminalGuiClosure.csproj
eng/release/dependencies/terminal-gui/probe/packages.lock.json
```

Retain terminal dependency provenance (`source.json`, `approval.json`,
`dependency-review.md`, the patch and generated package/SBOM evidence) as a
historical licensing record unless the parent explicitly relocates that record.
Do not ingest or regenerate third-party source for this cutover.

Replaced obsolete project/release/capability assertions in
`tests/Event.Architecture.Tests/SetupAssistantArchitectureTests.cs`; preserve
its backend portability registry, schemas, value-free target/revision bindings
and SetupLive routing/security expectations under the retained Event owner.
Retain `ConfiguredAdministratorBootstrapArchitectureTests.cs` (its obsolete
offline-project test is replaced by the exporter inward dependency assertion).
Retain `SetupLiveWireArchitectureTests.cs` and
`SetupLiveGeneratedContractTests.cs`.

Updated, not deleted, `Explore.slnx`, `Directory.Packages.props`, `NuGet.Config`,
`.github/workflows/test.yml` and `_build-test.yml`. Removed fourteen project
registrations plus the two old generator registrations; added the retained
exporter solution registration. Removed old product test/native-save OS jobs,
browser publication, package/probe gates and their audited compiler-runtime
isolation steps. Kept the separate Event SetupLive security step and coverage
aggregation. Removed obsolete generated-input exclusions from
`eng/tools/StripAboutMeHeaders.cs`.

Zero-caller removals are the eight central pins `Avalonia`,
`Avalonia.Desktop`, `Avalonia.Headless`, `Avalonia.Themes.Fluent`,
`CommunityToolkit.Mvvm`, `ISLAMU.Terminal.Gui`, `Spectre.Console`,
`Spectre.Console.Cli`, plus the
`islamu-terminal-gui` NuGet source/mapping. After source/probe deletion no
remaining csproj referenced these packages.
Shared `Microsoft.Extensions.DependencyInjection*` and `YamlDotNet` are retained.
Do not remove a license-policy entry merely because one product caller moved.

## Retained owners and verification

Keep `eng/configurator/DefinitionExporter/` and
`configurator/definitions/islamu-event/` entirely. The existing unrelated
`eng/configurator/DefinitionExporter/packages.lock.json` edit belongs to the
parent and was not rewritten. Keep both relocated SetupLive source files and
both client test files, `.env.example`, Compose, Event portable schemas, Domain,
Application, Wire, Persistence, bootstrap and all backend onboarding semantics.

The retained API command is:

```bash
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --no-build --verbosity minimal -- --treenode-filter "/*/*/*SetupLiveAuthoritySecurityTests/*" --minimum-expected-tests 35 --maximum-parallel-tests 1 --no-progress --report-trx --report-trx-filename SetupLiveAuthoritySecurityTests.trx
```

CI appends the unchanged optional Cobertura arguments and uses the retained
`test_setup` started output for coverage report validation. Do not run a broad
suite or provider matrix for this child cutover; the parent owns final checks.

The audited `.nupkg` above was removed with native `rm` after explicit
authorization for binary deletion; every authored text deletion/edit used
`apply_patch`. Ignored local build caches are not product source or new owners.

## Authored changed files

In addition to the literal deleted-file lists above, this cutover owns these
updated/created paths. The parent-owned exporter lockfile and native populated
release fragment are deliberately excluded.

```text
.agents/skills/setup-assistant-cli/SKILL.md
.github/workflows/_build-test.yml
.github/workflows/test.yml
Directory.Packages.props
Explore.slnx
NuGet.Config
PROJECTS.md
README.md
docs/internal/CI_CD_GOVERNANCE.md
docs/internal/CONFIGURATION.md
docs/internal/CONFIGURATOR_DEFINITION_EXPORT.md
docs/internal/MAPPING_AND_OPERATIONS.md
docs/internal/OPERATIONS.md
docs/internal/SECRETS.md
docs/internal/SELF_HOSTING.md
docs/internal/SETUP_ASSISTANT_ARCHITECTURE.md
docs/internal/SETUP_COMPOSITION_SCALE.md
docs/internal/TESTING.md
docs/internal/TROUBLESHOOTING.md
docs/internal/adr/ADR-035-offline-setup-artifact-boundary.md
docs/internal/adr/ADR-configurator-extraction.md
docs/internal/legal/dependencies/setup-assistant-ui.md
docs/public/documentation/readme/configuration-and-operations/environment-variables.md
docs/public/self-hosting/operator-identity-portability.md
docs/public/self-hosting/setup-assistant.md
dev/_journal/domains/configurator-extraction.md
dev/backlog/configurator-live-integration.md
eng/configurator/DefinitionExporter/Program.cs
eng/configurator/source-cutover-inventory.md
eng/tools/StripAboutMeHeaders.cs
tests/Event.Architecture.Tests/ConfiguratorDefinitionArchitectureTests.cs
tests/Event.Architecture.Tests/ConfiguredAdministratorBootstrapArchitectureTests.cs
tests/Event.Architecture.Tests/SetupAssistantArchitectureTests.cs
```
