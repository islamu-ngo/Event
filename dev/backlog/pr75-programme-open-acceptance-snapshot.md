# PR75 — preserved broader security-programme acceptance contracts

> Preservation snapshot: 2026-10-22, PR75 handoff at 84fc67c7. This is dated supporting evidence, not a current passing analysis or executable commit plan. The main report is [PR75 deferred implementation](pr75-remaining-implementation-handoff.md). Resolve stale paths and compare current Git source before acting. Local ignored notes and logs are not required to retain the contracts below.

This bounded extraction preserves phase starting paths, dependencies and unchecked acceptance records from the older approved programme. It intentionally excludes thousands of chronological session notes and obsolete commit recipes. Phase labels and unchecked boxes are **historical**: many fixes are already committed, and no fresh source audit of every broader phase is claimed. Reconcile each record against current HEAD and its tests before scheduling work. Latest user direction explicitly defers completion; older prohibitions on deferral are superseded by that direction. Do not restore the extracted Configurator/Setup Assistant, run obsolete tooling, or replay cached patches.

- [ ] Verify catalogue/projection coexistence12, prior detail/clock consumers and
- [ ] Complete seven scalar-handler/hierarchy dedicated routing (24 actual RED),
- [ ] Install/verify real read permissions, DI and Global readiness; create the
- [ ] Finish inherited repairs: review two literal fixes and six compiler-helper
- [ ] Unify authentication, lookup, governance and preference operations on EF;
- [ ] Move settings/administration/configuration-import and event/read policy
- [ ] Move dispatch/outbox/reconciliation/storage/privacy workflows to C# state
- [ ] Reconcile provider schema and least-privilege installation with the new
- [ ] Complete adversarial review, five-provider/migration/architecture exit gates,
- [ ] Reconcile original Runtime and Global creation owners, retaining existing
- [ ] Verify every API instance-parent/tenant-child path retains the same ordered
- [ ] Update paired technical/operator docs and I-VSD trust boundary, run targeted
- [ ] Implement tenant-bound native discovery snapshot capability
- [ ] Implement bounded native public discovery candidate reads
- [ ] Reconcile generated client guarded wire consumers
- [ ] Execute mixed readiness under original native owner
- [ ] Publish closed AI discovery enum schema
- [ ] Bind trusted human subjects before owner construction
- [ ] Adapt email constructors and explicit internal routing
## Phase 1: Security execution and evidence inventory — COMPLETE
**Depends on:** merged baseline. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `.github/workflows/security-tests.yml`
  - `docs/internal/CI_CD_GOVERNANCE.md`
  - `eng/security/security-control-register.json`
  - `docs/internal/SECURITY_VERIFICATION.md`
  - `eng/release/src/ISLAMU.ReleaseEngineering/SecurityChangeSelection.cs`
  - `eng/release/src/ISLAMU.ReleaseEngineering/SecurityControlRegister.cs`
  - `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/SecurityChangeSelectionTests.cs`
  - `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/SecurityControlRegisterTests.cs`
  - `eng/release/src/ISLAMU.ReleaseEngineering/Program.cs` (existing native dispatch companion)
  - `eng/release/src/ISLAMU.ReleaseEngineering/CandidateCommand.cs` (existing admission decision; execution packet before manifest generation)
  - `eng/release/src/ISLAMU.ReleaseEngineering/SecurityExecutionEvidence.cs` (proposed shared packet producer/validator)
  - `eng/release/src/ISLAMU.ReleaseEngineering/SecurityExecutionEvidence.Production.cs` (same partial owner; actual CI packet creation)
  - `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ReleaseCandidateVerificationTests.cs` (actual acceptance/rejection boundary)
  - `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/SecurityExecutionEvidenceTests.cs` (proposed bounded protocol cases)
  - `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/SecurityExecutionEvidenceFixture.cs` (proposed synthetic admission input producer, never workflow evidence)
  - `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/GovernedReleaseFixture.cs` (existing producer and tag-only sidecar-copy companion)
  - `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ReleaseTagVerificationTests.cs` (existing private producer companion)
  - `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/PublicationInventoryCommandTests.cs` (generated second-line release producer companion)
  - `src/Explore.ServiceDefaults/Diagnostics/EndpointSecurityInventory.cs` (proposed shared declared-metadata capture, not an HTTP endpoint)
  - `tests/Event.API.IntegrationTests/Hosting/ApiEndpointApplicabilityTests.cs` (actual API composition and optional MCP graph)
  - `tests/Explore.Blazor.IntegrationTests/Hosting/BffEndpointApplicabilityTests.cs` (actual split BFF composition and fixture classification)
  - `tests/Event.Standalone.IntegrationTests/StandaloneEndpointApplicabilityTests.cs` (actual combined composition and declared profile limits)
  - `tests/Event.Standalone.IntegrationTests/Fixtures/NonTestingStandaloneWebApplicationFactory.cs` (proposed controlled actual non-Testing composition)
  - `tests/Event.Standalone.IntegrationTests/HostedWorkerActivationTests.cs` (required activation/shutdown boundary; no descriptor-only proof)
  - `docs/internal/OPERATIONS.md` (selection/evidence operations companion)
  - `docs/internal/TESTING.md` (actual executable selectors/evidence companion)
  - `docs/internal/RELEASE_CHECKLIST.md` (candidate-security admission companion)
  - `docs/internal/RELEASE_POLICY.md` (truthful candidate evidence companion)
  - `docs/internal/RELEASE_RUNBOOK.md` (owned verification invocation companion)
## Phase 2: External Google and Keycloak runtime credential custody — IN PROGRESS
**Depends on:** Phase 1. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs`
  - `src/Explore.API/Controllers/InstanceOnboardingController.cs`
  - `src/Explore.API/Hateoas/Policies/InstanceOnboardingStatusLinkPolicy.cs`
  - `src/Explore.API/Hateoas/RouteNames.cs`
  - `src/Explore.Application/Contracts/Services/IAuthProviderConfigurationService.cs`
  - `src/Explore.Application/DTOs/Onboarding/AuthProviderConfigurationDto.cs`
  - `src/Explore.Application/DTOs/Onboarding/Validators/AuthProviderConfigurationDtoValidator.cs` — existing secret-field validator must follow the atomic contract change
  - `src/Explore.Blazor.Client/Pages/Admin/Instance/Components/InstanceAuthProviderSection.razor` — remove Google secret entry; render server-owned readiness
  - `src/Explore.Blazor.Client/Pages/Onboarding/AuthProviderConfiguration.razor` — remove secret-bearing client state and entry
  - `src/Explore.Blazor.Client/Services/InstanceOnboardingService.cs` — remove Google plaintext PATCH projection
  - `src/Explore.Application/DTOs/Instance/PatchInstanceSpecializedSettingsDtos.cs` — actual strict write/group DTO owner
  - `src/Explore.Blazor/Configuration/InfisicalConfigurationProvider.cs`
  - `src/Explore.Domain/Secrets/AuthenticationCredentialCutover.cs` — proposed capability-specific maintenance operation and value-free evidence
  - `tests/Event.Domain.UnitTests/Secrets/AuthenticationCredentialCutoverTests.cs` — proposed deterministic operation/state invariant tests
  - `src/Explore.Blazor/Configuration/InfisicalConfigurationSource.cs`
  - `src/Explore.Blazor/Services/DynamicAuthSchemeInitializer.cs`
  - `src/Explore.Blazor/Services/SetupSecretForwardingHandler.cs`
  - `src/Explore.Blazor/Services/Auth/BffProviderReadinessService.cs`
  - `src/Explore.Blazor/Extensions/BffAuthEndpoints.cs`
  - `src/Explore.Blazor/Extensions/HttpClientExtensions.cs`
  - `src/Explore.Blazor/Hosting/BlazorHostServiceCollectionExtensions.cs`
  - `src/Explore.Blazor/Hosting/BlazorHostApplicationExtensions.cs`
  - `src/Event.Standalone/Hosting/StandaloneHostApplicationExtensions.cs`
  - `src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs` — generated via owned generator, never hand-edited
  - `schemas/openapi_islamu-event.json` — generated via API OpenAPI composition, never hand-edited
  - `tests/Explore.Blazor.IntegrationTests/Extensions/BlazorConfigurationExtensionsTests.cs`
  - `tests/Explore.Blazor.IntegrationTests/Configuration/InfisicalConfigurationProviderTests.cs` — proposed actual transport acquisition contract
  - `tests/Explore.Blazor.IntegrationTests/Endpoints/BffAuthEndpointValidationTests.cs`
  - `tests/Explore.Secrets.UnitTests/Services/SecretResolverBindingTests.cs`
  - `tests/Explore.Secrets.UnitTests/Services/SecretRotationReplicaConvergenceTests.cs`
  - `tests/Event.Architecture.Tests/SecretAuthorityLeastPrivilegeArchitectureTests.cs`
  - `docs/public/documentation/readme/configuration-and-operations/infisical.md`
  - `docs/public/documentation/readme/configuration-and-operations/authentication-providers.md`
  - `docs/public/documentation/readme/configuration-and-operations/secrets.md`
  - `docs/public/documentation/readme/configuration-and-operations/backup-restore-upgrade.md`
  - `docs/internal/SECRETS.md`
  - `docs/internal/CONFIGURATION.md`
  - `docs/internal/BLAZOR.md`
  - `src/Explore.Application/Services/AuthProviderConfigurationService.cs`
  - `src/Explore.Application/Features/InstanceOnboarding/Handlers/Commands/UpdateAuthProviderConfigurationCommandHandler.cs`
  - `src/Explore.Blazor/Services/DynamicAuthSchemeManager.cs`
  - `src/Explore.Blazor/Services/IDynamicAuthSchemeManager.cs` — remove internal secret-readback refresh parameter
  - `tests/Explore.Blazor.IntegrationTests/Endpoints/LocalBffAuthenticationFlowTests.cs` — interface-only companion
  - `tests/Event.Standalone.IntegrationTests/Fixtures/StandaloneWebApplicationFactory.cs` — interface-only companion
  - `src/Explore.Blazor/Extensions/ConfigurationExtension.cs`
  - `tests/Event.API.IntegrationTests/Features/InstanceOnboardingControllerTests.cs`
  - `tests/Event.API.IntegrationTests/Features/ProviderCredentialHttpBoundaryTests.cs` — stale service-interface/secret-field fixture
  - `tests/Event.API.IntegrationTests/Features/SetupSecretDurableRevocationTests.cs` — stale removed Google field fixture
  - `tests/Event.API.IntegrationTests/Features/Hateoas/OnboardingStatusLinkPolicyTests.cs` — point setup authentication affordance to the nonsecret metadata route
  - `tests/Explore.Blazor.Client.Tests/Services/InstanceOnboardingServiceTests.cs` — stale removed Keycloak response field assertions
  - `tests/Explore.Secrets.UnitTests/Services/GoogleSecretAuthorityTests.cs`
  - `tests/Event.Persistence.IntegrationTests/Keycloak/KeycloakConnectionOwnershipPersistenceTests.cs` — explicit provider-service constructor companion
  - `tests/Event.Persistence.IntegrationTests/Repositories/VisitorAccessSettingsWriterTests.cs` — explicit provider-service constructor companion
  - `tests/Event.Application.UnitTests/DTOs/Onboarding/AuthProviderConfigurationDtoValidatorTests.cs` — replace raw-secret validation with authoritative ownership checks
  - `tests/Explore.Blazor.Client.Tests/Pages/Admin/InstanceAuthProviderSectionTests.cs` — stale removed Keycloak field fixture
  - `src/Explore.Application/Contracts/Secrets/IAuthenticationRuntimeSecretWriter.cs`
  - `src/Explore.Secrets/Services/AuthenticationRuntimeSecretWriter.cs`
  - `src/Explore.Secrets/Services/SecretResolver.cs` — exact runtime credential reference admission before cache/source dispatch
  - `src/Explore.Secrets/Extensions/SecretResolutionServiceCollectionExtensions.cs` — backend-only narrow writer registration
  - `src/Explore.Application/Contracts/Secrets/IInfisicalClientFactory.cs` — bounded structured provider failures, preserve existing write signature
  - `src/Explore.Secrets/Infrastructure/InfisicalClientFactory.cs` — actual SDK boundary failure translation
  - `tests/Explore.Secrets.UnitTests/SetupLive/InfisicalSdkFailureBoundaryTests.cs` — proposed local real SDK failure/status evidence
  - `tests/Explore.Secrets.UnitTests/Services/AuthenticationRuntimeSecretWriterTests.cs` — proposed narrow writer dispatch/state tests
  - `src/Explore.Secrets/Extensions/SecretResolutionServiceCollectionExtensions.cs`
  - `src/Explore.Persistence/Services/ProviderCredentialDataMigration.cs`
  - `src/Event.MigrationService/Worker.cs`
  - `src/Explore.Application/DTOs/Onboarding/AuthProviderConfigurationDto.cs`
  - `src/Explore.Application/DTOs/Secrets/SecretOwnershipDto.cs`
  - `src/Explore.Blazor/Configuration/InfisicalConfigurationProvider.cs`
## Phase 3: Coherent PostgreSQL isolation — IN PROGRESS
**Depends on:** Phase 1. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Persistence/Schema/ProviderPrimitives/ExploreDatabaseMigrator.cs`
  - `src/Explore.Persistence/Security/ProviderPrimitives/PostgresTenantRowLevelSecurityModel.cs`
  - `src/Explore.Persistence/Security/ProviderPrimitives/PostgresTenantSessionInterceptor.cs`
  - `src/Explore.Persistence/PersistenceServicesRegistration.cs`
  - `tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresTenantSessionRlsPrototypeTests.cs`
