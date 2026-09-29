# Major Decisions

Last Updated: 2026-04-30 Europe/Brussels

## 2026-04-30 Europe/Brussels - Event-Scoped Operational Roles: Assignment-Only Authorization Boundary

### Event Role Authority Is Persisted as `EventRoleAssignment`, Not Global Roles or Generic Resource Grants

- **Decision:** Event operational authority is represented by `EventRoleAssignment(TenantId, EventId, UserId, RoleId)` with lifecycle state, audit fields, and app-managed numeric `Version` concurrency. `RoleScopeEnum.Event` classifies seeded role templates but does not itself grant authority. Generic `ResourceRoleAssignment(ScopeType, ScopeId, UserId, RoleId)` and Keycloak/JWT per-event roles are explicitly rejected.
- **Why:** Event roles such as `RegistrationManager` and `CheckInStaff` must apply to exactly one event. Global roles or generic resource grants blur tenant/event boundaries, are hard to prove in Cerbos/local parity tests, and make HAL/UI affordances vulnerable to role-name shortcuts.
- **Impact:** Foundation files include `Explore.Domain/EventRoleAssignment.cs`, `Explore.Domain/Enums/EventRoleAssignmentStatus.cs`, `Explore.Persistence/Configurations/Entities/EventRoleAssignmentConfiguration.cs`, `Explore.Application/Contracts/Persistence/IEventRoleAssignmentRepository.cs`, `Explore.Application/Contracts/Services/IEventAuthoritySnapshotService.cs`, and migration `Explore.Persistence/Migrations/20260430162948_AddEventRoleAssignments.cs`.
- **Consequence:** Future assignment commands must enforce owner invariants and authority ceiling against this assignment model. Blazor remains HAL-driven and must not inspect role names/claims. Cerbos payloads must be scoped to the checked event or explicit batch.

### Event-Child Authorization Fails Closed Without Tenant + Event Context

- **Decision:** Event-family fallback authorization requires a valid `tenantId` and `eventId` in resource attributes before inherited tenant/org authority can allow access. Batch fallback must also verify that the resource tenant matches the precomputed batch `AuthorityProfile.TenantId`.
- **Why:** Missing `eventId` or cross-tenant `tenantId` in event-child resources can turn event-local authority into broader access. Cerbos/local parity depends on both engines receiving the same tenant/event resource contract.
- **Impact:** `ResourceDescriptors` now emits event context for event, event registration, and session agenda item resources; fallback single and batch paths deny missing event context and tenant mismatch; tests cover missing event ID and cross-tenant batch denial.
- **Consequence:** Any future event-child resource (payments, content review, check-in, speaker coordination, moderation, contact-share consent where applicable) must either carry tenant/event context or document why it is not event-scoped. Parent lookup must be tenant-safe.

## 2026-04-28 Europe/Brussels - Organizer Email Consent: Event-Triggered, Organizer-Scoped Sharing

### Consent Is Scoped to Recipient Actor + Purpose, Not to Each Event

- **Decision:** A user's opt-in email-sharing consent is unique per `(TenantId, UserId, RecipientActorId, PurposeCode)`. `SourceEventId` and `SourceEventRegistrationIntentId` are audit context showing where the consent was collected; they are not part of the uniqueness boundary.
- **Why:** The user-facing promise is "share my email with this organizer for future events and related updates," not "share my email for this one event only." Organizer-scoped consent prevents duplicate prompts across events by the same approved organization while still preserving the event/registration that collected the opt-in.
- **Impact:** `EventContactShareConsentConfiguration` enforces `ix_eventcontactshareconsents_scope_unique` on tenant + user + recipient actor + purpose. Registration integration calls `IContactShareConsentService.ProcessRegistrationConsent(...)` after the registration intent succeeds and stores the parent `EventRegistrationIntent` id as audit context because a single intent can create multiple child registration rows; failures are logged but never roll back registration.
- **Consequence:** Any future consent status endpoint or UI must reason from the publisher actor, not only the event id. Event ids are filters/audit metadata, not consent identity.

### Email Sharing Uses Snapshots and Status Lifecycle, Not Live PII or Soft Delete

- **Decision:** Granted consent stores `EmailSnapshot`, `EmailNormalizedSnapshot`, `ConsentTextSnapshot`, `ConsentUiVersion`, and `GrantedAt`. Withdrawal sets `Status = Withdrawn` and `WithdrawnAt`; consent rows are not soft-deleted and exported emails are copied into `EventContactShareExportItem.EmailSnapshot`.
- **Why:** Consent and export records are evidence. They must reflect exactly what was shown and shared at the time, independent from later user email changes, profile deletion workflows, or export-time mutations.
- **Impact:** The Connected Apps settings UI lets users withdraw future platform sharing, and organization exports include only currently granted consents. Export audit headers/items preserve who exported, when, in what format, how many rows, and which email snapshots were emitted.
- **Consequence:** Future privacy work must distinguish platform visibility withdrawal from external mailing-list deletion. The UI copy intentionally warns that withdrawal does not guarantee removal from lists already compiled outside the platform.

### Only Approved Organization Actors Can Receive Registration-Time Email Consent

- **Decision:** Registration-time consent is processed only when the event publisher resolves through `Event.ActorId -> Actor.OrganizationId -> Organization` and that organization is approved. The schema remains actor-based (`RecipientActorId`) for future extensibility, but current business logic restricts recipients to approved organizations.
- **Why:** Email sharing with unverified or non-organization actors creates avoidable privacy risk. Actor-based storage avoids a schema redesign if future recipient types are deliberately introduced.
- **Impact:** `ContactShareConsentService` returns a safe no-op for unapproved/non-organization publishers. The registration handler wraps consent processing in a fail-safe try/catch so consent infrastructure cannot break event registration.
- **Consequence:** New recipient types require an explicit policy decision, Cerbos policy update, and UI copy review; they must not be enabled by merely passing a different actor id.

## 2026-04-27 Europe/Brussels - Multi-Theme Appearance: Presets vs. Profiles Architecture

### Presets Are Templates; Profiles Are Snapshots; Active Preference Points to a Profile

- **Decision:** When a user selects a theme, the system clones the preset into a `UserAppearanceProfile` (with palette snapshots and lineage tracking). The `UserAppearancePreference.ActiveProfileId` points to this clone, never to the original preset. Tenant changes, deletions, or restrictions cannot break a user's UI.
- **Why:** The original `UiTheme` entity mixed too many concepts — system preset, tenant custom, selectable theme, active user theme, mutable admin object. Mixing these made it impossible to guarantee stability when a tenant modified or deleted a theme that users had selected.
- **Impact:** Three new domain entities (`UiThemePreset`, `UserAppearanceProfile`, `UserAppearancePreference`), three new repositories, full EF Core configuration, 4 built-in presets seeded deterministically via `LookupTableSeeder`, `IAppearanceResolutionService` with 6-tier fallback chain, 9 API endpoints, 9 BFF proxy endpoints, `IAppearanceThemeService` Blazor state service with reactive `Changed` event, `ThemeQuickSwitcher` component, rewritten `SettingsAppearance` page.
- **Consequence:** `DefaultThemeId` on `UserAppearancePreference` is now `ActiveProfileId` (points to a profile, not a theme). Old `appearance.default_theme_id` setting key is `[Obsolete]` but still exists for backward compat. `UiTheme` entity/table preserved for migration — not deleted.

### Blazor Client Is the Runtime Authority for System Dark Mode

- **Decision:** `ServerEffectiveDarkMode` in `ResolvedAppearanceDto` is nullable (`bool?`). The Blazor client calls `MudThemeProvider.GetSystemDarkModeAsync()` when `ThemeMode == System` to determine the effective mode at runtime.
- **Why:** The server cannot know the browser's preferred color scheme at request time. Only the client-side `prefers-color-scheme` media query can resolve "System" mode.
- **Impact:** `ResolveEffectiveDarkModeAsync(MudThemeProvider)` on `IAppearanceThemeService` is the single source of truth. SSR bootstrap relies on cookie hints (`theme=dark/light`); InteractiveServer resolves the full truth.

### BFF Proxies All Appearance API Calls; Anonymous Users Get Cookie-Based Fallback

