# Historical Test Deletions — Assertion-Family Reconciliation

Date: 2026-10-23 Europe/Brussels
Source interval: `ac6e6a123c1ab9b32c16ab00e50e813bea29272f` → `fefcea5f16e3bd723e22076d505a65c696c4e40b`.
Reviewed test-source candidate: `56de0c27cd07b9ea6c0b70793326e220777c04f2`.
Documentation promotion baseline: `1fdc0acffb094d99d9afa9111b75237e8d13a6be`.
Evidence classification: **STATIC** semantic reconciliation; no replacement execution is attested by this document.

## Scope And Claim Boundary

The historical interval contains **23 deleted C# test paths**: nine Application, eleven Persistence and three Infrastructure. This document reconciles those exact paths and their assertion families; it does not audit unchanged tests, new cohorts or the whole current portfolio.

All 23 paths have an inspected current assertion surface or a demonstrated removed production contract below. No unique lost business invariant requiring restoration was identified within these inspected families. This is a bounded **STATIC** conclusion, not proof that replacements pass. Exact destination cases and variants still require candidate-bound runtime evidence in their declared profiles. Retained tests using internal mocks remain observation evidence; their folder names do not make them authenticated provider tests.

The mapping derives from historical source, current assertions, explicit method variants, production-contract removal evidence and project compilation links. Similar names and rename detection supplied navigation candidates only. A stale graph could not resolve the historical relationships; it was not treated as evidence of preservation or absence of impact.

## Compiled Owners And Execution Profiles

All destinations under `tests/Event.API.IntegrationTests/` compile into `Event.API.IntegrationTests`; their class namespaces use `Event.Api.IntegrationTests`. Partial declarations retain a single compiled class identity. Files under Authentication are not automatically HTTP tests: many authenticate a real Local request and then invoke a production handler/service through its selected mutation owner. They prove the declared in-process/provider boundary only.

The API project explicitly links `AuthenticatedSettingsFixture.cs`, `.Postgres.cs`, `.TenantCreation.cs`, `InstanceSettingsCommandFixture.cs`, `TestDbContextOptions.cs`, `PostgreSqlContainerFixture.cs`, and `ConfigurationManifestBootstrapTestHost.cs` from Persistence. It also links shared `OperationalEndpointTestTransport`, `ExternalDataProtectionTestAuthority`, `TransactionCleanupFault`, `ProviderNeutralCorpus` and `SmtpSettingsDatabase`. Such fixture files are not independently executable test cohorts. Infrastructure also links the base authenticated settings fixture, instance command fixture, database options and shared authorities; Persistence links `CurrentHumanPrincipalFixture` from Infrastructure and the shared transaction/data-protection sources.

Profile aliases used below:

- **L**: real Local sign-in through `LocalIdentityLifecycleHttpFixture`, then original request/lifetime and production selected mutation execution against its SQLite-backed disposable native store. It is not a PostgreSQL locking/RLS claim.
- **P**: real PostgreSQL fixture and selected native authority. Docker/provider execution is required; no result is inferred from source.
- **C**: in-process contract/native factory with supplied scope/principal. This is not itself real login evidence.
- **O**: observation/unit-style service with substituted repository/package/cache ports. No provider/authentication claim follows.

## Exact Path Reconciliation

Historical source links bind the deleted files to the original commit. Destination links are repository-relative and identify physical files; the compiled class column governs test selection. Numbers are record references, not implementation phases.