- [ ] **3.1 Red real isolation/activation contracts** — preserved aliases: `8.1`
  - **Acceptance:** author ordinary-role application-table read/insert/update/delete, absent tenant, nullable/global/parent-derived policy, pool reuse, already-open tenant switch and worker tests; verify fixture uses distinct injected runtime/migrator credentials and does not exclude the actual role from assertions.
  - **Dependencies:** Phase 1; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** XL.
- [ ] **3.2 Green policy/session/role authority** — preserved aliases: `8.2`
  - **Acceptance:** move policy/constraint DDL to migration authority; require coherent mandatory PostgreSQL activation and read-only readiness verification; implement immutable tenant leases and bounded global worker authority; verify normal runtime cannot alter policies or bypass all RLS and other providers do not receive false RLS claims.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **3.3 Wire affected global callers and truthful docs** — preserved aliases: `8.3`
  - **Acceptance:** classify authentication/bootstrap/queue/fanout/maintenance work and bind exact authority/pool; reconcile pipeline/native CQS/RLS descriptions; verify legitimate global work is possible only through its explicit bounded scope. Inspect each worker's actual enablement, cancellation/session/lease cleanup and dynamic credentials, and document actual role inputs. Test missing/duplicate/null/normalized/provider-specific constraints and distinct runtime/migrator authority; reuse current SecretBinding computed-slot policy instead of treating its historical MySQL defect as still open.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **3.4 Portable binding uniqueness reconciliation** — preserved aliases: `SEC:BIND-001`
  - **Acceptance:** Verify the current computed-slot/entity configuration and existing ProviderPersistsOnlyTenantQualifiedOpaqueMetadata contract first; cross-tenant and instance/tenant coexistence succeed and same-scope duplicates fail on all five final engines. Add only missing boundaries or a reproduced repair, never assume the historical MySQL defect remains.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **3.5 Phase gate** — preserved aliases: `8.4`
  - **Acceptance:** targeted Persistence build and one selected `tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj` PostgreSQL lane including `PostgresTenantLeaseIsolationTests` and actual-table role/activation contracts. Application in-memory lease tests are Ring 1, not isolation certification. Record real-boundary Green before delivery; unavailable approved engine means blocked proof. Final Ring 3 supplies the complete provider union, not the first execution of this invariant.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **3.6 Phase commits** — preserved aliases: `8.5`
  - **Acceptance:** split if actual worker rewiring creates separate independently reviewable outcomes; verify each complete vertical outcome has its own truthful contract and no runtime superuser workaround.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
## Phase 4: Connection-bound webhook delivery — IN PROGRESS
**Depends on:** Phase 1. **Scenario ownership:** S09.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Infrastructure/Webhooks/WebhookEndpointSafetyPolicy.cs`
  - `src/Explore.Infrastructure/Webhooks/WebhookDeliveryDrainService.cs`
  - `src/Explore.Infrastructure/Services/Registration/Providers/SubmissionSinks/WebhookRegistrationProviderSubmissionSink.cs`
  - `src/Explore.Infrastructure/InfrastructureServicesRegistration.cs`
  - `src/Explore.Infrastructure/Http/OutboundEndpointPolicy.cs`
  - `src/Explore.Infrastructure/Http/VettedAddressConnector.cs`
  - `tests/Explore.Infrastructure.Tests/Infrastructure/Http/OutboundTransportSecurityTests.cs`
- [ ] **4.1 Red destination handoff** — preserved aliases: `4.1`
  - **Acceptance:** test S09 with controlled resolution/connection signals, mixed/rebound/mapped/metadata answers and allowed CIDR; verify forbidden peer receives zero body/signature and a safe approved peer is actually selected.
  - **Dependencies:** Phase 1; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **4.2 Green vetted connection profiles** — preserved aliases: `4.2`
  - **Acceptance:** connect only to connection-time vetted addresses with original hostname TLS, all-answer validation and redirects/cookies/uncontrolled proxies off; wire both webhook send registrations; verify metadata policy cannot disappear through broad private opt-in and policy denial is PermanentBeforeHandoff, not retryable network error.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **4.3 Preserve delivery lineage and docs** — preserved aliases: `4.3`
  - **Acceptance:** retain frozen destination/payload/credential snapshot, claim fences, retention and retry outcomes; document exact private exceptions and host/egress semantics; verify no widened authorization or signature change.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **4.4 Phase gate** — preserved aliases: `4.4`
  - **Acceptance:** targeted Infrastructure build plus `tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj`, NEW `OutboundTransportSecurityTests`. Pure address classification stays in memory; delivery proof exercises the registered connector/handler against controlled approved peers, including TLS/origin/pool identity, before claiming socket confinement. A resolver/connector delegate that grants the policy under test cannot supply boundary Green.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **4.5 Phase commit** — preserved aliases: `4.5`
  - **Acceptance:** verify shared connector exists because three concrete consumers require it, with only webhook wiring in this slice.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 5: Request-scoped Keycloak administration — IN PROGRESS
**Depends on:** Phase 2, Phase 4. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Application/DTOs/Onboarding/KeycloakBootstrapRequestDto.cs`
  - `src/Explore.Application/DTOs/Onboarding/KeycloakRealmDoctorRequestDto.cs`
  - `src/Explore.Application/DTOs/Onboarding/KeycloakRealmSyncPreviewRequestDto.cs`
  - `src/Explore.Application/DTOs/Onboarding/KeycloakRealmSyncApplyRequestDto.cs`
  - `src/Explore.Application/DTOs/Onboarding/KeycloakClientSecretRotationRequestDto.cs`
  - `src/Explore.Infrastructure/Services/Keycloak/KeycloakAdminClient.cs`
  - `src/Explore.API/Controllers/InstanceAuthenticationSettingsController.cs`
  - `src/Explore.Blazor.Client/Pages/Onboarding/AuthProviderConfiguration.razor`
  - `src/Explore.Blazor.Client/Pages/Admin/Instance/Components/InstanceAuthProviderSection.razor`
  - `src/Explore.Blazor.Client/Services/InstanceOnboardingService.cs`
- [ ] **5.1 Red fresh-pair and partial-completion authority** — preserved aliases: `KC-201`
  - **Acceptance:** Execute failing current-route/native authority, absent/partial pair, all UI terminal outcomes, no credential-forwarding redirect and external-success/local-failure tests before Green; no canary reaches final output.
  - **Dependencies:** Phase 2, Phase 4; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **5.2 Green request-scoped provider operations** — preserved aliases: `KC-202`
  - **Acceptance:** Verify bootstrap/doctor/preview/apply/rotation use fresh complete synchronous pairs under server-derived authority and safe connection profiles, never fallback or a background retry/outbox; external partial completion has a bounded reconciliation action, not rollback.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **5.3 Wire forms and source-owned contracts** — preserved aliases: `KC-203`
  - **Acceptance:** Verify both username/password clear on success/rejection/timeout/cancellation/navigation/disposal and fresh re-entry is required; generated API/client and accessible HAL-only forms travel with their source and dual docs.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **5.4 Phase verification**
  - **Acceptance:** Verify the selected owning lane and one Release build are green after all phase tasks; phase-attributable failure blocks closure.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **5.5 Phase atomic commits**
  - **Acceptance:** Verify exact literal path packets and allocated release metadata are truthful, hook validation passes and only owned outcomes are committed; no unrelated dirty state is staged.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 6: Dedicated Keycloak lifecycle service identity — IN PROGRESS