- **Decision:** The Blazor client never calls `api/user/appearance/*` directly. All calls go through `/bff/appearance/*` endpoints in `BffPreferenceEndpoints.cs`. For authenticated users, requests are proxied to the API. For anonymous users, a `ResolvedAppearanceDto` is constructed from cookies.
- **Why:** The BFF pattern keeps auth tokens on the server side and provides a consistent API surface. Cookie-based fallback enables SSR to render correct theming before authentication.
- **Impact:** 9 new BFF endpoints. Anonymous theme/direction/language cookies are set and read by the BFF, mirroring the existing `/bff/theme`, `/bff/language`, `/bff/direction` pattern.

### ThemeQuickSwitcher Is Self-Contained; No Parent Callbacks

- **Decision:** The `ThemeQuickSwitcher` component injects `IAppearanceThemeService` directly and calls `SetThemeModeAsync()` on mode selection. The old `[Parameter] EventCallback OnToggleTheme` on `NavMenu` was removed.
- **Why:** Reactive state management via `IAppearanceThemeService.Changed` event means any component can react to theme changes without parent-child callback chains. The switcher doesn't need to know about `MainLayout` or vice versa.
- **Impact:** `NavMenu` no longer has `OnToggleTheme`. `MainLayout` subscribes to `Changed` and updates `_theme`/`_isDarkMode` in the handler. The floating chrome-less toggle in `MainLayout` still calls `DarkModeToggle()` directly (for setup/onboarding pages that don't show the navbar).

### Client DTOs Duplicate Server DTOs Due to Architecture Boundary

- **Decision:** `Explore.Blazor.Client/Services/Appearance/AppearanceDtos.cs` contains client-side DTOs (`ResolvedAppearanceDto`, `AvailablePresetDto`, `UserAppearanceProfileDto`, `ClientPaletteDto`, etc.) that mirror server-side DTOs in `Explore.Application.DTOs.Appearance`. The Blazor.Client project cannot reference `Explore.Application` (Clean Architecture boundary).
- **Why:** The Blazor.Client project uses direct `HttpClient` calls to BFF endpoints rather than the NSwag-generated `IEventApiClient`. The NSwag client doesn't have the new appearance endpoints yet. When regen happens, some of these client DTOs can potentially be replaced by generated types.
- **Impact:** Class name collisions (`CS0104`) in `BffPreferenceEndpoints.cs` which imports both namespaces. Fixed by using only `Explore.Application.DTOs.Appearance` in the BFF project.

## 2026-04-24 Europe/Brussels - AI-Native Contribution System (Context Contract as Primary Abstraction)

### Contribution Contract becomes the AI operating model

- **Decision:** Every change to the repo — by any agent, any tool, any human — is routed through a machine-readable **Contribution Contract** at `.claude/contract/intents.yaml`. The contract deterministically answers 8 questions per change: intent kind, authoritative rules, must-read files, may-change paths, minimum tests, docs to update, PR checklist, forbidden-without-approval actions. This replaces the prior "read everything or ask" model that a cold-start agent would fall back on.
- **Why:** The CTO review of plan v3 was blunt — "the measure of success is not whether the docs look elegant; it is whether a brand-new agent can land the right PR with the right tests and no architectural damage." Path-scoped rules answer _what rules apply to this file_; the Contribution Contract answers _what context does this work need, before I open a file_. Both are necessary; neither is sufficient alone.
- **Impact:** 10 intents v1 covering the primary change categories (add-get-endpoint, add-write-endpoint, add-hal-link, add-cqrs-handler, add-ef-migration, update-repository-query, blazor-component-affordance, bff-auth-bug, cerbos-policy-change, openapi-contract-change). Validated against `.claude/contract/schema.json` (JSON Schema 2020-12) by `AgentContextIntentManifestTests`. Benchmarked by 8 cold-start scenarios at `.agents/benchmarks/cold-start-tasks.yaml` which map one-to-one onto the intents.
- **Consequence:** Adding a new change type now requires adding an intent; skipping that step means the change is "off-contract" and noted in the PR description. Drift is bounded by CI, not goodwill.

### AGENTS.md is the authoritative AI-agent contract; AGENTS.md is a 3-line redirect stub

- **Decision:** After initially making `AGENTS.md` the authoritative tool-neutral entrypoint and `AGENTS.md` a thin Claude-specific bootloader (Phase 1), the final state (per user request m0102) inverts this: `AGENTS.md` carries the full 14-section AI-agent contract (344 lines including Contribution Contract, Release artifact, Cold-Start Flow, Rule Authority Order, 7 general + 13 non-inferable CRITICAL RULES, Task-Routing Entrypoints, Absolute Fetch Rule, Verification Policy, Blazor UI Workflow pointer, Claude-Specific Operational Rules, Coding & File Standards, Collaboration, Tool-Specific Bootloaders, Enforcement, Shell Behavior Rules Appendix, See Also footer). `AGENTS.md` becomes exactly:

  ```
  # AI Agents

  See [AGENTS.md](AGENTS.md) for AI agent instructions.
  ```

- **Why:** The user preferred a single primary file (`AGENTS.md`) with a pointer for tools that auto-discover `AGENTS.md`, rather than two files that both carry content. This eliminates cross-file duplication while preserving cross-tool compatibility via the one-line redirect.
- **Impact:** All agents still link to `AGENTS.md` in their Mandatory Reads — the link resolves (stub exists), redirects to `AGENTS.md` (which holds the content). `ContextSystemHelpers.RepoRoot` continues to walk up looking for both files and finds both. `AgentContextDuplicationTests` keeps preventing any agent from inlining the now-CLAUDE-owned project context. `.github/copilot-instructions.md` points at `AGENTS.md` and inherits the redirect for free.
- **Consequence:** The "tool-neutral entrypoint" guarantee is now provided by the pointer stub, not by content parity. Every other tool (Codex, Cursor, Gemini, Zed, Aider) that discovers `AGENTS.md` will follow the single link with zero ambiguity.

### Context-system enforcement lives in `Event.Architecture.Tests` (parity with code architecture gates)

- **Decision:** The 4 new CI tests (`AgentContextSchemaTests`, `AgentContextLinkTests`, `AgentContextIntentManifestTests`, `AgentContextDuplicationTests`) are added to the existing `Event.Architecture.Tests` project — not a new one. They use the same TUnit + `[Test] public async Task X()` + `await Assert.That(...).IsEmpty()` pattern as `CleanArchitectureTests`, `CqrsPatternTests`, etc. A shared `ContextSystemHelpers.cs` carries the markdown/YAML/link parsers.
- **Why:** The repo already treats architecture tests as CI gates; the AI-context layer deserves the same seriousness. Colocating keeps maintenance in one place, reuses the TUnit runner, and signals to maintainers that context drift is a real regression, not a "docs nit."
- **Impact:** `.github/workflows/agent-context.yml` runs the whole `Event.Architecture.Tests` project (TUnit doesn't support `--filter`) — the ~3-second marginal cost is negligible. No new NuGet dependency was added: a narrow regex-based markdown reader and a state-machine YAML reader replaced the originally considered Markdig + YamlDotNet.
- **Consequence:** Changes to `AGENTS.md`, `AGENTS.md`, `docs/**`, `.claude/**`, or the context-test files themselves trigger the workflow. Any dead link, missing frontmatter key, invalid intent reference, or agent-file duplication blocks merge.

### Source projects use `Explore.*`; test projects use `Event.*` (mostly)

- **Decision:** All path-scoped rules, intent manifest entries, and context helpers are anchored to this naming split: source = `Explore.API`, `Explore.Application`, `Explore.Domain`, `Explore.Persistence`, `Explore.Blazor`, `Explore.Blazor.Client`, `Explore.Infrastructure`, `Explore.AppHost`, `Explore.Secrets`, `Explore.Diagnostic`, `Explore.ServiceDefaults`; tests = `Event.API.IntegrationTests`, `Event.Application.UnitTests`, `Event.Architecture.Tests`, `Event.Domain.UnitTests`, `Event.Persistence.IntegrationTests`, `Event.Benchmarks`, `Event.MigrationService` (a hosted MigrationService, not a test project despite the `Event.*` prefix), plus the `Explore.*` Blazor/Secrets test projects. Plan v4 drafts incorrectly used `Event.*` for source paths; caught by a subagent during Phase 2 rule-writing and corrected in `intents.yaml` + rule files.
- **Why:** This split is historical and not inferable from code. `docs/GOVERNANCE.md` tree diagrams still reference the old `Event.*` source naming in places — flagged as pre-existing staleness, **not** fixed in this pass (per AGENTS.md no-pre-existing-fixes rule).
- **Impact:** Every `paths_in_scope` glob in `intents.yaml`, every `paths:` glob in `.claude/rules/*.md`, every path reference in `AgentContext*Tests.cs`, and every benchmark scenario must respect this split. A new agent that assumes `Event.Domain` is a source project will get empty results from every grep and be surprised.
- **Consequence:** Future cleanup of `docs/GOVERNANCE.md` should align its tree diagrams with the `Explore.*` source naming. Until then, the Contribution Contract is the authoritative path map — an intent's `paths_in_scope` is the ground truth.

## 2026-04-22 Europe/Brussels - Hierarchical Settings: UI Appearance Architecture

### UiTheme as First-Class Aggregate (Not JSON Settings)
- Decision: UI themes are stored as a first-class `UiTheme` aggregate with a dedicated `ui_themes` table, palette VO, and admin CRUD surface — not as serialized JSON blobs inside `SystemSetting`/`UserPreference` rows. `AppearanceSettingGroup` stores only `DefaultThemeId` (Guid) as a preference and resolves the palette by querying the `UiTheme` aggregate at render time.
- Why: Themes are shared, versioned, governance-cascaded, and admin-managed entities. Storing palette JSON inside preference rows would duplicate data per user, prevent sensible admin CRUD, and make tenant-scoped catalog governance impossible. A first-class aggregate supports catalog CRUD, default-theme promotion, tenant vs platform scoping, and cache-friendly `GetDefaultThemeAsync` lookups.
- Impact: `UiThemeConfiguration` (EF), `UiThemeRepository` with scope-aware queries, `IUiThemeRepository` methods (`ClearDefaultAsync`, `GetOwnedThemesAsync`, `GetAvailableThemesForTenantAsync`, `GetDefaultThemeAsync`, `ThemeKeyExistsAsync`), admin CRUD command/query handlers, and `api/admin/ui-themes` controller. `AppearanceSettingDefinitions.DefaultThemeId` is `SettingType.String` storing the Guid; `AppearanceSettingGroup.DefaultThemeId` is nullable `Guid`.
- Consequence: Per-user preference storage remains a thin pointer (serialized Guid), consistent with the sparse-override model of hierarchical settings. Palette ownership stays with the aggregate; preferences stay with the user. Invalidation rules simplify: catalog CRUD does NOT invalidate preference resolver cache.

### DefaultThemeId as Soft-Pointer with Fallback Chain (Not FK)
- Decision: `UserPreference.Value` for `Appearance.DefaultThemeId` stores a serialized Guid string; there is NO database foreign key from `UserPreference` to `UiTheme`. Clients implement a 4-step fallback chain when resolving: (1) by id → (2) tenant default → (3) platform default → (4) hardcoded built-in palette.
- Why: Themes can be hard-deleted by admins (with the default-theme protection invariant). A strict FK would require cascading preference rewrites on delete or blocking legitimate catalog hygiene. The soft-pointer pattern keeps admin workflows independent of preference sprawl: deleting a theme just makes existing references fall through to the tenant/platform default, which the UI already handles gracefully.
- Impact: `AppearanceThemeService.ResolveActiveThemeAsync` in the Blazor client implements the chain. No EF migration needed for orphan cleanup. No database-level cascade on theme delete.
- Consequence: Client-side fallback responsibility is mandatory — any new consumer of `DefaultThemeId` must implement the chain. Documented in `implementation-report.md` as "Dangling-pointer policy".

### BFF Endpoints Perform Lossless Round-Trip (Not Partial PATCH)
- Decision: `/bff/theme`, `/bff/language`, `/bff/direction` each mutate a single field but always persist the full `UpdateUserAppearancePreferencesDto` to the API. Each endpoint reads current server state first (via `GET api/user/appearance` when authenticated, cookies when anonymous), applies a record `with { X = newValue }` mutation, then PUTs the full record.
- Why: The prior implementation passed only the field being edited and let the server DTO default-initialize the others — silently overwriting user preferences on every theme/language/direction click. The API contract is a full DTO (not JSON PATCH), so the BFF is the right place to reconstruct the complete record.
- Impact: `ReadCurrentPreferencesAsync` / `ReadAuthenticatedAsync` / `ReadCookiePreferences` / `PersistAuthenticatedAsync` helpers added to `BffPreferenceEndpoints.cs`. `UserAppearancePreferencesDto` converted from `class` to `record class` to enable `with { }` without cloning boilerplate.
- Consequence: Cookie mirror is now consistent with API state at all times. No silent preference regressions. Other BFF-mediated partial-update patterns should follow the same read-mutate-write shape.

### Dynamic Client Theme via Post-Auth Rebuild (Not SSR-Gated)
- Decision: `MudThemeProvider` initializes with the built-in palette at `OnInitialized`; post-authentication, `MainLayout.OnAfterRenderAsync` calls `AppearanceThemeService.ResolveActiveThemeAsync()` and rebuilds the `MudTheme` with the user's resolved `UiTheme` palettes, then triggers `StateHasChanged`.
- Why: InteractiveAuto renders don't have `HttpContext`; forcing theme data into the SSR path would couple the Blazor server render to API calls it doesn't need for anonymous users. Post-auth rebuild keeps SSR fast, stays within the render-mode constraints, and tolerates failures gracefully — if the API is unreachable, the built-in palette remains.
- Impact: Two render passes for authenticated users (one built-in, one themed), but only one pass for anonymous users. Blazor server doesn't need any appearance-specific code. `AppearanceThemeService` is the single source of truth for client-side theme resolution.
- Consequence: A minor visual "pop" possible on first authenticated render if the user's theme diverges significantly from the built-in. Deemed acceptable for v1; future optimization could prefetch the theme server-side during the Blazor server render via BFF or hydrate from a signed cookie.

## 2026-04-21 Europe/Brussels - Onboarding Bugfix: Three Interconnected Failures

### Pre-onboarding = SingleTenant (Not MultiTenant)
- Decision: `DeploymentModeProvider` returns `SingleTenant` when `InstanceBootstrapState` is null or incomplete, instead of the previous default of `MultiTenant`.
- Why: On a fresh install, there are no tenants in the database. MultiTenant mode requires tenant resolution (via X-Tenant-Slug, custom domain, or subdomain), but during onboarding none of these exist yet. Serving the default tenant (`PlatformDefaults.DefaultTenantId`) allows all API paths to function during setup. This is the safest closed default.
- Impact: `ApiTenantResolutionMiddleware` falls back to DefaultTenantId instead of returning 404 "Tenant not resolved". Test fixtures that explicitly set `Deployment:Mode=MultiTenant` in config still work because the new Layer 1 (explicit config) wins over Layer 3 (DB).
- Consequence: Added `IConfiguration` dependency to `DeploymentModeProvider` constructor for Layer 1.

### Dynamic JWT Authority via IPostConfigureOptions (Not Static Binding)
- Decision: API JWT bearer authority and JWKS are resolved dynamically at runtime, not statically at startup.
- Why: After onboarding saves Keycloak configuration to the database, the API must immediately validate JWTs signed by the newly-configured Keycloak realm. Static startup binding cannot know the authority URL until it's persisted. The `IPostConfigureOptions<JwtBearerOptions>` pattern allows injecting a `DynamicJwtConfigurationService`-managed `ConfigurationManager<OpenIdConnectConfiguration>` that reads from env vars at startup and swaps to DB-sourced config after onboarding.
- Impact: Three onboarding/config handlers call `IJwtAuthorityRefreshNotifier.ReloadAsync()` post-commit, which swaps the JWT ConfigurationManager atomically. The post-configure callback applies to `JwtBearerDefaults.AuthenticationScheme` only.
- Consequence: New contract `IJwtAuthorityRefreshNotifier` in Application layer. New singleton `DynamicJwtConfigurationService` + `DynamicJwtBearerPostConfigureOptions` in API project. Existing `AuthenticationExtensions.AddApiAuthentication()` no longer sets static Authority/MetadataAddress/ValidIssuer.

### Graceful Token Refresh Failure (Not Silent RejectPrincipal)
- Decision: On `invalid_grant`/`invalid_token` from Keycloak, the BFF signs the user out and redirects HTML navigations to `/login?session=expired&reason={}` rather than silently rejecting the principal.
- Why: `context.RejectPrincipal()` leaves a broken auth cookie that causes an infinite loop on every request. Keycloak returns `invalid_grant` when the realm/client was reconfigured mid-session (during onboarding), the refresh token expired/revoked, or there's clock skew. Clearing the cookie and redirecting gives the user a clear re-authentication path.
- Impact: HTML navigations (GET + Accept: text/html) get redirected. XHR/API requests still get 401 (no redirect). `RefreshResult` struct tracks failure reasons. `RejectAndSignOutAsync` is the new centralized failure handler in `TokenRefreshCookieEvents`.
- Consequence: User experiences a clear redirect to login with explanatory reason code instead of an unresponsive app.

## 2026-04-21 Europe/Brussels - Onboarding Bugfix: Setup Secret Circuit Context Resolution

### JWT Bearer Token as User Identity Source in Blazor Circuit
- Decision: When `IHttpContextAccessor.HttpContext` is null (Blazor InteractiveServer circuit), extract userId from the `Authorization: Bearer <token>` header set by `AccessTokenForwardingHandler` rather than introducing a new circuit-specific service.
- Why: `AccessTokenForwardingHandler` already runs first in the BffClient pipeline and sets the Authorization header from `CircuitAccessTokenService`. The JWT contains `sub`, `ClaimTypes.NameIdentifier`, and `sid` claims — the same fallback chain used by `ClaimHelper.GetUserId`. No new DI registrations or service patterns needed.
- Impact: `SetupSecretForwardingHandler.ExtractUserIdFromAuthorizationHeader()` parses JWT via `JwtSecurityTokenHandler`, extracts userId claims, falls back to `SetupSecretSessionService.GetForUser(userId)`. This is a private static method with no new dependencies.
- Consequence: Any future DelegatingHandler that needs userId in circuit context should use the same pattern — parse the Authorization header set by the upstream handler.

### JS Interop for Onboarding Secret Sync (Not sessionStorage)
- Decision: Replace dead `sessionStorage.getItem("setup-secret")` in `InstanceOnboarding.razor` with `syncSetupSecret(null)` JS interop call via `/js/bff.js`, matching the pattern used by Setup.razor and AuthorizationProviderConfiguration.razor.
- Why: `sessionStorage['setup-secret']` was never written by any code path — the sync was dead code. The BFF `/bff/setup-secret/sync` endpoint exists specifically to transfer the HTTP-only cookie secret into `SetupSecretSessionService` keyed by authenticated userId. JS interop goes through YARP which correctly forwards cookies. `sessionStorage` would require explicit writes in the login flow that don't exist.
- Impact: `OnAfterRenderAsync` now calls `bffModule.InvokeAsync<BffMutationResult>("syncSetupSecret", (string?)null)`. Error handling for 400/410 statuses clears the secret and redirects to `/setup`. Tests updated with `SetupBffJsModule()` mock.
- Consequence: All three onboarding pages (Setup, AuthorizationProviderConfiguration, InstanceOnboarding) now use the same `/js/bff.js` pattern for secret management. No more `sessionStorage` involvement in onboarding flows.

## 2026-04-16 Europe/Brussels - Blazor Clean Code Refactor: CTO Review Binding Decisions

### ServiceResult<T> as Structured Error Contract (Not String-Only)
- Decision: All Blazor service methods return `ServiceResult<T>` with `FailureCategory` enum, `ErrorCode`, `UserMessage`, `DeveloperMessage`, `ValidationErrors`, `IsRetryable`, `HttpStatusCode`.
- Why: String-only failures (`return null` or `return "error"`) cannot drive differentiated UI responses. The UI needs machine-readable error categories to select the correct presentation tier (inline validation vs banner vs snackbar vs re-auth flow vs error state).
- Impact: Every service method in `Explore.Blazor.Client/Services/` migrates from try/catch→null to try/catch→ServiceResult. Static factories (`FromApiException`, `TransientFailure`, `SessionExpired`, etc.) standardize construction. `FailureCategory` enum values: Validation, NotFound, Forbidden, SessionExpired, ProviderUnavailable, ProviderMisconfigured, TransientFailure, Unknown.

### Wave-Based Delivery Over Flat Phase Sequencing
- Decision: 21 phases reorganized into 5 delivery waves: A (Safety+Baseline), B (BFF Hardening), C (Service Contract Reform), D (UI Decomposition), E (Conformance+Polish).
- Why: Flat phase lists create false linearity. Semantic phases (e.g., ServiceResult migration) must ship as a complete wave — a half-finished contract reform is worse than none. Waves group related changes that should be reviewed and merged together.
- Impact: Implementation follows wave boundaries. Each wave has acceptance gates. PRs should not cross wave boundaries without justification.

### DynamicAuthSchemeManager Stop-the-Line Protocol
- Decision: Split into Phase 6A (stabilize + test + document current behavior) and Phase 6B (refactor). No refactoring until full behavioral understanding is documented.
- Why: 539-line state machine with dual locking (SemaphoreSlim + object lock), 8 public methods, runtime scheme mutation. Refactoring without understanding risks breaking auth for all users. Architectural decision required: should scheme mutation happen at runtime or only at startup?
- Impact: Phase 6A in Wave B produces test suite + state diagram. Phase 6B deferred until architectural direction is decided.

### State Classification Before Component Decomposition
- Decision: Complex page components (EventList 2651 lines, EventDetail 1747 lines) must classify all internal state as URL/service/local/computed BEFORE any extraction begins.
- Why: Extracting sub-components without understanding state ownership creates prop-drilling, circular dependencies, or broken reactivity. The page coordinator pattern requires knowing which state is the source of truth.
- Impact: Phase 17A (state classification) placed in Wave A, before Phase 15 (component decomposition) in Wave D.

### Operability as First-Class Engineering Concern
- Decision: New Phase X (Operability Diagnostics) added to Wave B. Includes startup config validation, diagnostics endpoints, feature-unavailable vs misconfigured distinction, self-hoster support.
- Why: The Blazor BFF is the entry point for all users. Config errors at startup (wrong auth provider URL, missing secrets, unreachable YARP target) should fail loud with actionable diagnostics, not silently degrade. Self-hosters need clear feedback when misconfigured.
- Impact: Startup validation checklist (auth, YARP, cookies, HTTPS, secrets). Error state distinction matrix maps ServiceResult categories to UI presentations.

### Change Type Classification on Every Task
- Decision: Every task carries a label: STRUCTURAL, BEHAVIORAL, SECURITY, CONTRACT, or OPERATOR. PRs must not mix SECURITY with STRUCTURAL without explicit justification.
- Why: Code reviewers need to know the change risk profile at a glance. A structural split (rename/move) has different review criteria than a behavioral change (logic alteration) or security fix.
- Impact: Task files and PR descriptions carry change type headers. Review checklists differ by type.

## 2026-04-12 Europe/Brussels - EAV Milestone D1: Projection System Architecture Decisions

### Concurrency Exception Translation in UnitOfWork (not MediatR pipeline)
- Decision: `DbUpdateConcurrencyException` → `ConcurrencyConflictException` translation lives in `EfCoreUnitOfWork`, not a MediatR `IPipelineBehavior`.
- Why: Application layer cannot reference `Microsoft.EntityFrameworkCore` under Clean Architecture dependency rules. The UoW already owns EF-specific semantics. All write paths go through `ExecuteInTransactionAsync`, so translation is centralized.
- Impact: `ConcurrencyConflictException` with `Code=concurrent_update` + entity metadata. `GlobalExceptionHandler` maps to 409 + RFC 7807 extensions.

### Advisory Lock Coordination via Raw ADO (not EF SqlQueryRaw)
- Decision: Projection updaters acquire `pg_try_advisory_xact_lock` and `pg_try_advisory_xact_lock_shared` via raw `DbConnection.CreateCommand()`, not EF's `Database.SqlQueryRaw<bool>`.
- Why: EF Core's `SqlQueryRaw<T>` doesn't reliably handle scalar boolean returns from PostgreSQL functions. Raw ADO is a single-statement call with no column-name ambiguity.
- Impact: Lock key pair = `fnv1a(ProjectionName)` + `fnv1a(tenantId)`. Command enlists on `Database.CurrentTransaction.GetDbTransaction()`.

### Single-Transaction Rebuild for D1 Baseline
- Decision: `RebuildForTenantAsync` uses a single xact-scoped advisory lock and commits once at the end. Per-batch commit + session-scoped lock deferred to D2 Operability.
- Why: Session-scoped advisory locks require holding the same connection across multiple commits, which is complex with EF Core's pooled DbContext pattern. D1 prioritizes correctness over scalability.
- Impact: Status row only becomes visible after full commit (no "live" Rebuilding status during execution). Acceptable for D1.

### Separate ProjectionTestContainerFixture Using EnsureCreatedAsync
- Decision: Projection integration tests use a dedicated `ProjectionTestContainerFixture` with `EnsureCreatedAsync()` instead of the shared `PostgreSqlContainerFixture` with `MigrateAsync()`.
- Why: Concurrent multi-agent development creates model-vs-migration drift (entities in model without migration files). `EnsureCreatedAsync` creates the schema from the current model, bypassing migration-file issues.
- Impact: Projection tests have their own minimal lookup seeding (5 rows). Existing fixture unchanged for other tests.

## 2026-04-12 Europe/Brussels - Event Scheduling Refactor: Architecture Decisions

### EventDay as First-Class Entity (not derived grouping)
- Decision: `EventDay` is a persistent aggregate member, not a `GROUP BY LocalStartDate` projection.
- Why: Five requirements need stable identity + authored state that a derived projection cannot carry: custom day labels, day-specific descriptions/banners, day-specific publishing state, day-level admin UX (reorder/lock/hide/attach media), day-level registration/business rules needing a FK target.
- Impact: `EventDay` entity with all tenant/audit/soft-delete/concurrency interfaces. Sessions link via nullable `EventDayId` FK.

### Two-Layer Same-Room Overlap Enforcement
- Decision: Layer A (async FluentValidation) is explicitly necessary-but-not-sufficient. Layer B (serializable transaction re-check at save time) is mandatory from day one.
- Why: Application-level validation alone cannot protect against concurrent writes, racing requests, or out-of-band mutations. The plan explicitly rejects the check-then-act pattern.
- Impact: `EventSessionRepository.CreateWithRoomOverlapGuardAsync` and `UpdateWithRoomOverlapGuardAsync` wrap the re-check + save in `IsolationLevel.Serializable`.

### IEventScheduleProjectionCalculator as Single Recompute Authority
- Decision: Stateless domain service in `Explore.Domain/Services/Scheduling/` is the sole authority for UTC→local projection writes. Entities expose `Reschedule()` / `ReprojectLocalTimes()` aggregate methods that accept the calculator.
- Why: Prevents scattered UTC→local conversion across handlers, validators, mappers, and seeders. DST-aware logic is shared identically between `EventSession`, `EventAgendaItem`, and `EventDay` backfill.
- Impact: Handlers call aggregate methods only. Architecture tests should enforce that no handler writes `LocalStart*`/`LocalEnd*` directly.

### EventRegistrationIntent Parent Layer
- Decision: Named `EventRegistrationIntent` (not `EventRegistrationGroup`). Parent carries scope + policy snapshot. `EventRegistration` remains the child concrete session entitlement/access row.
- Why: "Intent" precisely describes what the parent preserves — why the user registered. "Group" is too generic and could be confused with org/user groups.
- Impact: `CreateEventRegistrationDto` repurposed to intent-first shape. Handler creates parent + derived children atomically in serializable tx. NSwag client is stale until Phase 6 regen.

### RegistrationPolicyRules — Null Policy = Flexible
- Decision: When `Event.RegistrationPolicyId` is null, `RegistrationPolicyRules.IsScopeAllowed` treats it as `Flexible`, accepting all scopes.
- Why: Events created before the registration-policy field landed must still accept registrations during rollout. The null-means-Flexible convention avoids a migration that force-assigns policies to existing events.
- Impact: Pure domain function, single file (`Explore.Domain/Services/Registration/RegistrationPolicyRules.cs`).

## 2026-03-30 Europe/Brussels - API Testing Enterprise Grade: Technology Stack Decisions

### Database Reset: Respawn v7.0.0
- Decision: Use Respawn v7.0.0 with `DbAdapter.Postgres` for deterministic intertest data cleanup.
- Why: Industry standard for .NET test database reset. Analyzes FK relationships once, then performs fast ordered truncation. Lookup tables preserved via `TablesToIgnore`.
- Impact: Must add `Respawn v7.0.0` to `Directory.Packages.props` and `Event.API.IntegrationTests.csproj`.

### Schema Setup: MigrateAsync() Over EnsureCreated()
- Decision: Use `MigrateAsync()` for all PostgreSQL-backed test fixtures (both API and persistence).
- Why: Exercises the real migration chain, catching migration ordering issues and schema drift.

### Test Parallelism: Parallel From Day One
- Decision: Run PostgreSQL-backed API tests in parallel with per-test Respawn reset for isolation.
- Why: TUnit runs parallel by default. Per-test `ResetAsync()` before seeding gives each test a deterministically clean database.

### Contract Host Database: Keep EF InMemory
- Decision: Keep EF InMemory for the Contract host only. RealRuntime and Stress use PostgreSQL.
- Why: Contract tests verify serialization/ProblemDetails/HAL/headers — provider doesn't matter. Keeps them fast and Docker-independent.

### TUnit Fixture Model: SharedType.Keyed for Host Profiles
- Decision: Use TUnit `SharedType.Keyed` with keys "Contract", "RealRuntime", "Stress" for API test fixtures.
- Why: Allows multiple fixture types to coexist in the same assembly without lifecycle interference.

## 2026-03-29 Europe/Brussels - Customization Sidebar: UX Architecture Decisions

### Sticky RightSidebar Over Overlay Drawer
- Decision: Replaced MudDrawer overlay with a custom content-pushing sticky `RightSidebar` common component (`Components/Common/RightSidebar.razor`).
- Why: Overlay drawers obscure page content, making it impossible to see customization effects in real-time. Sticky sidebar pushes main content left, preserving visibility. Component is reusable for future AI assistant panel.

### EventCard Progressive Disclosure (Icons + Hover)
- Decision: Replace verbose text labels with icon badges (visibility/audience/format) using MudTooltip. CompactGrid uses `+N more` chip with hover reveal for hidden fields.
- Why: Reduces visual clutter while keeping all information accessible. Icons are universally understood; tooltips provide full labels on demand. Hover reveal avoids permanent information overload in compact layouts.

### UserSettingsService Auth Branching (API vs localStorage)
- Decision: Single `UserSettingsService` branches on authentication state — authenticated users persist via API settings endpoints, anonymous users use browser localStorage. SSR-safe via `IJSRuntime` availability check.
- Why: Avoids two separate service implementations. Anonymous users get instant UX without account creation. No anonymous→authenticated migration (D7 decision) keeps implementation simple. localStorage is acceptable for non-critical display preferences.

### Feature-Flag Bypass for Development
- Decision: Hardcode `_showCustomizationButton = true` instead of reading from settings feature-flag infrastructure.
- Why: Feature-flag infrastructure depends on tenant config that isn't reliably available in all dev environments. Must re-enable before production deployment.

## 2026-03-29 Europe/Brussels - EAV Milestone C: EventSession Layer 3 Parity

### Session Templates: Owned Children of Event Templates
- Decision: `EventSessionTemplate` has `EventTemplateId` FK — session blueprints exist only under an event template. Uniqueness is `(EventTemplateId, SessionTemplateKey, Version)`.
- Why: Sessions are child aggregates of events. Template hierarchy mirrors runtime hierarchy. Prevents orphaned session templates.

### Session Instantiation: Mirror Event Pattern Exactly
- Decision: `EventSessionTemplateInstantiationService` uses identical in-memory instantiation + handler persistence pattern as `EventTemplateInstantiationService`. Same two-pass provenance matching algorithm.
- Why: Architectural consistency — same patterns reduce cognitive load and enable shared test strategies. Session-specific differences are minimal (FK target is EventSessionId instead of EventId).

### Session Creation: Optional SessionTemplateId (Guid?)
- Decision: `CreateEventSessionDto.SessionTemplateId` is `Guid?`. Null = no template. Non-null = fetch, guard published+active, instantiate inside transaction.
- Why: Mirrors event creation pattern exactly. Zero breaking changes to existing session creation flow.

### Session Projection: Mirror Event Projection Shape
- Decision: `EventSessionCustomPropertyProjection` uses same column structure as `EventCustomPropertyProjection` (Namespace, Key, PropertyType, value columns, exposure flags, provenance).
- Why: Unified projection shape enables shared discovery/search/filter infrastructure in Milestone D without session-specific query paths.

## 2026-03-29 Europe/Brussels - EAV Milestone B: Event Template + Runtime Baseline

### Template Instantiation: In-Memory Service + Handler Persistence
- Decision: `EventTemplateInstantiationService` creates in-memory entities only. The `CreateEventCommandHandler` persists via repository calls inside a transaction.
- Why: Keeps instantiation logic testable without DbContext. Handler owns the persistence strategy and transaction boundary.

### Provenance Matching: Two-Pass Algorithm
- Decision: Match existing runtime definitions to template definitions first by `SourceTemplateDefinitionId` (exact lineage), then unmatched by normalized `Namespace+Key` (repair/backfill). Track consumed matches to prevent double-matching.
- Why: SourceId is the authoritative provenance link. Namespace+Key fallback only for definitions that lost their lineage (manual creation, data migration).

### Event Creation: Optional TemplateId (Guid?)
- Decision: `CreateEventDto.TemplateId` is `Guid?`. Null = no template, existing flow untouched. Non-null = fetch template, guard published+active, instantiate inside existing transaction.
- Why: Zero breaking changes to existing event creation. Template selection is purely additive. Guard prevents instantiating draft/inactive templates.

### Runtime Definition Edit Flows: Event-Local Only
- Decision: Runtime queries and edit commands operate exclusively on event-local definitions (`GetDefinitionsForEventPaged(eventId)`). No implicit template re-reads during editing.
- Why: Event-local state is the source of truth after instantiation. Template changes require explicit sync (Milestone E).

### Ad-Hoc Definitions: No Provenance
- Decision: Runtime definitions created without a template get `InstantiatedAt = DateTimeOffset.UtcNow` but null provenance fields (SourceTemplateId, SourceTemplateKey, etc.).
- Why: Clean distinction between template-derived and manually-created definitions. Provenance fields are only meaningful for template lineage.

## 2026-03-26 Europe/Brussels - Enterprise Footer Customization: Blazor UI Implementation

### Footer Template Dispatch via Switch (ADR-005)
- Decision: Use `switch` on template key string in `Footer.razor` to dispatch to 4 typed template components (`FooterTemplateStandard3Col`, `FooterTemplateStandard2Col`, `FooterTemplateMinimal`, `FooterTemplateCommunity`).
- Why: 4 fixed templates → compile-time safety, simple to reason about. `DynamicComponent` deferred to Phase 2+ when newsletter/HTML fragment blocks are added.

### Footer Admin: Typed HTTP Client (Not NSwag)
- Decision: Create `IFooterAdminService` + `FooterAdminService` as typed HttpClient service following `ITenantNavigationService` pattern, instead of using NSwag-generated client.
- Why: Footer admin endpoints were not covered by existing NSwag generation. Typed client provides explicit control over models and error handling. Registered via `AddTypedApiClient` with interactive resilience.

### Footer Governance Available in All Deployment Modes
- Decision: Lock toggles shown in both single-tenant and multi-tenant modes. Info alert in single-tenant explains locks have no effect.
- Why: User explicitly requested footer customization for all deployment modes, not just multi-tenant.

### Default Footer Seeded via Runtime Seeder
- Decision: Default link groups (Quick Links: About/Events/Contact + Legal: Terms/Privacy) seeded at runtime via `LookupTableSeeder.SeedDefaultFooterLinkGroupsAsync()` with deterministic GUIDs and `TenantId = null`.
- Why: Follows existing seeding pattern. Avoids EF Core 10 `HasData()` circular FK bug (#36682). Idempotent check prevents re-seeding.

### Community Guidelines Link: Dynamic Runtime Conditional
- Decision: Community guidelines link rendered conditionally in footer templates based on `AllowUserSubmittedEvents || AllowOrganizationSubmittedEvents || AllowGroupSubmittedEvents` — same rule as sidebar in `MainLayout.razor.cs`.
- Why: User explicitly requested same logic as sidebar. Not stored as a DB link since visibility is determined by runtime policy, not admin configuration.

## 2026-03-26 Europe/Brussels - API Enterprise Hardening

### ValidationBehavior: Delete (Option A — Manual Validation)
- Decision: Delete `ValidationBehavior.cs` rather than enabling pipeline validation.
- Why: Per AGENTS.md rule, validators are manually instantiated in handlers. The behavior was never registered and is dead code. Enabling it would require auditing 617 handlers for double validation — unacceptable risk for the benefit. Manual validation gives handlers explicit control over validation timing and error shaping.
- Files removed: `Explore.Application/Behaviors/ValidationBehavior.cs`

### Idempotency Store: Database-backed (PostgreSQL)
- Decision: Store idempotency keys in PostgreSQL via EF Core, not Redis.
- Why: Auditability — idempotency records need to survive Redis flushes, be queryable for debugging, and participate in the same transactional boundary as the command they protect. Redis would require a separate reliability story. 24-hour TTL via `ExpiresAt` column keeps the table bounded.
- Files: `Explore.Domain/IdempotencyRecord.cs`, `Explore.Persistence/Repositories/IdempotencyRepository.cs`, `Explore.API/Middleware/IdempotencyMiddleware.cs`

### URL Versioning: IApplicationModelConvention (No Controller Modifications)
- Decision: Add URL segment versioning (`/api/v0.1/actor`) alongside existing media-type versioning via a `VersionedRouteConvention` that automatically adds versioned route templates to all controllers.
- Why: Modifying 58 controller files to add a second `[Route]` attribute is fragile and creates merge conflicts. The convention approach is zero-touch for controller authors and automatically applies to new controllers.
- Files: `Explore.API/Extensions/ApiVersioningExtensions.cs`

### Swashbuckle: Kept (User Decision)
- Decision: Do NOT remove Swashbuckle. Keep both Swagger UI and Scalar/native OpenAPI.
- Why: User explicitly requested to keep Swashbuckle. Blazor client generation and existing tooling depends on `/swagger/v0.1/swagger.json`.

### SafeMode: One-Way Latch (No Programmatic Deactivation)
- Decision: Changed `SafeMode` from public get/set to private set with `ActivateSafeMode()` method. Once activated, safe mode persists until instance restart.
- Why: Previously, RuntimeAuthorizationProvider toggled SafeMode on/off per-request in a try/finally. This allowed transient oscillation between safe and normal mode when BYO Cerbos was intermittently failing. The latch pattern is more secure — once the PDP is detected as unreachable, deny-all stays until an operator restarts the instance.

## 2026-03-25 Europe/Brussels - CSS Modernization: @layer Architecture + Design Tokens + Wrapper Components

- Decision: Replace monolithic `StyleGlobal.css` with `@layer`-based cascade architecture (7 layer files), 3-tier design token system, MudBlazor wrapper components, and modern CSS features (oklch, clamp, CSS nesting, container queries).
- Why: The existing 660+ line monolithic CSS file mixed reset, tokens, components, utilities, and global MudBlazor overrides without cascade control. MudBlazor v9 removed `MudGlobal` defaults. Global `.mud-*` class overrides violated CSS isolation skill guidance.
- Key decisions:
  1. **@layer ordering** (`reset → base → tokens → mudblazor-overrides → components → utilities`) — later layers win regardless of specificity.
  2. **3-tier tokens** (Primitives → Semantic → Component) — semantic aliases point to `--mud-palette-*` for dark mode compatibility.
  3. **Wrapper components** (`AppButton`, `AppCard`, `AppTextField<T>`, `AppIconButton`, `AppDialogShell`) — composition via `CaptureUnmatchedValues`, not inheritance.
  4. **DialogOptionsFactory** — static presets (`Small`, `Medium`, `Confirmation`, `Editor`) replace inline `new DialogOptions { ... }`.
  5. **oklch** for all color mixing and shadows — perceptually uniform, replaces `rgba` and `color-mix(in srgb, ...)`.
  6. **Fluid typography** with `clamp()` for H1-H5 — eliminates breakpoint-based typography queries.
  7. **Global `.mud-*` exception policy** — documented whitelist in `mudblazor-overrides.css` header, each block requires justification comment.
  8. **CSS nesting** — native `&` for pseudo-classes/modifiers/media queries. BEM element selectors stay flat (no `&__element` concatenation).
  9. **Container queries** in EventList — 5 viewport media queries converted to `@container` queries.
  10. **DefaultBorderRadius** changed from 8px to 12px in `AppearanceThemeService.cs`.
- Files: `Explore.Blazor/wwwroot/css/` (7 layer files), `Explore.Blazor.Client/Components/Common/` (5 wrapper components), `Explore.Blazor.Client/Services/DialogOptionsFactory.cs`.
- Follow-up: Remaining MudButton/Card/TextField/IconButton → wrapper migrations in ~80 files (Tier 2+3).

## 2026-03-22 Europe/Brussels - Hierarchical Settings Preferences: Theme Storage And Runtime Boundary

- Decision: Keep hierarchical settings for precedence, defaults, and approved user overrides, but do not store the theme catalog as JSON in generic settings. Theme catalogs must be modeled as first-class entities with audit and concurrency support.
- Why: The feature needs admin-managed lists, deterministic defaults, fallback when a selected theme is removed, and safe concurrent editing. Those concerns become brittle if theme definitions are hidden inside settings payloads.
- Consequence: The next implementation slice must start with an ADR, then define theme entities/value objects plus reference-based appearance settings such as `appearance.default_theme_id` and `appearance.theme_mode`.

## 2026-03-22 Europe/Brussels - Hierarchical Settings Preferences: Layouts Must Not Be The Theme Engine

- Decision: `Explore.Blazor.Client/Layout/MainLayout.razor.cs` and `Explore.Blazor.Client/Layout/SetupLayout.razor.cs` should become thin consumers of a dedicated runtime theming service instead of owning precedence and palette composition logic.
- Why: The current layout files already duplicate theme-building code. If precedence, bootstrap, and fallback rules are left there, theming behavior will spread across UI lifecycle code and become hard to test.
- Consequence: Before UI work starts, introduce a dedicated service boundary such as `IThemeCompositionService` or `IAppearanceRuntimeService` and define SSR/bootstrap authority order in the ADR.

## 2026-03-16 Europe/Brussels - Event Scheduling Refactor: Registration Architecture

- Decision: Do not redefine the current session-level `EventRegistration` rows as the abstract parent registration concept. Instead, add a new parent intent/group layer above them and keep the child/session rows as the concrete entitlement/access records.
- Why: The existing platform semantics, UI, and capacity logic are already centered on session-level access. Keeping child rows concrete reduces migration pain, preserves understandable attendance/capacity behavior, and still allows event/day/session-selection policy-aware UX.
- Consequence: Future implementation should choose a dedicated parent name such as `EventRegistrationIntent` or `EventRegistrationGroup`, preserve temporary compatibility for session-level contracts, and backfill intent semantics above existing session rows.

## 2026-03-16 Europe/Brussels - Event Scheduling Refactor: Overlap Validation Strategy

- Decision: Enforce “same room + overlapping time = invalid” first in create/update session DTO validators using async FluentValidation with repository-backed checks.
- Why: This gives fail-fast behavior aligned with the repo’s validator-first patterns and avoids overcommitting to a database-only conflict strategy before the new room model is fully established.
- Consequence: The first implementation slice should add the necessary repository/service checks for validator use, then optionally add stronger persistence hardening later if race conditions or scale demand it.

## 2026-03-13 Europe/Brussels - HTTP Resilience Refactor: Tenant/Auth Ordering Decision

- Decision: Keep the current API ordering assumption that authentication scheme selection does not depend on tenant resolution.
- Why: Direct inspection of `Explore.API/Program.cs` showed the policy scheme switches only on the presence of `X-API-Key`; tenant context is not consulted to decide JWT vs API-key auth.
- Consequence: Phase 3 middleware work should focus on forwarded-header trust, request logging placement, and cancellation propagation rather than forcing tenant resolution ahead of authentication.

## 2026-03-13 Europe/Brussels - Forwarded Headers Trust Model

- Decision: Configure forwarded-header trust explicitly in both API and BFF hosts via `ForwardedHeaders:KnownProxies` and `ForwardedHeaders:KnownNetworks`, with development-only trust-all fallback when the config is empty.
- Why: The previous BFF behavior effectively trusted every proxy by clearing trust lists unconditionally, which is not acceptable as the long-term security baseline.
- Implementation anchors:
  - `Explore.API/Program.cs`
  - `Explore.API/appsettings.json`
  - `Explore.Blazor/Extensions/MiddlewareExtensions.cs`
  - `Explore.Blazor/appsettings.json`

## 2026-02-23 18:12 Europe/Brussels - Admin Consolidation Handoff Scope

- Decision: Consolidate admin UX into two panel pages only:
  - `Explore.Blazor.Client/Pages/Admin/Tenant/TenantAdminSettings.razor`
  - `Explore.Blazor.Client/Pages/Admin/Instance/InstanceAdminSettings.razor`
- Why: User explicitly requested eliminating split between `/admin` and separate admin pages, and matching existing settings-style panel navigation.
- Implication: Legacy `/admin` dashboard and standalone lookup/admin pages are target candidates for removal after migration.

## 2026-02-23 18:12 Europe/Brussels - SMTP Configuration Placement

- Decision: Add SMTP configuration under instance admin panel as a dedicated sidebar section, with a test connection action.
- Why: SMTP credentials are platform-level concern requested for platform/instance administrators.
- Implementation anchor points:
  - UI pattern: `Explore.Blazor.Client/Components/Admin/Instance/InstanceStorageSection.razor`
  - API pattern: `Explore.API/Controllers/InstanceOnboardingController.cs` storage settings/test endpoints
  - Setting keys: `Explore.Domain/Constants/GovernanceSettingKeys.cs` (`EmailSmtp*`, `EmailFrom*`)

## 2026-02-23 18:12 Europe/Brussels - Dev Docs Continuity Protocol

- Decision: Before context reset, update every active context/tasks file with a timestamped checkpoint entry, and add deep handoff detail to the currently active track only.
- Why: Ensures broad continuity for all active tracks while preserving high-signal detail where active implementation is ongoing.

## 2026-02-23 18:47 Europe/Brussels - Admin Consolidation Implementation Completed

- Decision: Complete the consolidation by deleting legacy standalone admin pages/routes after embedding equivalent capabilities into panel sections.
- Why: Prevent duplicate administrative entry points and keep one authoritative settings-style admin UX per role.
- Outcome:
  - Tenant administration now hosts organizations + lookup management.
  - Instance administration now hosts SMTP settings + test connection.
  - Navbar admin dropdown routes now point directly to tenant/instance administration pages.

## 2026-02-23 18:47 Europe/Brussels - Verification Baseline for This Delivery

- Decision: Treat successful `dotnet build` + targeted Blazor and Application unit tests as release gate for this session due lack of Razor LSP in environment.
- Why: Ensures functional validation while acknowledging toolchain limitation for `.razor` diagnostics.
- Evidence:
  - Build passed.
  - Blazor client tests passed (522).
  - Application unit tests passed (278).

## 2026-02-27 Europe/Brussels - Blazor Folder Restructure Continuation Baseline

- Decision: Treat `dev/active/blazor-folder-restructure` as implementation-complete with remaining work focused on checklist/doc synchronization and optional full-suite gate validation.
- Why: Core migration, imports, dialog helper refactor, and targeted Blazor test loop are already green; unresolved items are primarily documentation fidelity and broader release assurance.
- Verification anchor:
  - `dotnet build --configuration Release --verbosity quiet` passes (warnings only).
  - `dotnet test --project Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --verbosity quiet` passes (warnings only).

## 2026-02-27 Europe/Brussels - Context Reset Handoff Policy (Active Docs)

- Decision: Append explicit session checkpoint blocks to every `dev/active/*-context.md` and `dev/active/*-tasks.md` file during context-limit handoff.
- Why: Ensures no active track is left without fresh continuity metadata, reducing reset-time archaeology and ambiguity.

## 2026-02-27 Europe/Brussels - Blazor Client Contracts Boundary

- Decision: Standardize on root `Explore.Blazor.Client/Contracts` for interface contracts and keep `Explore.Blazor.Client/Services` as implementation-only.
- Structure adopted:
  - `Contracts/Services/{Lookup,Events,Organizations}`
  - `Contracts/Providers`
  - `Contracts/Interop`
- Why: Supports future non-service abstractions (providers/interop), improves testability, and avoids conflating API proxy interfaces with concrete service implementations.
- Verification:
  - `dotnet build --configuration Release --verbosity quiet` passed after namespace and Razor import updates.
  - `dotnet test --project Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --verbosity quiet` passed (518 tests).

## 2026-03-03 Europe/Brussels - Notification System: Lookup Entity Refactor

- Decision: Replace string-based `Type` and `EntityType` fields on `Notification` with proper FK lookup entities (`NotificationType`, `NotificationEntityType`) following the ApprovalStatus pattern.
- Why: Type safety, referential integrity, eliminates magic strings, enables filtering/reporting by notification type with proper indexes.
- Pattern: `int Id`, `string MasterCode`, `string FullName`, `string? Description` with companion enum in `Explore.Domain/Enums/`. Seeded via `LookupTableSeeder` at runtime (not HasData, due to EF Core 10 bug #36682).
- Enums: `NotificationTypeEnum` (10 values), `NotificationEntityTypeEnum` (6 values).

## 2026-03-04 Europe/Brussels - Notification System: Materialized Fan-Out with Scope Metadata

- Decision: Notifications stay per-human-user (`UserId` is always the recipient). Added `SourceActorId`, `RecipientContextActorId`, and `NotificationScopeId` (FK→ActorType) for multi-scope targeting.
- Why: Enterprise notification systems need org/group scope without sacrificing read-path performance. Fan-out at write time means read queries stay O(1) per user.
- Architecture:
  - `NotificationScopeId` (int, FK→ActorType) — classifies scope: User(1)=Personal, Organization(2), Group(4), System(5)
  - `SourceActorId` (Guid?, FK→Actor) — who/what triggered the notification
  - `RecipientContextActorId` (Guid?, FK→Actor) — which org/group context for UI differentiation
- Rejected alternatives:
  - Option A (Replace UserId with ActorId): Kills hot read path, requires JOIN for every notification query.
  - Option C (NotificationRecipient junction table): Over-engineered for our scale, adds N+1 risk.
- Verification: 474 tests passing (363 app + 79 domain + 32 architecture).

## 2026-03-04 Europe/Brussels - Bots/System Are Senders Not Receivers

- Decision: Bot and System actors should NOT receive notifications. They should consume domain events or message queues for automation. However, they CAN be notification sources (`SourceActorId`).
- Why: Notifications are best-effort, human-oriented (dismissable, soft-deletable). Bots need guaranteed delivery, ordering, retry semantics. Different delivery guarantees → different mechanisms.
- Implication: Fan-out logic should filter by ActorType=User when distributing org/group notifications to members.

## 2026-03-04 Europe/Brussels - Reuse ActorType as Notification Scope

- Decision: Instead of creating a new `NotificationScope` lookup entity, reuse the existing `ActorType` entity as the scope classifier for notifications.
- Why: ActorType already has the exact values needed (User=1, Organization=2, Group=4, System=5). Creating a duplicate lookup adds no value and introduces synchronization burden.
- Trade-off: Semantic coupling between actor classification and notification scoping, but the domain concepts are genuinely aligned.

## 2026-04-18 Europe/Brussels - Blazor Clean Code Refactor v3 + Wave 0 Implementation

### Wave 0: Blocking Pre-Flight Hotfixes Required
- Decision: 6 stop-the-line defects must merge before any Wave A work: singleton state leakage, async void crash, .Result deadlocks, missing Cache-Control, missing YARP timeout.
- Why: Two singleton services hold per-user/per-circuit state (SetupSecretSessionService, IDynamicAuthSchemeManager). AnalyticsInitializer.razor had async void that could crash Blazor Server. .Result could deadlock Blazor Server. Auth endpoints lacked Cache-Control: no-store. YARP had no request timeout.
- Impact: All 6 hotfixes implemented and verified. Build green (0 errors). 1957 tests pass, 28 pre-existing failures unchanged. Zero new failures.

### Render Mode: InteractiveServer Default, Cohort Migration
- Decision: All 32 pages currently hardcode `@rendermode InteractiveServer`. Project has dynamic configurable render mode. Default should be InteractiveServer app-wide. Phase A0 will use cohort-based migration for pages eligible for InteractiveAuto.
- Why: InteractiveAuto adds WASM download latency for interactive pages but enables faster post-load interactions. Public-facing pages benefit most. Admin/setup pages should stay Server-only.
- Impact: Phase A0 will categorize pages into 4 cohorts (Static SSR, InteractiveAuto public, InteractiveAuto user, InteractiveServer admin).

### Static Field Fix: SetupSecretSessionService
- Decision: Removed `static` keyword from SetupSecretSessionService `_store` and `CleanupExpiredEntries()`. Registration remains Singleton (not changed to Scoped).
- Why: The `static` keyword was redundant on a Singleton — there's only one instance anyway. Making it Scoped would require auditing all consumers for scoped resolution. The `static` keyword on a Singleton field is misleading (suggests legitimate cross-instance sharing) rather than accidental.
- Impact: CircuitAccessTokenService `_tokenStore` LEFT as static because `GetTokenForUser()` static method requires static store for cross-circuit token resolution in AccessTokenForwardingHandler. This is intentional architectural debt documented for Wave B Phase 3.

### Blazor Arch Tests: 13 Tests Planned
- Decision: 13 architecture tests total: 2 existing (IEventApiClient injection, ITranslationService placement) + 11 new. Uses file-scanning approach (no project reference to Blazor assemblies).
- Impact: Tests cover Console.WriteLine, [Inject] interface-only, DialogOptionsFactory, NavigationManager in shared components, IJSRuntime in services, ISnackbar in data services, singleton mutable state, async void, .Result/.Wait(), IConfiguration direct injection, service locator, model classes in interface files. Known exceptions tracked for pre-existing violations being fixed in later phases.

## 2026-04-20 Europe/Brussels - Event Scheduling Refactor Session 3: Blazor UI + Inline Scheduling

### CreateEvent Inline Scheduling: API-First Creation Order
- Decision: CreateEvent sends Days/Rooms/AgendaItems as nested collections in `CreateEventDto`. The `CreateEventCommandHandler` creates the event first, gets the EventId, then cascades child records in order (Days → Rooms → AgendaItems). This mirrors the existing session + template instantiation pattern.
- Why: The UI previously could not schedule days/rooms/agenda at creation time because the EventId didn't exist yet. By moving the creation-order logic to the API handler within the existing transaction, the UI collects all data locally and sends it in one POST request.
- Impact: `CreateEventDto` extended with 3 optional nested collections (`InlineEventDayDto`, `InlineLocationRoomDto`, `InlineEventAgendaItemDto`). Handler's `CreateInlineSchedulingAsync` method auto-links EventDayId from created days. EditEvent continues using separate service calls (event already has an ID).

### Client-Side DTO Mirrors for HAL Deserialization
- Decision: Manually create client-side DTO classes in `SchedulingDtos.cs` rather than relying on NSwag-generated types.
- Why: NSwag generates HAL wrapper types (`HalResourceOfEventDayDto`) as empty shells with only `[JsonExtensionData] AdditionalProperties`. The embedded items come as `ICollection<object>`. Client DTOs with `[JsonPropertyName]` attributes enable JSON round-trip deserialization via `HalResourceExtensions.DeserializeItems<T>()`.
- Impact: 6 new DTO classes (`EventDayListDto`, `EventDayDto`, `EventAgendaItemListDto`, `EventAgendaItemDto`, `LocationRoomListDto`, `LocationRoomDto`). Using `string.Empty` defaults instead of `required` keyword to avoid CS9035 with the `?? new T()` fallback pattern.

### RegistrationPolicyHelper: Client-Side Mirror of Domain Rules
- Decision: Duplicate `RegistrationPolicyRules` logic in the Blazor client project as `RegistrationPolicyHelper` rather than sharing the Domain assembly.
- Why: Explore.Blazor.Client cannot reference Explore.Domain (WASM payload bloat, Clean Architecture rules). The policy rules are simple static mappings (6 policies → 3 scopes) that are stable and unlikely to diverge.
- Impact: `RegistrationPolicyHelper.cs` in `Helpers/` namespace. Constants for scope/policy IDs match the Domain enum values. 17 bUnit tests verify all policy→scope mappings.