| # | Deleted physical path | Current physical/compiled owner | Disposition and profile |
| --- | --- | --- | --- |
| 1 | [`tests/Event.Application.UnitTests/Features/ConfigurationManifest/InstanceOperatorIdentityPortabilityContractTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Application.UnitTests/Features/ConfigurationManifest/InstanceOperatorIdentityPortabilityContractTests.cs) | [`Authentication/InstanceOperatorIdentityPortabilityContractTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/InstanceOperatorIdentityPortabilityContractTests.cs), class `InstanceOperatorIdentityPortabilityContractTests` | Retained six families through real HTTP/relational fixture; see §1 |
| 2 | [`tests/Event.Application.UnitTests/Features/CustomProperties/CustomPropertyConversionHandlerTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Application.UnitTests/Features/CustomProperties/CustomPropertyConversionHandlerTests.cs) | [`Features/NativeEventPublicActionsHttpTests.CustomPropertyConversion.cs`](../../../tests/Event.API.IntegrationTests/Features/NativeEventPublicActionsHttpTests.CustomPropertyConversion.cs), class `NativeEventPublicActionsHttpTests` | Five method families retained, including four typed-value variants; L |
| 3 | [`tests/Event.Application.UnitTests/Features/EventCustomProperties/Commands/CreateRuntimeCustomPropertyDefinitionQuotaTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Application.UnitTests/Features/EventCustomProperties/Commands/CreateRuntimeCustomPropertyDefinitionQuotaTests.cs) | [`Features/NativeEventPublicActionsHttpTests.CustomPropertyQuota.cs`](../../../tests/Event.API.IntegrationTests/Features/NativeEventPublicActionsHttpTests.CustomPropertyQuota.cs), same partial class | Six old boundary cases consolidated into six explicit arguments; L |
| 4 | [`tests/Event.Application.UnitTests/Features/EventSessionCustomProperties/Commands/SetEventSessionCustomPropertyValueCommandHandlerTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Application.UnitTests/Features/EventSessionCustomProperties/Commands/SetEventSessionCustomPropertyValueCommandHandlerTests.cs) | [`Features/NativeEventPublicActionsHttpTests.CustomPropertyValueValidation.cs`](../../../tests/Event.API.IntegrationTests/Features/NativeEventPublicActionsHttpTests.CustomPropertyValueValidation.cs), same partial class | Three old denial cases consolidated into three explicit arguments plus valid control; L |
| 5 | [`tests/Event.Application.UnitTests/Features/Events/DeleteEventCommandHandlerTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Application.UnitTests/Features/Events/DeleteEventCommandHandlerTests.cs) | [`Features/NativeEventPublicActionsHttpTests.EventDeletionAuthority.cs`](../../../tests/Event.API.IntegrationTests/Features/NativeEventPublicActionsHttpTests.EventDeletionAuthority.cs), same partial class | All three outcomes retained and strengthened; C |
| 6 | [`tests/Event.Application.UnitTests/Features/InstanceOnboarding/Commands/UpdateAuthorizationProviderConfigurationCommandHandlerTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Application.UnitTests/Features/InstanceOnboarding/Commands/UpdateAuthorizationProviderConfigurationCommandHandlerTests.cs) | [`Authentication/HumanSettingsMutationPreparationTests.AuthorizationHandlerObservationMigration.cs`](../../../tests/Event.API.IntegrationTests/Authentication/HumanSettingsMutationPreparationTests.AuthorizationHandlerObservationMigration.cs), class `HumanSettingsMutationPreparationTests` | Nine scenarios retained as eight explicit arguments plus separate actual setup case; L |
| 7 | [`tests/Event.Application.UnitTests/Operations/NativeGroupMemberMutationTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Application.UnitTests/Operations/NativeGroupMemberMutationTests.cs) | [`Authentication/HumanAccountMembershipCustodyTests.GroupTransitions.cs`](../../../tests/Event.API.IntegrationTests/Authentication/HumanAccountMembershipCustodyTests.GroupTransitions.cs), class `HumanAccountMembershipCustodyTests` | Both method names retained; actual identity and membership state replace fake-store assumptions; L |
| 8 | [`tests/Event.Application.UnitTests/Operations/NativeLocationRoomMutationTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Application.UnitTests/Operations/NativeLocationRoomMutationTests.cs) | [`Features/NativeEventPublicActionsHttpTests.RoomScheduleMutation.cs`](../../../tests/Event.API.IntegrationTests/Features/NativeEventPublicActionsHttpTests.RoomScheduleMutation.cs), class `NativeEventPublicActionsHttpTests` | Original method retained with actual scheduled rows, request and selected owner; L |
| 9 | [`tests/Event.Application.UnitTests/Services/InstanceStorageSettingServiceTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Application.UnitTests/Services/InstanceStorageSettingServiceTests.cs) | [`Authentication/InstanceStorageSettingServiceTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/InstanceStorageSettingServiceTests.cs), compiled class **`HumanSettingsMutationPreparationTests`** | Both method names retained; committed row-set/audit/omission assertions replace captured mock writes; L |
| 10 | [`tests/Event.Persistence.IntegrationTests/Repositories/EmailDeliveryDisableCommandHandlerTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/EmailDeliveryDisableCommandHandlerTests.cs) | [`Authentication/EmailDisableHandlerAuthenticationTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/EmailDisableHandlerAuthenticationTests.cs), class `EmailDisableHandlerAuthenticationTests` | Fourteen original methods/variants retained; fixture helpers moved to `EmailDisableHandlerSession.cs`; L |
| 11 | [`tests/Event.Persistence.IntegrationTests/Repositories/EmailDeliveryDisableCommitTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/EmailDeliveryDisableCommitTests.cs) | [`Authentication/EmailDisableCommitTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/EmailDisableCommitTests.cs), class `EmailDisableCommitTests` | Six original methods/variants retained through `EmailDisableCommitAuthenticatedFixture`; L |
| 12 | [`tests/Event.Persistence.IntegrationTests/Repositories/EmailDeliveryDisableImpactTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/EmailDeliveryDisableImpactTests.cs) | [`Authentication/EmailDeliveryDisableImpactTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/EmailDeliveryDisableImpactTests.cs), class `EmailDeliveryDisableImpactTests` | Five names retained; transaction case becomes stronger acquired-proof case; L |
| 13 | [`tests/Event.Persistence.IntegrationTests/Repositories/EmailDeliverySettingBatchTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/EmailDeliverySettingBatchTests.cs) | [`Authentication/EmailSettingBatchAuthenticationTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/EmailSettingBatchAuthenticationTests.cs), class `EmailSettingBatchAuthenticationTests` | Three names/variants retained; SMTP fixture plus actual Local mixed batch; L |
| 14 | [`tests/Event.Persistence.IntegrationTests/Repositories/InstanceAdminEmailSettingMutationTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/InstanceAdminEmailSettingMutationTests.cs) | [`Authentication/InstanceAdminEmailSettingAuthenticationTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/InstanceAdminEmailSettingAuthenticationTests.cs), class `InstanceAdminEmailSettingAuthenticationTests` | Original method and both lock/unlock arguments retained; L |
| 15 | [`tests/Event.Persistence.IntegrationTests/Repositories/InstanceSmtpSettingsMutationTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/InstanceSmtpSettingsMutationTests.cs) | [`Authentication/InstanceSmtpPatchAuthenticationTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/InstanceSmtpPatchAuthenticationTests.cs), class `InstanceSmtpPatchAuthenticationTests` | Original method and three group-presence variants retained; L |
| 16 | [`tests/Event.Persistence.IntegrationTests/Repositories/LocationPrivacyCorrectionOutboxPostgreSqlTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/LocationPrivacyCorrectionOutboxPostgreSqlTests.cs) | [`Authentication/LocationPrivacyCorrectionOutboxPostgreSqlTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/LocationPrivacyCorrectionOutboxPostgreSqlTests.cs), class `LocationPrivacyCorrectionOutboxPostgreSqlTests` | Both original names retained with native production of correction; P, `EventLocationPrivacy` category |
| 17 | [`tests/Event.Persistence.IntegrationTests/Repositories/TenantBrandingProvisioningConcurrencyTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/TenantBrandingProvisioningConcurrencyTests.cs) | [`Authentication/TenantBrandingNativeAuthenticationTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/TenantBrandingNativeAuthenticationTests.cs), class `TenantBrandingNativeAuthenticationTests` | All five original names/variants retained; P with real Local identity, `Runtime` category |
| 18 | [`tests/Event.Persistence.IntegrationTests/Repositories/TenantDelegationMutationTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/TenantDelegationMutationTests.cs) | [`Authentication/TenantDelegationAuthenticationTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/TenantDelegationAuthenticationTests.cs), class `TenantDelegationAuthenticationTests` | Three original names/variants retained; SMTP and non-SMTP fixture profiles; L |
| 19 | [`tests/Event.Persistence.IntegrationTests/Repositories/TenantPlanEmailMutationTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/TenantPlanEmailMutationTests.cs) | [`Authentication/TenantPlanEmailAuthenticationTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/TenantPlanEmailAuthenticationTests.cs), class `TenantPlanEmailAuthenticationTests` | Five original names/variants retained; scenarios/helpers in `.Scenarios.cs` and `.HumanAuthority.cs`; L |
| 20 | [`tests/Event.Persistence.IntegrationTests/Repositories/VisitorAccessSettingsWriterTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Event.Persistence.IntegrationTests/Repositories/VisitorAccessSettingsWriterTests.cs) | [`Authentication/VisitorAccessSettingsWriterTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/VisitorAccessSettingsWriterTests.cs) and [`ResourceVisitorImportConsumptionTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/ResourceVisitorImportConsumptionTests.cs) | Fifteen names retained, one renamed provider case, one moved rollback case; L |
| 21 | [`tests/Explore.Infrastructure.Tests/Infrastructure/AuthorizationProviderConfigurationServiceTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Explore.Infrastructure.Tests/Infrastructure/AuthorizationProviderConfigurationServiceTests.cs) | [`Authentication/AuthorizationProviderConfigurationServiceTests.cs`](../../../tests/Event.API.IntegrationTests/Authentication/AuthorizationProviderConfigurationServiceTests.cs), class `AuthorizationProviderConfigurationServiceTests` | Eighteen names retained; retired credential-flags case intentionally replaced; mixed O/L, not blanket real-auth |
| 22 | [`tests/Explore.Infrastructure.Tests/Infrastructure/PolicySyncServiceTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Explore.Infrastructure.Tests/Infrastructure/PolicySyncServiceTests.cs) | No direct successor: production `PolicySyncService` and interface façade removed in `ceb5c2afe9811d5a6ff308a37337523f92568146` | Delete-obsolete: all five tests belonged to the removed compatibility contract; current publisher remains separately tested |
| 23 | [`tests/Explore.Infrastructure.Tests/Infrastructure/Webhooks/WebhookDeliveryClaimIntegrationTests.cs`](https://github.com/islamu-ngo/Event/blob/ac6e6a123c1ab9b32c16ab00e50e813bea29272f/tests/Explore.Infrastructure.Tests/Infrastructure/Webhooks/WebhookDeliveryClaimIntegrationTests.cs) | [`WebhookLocalDeliveryNativeAuthorityTests.LegacyClaims.cs`](../../../tests/Event.Persistence.IntegrationTests/Repositories/WebhookLocalDeliveryNativeAuthorityTests.LegacyClaims.cs), compiled class `WebhookLocalDeliveryNativeAuthorityTests` | All eight original method names retained against native Global/Runtime owners; P |

## Method And Assertion-Family Mappings

### 1. Operator identity portability

All six original names are retained in the destination class:

- `ExportImportPreservesTargetAuthorityRotatesRevisionAndAuditsOnlyDigests`: legal-name replacement, target operator identity retention, revision rotation, commerce readiness, one audit/outbox message, canonical actor, event type, prior revision hash, content digest and committed revision; serialization excludes the legal-name canary.
- `InvalidLegalIdentityCannotReplaceCurrentDocumentOrEmitSuccessAudit`: each invalid legal identity is denied, current digest unchanged, no success event.
- `StaleRevisionAndCompetingImportsCannotOverwriteWinningDocument`: exactly one winner/event, expected legal-name winner, stale writer rejection. Current HTTP response additionally proves 409 ProblemDetails with `concurrent_update`.
- `TamperingOfficialClaimsAndNonAdminCallersFailClosed`: malformed/official-source claims and nonadministrator import/export rejected; no event; official-instance flag remains false.
- `PersistenceFailureRollsBackWithoutAuditAndMissingTargetUsesExplicitRevision`: injected physical persistence failure leaves no identity/event; correct missing-target revision permits import and gives target-owned operator identity.
- `AuditPersistenceFailureAfterIdentityWriteRollsBackBothChanges`: fault observes identity and audit pending together; failure retains original document digest and no event.

Current nested `Scenario` uses `OperatorIdentityHttpMigrationFixture` for actual export/import HTTP requests; transaction failure uses current interception rather than fake in-memory unit-of-work rollback. This is stronger than the original fake-store boundary, but needs its own real result.

### 2. Custom property conversion

| Original method | Current `NativeEventPublicActionsHttpTests` method | Inspected invariant |
| --- | --- | --- |
| `Create_IgnoresForgedIdentityLifecycleAndDefaultOption_UsesTrustedContext` | `DefinitionCreateIgnoresForgedIdentityLifecycleAndDefaultOption` | Forged identity, tenant, author, deletion, template and default-option facts cannot overwrite trusted scope; normalized display name and generated identity preserved |
| `Update_AppliesExplicitClear_PreservesIdentityProvenanceAndExistingOptions` | `DefinitionUpdateClearsDescriptionAndPreservesProvenanceAndOptions` | Explicit null description/default text, target/tenant/event/author/creation/template provenance/default option/options/deletion and updated actor preserved |
| `Update_StaleConcurrencyStamp_DoesNotChangeTrackedState` | `DefinitionUpdateWithStaleStampPreservesStoredState` | Stale expectation throws and stored definition state remains unchanged |
| `SetValue_RoundTripsTypedPayloadWithTrustedAudit` | `DefinitionValueRoundTripsTypedPayloadWithTrustedAudit` | Text/Number/Boolean/DateTime arguments preserve trusted tenant/author/deletion and typed payload, including zero/false; option remains absent |
| `SetMultiValues_UsesCommandScopeAndSequence_NotItemIdentityOrOrdinal` | `DefinitionMultiValuesUseCommandScopeAndSequence` | Literal Arabic/English order and 0/1 ordinals; command-level definition/event/tenant/author override per-item forged identity/ordinal |

Intentional contract correction: the old mocked update pinned `ConcurrencyStamp == Stamp` and reference identity for option objects. The current real update requires a new stamp unequal to both expected and forged values; persisted option identity replaces process-object reference equality. Reintroducing same-stamp/reference assertions would freeze mock implementation mechanics rather than preserve consumed behavior.

### 3. Definition quotas

All six old methods map to `NativeEventPublicActionsHttpTests.DefinitionCreationEnforcesPersistedQuotaAtEveryBoundary(session,count)`:

| Original method suffix | Current arguments |
| --- | --- |
| `EventHandle_WhenDefinitionCountIsOneBelowQuota_DoesNotReturnQuotaFailure` | `(false,2)` with limit 3 |
| `EventHandle_WhenDefinitionQuotaReached_ReturnsQuotaFailure` | `(false,3)` |
| `EventHandle_WhenDefinitionCountIsAboveQuota_ReturnsQuotaFailure` | `(false,4)` |
| `EventSessionHandle_WhenDefinitionCountIsOneBelowQuota_DoesNotReturnQuotaFailure` | `(true,1)` with limit 2 |
| `EventSessionHandle_WhenDefinitionQuotaReached_ReturnsQuotaFailure` | `(true,2)` |
| `EventSessionHandle_WhenDefinitionCountIsAboveQuota_ReturnsQuotaFailure` | `(true,3)` |

Current assertions preserve failure code, quota key/limit/actual/attempted/scope/tenant and error content. Persisted definition count remains unchanged on denial; below-limit creation now explicitly succeeds and increases count. This replaces `DidNotReceive(CreateWithOptions)` with an actual no-effect outcome.

### 4. Session custom-property value validation

All old cases map to `NativeEventPublicActionsHttpTests.SessionValueRejectsInvalidOrdinalDuplicateOrInactiveOption(scenario)`: `Handle_WhenSingleValueDefinitionReceivesOrdinalOne_ReturnsFailure` → `0`; `Handle_WhenMultiValueDefinitionReceivesDuplicateNormalizedValue_ReturnsFailure` → `1`; `Handle_WhenOptionDefinitionReceivesInactiveOption_ReturnsFailure` → `2`. Current cases assert unsuccessful result, errors and unchanged persisted count (one preexisting duplicate versus none). They then correct ordinal/value/activation and assert successful persisted payload/option. No old solely mock-call assertion needs preservation.

### 5. Event deletion

- `MissingEvent_ReturnsNotFound` → `NativeEventDeletionPreservesMissingAndForeignNotFoundResponses`: missing and foreign root both produce NotFound with exact ID.
- `CallerWithoutEventAuthority_ReturnsForbiddenOutcome` → `EventDeletionDeniesNonownerEvenWithInstanceAdministration(false/true)`: denial code and surviving event, including a forged posted owner ID and administrator variant.
- `PaidEvidence_ReturnsConflictOutcome` → `PaidEvidenceBlocksEventDeletionIncludingAfterAnEarlierEmptyRead(false/true)`: paid-evidence conflict and retained event, including evidence arriving at the authority transaction boundary.

Additional current owner/cascade, revocation, cancelled commit and selected-Cerbos-outage cases are stronger neighbouring evidence; they are not used to erase any of the three explicit mappings.

### 6. Authorization-provider command handler

Eight old methods map individually to `HumanSettingsMutationPreparationTests.AuthorizationHandlerRetainsOriginalBusinessScenariosWithPrivateObservation(scenario)`:

| Old method | Argument |
| --- | --- |
| `Handle_WhenUserIsNotInstanceAdmin_ReturnsUnauthorized` | `non-administrator` |
| `HandleSetup_WhenSetupModeBecomesInactive_DeniesMutation` | `setup-inactive` |
| `Handle_WhenConfigurationIsInvalid_ReturnsValidationFailure` | `invalid-configuration` |
| `Handle_WhenProviderIsLocal_AppliesConfigurationWithoutVerification` | `local` |
| `Handle_WhenDeploymentOwnsProvider_RejectsAdminOverride` | `deployment-owned` |
| `Handle_WhenCerbosEndpointVerifies_NormalizesEndpointAndMarksVerified` | `cerbos-serving` |
| `Handle_WhenCerbosAdminEndpointIsRejected_ReturnsFailureWithoutSaving` | `admin-endpoint-rejected` |
| `Handle_WhenCerbosEndpointDoesNotVerify_ReturnsFailureWithoutSaving` | `cerbos-not-serving` |

`HandleSetup_WhenSetupModeIsActive_AppliesConfigurationWithoutAdminIdentity` → `AuthorizationHandlerSetupUsesAcceptedBootstrapWithoutAdministratorIdentity`.

Current guards preserve success/denial and no-write outcomes, normalized endpoint persistence and serving health requirement. They add immutable original command, exact source/actor, actual final persisted effects, and actual health transport requests (only gRPC Health/Check). Local mode avoids health traffic. Setup uses a real accepted bootstrap with null audit actor. Old message prose and internal `Received` mirrors are replaced by failure codes/errors and committed rows. This does not certify an external live Cerbos deployment.

### 7. Group membership

Both original names are retained on `HumanAccountMembershipCustodyTests`:

- `RoleChangesAndDeletion_PreserveAnAdministratorAcrossMembershipTransitions`: deny last-administrator demotion/deletion without row changes; add actual successor; reject stale changes; permit demotion when successor exists; preserve successor; permit original member deletion under successor actor and audit it canonically.
- `Addition_RequiresTenantParticipationAuthorityAndUniqueMembership`: valid tenant/group placement and caller membership authority required; failed attempts leave rows equivalent; valid addition persists exact user/tenant/group placement/role/position; duplicate leaves membership unchanged.

### 8. Location room

`NativeEventPublicActionsHttpTests.ActiveSchedule_BlocksReparentingButAllowsSameLocationUpdates` retains the old method identity and all reparent/same-location controls. Current assertions use actual scheduled EventSession/EventLocation rows, deny reparent without name/location/stamp/tenant change, permit rename and stamp advance, remove real room schedule reference, then permit move while preserving event location and tenant. It checks exact selected context/lifetime and no lingering transaction. No fake repository predicate remains necessary to preserve the business rule.

### 9. Storage group omission

The original `InstanceStorageSettingServiceTests` class becomes methods on **`HumanSettingsMutationPreparationTests`**, despite the physical filename retaining its old stem:

- `ApplySettingsAsync_WhenOnlyPolicyGroupIsSupplied_WritesOnlyPolicyKeys`: exact committed policy key set, literal provider/limits/lock values, canonical audit actor; untouched locked S3 bucket retained. Current route matrix is an intentional additional policy effect, not accidental cross-group S3 modification.
- `ApplySettingsAsync_WhenOnlyS3GroupIsSupplied_WritesOnlyS3KeysAndSkipsRedactedCredentials`: exact endpoint/public endpoint/bucket/region/path style/expiration set and literal values; no removed baseline row; omitted locked policy value/audit preserved. Old captured Upsert-key lists are superseded by before/after persisted row sets.

### 10. Email disable handler

All fourteen original names remain on `EmailDisableHandlerAuthenticationTests`; original argument families remain explicit:

| Method | Family/variants preserved |
| --- | --- |
| `PreviewBindsActualAffectedScopesWithoutChangingSettings` | Actual scopes/revisions/token versus unchanged policy/cache/audit |
| `LockedOrNoOpPreviewNeverIssuesConfirmation` | Locked/no-op variants; no confirmation |
| `UnauthorizedTargetsCannotPreviewOrDisable` | Missing actor, instance target, other tenant, revoked administrator; no mutation |
| `EmptyTenantTargetReturnsValidationBeforeSharedLease` | Empty Guid rejected before lock/transaction |
| `AuthorityRevokedWhileAcquiringLeaseIsRechecked` | Preview/disable × tenant/instance; revocation at lease boundary |
| `EarlierAdministrationCannotOverrideRevokedGrant` | Preview/disable × tenant/instance; prior check cannot authorize later write |
| `MissingPreviewScopeReturnsNotFoundWithoutMutation` | Missing target/no effect |
| `DisablePublishesCommittedWriterChangesWithCurrentActorAndExactTarget` | Tenant/instance; cache invalidates after physical commit; exact notification actor/target/key/old/new |
| `ExactAcknowledgementCannotBeReplacedByValidToken` | Null, empty, trailing-space, lowercase acknowledgement denied |
| `ConfirmationBindsActorTargetRevisionAndProtectedToken` | Actor, target, revision, token substitutions denied |
| `ChangedRevisionOrAffectedMembershipRejectsOldConfirmation` | Revision versus scope-membership change |
| `ConfirmedTokenCannotReplayImmediatelyOrAfterReenable` | Tenant/instance; once-only disable and retained reenable |
| `FailedCommitLeavesPolicyCacheAndAuditUntouched` | Tenant/instance; actual failure rolls back policy, cache and event effects |
| `PostCommitAuditFailurePreservesDisabledPolicyAndInvalidatedCacheAfterDisconnect` | Tenant/instance; physical commit survives subsequent audit failure/disconnect and is not reported as rollback |

Moved helper assertions are in `EmailDisableHandlerSession`: `CachedSentinelAsync`, untouched/empty effect checks, lease-key set/order and serializable isolation observations. Their absence from the destination main file is not lost coverage. Intentional privacy change: emitted audit log now permits only `Scope`; actor/tenant/old/new/setting values remain guarded on the committed notification, not leaked as routine logging fields.

### 11. Email disable commit

All six names remain on `EmailDisableCommitTests`:

- `ConfirmedDisable_CommitsOnlySelectedIntentAndRetainsConfiguration` (tenant/instance): exact selected value/revision/suppression timestamp, untouched other scope and inherited metadata, retained SMTP/configuration rows, committed notification target/actor/value.
- `ChangedRevisionOrAffectedMembership_RejectsOldConfirmation` (revision/membership): stale confirmation conflicts, unchanged state and no notifications.
- `Confirmation_DoesNotReplaceExactAcknowledgementOrFreshAuthority` (acknowledgement/different actor/revoked role/different current tenant): unsuccessful exact-current-authority checks, unchanged state/no notifications.
- `ConfirmedToken_CannotDisableAgainAfterReenable` (tenant/instance): token replay rejected, reenabled state survives, one notification only.
- `RevisionWriteFailure_RollsBackConfirmedSettingAndSuppression` (tenant/instance): injected revision failure retains selected/other/inherited/metadata state and publishes nothing.
- `NewlyLockedTenantDelegation_ConflictsWithPreviouslyValidDisablePreview`: new lock conflicts without changing target/configuration metadata or notifications.

`EmailDisableCommitAuthenticatedFixture` now resolves actual Local principal/lifetime and production preparation rather than manufacturing administration. Changes in direct foreign-target denial form are stricter fail-closed behavior, not lost effects checks.

### 12. Email disable impact

Five old names remain on `EmailDeliveryDisableImpactTests`: `InstanceDisable_IncludesInheritedAndMisconfiguredScopesButPreservesIndependentEnablement`; `TenantDisable_RespectsInstanceAndDelegationLocks` (two lock variants); `TenantDisable_OnlyAffectsSelectedTenantAndUsesZeroForAbsentControl`; `ScopeOwnLock_OnlyLocksDescendantsAndStillAllowsDisable` (tenant/instance); `InstancePreview_ReflectsCommittedMembershipChangesWithoutAnInstanceRevisionChange`. Their actual scope, revision-zero, locking, no-tracking and dynamic membership families remain.

`Preview_RequiresCallerTransaction` → `Preview_RequiresOriginalAcquiredProofNotOnlyCallerTransaction`: missing transaction fails; merely opening serializable transaction without acquired original authority now also fails. Foreign target now throws UnauthorizedAccessException instead of returning null, preserving non-disclosure with a stricter trust boundary.

### 13. Email setting batches

All names remain on `EmailSettingBatchAuthenticationTests`:

- `SmtpBatch_CommitsAllFieldsAndNotificationsOrRollsBack(rejectPort)`: literal host/port pair all-or-nothing, revision and exact notifications/canonical actor, no leaked transaction.
- `StrictSmtpBatch_RejectsForeignCategoryWithoutWritingValidEmailField`: valid SMTP member cannot slip through a rejected foreign-category batch; no notification.
- `BestEffortEventsBatch_AppliesPublicationPolicyAndRejectsForeignSmtpWithoutNestedTransaction`: event setting accepted, SMTP member rejected with reason, exact rows/notifications, original Global owner/preparation/lifetime and no nested transaction.

### 14. Instance administrator email locks

`InstanceAdminEmailSettingAuthenticationTests.TenantSmtpLockMutation_CompletesAndPublishesOnlyAfterCommit(lockSetting)` retains true/false variants and setting identity/creation metadata, canonical updater, tenant revision increment and exact tenant/key/actor notification. The observer sees committed state; no publish-before-commit interpretation is introduced.

### 15. Concurrent SMTP group patches

`InstanceSmtpPatchAuthenticationTests.PatchWaitingAtSmtpFence_PreservesConcurrentSaveOutsideSuppliedGroups(includeConfiguration,includePolicy)` retains the three original `(false,true)`, `(true,false)`, `(true,true)` arguments. Separate concurrent contexts/connections; pending writer reaches global fence before beginning transaction; unrelated concurrent supplied-group save commits first; omitted groups survive; final expected rows/delivery enablement/lock facts and exact notifications remain. Current assertions additionally observe no early notifications and canonical actor.

### 16. Location privacy correction outbox

Both names remain on `LocationPrivacyCorrectionOutboxPostgreSqlTests`:

- `RetryAfterPartialCorrection_PersistsPendingProcessingAndCompletedStates`: actual pending→processing→retry pending→processing→completed persistence; failed partial dispatch/retry fences; original correction identity; planner input tenant/event/correction kept across attempts.
- `TerminalCorrection_RemainsVisibleAndReconcilesWithFencedLease`: dead-letter visibility, current fenced reconciliation and final state; direct dispatch/reconcile without acquired work rejected; planner input custody and accepted native production.

Profile: linked shared `PostgreSqlContainerFixture`, `NotInParallel("PersistenceDb")`, `Category("EventLocationPrivacy")`; native location producer plus ordinary consumer. A class being in API does not turn these into HTTP or fast pure tests.

### 17. Tenant branding provisioning

All five names remain on `TenantBrandingNativeAuthenticationTests`:

- `TwoMissingDocuments_WithDifferentNames_ConvergeWithoutOverwritingWinnerOrWarmCache`: concurrent missing-document winner and warmed-cache identity/content.
- `AmbientConflict_PreservesSavepointTrackingAndCallerCommitOrRollback(rollback)`: savepoint conflict cannot lose caller-owned tracking/transaction or overwrite winning branding, both owner outcomes.
- `SnapshotConflict_EscapesForOwnerRollbackAndFreshTransactionRetry`: stale snapshot escapes for owner rollback then fresh retry; no fake success within failed transaction.
- `MissingTenantAndPrimaryKeyCollision_AreNotMistakenForProvisioningRaces`: real missing tenant and unrelated primary-key failure are not swallowed as expected provisioning races.
- `CancelledInsert_RollsBackOnlyAttemptAndRetainsUsableOwnerTransaction`: cancelled attempt leaves caller transaction usable and winner preserved.

`TenantBrandingNativeAuthenticationFixture` creates/authenticates a new Local account in PostgreSQL and retains the enrolled Runtime context/credential lifetime; additional current lock-field/CAS variants strengthen distinct mutation cases. `Runtime` category and real PostgreSQL fixture still require execution.

### 18. Tenant delegation

All three names remain on `TenantDelegationAuthenticationTests`:

- `GlobalSmtpFencePrecedesMixedPatchTransaction_AndNotificationsFollowCommit`: actual global fence before transaction, incomplete writer/no notifications/unchanged snapshots while held, seven canonical notifications after final committed row set; exact delegation settings and SMTP metadata/revision.
- `NonSmtpPatch_PreservesDeploymentRulesWithoutAcquiringSmtpFence`: no SMTP fence for unrelated patch, persisted delegation booleans and exact notification keys/actor; SMTP rows/revision unchanged.
- `InvalidOrUnauthorizedPatch_ProducesNoTransactionOrNotifications(unauthorized)`: malformed/unauthorized variants deny before transaction with no row/revision/notification effect.

### 19. Tenant plan email

All five names remain on `TenantPlanEmailAuthenticationTests`:

- `ApplyingAssignment_LoadsAndAppliesPersistedVersionContent(mixed)`: persisted published version (not posted values) controls SMTP and mixed AI/publication settings, assignment/log/notification effects.
- `SmtpPlanWaitsForGlobalFence_BeforeStartingTransaction`: independent owner, pending writer reaches fence before transaction, completes after release and applies full plan.
- `WrongTenantOrLockedInstanceSetting_RejectsWithoutPartialPlan(locked)`: foreign target now throws UnauthorizedAccessException; instance lock returns `tenant_plan_setting_locked`; both call shared unchanged-state assertions.
- `PersistedQuotaAboveInstanceCeiling_RejectsWithoutPartialPlan`: persisted quota ceiling conflict and no partial plan.
- `UnsafePublicationPolicy_RollsBackAlreadyWrittenSmtpAndAssignment`: observes proposed SMTP inside transaction, then rejected publication policy restores exact settings/revision/suppression/assignment/log and no notifications.

`AssertAppliedAsync`, `AssertUnchangedAsync` and `AssertAssignmentUnchangedAsync` in `.Scenarios.cs` preserve assertion families moved out of the main file. They are part of the same compiled partial class, not unexecuted dead helper evidence.

### 20. Visitor access settings

The following original names remain on `VisitorAccessSettingsWriterTests` with their original variants: `InstanceModeChange_RejectsInheritedAccountRequiredConfiguration` (AnonymousOnly/DirectoryListingOnly); `InstanceProviderMutation_IncludesOtherTenantsWithNoOverrides`; `LastProviderRemovalAndPublication_ShareOuterLeaseBeforeEitherSnapshot` (both orderings); `NativeSettingHandlers_RejectUnsafeVisitorModeWithoutPartialEffects` (scalar/batch/control-plane); `InheritanceTransitions_AreEvaluatedAgainstAffectedEvents` (reset/lock/unlock); `ControlPlaneLockTransitions_PreserveUsableInheritedProviderPolicy` (lock/unlock); `ProviderReplacement_IsValidatedAsCompleteBatchAndKeepsOperatorAuthenticationIndependent`; `RepositoryBypassesRejectVisitorKeysBeforeAnyBatchWrite_AndUnrelatedWritesRemainAvailable`; `MissingOuterOwnership_IsRejectedBeforeVisitorMutationEvenInsideSerializableTransaction`; `ReadConfigurationResolvesNormalizedPrimaryProviderMetadata`; `ApplyConfigurationPersistsPrimaryProviderAsLookupIdentifier`; `ReadConfigurationForAtprotoPrimaryForcesAtprotoLoginEnabled`; `ApplyAtprotoPrimaryPersistsEnabledAtprotoAxis`; `PlanAssignment_RejectsUnsafeModeAndRollsBackOtherPlanSettings`; `ManifestBoundary_DoesNotBroadenProhibitedVisitorKeys_AndAllowsExistingCatalogWrites`.

Families cover unsafe inherited account-required mode denial without effects, other-tenant scope, ordering/fence and publication, complete-provider replacement independent of operator authentication, guarded repository bypass, current normalized lookup metadata/ID serialization, ATProto axis, plan rollback and restricted manifest keys. Actual original ownership denial now throws UnauthorizedAccessException where old direct repository code could reach weaker InvalidOperationException.

Renamed `ProviderRoot_RejectsLastPathRemovalAndPreservesFieldsTheCallerDidNotSupply` → `ProviderRoot_RejectsLastPathRemovalAndPreservesUnchangedCompleteGroupFields`: rejects removal of the last usable path without persisting proposed endpoint; later accepted complete-group update retains Google enablement/onboarding/signup metadata and configured Keycloak authority. Complete submitted groups replace obsolete partial DTO assumptions.

Moved `BatchStorageFailure_RollsBackAcceptedVisitorWriteAndDefersAllNotifications` → **`ResourceVisitorImportConsumptionTests.BatchStorageFailure_RollsBackAcceptedVisitorWriteAndDefersAllNotifications`**. It still asserts rollback of mode plus unrelated catalog member and no published notifications under an injected storage failure. It is not lost because it no longer sits next to the visitor writer tests.

### 21. Authorization-provider configuration service

All original method names remain except `ReadConfigurationAsync_WhenAdminApiCredentialsResolve_ReturnsConfiguredFlagsWithoutSecrets`, whose two configured flags/secret-resolution contract was intentionally retired. Current `RuntimeSecretCatalogueRejectsRetiredCerbosAdministratorBindings` rejects both retired secret keys, and `ReadConfigurationAsync_IgnoresLegacyStoredAdminCredentials` asserts neither synthetic legacy username nor password is serialized. There is no current credential-flag contract to restore.

Retained read/bootstrap families (O): `ReadConfigurationAsync_WhenEnvironmentEndpointExists_BootstrapsEditableApplicationManagedMetadata`, `ReadConfigurationAsync_WithExplicitLocalProvider_IsReadyWithoutCerbos`, `ReadConfigurationAsync_WithExplicitCerbosProvider_RemainsPendingUntilServerReconciliationSucceeds`, `ReconcileDeploymentProviderAsync_WhenCanceled_RecordsSafeFailedState`, `BootstrapState_ConcurrentCallersShareOneReconciliation`, `BootstrapState_CanceledJoinerDoesNotCancelOwner`, `ReadConfigurationAsync_WhenDatabaseEndpointExists_DatabaseWinsOverEnvironmentBootstrap`, and `ReadConfigurationAsync_WhenEndpointDeploymentManaged_UsesEnvironmentAndReturnsReadOnlyMetadata`. These preserve endpoint precedence, ownership/editability/bootstrap metadata, local readiness versus verified/synchronized Cerbos readiness, safe cancellation and single-owner/coalesced reconciliation. They still use substituted repository/package/cache ports via `CreateService`; do not count them as real runtime authentication/provider evidence.

Retained write families (L): `ApplyConfigurationAsync_WithDeploymentManagedProvider_RejectsApplicationOverride`; `ApplyConfigurationAsync_WhenEndpointAndCredentialsDeploymentManaged_DoesNotPersistOwnedValues`; `ApplyConfigurationAsync_WithAdminEndpoint_NeverPersistsCredentials`; `ApplyConfigurationAsync_WithBareEndpoints_NormalizesBeforeStorage`; `ApplyConfigurationAsync_WithLocalProvider_InvalidatesRuntimeProviderModeCache`; `ApplyConfigurationAsync_WhenCredentialsAreOmitted_PreservesExistingSecrets`; `ApplyConfigurationAsync_WithUnsafeAdminEndpoint_DoesNotPersistOrInvalidateProviderModeCache`. Current mutation service uses `ResourceVisitorAuthenticatedFixture`, real system repository and policy mutation selection; `SettledCacheActivation` reads committed provider rows at each activation callback. Mock call-count expectations become committed row/activation behavior. Some method stems retain historical wording; the actual current effect assertions decide scope.

Endpoint-validator families `VerifyCerbosAdminEndpointAsync_WithUnsafeEndpoint_ReturnsFalseWithoutPersistingSecrets` and `VerifyCerbosAdminEndpointAsync_WithBareRemoteHost_NormalizesAndAccepts` remain O; no external endpoint liveness is claimed.

### 22. Removed policy synchronization façade

The exact production removal in `ceb5c2afe9811d5a6ff308a37337523f92568146` deletes `PolicySyncService`, whose XML summary called it a compatibility façade delegating to `IPolicyPackageService`. Current repository search has no `PolicySyncService`, `IPolicySyncService` or public old sync/summary consumer. `PolicyPackageInfo` remains as a type definition without another current source reference; that residue does not restore the removed service contract. The private `CerbosPolicyPackageService.ReloadAllInstancesAsync` is a different internal publisher operation.

Thus all five old tests are delete-obsolete, not missing replacements:

- `SyncRolePoliciesAsync_DelegatesToPackagePublisher`, `SyncAllPoliciesAsync_DelegatesToPackagePublisher`, `ReloadAllInstancesAsync_DelegatesToPackagePublisher`: mock call mirrors on removed methods.
- `GetPolicySummaryAsync_UsesPackageManifest`: summary count/hash/time/zero-role/zero-permission mapping on removed `PolicyPackageInfo` facade output; no surviving consumer requires this shape.
- `PublishFailure_DoesNotThrowToRoleMutationCallers`: removed role-mutation façade behavior, not a current requirement to swallow native policy publication failure.

Current publisher behavior remains separately guarded by `Explore.Infrastructure.Tests.Infrastructure.CerbosPolicyPackageServiceTests`, including upload/schema/reload and safe-failure diagnostics. It is not falsely presented as an exact successor for deliberately removed facade APIs.

### 23. Webhook claim, recovery and circuit state

All eight names remain on **`Event.Persistence.IntegrationTests.Repositories.WebhookLocalDeliveryNativeAuthorityTests`** in `.LegacyClaims.cs`:

- `ClaimDue_WithRetryingExecutionStrategy_ClaimsPrimaryTargetAsRetriableUnit`: retries-enabled strategy, one target/message identity and initial fence 1. Neither old nor new case injects a transient retry; this is a retries-enabled execution profile, not empirical replay-after-transient proof.
- `ClaimDue_WhenWorkersCompete_ProducesOneFencedOwner`: synchronized competitors, exactly one nonempty owner and one empty result; wrong token/fence cannot read claim, correct token/fence can.
- `GetActiveClaimAsync_WhenEndpointIsArchivedAfterClaim_ReturnsNull`: archived endpoint revokes active claim visibility.
- `RecoverExpiredClaim_AppendsFailureEvidenceAndReschedulesTarget`: exactly one recovery; RetryDue/no lease, failed attempt evidence with current fence and `processing_lease_expired`.
- `RecoverExpiredClaim_WhenRetryBudgetIsExhausted_DeadLettersTarget`: exhausted target dead-letters.
- `RecordFailure_WhenClaimLeaseIsStale_DoesNotMutateEndpoint`: stale lease cannot change failure count or Active status.
- `RecordFailure_RequiresCurrentTokenAndFence_AndAutoPausesExactlyOnce`: wrong token/fence denied; first owned count 1; second owned count 2 triggers pause; subsequent denied; exact final count/status.
- `EndpointCircuit_AutoPauseAndTenantScopedResumeResetState`: two owned failures pause; wrong tenant cannot resume; correct tenant resumes with zero failures, cleared circuit/pause timestamps/reason and canonical actor.

Native denial now returns null instead of old synthesized failure-state values in some branches. Actual no-effect/circuit state remains asserted; preserving old sentinel DTO values would reintroduce an obsolete authority seam. Source seeding uses shared PostgreSQL fixture and separate Global/Runtime contexts; the old dedicated Explicit runtime container/inline credential fixture was not preserved. No runtime success is inferred from that correction.

## Restoration Decision And Required Execution

**Source-level restoration list: none identified.** Every historical business family has an inspected current identity or a demonstrated removed production contract. The principal remaining risk is execution/admission: moved PostgreSQL tests must run in their API/Persistence owner and correct category, while pure observation mocks must not be reported as real authenticated provider execution. Actual no-zero/no-skip identity/variant matching still governs final acceptance.

For source-to-result matching, execute exact classes above against the committed candidate, with their real fixture prerequisites. In particular `NativeEventPublicActionsHttpTests`, `HumanSettingsMutationPreparationTests` and `HumanAccountMembershipCustodyTests` are partial classes spanning many files; file stem is insufficient selector. `InstanceStorageSettingServiceTests.cs` has no current class of that name. `.LegacyClaims.cs` compiles into `WebhookLocalDeliveryNativeAuthorityTests`. Retain separate actual TRX results/profile facts; do not turn static completeness or the 23-path census into passing evidence.

## Evidence Use And Verification Boundary

Reproduce the historical path census with `git diff --no-renames --name-status ac6e6a123c fefcea5f16 -- tests/Event.Application.UnitTests tests/Event.Persistence.IntegrationTests tests/Explore.Infrastructure.Tests`. Historical-source links above bind the original assertions; current code links identify their compiled owners. This document records source inspection only and contains no build, test, provider, container or matrix result.

Actual execution records must separately name the exact committed candidate, selected methods and variants, provider/authentication/profile, pass/fail/skip counts and retained artifact identity. A later documentation commit is not a new tested code candidate. Follow the [testing guidance](../TESTING.md) for the owning lanes. The [add-on/recovery disposition record](TEST_COHORT_DISPOSITIONS.md) covers a separate current relocation and cannot substitute for historical replacement results.