**Depends on:** Phase 2, Phase 4. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Infrastructure/Services/Keycloak/KeycloakAccountAuthorityLifecycleEmailService.cs`
  - `src/Explore.Infrastructure/Services/Keycloak/KeycloakLifecycleEmailOptions.cs`
  - `src/Explore.Infrastructure/InfrastructureServicesRegistration.cs`
  - `tests/Explore.Infrastructure.Tests/Infrastructure/KeycloakAccountAuthorityLifecycleEmailServiceTests.cs`
- [ ] **6.1 Red dedicated lifecycle identity** — preserved aliases: `KC-204`
  - **Acceptance:** Execute failing client-credentials-only, wrong realm/account/tenant, selected-authority failure and no human/BFF fallback contracts with generated canaries; preserve actual lifecycle business authority.
  - **Dependencies:** Phase 2, Phase 4; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **6.2 Green minimum-role service delegation** — preserved aliases: `KC-205`
  - **Acceptance:** Verify the dedicated API identity performs each exact permitted provider-owned required action, removing a required privilege fails, and a version-bound deployment validator rejects excess roles; no broad introspection credential is introduced to claim least privilege.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **6.3 Wire rotation and operator recovery** — preserved aliases: `KC-206`
  - **Acceptance:** Verify exact API runtime root, no retained secret/token/job/event output, selected-authority failure codes, replica convergence/rotation and disabled-feature recovery; fresh prior validated service authority may restore operation but human password grant never returns.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **6.4 Phase verification**
  - **Acceptance:** Verify the selected owning lane and one Release build are green after all phase tasks; phase-attributable failure blocks closure.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **6.5 Phase atomic commits**
  - **Acceptance:** Verify exact literal path packets and allocated release metadata are truthful, hook validation passes and only owned outcomes are committed; no unrelated dirty state is staged.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 7: Scoped AI transport authority — IN PROGRESS
**Depends on:** Phase 4. **Scenario ownership:** S10.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.API/Controllers/AiAssistantController.cs`
  - `src/Explore.Infrastructure/Ai/AiProviderSettingsValidator.cs`
  - `src/Explore.Infrastructure/Ai/RuntimeAiChatProvider.cs`
  - `src/Explore.Infrastructure/Ai/OpenAiCompatibleChatProvider.cs`
  - `src/Explore.Infrastructure/InfrastructureServicesRegistration.cs`
  - `tests/Explore.Infrastructure.Tests/Infrastructure/AiEndpointAuthorityTests.cs`
  - `tests/Event.API.IntegrationTests/Features/AiAssistantModelDiscoverySecurityTests.cs`
- [ ] **7.1 Red privileged endpoint scope** — preserved aliases: `5.1`
  - **Acceptance:** test S10 and generated credential canaries through discovery/runtime/saved overrides; verify unauthorized actors cause no outbound operation and tenant endpoints cannot reuse instance-local allowance.
  - **Dependencies:** Phase 4; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **7.2 Green every actual transport** — preserved aliases: `5.2`
  - **Acceptance:** apply public-only tenant profile and exact operator-origin profile at connection time, disable redirects and custom-key leakage, integrate actual SDK transport seams; verify literal/DNS/mixed/redirect checks cannot be bypassed by alternate provider construction.
  - **Dependencies:** Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **7.3 Wire bounded public failures** — preserved aliases: `5.3`
  - **Acceptance:** replace raw HttpRequestException message reflection and unsafe discovery fallback with fixed outcomes; regenerate changed metadata and document explicit origin ownership; verify valid configured local self-hosted AI remains possible.
  - **Dependencies:** Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **7.4 Phase gate** — preserved aliases: `5.4`
  - **Acceptance:** common build plus Infrastructure project, NEW `AiEndpointAuthorityTests`, in-memory actual adapter/connector seams.
  - **Dependencies:** Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **7.5 Phase commit** — preserved aliases: `5.5`
  - **Acceptance:** verify all enabled AI transport owners are covered or the candidate register remains blocked, never marked protected by named-client-only evidence.
  - **Dependencies:** Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 8: Operator-owned Cerbos publication and one-time administration — IN PROGRESS
**Depends on:** Phase 2, Phase 4. **Scenario ownership:** S11.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Infrastructure/Services/CerbosAdminEndpointValidator.cs`
  - `src/Explore.Infrastructure/Services/CerbosPolicyPackageService.cs`
  - `src/Explore.Infrastructure/Services/CerbosPolicyPackageOptions.cs`
  - `src/Explore.Infrastructure/InfrastructureServicesRegistration.cs`
  - `tests/Explore.Infrastructure.Tests/Infrastructure/CerbosPolicyPackageServiceTests.cs`
  - `tests/Explore.Infrastructure.Tests/Infrastructure/CerbosPublicationTransportTests.cs`
  - `src/Explore.Infrastructure/Services/CerbosConfigResolver.cs`
  - `src/Explore.Infrastructure/Services/PolicySyncService.cs`
  - `src/Explore.Application/Contracts/Infrastructure/IPolicySyncService.cs`
  - `src/Explore.API/BackgroundServices/CerbosPolicyBootSyncRunner.cs`
  - `.github/workflows/cerbos-policy-check.yml`
  - `.github/workflows/deploy-coolify.yml`
  - `src/Explore.Application/DTOs/Onboarding/AuthorizationPolicyPackageSyncRequestDto.cs`
- [ ] **8.1 Red BYO/instance separation** — preserved aliases: `6.1`
  - **Acceptance:** test S11 with mixed DNS, redirected credential-bearing requests and approved instance-local publishing; verify denied peers receive no schema/policy/Basic credentials.
  - **Dependencies:** Phase 2, Phase 4; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **8.2 Green policy-bearing client construction** — preserved aliases: `6.2`
  - **Acceptance:** preserve isByo trust classification through actual Refit HTTP handler selection, vetted connection and redirects off; verify provider failures never authorize by fallback.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **8.3 Wire safe diagnosis and deployment contract** — preserved aliases: `6.3`
  - **Acceptance:** classify invalid origin/network outcomes without provider content, retain publication authority checks and document narrow exceptions; verify no production broad private override. Wire the custody contracts specified in 8.1 and prepare the one-time UI/manual recovery contract. Do not remove reusable fallback/runtime publishers here: retirement belongs to 8.5 after verified 8.4 publication readiness.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **8.4 Publication prerequisite and deploy receipt** — preserved aliases: `CB-304`, `CB-305`, `CB-306`
  - **Acceptance:** Verify failed, absent, stale or wrong-revision/content-hash/environment publication receipts prevent deployment, while reusable CI publication/manual ZIP or cerbosctl recovery is operator-owned. Establish this before runtime publisher removal; preserve policy/package validation and required workflow identities.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **8.5 One-time administration and runtime removal** — preserved aliases: `CB-301`, `CB-302`, `CB-303`
  - **Acceptance:** after 8.4 readiness is proven, require fresh complete synchronous pairs, clear both fields at every terminal outcome and remove reusable cache/config/binding/admin fallback together with startup/role-change publishers in the same safe D4/D5 boundary. Wrong-tenant targets and redirects receive no credentials. Use explicitly scoped non-admin machine authority only where supported; otherwise fresh operator input is required and unattended admin publication stays unavailable. Preserve principal/cache updates and tenant-qualified evaluation; historical receipt is not live-store equality.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **8.6 Phase gate** — preserved aliases: `6.4`
  - **Acceptance:** common build plus Infrastructure project, NEW `CerbosPublicationTransportTests`.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **8.7 Phase commit** — preserved aliases: `6.5`
  - **Acceptance:** verify exact assigned publication/transport slice and emitted fragment.
  - **Dependencies:** Phase 2, Phase 4; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 9: External Data Protection rings — IN PROGRESS
**Depends on:** Phase 2. **Scenario ownership:** S23, S34.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs`
  - `src/Event.Standalone/Hosting/CombinedApiDataProtectionExtensions.cs`
  - `src/Explore.Blazor/Extensions/BffDataProtectionExtensions.cs`
  - `src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs`
  - `tests/Explore.Secrets.UnitTests/Services/ExternalDataProtectionRingTests.cs`
- [ ] **9.1 Red independent custody/recovery** — preserved aliases: `12.1`
  - **Acceptance:** test S23/S34 missing/wrong/tampered/retired ring, intended shared-host versus forbidden purpose and replica/cutover/restore; verify a DB/Redis-only copy cannot supply key authority.
  - **Dependencies:** Phase 2; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **9.2 Green selected-authority ring** — preserved aliases: `12.2`
  - **Acceptance:** load bounded versioned ring through exclusive approved authority, disable automatic app writes/key generation and remove database/Redis/file fallback; validate active/read/retired windows before readiness; verify no recursive DP wrapping or ungoverned writable secret service.
  - **Dependencies:** Phase 2; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **9.3 Wire provisioning and overlap** — preserved aliases: `12.3`
  - **Acceptance:** implement narrow operator .NET tool and safe status, purpose partition/discriminator, coordinated restart/drain, expiry alerts and approved session invalidation/cutover; verify no secret value reaches source/runtime evidence, and history/storage removal does not delete another authority.
  - **Dependencies:** Phase 2; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **9.4 Phase gate** — preserved aliases: `12.4`
  - **Acceptance:** targeted Standalone build plus Secrets project, NEW `ExternalDataProtectionRingTests`; actual owning host registration/restart/restore Green is required before delivery. Final admission additionally consumes actual deployment replica receipts; unit ring parsing cannot certify topology or purpose isolation.
  - **Dependencies:** Phase 2; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **9.5 Phase commit** — preserved aliases: `12.5`
  - **Acceptance:** split operator tool from ring runtime only if each is buildable/truthful independently; verify no prohibited generated key DB exception is smuggled into docs.
  - **Dependencies:** Phase 2; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 10: Consumer-isolated credential deployment — SOURCE AUTHORSHIP COMPLETE; EXECUTION DEFERRED
**Depends on:** Phase 2, Phase 5, Phase 6, Phase 8. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Secrets/Configuration/InfisicalConfigurationProvider.cs`
  - `src/Explore.Blazor/Configuration/InfisicalConfigurationProvider.cs`
  - `src/Explore.AppHost/AppHost.cs`
  - `src/Event.Setup.Core/Environment/PlatformEnvironmentCatalogue.cs`
  - `src/Event.Setup.Core/Environment/PlatformEnvironmentMetadata.cs`
  - `.env.example`
  - `docker-compose.yml`
- [ ] **10.1 Red actual pre-acquisition consumer separation** — preserved aliases: `DP-401`
  - **Acceptance:** Execute failing API/BFF/Combined acquisition contracts against runtime-generated canaries; provisioning/infrastructure/wrong-consumer/missing-provenance/reference-expanded values are never requested or materialized, including logical keys bound to stale forbidden coordinates. Auth-only work already owned by Phase 2 is reused with explicit applicability, not reimplemented.
  - **Dependencies:** Phase 2, Phase 5, Phase 6, Phase 8; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **10.2 Green exact roots and machine identities** — preserved aliases: `DP-402`
  - **Acceptance:** Verify runtime identities cannot acquire provisioning roots; API, BFF, Combined exact union, one-shot publisher/provisioner and provider infrastructure receive only their declared roots before fetching, with no recursion/reference expansion/fallback and no removal of unrelated service roots.
  - **Dependencies:** Phase 2, Phase 5, Phase 6, Phase 8; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **10.3 Wire catalogue, Compose and Aspire projections** — preserved aliases: `DP-403`
  - **Acceptance:** Verify environment metadata, emitted topology, readiness/publication ordering and one-shot credential projection agree; old replicas drain and selected authority stays fail-closed. Remove only proven obsolete mappings/admin inputs after Phases 2/5/6/8 converge, with public/internal docs and governed deployment impact.
  - **Dependencies:** Phase 2, Phase 5, Phase 6, Phase 8; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **10.4 Phase verification**
  - **Acceptance:** Verify the selected owning lane and one Release build are green after all phase tasks; phase-attributable failure blocks closure.
  - **Dependencies:** Phase 2, Phase 5, Phase 6, Phase 8; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **10.5 Phase atomic commits**
  - **Acceptance:** Verify exact literal path packets and allocated release metadata are truthful, hook validation passes and only owned outcomes are committed; no unrelated dirty state is staged.
  - **Dependencies:** Phase 2, Phase 5, Phase 6, Phase 8; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 11: Authentic token-context evidence — IN PROGRESS
**Depends on:** Phase 1. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.API/Extensions/AuthenticationExtensions.cs`
  - `src/Explore.API/Authentication/DynamicJwtBearerPostConfigureOptions.cs`
  - `tests/Event.API.IntegrationTests/Features/SecurityIntegrationTests.cs`
  - `tests/Event.API.IntegrationTests/Fixtures/KeycloakTokenClient.cs`
  - `docker/keycloak/ISLAMU-realm.test.json`
  - `tests/Event.API.IntegrationTests/Authentication/KeycloakTokenContextHttpTests.cs`
  - `tests/Event.API.IntegrationTests/Fixtures/KeycloakTokenContextFixture.cs`
- [ ] **11.1 Red genuinely signed matrix** — preserved aliases: `11.1`
  - **Acceptance:** generate issuer/signing metadata at runtime and independently sign valid/expired/wrong-key/issuer/audience/azp/type/not-before cases; verify production JwtBearer rejects each isolated condition with unchanged protected state and unexpected fixture shape fails.
  - **Dependencies:** Phase 1; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **11.2 Green independent context policy** — preserved aliases: `11.2`
  - **Acceptance:** require intended API audience and separately documented client restriction, define missing/duplicate/type behavior, reject ID-token substitution; verify configured service clients work without broadening unrelated schemes.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **11.3 Repair advertised assertions and docs** — preserved aliases: `11.3`
  - **Acceptance:** replace unresigned expiry, missing-audience mislabeled success and conditional signature assertion; verify decoded signature-byte mutation, retained Local isolation scope, explicit five-minute normal skew versus purpose-specific zero skew and actual realm mapper contract.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **11.4 Phase gate** — preserved aliases: `11.4`
  - **Acceptance:** common build plus API integration project, NEW `KeycloakTokenContextHttpTests`, generated-authority/TestServer fixture only; no container class dependency.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **11.5 Phase commit** — preserved aliases: `11.5`
  - **Acceptance:** verify source policy and evidence tests travel together; do not claim actual realm/MFA proof until Ring 3/release input.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 12: Uniform Local login and recovery — IN PROGRESS
**Depends on:** Phase 1. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.API/ExceptionHandling/LocalAuthenticationResultMapper.cs`
  - `src/Explore.Persistence/Identity/LocalIdentityAuthService.cs`
  - `src/Explore.Application/Features/Authentication/Local/Handlers/Commands/RequestLocalPasswordRecoveryCommandHandler.cs`
  - `src/Explore.Application/Services/DefaultAccountAuthorityLifecycleEmailService.cs`
  - `src/Explore.Infrastructure/Authentication/LocalIdentityLifecycleEmailService.cs`
- [ ] **12.1 Red non-enumeration/failure matrix** — preserved aliases: `9.1`
  - **Acceptance:** interleave missing/wrong/locked attempts and inject classified target-only pre-commit failure; verify status/machine fields match, no token/challenge/session leaks, internal lockout remains effective, and cancellation/committed-delivery ownership are asserted separately.
  - **Dependencies:** Phase 1; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **12.2 Green public envelope/outcome translation** — preserved aliases: `9.2`
  - **Acceptance:** normalize public outcomes, preserve internal counters/dummy work, translate expected lifecycle failure to value-free internal category; verify uncommitted work is not falsely retried durably and genuine invariant/programming defects are not indiscriminately swallowed.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **12.3 Wire owner remedy and docs** — preserved aliases: `9.3`
  - **Acceptance:** keep correct locked-password denial and independently verified recovery channel, document privacy-safe guidance; verify no SMTP/provider change alters selected login authority.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **12.4 Phase gate** — preserved aliases: `9.4`
  - **Acceptance:** common build plus Application project, NEW `LocalPublicFailureContractTests`; HTTP/Identity evidence executes once at final gate.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **12.5 Phase commit** — preserved aliases: `9.5`
  - **Acceptance:** verify uniform-intake outcome and docs constitute one privacy behavior slice.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 13: Shared Local password and hash policy — SOURCE AUTHORSHIP COMPLETE; EXECUTION DEFERRED
**Depends on:** Phase 12. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Application/Configuration/LocalIdentityOptions.cs`
  - `src/Explore.Persistence/PersistenceServicesRegistration.cs`
  - `src/Explore.Application/Features/Authentication/Local/Validators/LocalAuthRequestDtoValidator.cs`
  - `src/Explore.Persistence/Identity/LocalIdentityLifecycleStore.cs`
  - `src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.cs`
  - `src/Explore.Domain/Authentication/LocalPasswordPolicy.cs`
  - `tests/Event.Domain.UnitTests/Authentication/LocalPasswordPolicyTests.cs`
- [ ] **13.1 Red shared password boundary** — preserved aliases: `10.1`
  - **Acceptance:** specify 14/15/128/129, full blocked long password, Unicode policy and passphrase cases, mutation rejection retaining original stamp/proof; verify login shape validation is separate from creation minimum.
  - **Dependencies:** Phase 12; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **13.2 Green one server policy and snapshot** — preserved aliases: `10.2`
  - **Acceptance:** enforce on bootstrap/create/change/recovery/replacement, load bounded versioned operator-owned blocklist atomically, reject unusable required Local snapshot; verify no third-party password-dependent network call or unlicensed copied dataset is introduced.
  - **Dependencies:** Phase 12; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **13.3 Wire salted adaptive hash evidence** — preserved aliases: `10.3`
  - **Acceptance:** expose safe effective algorithm/work-factor status, verify independent salt/rehash behavior through native persisted Identity, document offline calibration and compromise-driven replacement; verify no hash/password appears in diagnostics or tests' source.
  - **Dependencies:** Phase 12; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **13.4 Phase gate** — preserved aliases: `10.4`
  - **Acceptance:** targeted affected build plus Domain project, NEW `LocalPasswordPolicyTests`; actual Identity hash/mutation Green closes before the owning delivery claim. Final Ring 3 supplies the regression union, not the first credential-mutation proof.
  - **Dependencies:** Phase 12; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **13.5 Phase commit** — preserved aliases: `10.5`
  - **Acceptance:** verify password policy, all adapters and operator contract are complete without an automatic destructive credential reset.
  - **Dependencies:** Phase 12; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 14: Bounded custom-property patterns — SOURCE IMPLEMENTATION IN PROGRESS; EXECUTION DEFERRED
**Depends on:** Phase 1. **Scenario ownership:** S12, S35, S37.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Application/Features/CustomProperties/CustomPropertyRuntimeValueValidator.cs`
  - `src/Explore.Domain/CustomProperties/CustomPropertyPatternPolicy.cs`
  - `tests/Event.Domain.UnitTests/CustomProperties/CustomPropertyPatternPolicyTests.cs`
- [ ] **14.1 Red pattern and nullable candidate specification** — preserved aliases: `7.1`
  - **Acceptance:** test S12/S35/S37 malformed/unsupported patterns, exact bounds and length-first behavior plus omitted/null/empty/value Options/nested fields across general/event/session definitions; reproduce the Options null dereference, preserve legal Description clearing and compare equivalent create/merged-update candidates. Verify no elapsed-time assertion, prompt/source scraping or internal matcher mock is needed; assert invalid candidates leave tracked state unchanged.
  - **Dependencies:** Phase 1; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **14.2 Green one bounded candidate rule** — preserved aliases: `7.2`
  - **Acceptance:** implement 2,048-character pattern/non-backtracking/100 ms matching policy and stable null/required-collection failures; reconstruct update candidate before mutation and use the same create invariant validator in all three definition families and four value commands. Verify no 500, blanket null-forgiving/default coercion or partial mutation on rejected null/pattern/option/reference/bound candidates.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **14.3 Wire intentional syntax and wire contract** — preserved aliases: `7.3`
  - **Acceptance:** preserve option/reference validation, remove divergent match paths and document unsupported constructs/omitted-null semantics/fixed codes. Add actual JSON handler and fresh relational reload proof for successful null clears and invalid collection rejection, generated client PATCH/If-Match semantics, and exact update-handler ownership before edits; verify pattern/value content never enters failures/logs.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **14.4 Phase gate** — preserved aliases: `7.4`
  - **Acceptance:** common build plus Domain project, NEW `CustomPropertyPatternPolicyTests`.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **14.5 Phase commit** — preserved aliases: `7.5`
  - **Acceptance:** verify all four command/definition consumers and the breaking fragment are owned together.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 15: Bounded AI provider consumption — AI ADAPTER SOURCE IMPLEMENTED; EXECUTION DEFERRED
**Depends on:** Phase 7. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Infrastructure/Http/BoundedResponseReader.cs`
  - `tests/Explore.Infrastructure.Tests/Infrastructure/Http/BoundedResponseReaderTests.cs`
  - `src/Explore.Infrastructure/Ai/OpenAiCompatibleChatProvider.cs`
  - `src/Explore.Infrastructure/Ai/OpenAiResponsesChatProvider.cs`
  - `src/Explore.Infrastructure/Ai/AnthropicCompatibleChatProvider.cs`
  - `src/Explore.Infrastructure/Ai/MicrosoftExtensionsAiChatProvider.cs`
  - `src/Explore.Infrastructure/Ai/AnthropicChatProvider.cs`
  - `src/Explore.Infrastructure/Ai/IAnthropicMessagesApi.cs` (remove the unused buffered Refit response contract)
  - `src/Explore.Infrastructure/Ai/AiModelDiscoveryGateway.cs`
  - `src/Explore.Infrastructure/Ai/AiTransportAuthority.cs`
  - `src/Explore.Infrastructure/Ai/AiResponseLimits.cs` (new nonsecret options)
  - `src/Explore.Infrastructure/Ai/AiResponseReader.cs` (new allocation-free schema scan before DTO/SDK parsing)
  - `src/Explore.Infrastructure/Ai/AiResponseBudgetHandler.cs` (new SDK transport boundary)
  - `src/Explore.Infrastructure/InfrastructureServicesRegistration.cs` (AI options, named clients and SDK factory only; Coop and credential blocks remain separately owned)
  - `tests/Explore.Infrastructure.Tests/Infrastructure/AiBoundedConsumptionTests.cs` (new observable adapter/SDK regressions)
  - `tests/Explore.Infrastructure.Tests/Infrastructure/OpenAiCompatibleChatProviderTests.cs` (replace the touched delay test with exact timer/cancellation signals)
  - `tests/Explore.Infrastructure.Tests/Infrastructure/MicrosoftExtensionsAiChatProviderTests.cs` (align SDK test profile admission)
  - `docs/internal/AI_PROVIDER_CONSUMPTION.md` (new technical source anchor)
  - `docs/public/documentation/readme/configuration-and-operations/ai-provider-response-limits.md` (new operator guide)
- [ ] **15.1 Red allocation boundary** — preserved aliases: `13.1`
  - **Acceptance:** construct known/unknown/dishonest/chunked/expanded/deep/huge-unknown-field responses; verify exact limit succeeds, oversize stops at limit plus one, rejected known length reads no body, caller cancellation/disposal are distinct.
  - **Dependencies:** Phase 7; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **15.2 Green actual bounded reads/parsing** — preserved aliases: `13.2`
  - **Acceptance:** implement decoded-byte/depth/item/scalar limits from D11 before string/SDK materialization; verify all enabled transports, custom headers and provider catalog/completion paths share the enforced budget, not just direct OpenAI readers.
  - **Dependencies:** Phase 7; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **15.3 Wire fixed safe failure outcomes** — preserved aliases: `13.3`
  - **Acceptance:** preserve normal provider retry/timeout semantics, make security rejection non-reflecting and not falsely successful fallback; replace existing 1,100 ms deadline test delays with exact cancellation/read signals when touching those tests; verify no timing-luck assertion remains in changed tests.
  - **Dependencies:** Phase 7; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **15.4 Phase gate** — preserved aliases: `13.4`
  - **Acceptance:** common build plus Infrastructure project, NEW `BoundedResponseReaderTests`.
  - **Dependencies:** Phase 7; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **15.5 Phase commit** — preserved aliases: `13.5`
  - **Acceptance:** verify reader plus AI adapter integration is one complete budget outcome and SDK gaps remain admission-blocked until proven.
  - **Dependencies:** Phase 7; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 16: Bounded Coop consumption — SOURCE IMPLEMENTATION IN PROGRESS; EXECUTION DEFERRED
**Depends on:** Phase 15. **Scenario ownership:** S21.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Infrastructure/Services/Moderation/Coop/CoopReviewQueueProvider.cs`
  - `src/Explore.Infrastructure/Services/Moderation/Coop/RuntimeModerationProviderResolver.cs`
  - `tests/Explore.Infrastructure.Tests/Infrastructure/Moderation/CoopReviewQueueProviderTests.cs`
- [ ] **16.1 Red moderation budgets** — preserved aliases: `14.1`
  - **Acceptance:** add S21 known/unknown/compressed/deep/huge-field cases and exact empty-success compatibility with the intentional provider contract; verify expected categories and no body reflection.
  - **Dependencies:** Phase 15; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **16.2 Green bounded Coop parse** — preserved aliases: `14.2`
  - **Acceptance:** apply shared bounded reader and D11 limits alongside duration, normalize fixed non-transient budget failures; verify existing retryable 503/network behavior and cancellation remain distinct.
  - **Dependencies:** Phase 15; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **16.3 Wire provider operator explanation** — preserved aliases: `14.3`
  - **Acceptance:** document ceilings and safe diagnosis, retain tenant provider selection and case ownership; verify output identity is not reconstructed from untrusted arbitrary fields.
  - **Dependencies:** Phase 15; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **16.4 Phase gate** — preserved aliases: `14.4`
  - **Acceptance:** common build plus Infrastructure project, existing `CoopReviewQueueProviderTests`.
  - **Dependencies:** Phase 15; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **16.5 Phase commit** — preserved aliases: `14.5`
  - **Acceptance:** verify this outcome contains no unrelated moderation-policy redesign.
  - **Dependencies:** Phase 15; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 17: Bounded ETag/idempotency capture — SOURCE COMPLETE, VERIFICATION DEFERRED
**Depends on:** delivered API-key baseline, Phase 9. **Scenario ownership:** S17.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.API/Middleware/ETagMiddleware.cs`
  - `src/Explore.API/Middleware/IdempotencyMiddleware.cs`
  - `tests/Event.API.IntegrationTests/Features/Middleware/ResponseCaptureBoundaryTests.cs`
  - `src/Explore.API/Middleware/ETagMiddleware.cs`
  - `src/Explore.API/Middleware/IdempotencyMiddleware.cs`
  - `src/Explore.Domain/IdempotencyRecord.cs`
  - `src/Explore.Application/Contracts/Persistence/IIdempotencyRepository.cs` — native rollback-release and payload-expiry contract documentation
  - `src/Explore.Persistence/Repositories/IdempotencyRepository.cs`
  - `tests/Event.Persistence.IntegrationTests/Repositories/IdempotencyRepositorySqliteTests.cs`
  - `tests/Event.Persistence.IntegrationTests/Repositories/IdempotencyRepositoryTests.cs`
  - `tests/Event.API.IntegrationTests/Features/IdempotencyMiddlewareTests.cs` — stale HTTP-validation release expectations only
  - `tests/Event.API.IntegrationTests/Features/AnonymousRegistrationRecoveryDeadlineHttpTests.cs` — expiry must recover without reacquiring/executing a historical identity
  - `docs/internal/API.md`
  - `docs/internal/API_COOKBOOK.md`
  - `docs/public/api-reference/readme/hal-rest.md`
- [ ] **17.1 Red threshold-crossing semantics** — preserved aliases: `15.1`
  - **Acceptance:** test S17 streaming/non-JSON/known/unknown length, compression, HEAD/304, partial response, cancellation and many controlled concurrent bodies; verify exact output bytes plus explicit retained-byte ceiling and committed-oversize retry behavior.
  - **Dependencies:** delivered API-key baseline, Phase 9; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **17.4 Phase gate** — preserved aliases: `15.4`
  - **Acceptance:** common build plus API integration project, NEW `ResponseCaptureBoundaryTests`, self-contained TestServer only.
  - **Dependencies:** delivered API-key baseline, Phase 9; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **17.5 Phase commit** — preserved aliases: `15.5`
  - **Acceptance:** verify capture implementation and receipt contract are inseparable here; split unrelated ETag optimization out rather than absorb it.
  - **Dependencies:** delivered API-key baseline, Phase 9; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 18: File lifecycle and final delivery policy — SOURCE AUTHORSHIP COMPLETE; EXECUTION DEFERRED
**Depends on:** Phase 1, Phase 9. **Scenario ownership:** S13, S29.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.API/Middleware/SecurityHeadersMiddleware.cs`
  - `src/Explore.API/Controllers/StorageObjectController.cs`
  - `src/Explore.API/Models/EventResourceFileResult.cs`
  - `src/Explore.Domain/Services/EventResources/EventResourceDocumentPolicy.cs`
  - `src/Explore.Application/Services/StorageContentSignaturePolicy.cs`
  - `src/Explore.Application/Services/StorageObjectContentReader.cs`
  - `src/Explore.Application/Services/EventResourceDocumentInspection.cs`
  - `tests/Event.Standalone.IntegrationTests/CombinedFileSecurityHeadersTests.cs`
  - `src/Explore.Persistence/Configurations/Entities/EventResourceConfiguration.cs`
- [ ] **18.1 Red every intake/delivery class** — preserved aliases: `16.1`
  - **Acceptance:** cover S13/S29 quarantine, exact MIME/signature/extension, disguised active content, OOXML expansion/relations/DTD, quota races, retention/hidden parent and final HTTP CSP; verify specialized event document controls are not falsely applied to generic prefix-only paths.
  - **Dependencies:** Phase 1, Phase 9; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **18.2 Green final trusted policy** — preserved aliases: `16.2`
  - **Acceptance:** compose endpoint sandbox/default-deny with global security headers, preserve attachment/safe name/nosniff/ranges and immediate authority check; verify no bytes on stale denial and exact intended bytes on authorized delivery in both hosts.
  - **Dependencies:** Phase 1, Phase 9; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **18.3 Close bounded storage gaps** — preserved aliases: `16.3`
  - **Acceptance:** enforce actual discovered generic/registration limits and service-owned root/ancestor custody; disallow hostile filesystem mutation as a supported local-storage profile rather than pretending lexical paths defeat symlink races; verify local roots aren't public mounts, object-store endpoints/policies are classified and cleanup cannot erase retained/shared uses.
  - **Dependencies:** Phase 1, Phase 9; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **18.4 Persisted relative availability constraint** — preserved aliases: `SEC:RES-AVAIL-001`
  - **Acceptance:** Verify same-anchor invalid direct insert/update is rejected, distinct-anchor and nullable valid windows round-trip, reconstruction/access stay bounded, and generated provider artifacts pass the final five-engine/pending-model gate without weakening existing domain semantics.
  - **Dependencies:** Phase 1, Phase 9; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **18.5 Protected destination hop and restore contract** — preserved aliases: `SEC:RES-DEST-001`, `SEC:RES-DEST-002`
  - **Acceptance:** Verify 301/302/303/307/308 supported-hop controlled origins receive no privileged header, wrong tenant/resource/purpose/tampering/key/app-identity yields no Location or reflected canary, permitted Location is no-store/no-referrer, and production keyring restore/rotation/retirement preserves encrypted destinations. Resource-owned real browser portions remain explicit final-admission evidence, not a fabricated intermediate pass.
  - **Dependencies:** Phase 1, Phase 9; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **18.6 Phase gate** — preserved aliases: `16.4`
  - **Acceptance:** targeted affected build plus Domain project, existing `EventResourceDocumentPolicyTests`; actual HTTP/storage/selected-provider Green closes each delivery outcome before its claim. Final exit aggregates providers and consumes separately owned browser/deployment receipts, not the first proof of final headers or persisted constraints.
  - **Dependencies:** Phase 1, Phase 9; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **18.7 Phase commits** — preserved aliases: `16.5`
  - **Acceptance:** separate final HTTP-header fix from independently changed upload/retention workflows; each discovered workflow must have a fully named reviewed vertical contract before staging.
  - **Dependencies:** Phase 1, Phase 9; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 19: Effective host, forwarding and operational access — SOURCE AUTHORED; ACCEPTANCE DEFERRED
**Depends on:** Phase 1. **Scenario ownership:** S18, S22.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.API/Hosting/ApiHostApplicationExtensions.cs`
  - `src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs`
  - `src/Event.Standalone/Hosting/StandaloneHostApplicationExtensions.cs`
  - `src/Explore.API/Middleware/ApiTenantResolutionMiddleware.cs`
  - `src/Explore.API/Controllers/EventTicketingController.cs`
  - `src/Explore.ServiceDefaults/Extensions.cs`
  - `tests/Event.Standalone.IntegrationTests/EffectiveHostSecurityTests.cs`
  - `tests/Event.Architecture.Tests/ReservedTenantSlugRouteCatalogTests.cs`
- [ ] **19.1 Red production-like authority** — preserved aliases: `17.1`
  - **Acceptance:** test S18/S22 trusted HTTPS-over-HTTP, untrusted proto/host/IP, malformed forwarding, valid/unknown tenant domains, canonical onboarding URLs and operational route access; verify final headers using actual shared pipeline with production-equivalent settings.
  - **Dependencies:** Phase 1; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **19.2 Green forwarding/domain/scrape boundary** — preserved aliases: `17.2`
  - **Acceptance:** apply trusted forwarding once before HSTS/redirect/admission, require approved effective hosts and canonical sensitive links, protect metrics/readiness without changing minimal safe liveness; verify direct listener/unauthorized scraper cannot expose tenant diagnostic data.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **19.3 Wire deployment inputs, activation and credential readiness** — preserved aliases: `17.3`
  - **Acceptance:** preserve legitimate custom-domain/subdomain routing, bounded trust lists and dedicated/Combined parity; document private backend reachability and scraper authority; verify no wildcard host config substitutes for persisted domain authorization. Reproduce actual Compose/catalogue runtime binding of omitted versus empty/default DB passwords, remove `docker-compose.yml:90-94` fallback passwords and require approved injected credentials through PlatformEnvironmentCatalogue/Dotenv readiness; NEW `DeploymentCredentialReadinessTests` covers default/missing/nondefault/role separation without logging values. Require actual enabled worker/middleware activation evidence, not a Testing exclusion or source-comment scan.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **19.4 Compiled reserved-root guardrail** — preserved aliases: `SEC:ROUTE-001`
  - **Acceptance:** Verify shipped compiled client routes, endpoint metadata and static-asset manifest roots are reserved case-insensitively; unknown parameter/catch-all ownership fails closed, missing segments produce deterministic machine-field diagnostics and legitimate reservations are not pruned. Do not scrape Razor/source/CSS or generate production constants from tests.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **19.5 Phase gate** — preserved aliases: `17.4`
  - **Acceptance:** common build plus Standalone integration project, NEW `EffectiveHostSecurityTests`.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **19.6 Phase commit** — preserved aliases: `17.5`
  - **Acceptance:** split protected operational access from transport ordering if separately reviewable; exact contracts below, no umbrella ingress redesign.
  - **Dependencies:** Phase 1; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 20: Early admission and fleet budget — SOURCE AUTHORSHIP COMPLETE; EXECUTION DEFERRED
**Depends on:** Phase 19. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.API/Extensions/RateLimitingExtensions.cs`
  - `src/Explore.API/Hosting/ApiHostApplicationExtensions.cs`
  - `tests/Explore.Blazor.IntegrationTests/EarlyAdmissionBoundaryTests.cs`
- [ ] **20.1 Red expensive-work prevention** — preserved aliases: `18.1`
  - **Acceptance:** saturate admitted requests with exact start/release signals, send invalid credentials/forwarding/loopback traffic and verify rejected requests never reach actual expensive identity/tenant/provider seams while valid shared-network users remain possible.
  - **Dependencies:** Phase 19; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **20.2 Green finite pre-auth and peer authority** — preserved aliases: `18.2`
  - **Acceptance:** add 32 in-flight/no-queue default before expensive work, finite peer/network buckets and retained post-auth policies; remove address-based unlimited loopback and use server-created bounded internal transport identity; verify Testing opt-out cannot accidentally run the security assertion with NoLimiter.
  - **Dependencies:** Phase 19; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **20.3 Wire aggregate ingress contract** — preserved aliases: `18.3`
  - **Acceptance:** require documented fleet budget across replicas, private backend confinement and bounded deployment validation; verify two-process test evidence and ingress enforcement inputs are required before fleet sign-off, without adding a pretend Redis distributed limiter.
  - **Dependencies:** Phase 19; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **20.4 Phase gate** — preserved aliases: `18.4`
  - **Acceptance:** common build plus BFF integration project, NEW `EarlyAdmissionBoundaryTests`.
  - **Dependencies:** Phase 19; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **20.5 Phase commit** — preserved aliases: `18.5`
  - **Acceptance:** verify early/public/internal policy paths and operator budget semantics are complete; no unrelated rate-policy tuning.
  - **Dependencies:** Phase 19; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 21: Caller-owned bypass and resource authority — SOURCE IN PROGRESS
**Depends on:** Phase 3. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Persistence/QueryFilters/QueryFilterExtensions.cs`
  - `src/Explore.Persistence/Repositories/GenericRepository.cs`
  - `src/Explore.Application/Authorization/RequestAuthorization.cs`
- [ ] **21.1 Red complete caller and transaction inventory** — preserved aliases: `19.1`
  - **Acceptance:** enumerate tenant-only/all-filter/context-wide bypass, FindAsync/tracked CRUD, raw provider SQL primitives and API/MCP/jobs/bootstrap/tool entry paths; record exact owning identifiers/predicates/status/privilege/RLS/transaction/cancellation dependencies. Verify each reachable bypass has same-tenant/cross-tenant/revoked-current-authority tests, including tracked foreign entity; each raw query tests malicious values as parameters and rejects caller-selected SQL identifiers/order fragments.
  - **Dependencies:** Phase 3; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** XL.
- [ ] **21.2 Green exact persisted authority** — preserved aliases: `19.2`
  - **Acceptance:** repair concrete missing predicates or stale authority at owning handler/repository boundary, retain soft-delete when not required, and narrow global maintenance; verify no local posted role/publisher/tenant fact authorizes mutation and no blanket bypass allowlist hides gaps.
  - **Dependencies:** Phase 3; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **21.3 Wire object/property/function parity** — preserved aliases: `19.3`
  - **Acceptance:** cover resource ownership, field updates, support scopes, deleted/hidden parents and direct HTTP/MCP access independently of HAL; verify legitimate exact-tenant global reads remain functional and selected Cerbos outage denies.
  - **Dependencies:** Phase 3; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **21.4 Phase gate** — preserved aliases: `19.4`
  - **Acceptance:** targeted affected build plus Application project, NEW `PersistedResourceAuthorityTests`; actual tracked-context/provider/HTTP Green closes before the repaired family's delivery claim. Final exit is the regression union, not permission to ship untested resource authority.
  - **Dependencies:** Phase 3; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **21.5 Phase commits** — preserved aliases: `19.5`
  - **Acceptance:** each independently repaired resource family closes under its own capability contract; verify exact files/tests/fragments and no repository-wide behavior sweep.
  - **Dependencies:** Phase 3; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
## Phase 22: Minimal public identity projections — NOT STARTED
**Depends on:** Phase 21. **Scenario ownership:** S26.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Application/DTOs/Event/EventDto.cs`
  - `src/Explore.Application/Mappings/EventMapper.Projections.cs`
  - `tests/Event.Application.UnitTests/Privacy/PublicIdentityDisclosureTests.cs`
- [ ] **22.1 Red field/disclosure contract** — preserved aliases: `20.1`
  - **Acceptance:** enumerate public publisher, contributor, actor, internal user/credential and participation/membership fields per detail/list/actor/export/count variant; verify S26 forbidden fields/existence are asserted for anonymous callers and hidden/deleted parents/cache/304 changes.
  - **Dependencies:** Phase 21; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **22.2 Green minimal public shape** — preserved aliases: `20.2`
  - **Acceptance:** remove internal submitter/user and private membership links from anonymous projections while retaining intentional public publisher/federation identity; require independently authorized private detail shape; verify revocation/visibility is checked at disclosure, including cached payloads.
  - **Dependencies:** Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **22.3 Wire generator and every changed consumer** — preserved aliases: `20.3`
  - **Acceptance:** regenerate contracts and migrate real API/BFF/client/job/export consumers, preserve public identity rendering and authorized private workflows; test changed nullable/removed/renamed/enum/HAL fields through generated serialization into the actual endpoint, actual deserialization and affected consumer behavior. Verify no stale old-shape reader, second email-correlation engine or incompatible publication-discovery work is absorbed.
  - **Dependencies:** Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **22.4 Phase gate** — preserved aliases: `20.4`
  - **Acceptance:** common build plus Application project, NEW `PublicIdentityDisclosureTests`; real anonymous HTTP/count/cache/race proof at final exit.
  - **Dependencies:** Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **22.5 Phase commit** — preserved aliases: `20.5`
  - **Acceptance:** verify approved disclosure defaults and generator outputs/docs are complete; apply user's pending answer if it arrives before approval.
  - **Dependencies:** Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 23: Race-safe tenant slug updates — NOT STARTED
**Depends on:** Phase 3, Phase 21. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Application/Features/Tenants/Handlers/Commands/CreateTenantCommandHandler.cs`
  - `src/Explore.Application/Features/Tenants/Handlers/Commands/UpdateTenantCommandHandler.cs`
  - `tests/Event.Application.UnitTests/Features/Tenants/TenantSlugAllocationTests.cs`
- [ ] **23.1 Red create/update and update/update contention** — preserved aliases: `SEC:SLUG-001`
  - **Acceptance:** Execute deterministic competing independent-context tests; conflicting normalized slug returns established bounded validation with unchanged tenants, at most one committed owner, no-op/self/case-only remains legal and reservation/format rejection precedes persistence.
  - **Dependencies:** Phase 3, Phase 21; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **23.2 Green shared allocation and post-commit cache** — preserved aliases: `SEC:SLUG-001`
  - **Acceptance:** Verify changed-slug updates use the same authoritative create allocation lock, owner query excludes self, the unique index remains the last race defense, cache refresh follows only successful commit and no authorization/alias/lifecycle semantics are changed.
  - **Dependencies:** Phase 3, Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **23.3 Phase verification**
  - **Acceptance:** Verify the selected owning lane and one Release build are green after all phase tasks; phase-attributable failure blocks closure.
  - **Dependencies:** Phase 3, Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **23.4 Phase atomic commits**
  - **Acceptance:** Verify exact literal path packets and allocated release metadata are truthful, hook validation passes and only owned outcomes are committed; no unrelated dirty state is staged.
  - **Dependencies:** Phase 3, Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 24: Durable business operation recovery — NOT STARTED
**Depends on:** delivered API-key baseline, Phase 17, Phase 21. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.API/Middleware/IdempotencyMiddleware.cs`
- [ ] **24.1 Red deterministic state/transaction boundary** — preserved aliases: `21.1`
  - **Acceptance:** classify every operation claiming logical idempotency, pre-subscribe to critical read/fence/write/commit/release signals and force overlap using independent contexts, not Task.WhenAll alone. Cover commit-before-response/retry/current revocation, caller cancellation/business-failure return after signaled writes, duplicate normalized rows, empty/missing parents, exact capacity/one-over, nullable false/zero, max aggregate and callback/refund/hold races; verify fresh persisted state/outbox/effects rather than inline UoW fakes.
  - **Dependencies:** delivered API-key baseline, Phase 17, Phase 21; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** XL.
- [ ] **24.2 Green caller-owned durable identity and outcome** — preserved aliases: `21.2`
  - **Acceptance:** reuse lineage/receipts/fences, add uniqueness/state/outbox in the owning transaction only where missing; classify every UoW success/business-failure/cancellation/exception/ambiguous-commit outcome and ensure no partial writes are mislabeled rollback. Await durable work, settle once, reauthorize recovery and explicitly represent unknown provider handoff; verify generic receipt release never enables repeated effects.
  - **Dependencies:** delivered API-key baseline, Phase 17, Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **24.3 Wire arithmetic, anomaly and recovery contracts** — preserved aliases: `21.3`
  - **Acceptance:** protect actual provider-side SUM range separately from checked requested quantities and monetary Int128; preserve idempotency/reconciliation/native guest recovery; add five-provider duplicate/empty/malformed/cross-scope and exact-boundary evidence with injected clocks. Generate changed schemas/contracts and document stable retry/revoke/manual-review categories; verify no arbitrary unique-row winner or raw SQL exception.
  - **Dependencies:** delivered API-key baseline, Phase 17, Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **24.4 No transparent credential mutation retry** — preserved aliases: `SEC:RETRY-001`
  - **Acceptance:** Verify credential-consuming transport never retries/hedges after ambiguous responses; owning operation identity survives, no consumed proof or secret is reused, and deliberate authorized recovery remains explicit. Preserve current cookie/tenant separation and native business/outbox authority.
  - **Dependencies:** delivered API-key baseline, Phase 17, Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **24.5 Phase gate** — preserved aliases: `21.4`
  - **Acceptance:** targeted affected build plus Domain project, NEW `DurableOperationRecoveryTests`; each changed family has real relational/commit/callback-race Red and delivery Green before its commit claim. Final Ring 3 aggregates the full provider union once; a pure state-machine test alone cannot establish transaction durability.
  - **Dependencies:** delivered API-key baseline, Phase 17, Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
- [ ] **24.6 Phase commits** — preserved aliases: `21.5`
  - **Acceptance:** close each affected business-operation family separately, with no umbrella registration/payment rewrite; verify its own event/effect/receipt state and fragment.
  - **Dependencies:** delivered API-key baseline, Phase 17, Phase 21; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** XL.
## Phase 25: Confidential logs, parsers and template output — SOURCE IN PROGRESS
**Depends on:** Phase 15, Phase 16, Phase 18. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Application/Telemetry/BusinessMetrics.cs`
  - `src/Explore.Infrastructure/Mail/SmtpEmailService.cs`
  - `tests/Explore.Infrastructure.Tests/Infrastructure/SecuritySinkConfidentialityTests.cs`
- [ ] **25.1 Red actual sink/parser inventory** — preserved aliases: `22.1`
  - **Acceptance:** resolve existing source symbols and all affected final logger/scopes/traces/exporters/mail/template output paths; verify generated canary secrets/PII/URLs/CRLF cannot leak or forge events, and actual duplicate/runtime-type/XML relation/YAML alias/template/parser inputs have bounded negative outcomes.
  - **Dependencies:** Phase 15, Phase 16, Phase 18; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **25.2 Green safe output and bounds** — preserved aliases: `22.2`
  - **Acceptance:** redact or omit raw exceptions/provider content and unnecessary tenant/user metric labels, reuse sanitized correlation, constrain confirmed parser/rendering sinks and output expansion; verify text remains data and no process/network side effect is introduced.
  - **Dependencies:** Phase 15, Phase 16, Phase 18; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **25.3 Wire diagnostic fidelity and non-applicability** — preserved aliases: `22.3`
  - **Acceptance:** preserve stable error/outcome/retry categories, classify exact absent shell/LDAP/XPath/NoSQL/binary mechanisms revision-bound, verify all REF-012-014/039-042/124/126/133 and hashing/provider gaps have real evidence rather than a source-search pass.
  - **Dependencies:** Phase 15, Phase 16, Phase 18; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **25.4 Phase gate** — preserved aliases: `22.4`
  - **Acceptance:** common build plus Infrastructure project, NEW `SecuritySinkConfidentialityTests`.
  - **Dependencies:** Phase 15, Phase 16, Phase 18; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **25.5 Phase commits** — preserved aliases: `22.5`
  - **Acceptance:** split telemetry redaction and any independently discovered parser/template behavior fix; verify no raw source or docs wording is used as a product test.
  - **Dependencies:** Phase 15, Phase 16, Phase 18; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 26: API-fed client context and ingress parity — NOT STARTED
**Depends on:** Phase 11, Phase 9, Phase 18, Phase 22, Phase 25. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Event.Web.BffHosting/Security/EventBffRequestEnricher.cs`
  - `src/Event.Web.BffHosting/Security/BffProxyHeaderSanitizer.cs`
  - `src/Event.Web.BffHosting/Proxy/EventApiProxyExtensions.cs`
  - `src/Event.Standalone/Middleware/CombinedApiBridgeMiddleware.cs`
  - `tests/Explore.Blazor.Client.Tests/Security/ApiContentContextTests.cs`
- [ ] **26.1 Red client, redirect and message authority** — preserved aliases: `23.1`
  - **Acceptance:** trace API-fed descriptions/comments/legal Markdown/custom URLs/identities/files and actual Window/Worker/MessagePort producer/consumer listeners; author rendered/typed interop tests for REF-043-062/067/115-116, attribute/event-handler/protocol attacks and actual final login/refresh/error Location variants (`//`, backslashes, control/encoded authority). Test owned-worker versus Window authority, strict reply shape/size/current invocation, wrong source/origin/stale/replay/cancel cases and no secret-bearing bundles.
  - **Dependencies:** Phase 11, Phase 9, Phase 18, Phase 22, Phase 25; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **26.2 Green context-safe consumers and return authority** — preserved aliases: `23.2`
  - **Acceptance:** reuse safe text/attribute/typed interop and framework local-return validation; reject active URL schemes and attacker effective hosts, keep DOM/SVG/CSS boundaries distinct. Inventory actual Window listeners; if present require exact origin/source/schema/correlation and explicit targetOrigin. Challenge dedicated-worker replies use owned worker/port identity rather than ineffective event.origin checks; verify valid local returns and challenge completion still work.
  - **Dependencies:** Phase 11, Phase 9, Phase 18, Phase 22, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **26.3 Preserve actual wire, ingress and cleanup parity** — preserved aliases: `23.3`
  - **Acceptance:** cover changed schema/nullable generated requests/responses and affected components plus cookie proof, failed refresh, privileged headers and purpose revocation. Own worker/listener/cancellation cleanup on success/error/disconnect/stop, ignore late replies and settle once. Normalize Combined errors through safe ProblemDetails; verify no token in console/storage/DTO diagnostics and no obsolete shape consumer.
  - **Dependencies:** Phase 11, Phase 9, Phase 18, Phase 22, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **26.4 Credential retry and resource ingress parity** — preserved aliases: `SEC:RETRY-001`, `SEC:RES-DEST-001`
  - **Acceptance:** Verify rendered/typed client and production Split/Combined transport contracts do not add credential retries or follow protected redirects with privileged headers; resource browser evidence remains namespaced and owned by its retained release brief.
  - **Dependencies:** Phase 11, Phase 9, Phase 18, Phase 22, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **26.5 Phase gate** — preserved aliases: `23.4`
  - **Acceptance:** targeted affected build plus client project, NEW `ApiContentContextTests`; no manually started browser/walkthrough gate. Actual automated host/provider/shipped-asset Green closes the corresponding changed ingress outcome before delivery; real browser/deployment admission remains separately owned final evidence.
  - **Dependencies:** Phase 11, Phase 9, Phase 18, Phase 22, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **26.6 Phase commits** — preserved aliases: `23.5`
  - **Acceptance:** context-safe client rendering and independently changed BFF boundary are separate contracts; verify each generator/source/consumer set is coherent.
  - **Dependencies:** Phase 11, Phase 9, Phase 18, Phase 22, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 27: Untrusted AI and action execution authority — SOURCE IN PROGRESS
**Depends on:** Phase 7, Phase 11, Phase 21, Phase 24, Phase 25. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Application/Features/AiAssistant/Prompting/AiSystemPromptFactory.cs`
  - `src/Explore.Application/Contracts/Infrastructure/Ai/AiChatModels.cs`
  - `src/Explore.Application/Features/AiAssistant/Actions/CreateEventDraftAiToolExecutor.cs`
  - `src/Explore.Application/Features/AiAssistant/Handlers/Commands/ConfirmAiProposedActionCommandHandler.cs`
  - `tests/Event.Application.UnitTests/Features/AiAssistant/PromptInjectionBoundaryTests.cs`
- [ ] **27.1 Red adversarial prompt/tool boundary** — preserved aliases: `24.1`
  - **Acceptance:** inventory all model-facing reference/context and executable tool/action paths; supply controlled provider outputs containing direct/indirect instructions, delimiter escape, forged system/tool history, unknown tool, wrong tenant/resource, private URL/secret demand, malformed/oversized arguments and destructive/stale proposal requests. Verify real orchestration/current authority/schema/confirmation blocks unauthorized data handoff/effect, while an allowed positive action works; do not pin prompt prose or depend on a live model refusal.
  - **Dependencies:** Phase 7, Phase 11, Phase 21, Phase 24, Phase 25; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **27.2 Green server-owned model and tool authority** — preserved aliases: `24.2`
  - **Acceptance:** keep trusted system messages and tool registry server-owned, preserve provenance and strict roles/schemas, bind actor/tenant/resource from current server context and dispatch through existing native authorized handlers. Initial bounds: at most 8 executable calls per turn and 16 per run, 64 KiB arguments per call, argument depth 16, bounded context/output and existing duration/cancellation authority. Verify no model-generated shell/SQL/template/script, provider endpoint or claimed Tool history becomes an approved capability.
  - **Dependencies:** Phase 7, Phase 11, Phase 21, Phase 24, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **27.3 Bind confirmation, disclosure and lifetime** — preserved aliases: `24.3`
  - **Acceptance:** bind persisted proposed action ID/version/exact payload/actor/tenant/expiry; reject altered/stale/replayed/revoked/canceled actions before dispatch, preserve transaction/operation identity and stop loops/work on cancellation. Verify prompt/reference/tool/provider failure content never leaks secrets/private entities through context/logs, and actual generated contracts/consumers remain coherent. Every action family gets source plus positive/negative authorization tests, not just CreateEventDraft.
  - **Dependencies:** Phase 7, Phase 11, Phase 21, Phase 24, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **27.4 Phase gate** — preserved aliases: `24.4`
  - **Acceptance:** common build plus `tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj`, NEW `PromptInjectionBoundaryTests`, controlled provider seam with actual orchestration/authority. HTTP/persisted/cancellation race evidence runs at Ring 3.
  - **Dependencies:** Phase 7, Phase 11, Phase 21, Phase 24, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **27.5 Phase commits** — preserved aliases: `24.5`
  - **Acceptance:** close typed model/tool trust and independently changed proposal execution as separate buildable vertical outcomes when separable; verify all enabled actions have owned tests and no broad agent framework was introduced.
  - **Dependencies:** Phase 7, Phase 11, Phase 21, Phase 24, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 28: Owned diagnostic process execution — NOT STARTED
**Depends on:** Phase 1, Phase 25. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `src/Explore.Diagnostic/Doctor/Infrastructure/DefaultDoctorProcessRunner.cs`
  - `eng/release/src/ISLAMU.ReleaseEngineering/GitCliffRenderer.cs`
- [ ] **28.1 Red process argument and lifecycle contracts** — preserved aliases: `25.1`
  - **Acceptance:** reproduce cancellation/timeout after a pre-subscribed child-start signal, then prove exit, owned child-tree termination and reader/handle cleanup; separately test exact argv containing spaces/quotes/metacharacters/option-shaped input with an approved controlled executable. Cover pre-start cancellation, start failure, nonzero exit, simultaneous stdout/stderr, output budget, stop race and generated secret in error output. A fake runner is not OS-boundary proof.
  - **Dependencies:** Phase 1, Phase 25; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **28.2 Green narrow typed process request** — preserved aliases: `25.2`
  - **Acceptance:** replace affected string argument assembly with a typed vector and approved executable/capability set; UseShellExecute remains false, environment/PATH is controlled, no shell/model code path, and options/paths cannot be caller-selected beyond the declared operation. Reuse existing GitCliff ArgumentList/--no-exec boundary; do not build a general process framework.
  - **Dependencies:** Phase 1, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **28.3 Own output, cancellation and diagnosis** — preserved aliases: `25.3`
  - **Acceptance:** concurrently drain finite stdout/stderr, terminate the owned process tree on cancellation/timeout, await exit/readers, then dispose; settle once across races and distinguish bounded outcomes without returning raw secret-bearing stderr. Initial combined decoded-output budget is 1 MiB; exceeded budget cancels/terminates safely. Verify no application-wide process killing, unbounded queue or disposal-only assumption.
  - **Dependencies:** Phase 1, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **28.4 Phase gate** — preserved aliases: `25.4`
  - **Acceptance:** targeted Diagnostic build plus `tests/Explore.Diagnostic.UnitTests/Explore.Diagnostic.UnitTests.csproj`, NEW `DoctorProcessContractTests`. Execute controlled real-child argv/output/cancellation Green at the owning delivery boundary; its intended failing lifecycle test already precedes 28.2. Final Ring 3 is the aggregate regression, not the first OS-boundary run. A fake runner pass cannot certify termination/reaping.
  - **Dependencies:** Phase 1, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **28.5 Phase commits** — preserved aliases: `25.5`
  - **Acceptance:** verify fixed caller/interface/runner/tests/docs are coherent; separate typed argument authority from independently reviewable cancellation/output ownership with the contracts below.
  - **Dependencies:** Phase 1, Phase 25; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
## Phase 29: Candidate admission and knowledge graduation — NOT STARTED
**Depends on:** Phase 1, Phase 2, Phase 3, Phase 4, Phase 5, Phase 6, Phase 7, Phase 8, Phase 9, Phase 10, Phase 11, Phase 12, Phase 13, Phase 14, Phase 15, Phase 16, Phase 17, Phase 18, Phase 19, Phase 20, Phase 21, Phase 22, Phase 23, Phase 24, Phase 25, Phase 26, Phase 27, Phase 28. **Scenario ownership:** the qualified supplementary scenarios and aliases below.
**Exact phase-owned starting paths (existing inventory or explicit proposal; refresh before edit):**
  - `.github/workflows/security-tests.yml`
  - `eng/release/src/ISLAMU.ReleaseEngineering/SecurityCandidateAdmission.cs`
  - `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/SecurityCandidateAdmissionTests.cs`
- [ ] **29.1 Red expanded candidate admission** — preserved aliases: `26.1`
  - **Acceptance:** add NEW `SecurityCandidateAdmissionTests`; verify wrong revision/image/config/profile, missing/unexecuted/zero-count/provider-skipped/terminated evidence, unresolved applicable reference/BUG/VULN finding and missing operational owner/proof are rejected before release. Every one of 10 BUG and 12 VULN records needs an explicit evidence or bounded non-applicability decision.
  - **Trusted provenance Red:** reject forged/recomputed evidence digests from an untrusted builder, fork workflow or wrong signer/subject; retain the existing release engine's trusted-bundle/policy/ancestry authority. Artifact digests identify content but do not authenticate who executed a test or supplied an operator receipt. Test missing/revoked required attestation and exact candidate/config/profile binding.
  - **Dependencies:** Phase 1, Phase 2, Phase 3, Phase 4, Phase 5, Phase 6, Phase 7, Phase 8, Phase 9, Phase 10, Phase 11, Phase 12, Phase 13, Phase 14, Phase 15, Phase 16, Phase 17, Phase 18, Phase 19, Phase 20, Phase 21, Phase 22, Phase 23, Phase 24, Phase 25, Phase 26, Phase 27, Phase 28; the phase’s source inventory precedes Red; no prior implementation in this phase. **Effort:** L.
- [ ] **29.2 Green immutable complete evidence packet** — preserved aliases: `26.2`
  - **Acceptance:** implement collector/validator for endpoint/profile/209-reference/31-qualified-finding/22-class/ASVS/test/scan/SBOM/migration/role/realm/ingress/ring/recovery/activation/lifetime/generated-wire evidence; verify actual digests/executed scope and no stale/unsupported claims or secrets/PII. Source scans are defense-in-depth inputs, not substitutes for actual effects; require no fallback passwords, no reusable onboarding administrator custody and prompt/message/process trust proof.
  - **Dependencies:** Phase 1, Phase 2, Phase 3, Phase 4, Phase 5, Phase 6, Phase 7, Phase 8, Phase 9, Phase 10, Phase 11, Phase 12, Phase 13, Phase 14, Phase 15, Phase 16, Phase 17, Phase 18, Phase 19, Phase 20, Phase 21, Phase 22, Phase 23, Phase 24, Phase 25, Phase 26, Phase 27, Phase 28; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **29.3 Operational admission and graduation** — preserved aliases: `26.3`
  - **Acceptance:** add concrete achievable vulnerability intake owner/acknowledgment policy and operator proof schema; require actual MFA/admin recovery, non-Testing critical activation/shutdown, egress/TLS/private listeners/fleet limits, final sinks, key/Identity/media/erasure restore and independent authorized security assessment. Promote durable decisions/lessons and only genuinely out-of-scope follow-ups to ADR/journal/backlog; verify none of the 22 mandatory classes or required remediation is deferred as a backlog shortcut.
  - **Dependencies:** Phase 1, Phase 2, Phase 3, Phase 4, Phase 5, Phase 6, Phase 7, Phase 8, Phase 9, Phase 10, Phase 11, Phase 12, Phase 13, Phase 14, Phase 15, Phase 16, Phase 17, Phase 18, Phase 19, Phase 20, Phase 21, Phase 22, Phase 23, Phase 24, Phase 25, Phase 26, Phase 27, Phase 28; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **29.4 External evidence and retained mixed-scope admission** — preserved aliases: `SEC:ROUTE-001`, `SEC:BIND-001`, `SEC:RES-DEST-001`
  - **Acceptance:** Verify resource browser/release partials, coverage/runtime prerequisites, custom-role parity and quarantine outcomes have current explicitly owned status; no skipped/zero/stale/terminated result is relabeled passed. Required non-Testing worker/role/socket/provider/keyring/OS/host/profile evidence and final matrix bind the exact candidate.
  - **Dependencies:** Phase 1, Phase 2, Phase 3, Phase 4, Phase 5, Phase 6, Phase 7, Phase 8, Phase 9, Phase 10, Phase 11, Phase 12, Phase 13, Phase 14, Phase 15, Phase 16, Phase 17, Phase 18, Phase 19, Phase 20, Phase 21, Phase 22, Phase 23, Phase 24, Phase 25, Phase 26, Phase 27, Phase 28; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **29.5 Final Ring 3 once** — preserved aliases: `26.4`
  - **Acceptance:** execute complete applicable project union, five-engine migration-repeat/anomaly/uniqueness/provider contracts and actual PostgreSQL ordinary-role/table/session/worker proof, genuine Keycloak/Cerbos, prompt-action/cancel races, host restart, generated wire/consumer, credential rendering and OS-process argv/cleanup lanes plus architecture/license/scan evidence. Before the API lane select a measured process-scoped GC/concurrency budget based on the documented earlyoom termination; verify exit/completion/nonzero selected totals, never call killed/skipped work green. Preserve exact revision/artifact/config identity; no intermediate/provider-neutral suite multiplication.
  - **Dependencies:** Phase 1, Phase 2, Phase 3, Phase 4, Phase 5, Phase 6, Phase 7, Phase 8, Phase 9, Phase 10, Phase 11, Phase 12, Phase 13, Phase 14, Phase 15, Phase 16, Phase 17, Phase 18, Phase 19, Phase 20, Phase 21, Phase 22, Phase 23, Phase 24, Phase 25, Phase 26, Phase 27, Phase 28; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **29.6 Phase gate and final review** — preserved aliases: `26.5`
  - **Acceptance:** targeted Release build plus release-engine project NEW `SecurityCandidateAdmissionTests`; reuse the same-revision passing build from current task 29.5 rather than repeat it unchanged. Complete anonymized security/database/runtime review and require a concrete counterexample/retest for findings. No weighted vote waives a defect.
  - **Dependencies:** Phase 1, Phase 2, Phase 3, Phase 4, Phase 5, Phase 6, Phase 7, Phase 8, Phase 9, Phase 10, Phase 11, Phase 12, Phase 13, Phase 14, Phase 15, Phase 16, Phase 17, Phase 18, Phase 19, Phase 20, Phase 21, Phase 22, Phase 23, Phase 24, Phase 25, Phase 26, Phase 27, Phase 28; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
- [ ] **29.7 Phase commits and admission handoff** — preserved aliases: `26.6`
  - **Acceptance:** execute separate evidence-admission and incident/knowledge contracts below; verify all 209 references, 27 consultation plus four qualified workstream findings, 22 classes and 50 scenarios have explicit supported-profile dispositions. Final reports distinguish source from executed effects and risk from confirmed exploit; release/deploy remains operator-authorized.
  - **Dependencies:** Phase 1, Phase 2, Phase 3, Phase 4, Phase 5, Phase 6, Phase 7, Phase 8, Phase 9, Phase 10, Phase 11, Phase 12, Phase 13, Phase 14, Phase 15, Phase 16, Phase 17, Phase 18, Phase 19, Phase 20, Phase 21, Phase 22, Phase 23, Phase 24, Phase 25, Phase 26, Phase 27, Phase 28; the preceding owned invariant/implementation record; closure records wait for all implementation work. **Effort:** L.
