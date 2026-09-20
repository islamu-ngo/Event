<!-- ABOUTME: I-VSD consultancy report on account deletion strategy for ISLAMU Event across self-hosted and multi-app ecosystems. -->
<!-- ABOUTME: Covers Keycloak identity separation, AT Protocol PDS delegation, configurable deletion scope via Instance Settings, and re-login policy. -->

# I-VSD Consultancy Report: Account Deletion in ISLAMU Event

Last Updated: 2026-06-04

## Scope

This report reviews the account deletion strategy for ISLAMU Event, a self-hostable multi-tenant event management platform that supports multiple authentication providers (Keycloak OIDC, AT Protocol, Google SSO). It addresses:

- Whether external identity accounts should be deleted when a user deletes their Event data
- How the deletion dialog should adapt to the enabled authentication providers for a given tenancy
- AT Protocol PDS delegation — when account deletion is not ISLAMU Event's responsibility
- Configurable deletion scope via Instance Settings (app-only vs. full-account options)
- Re-login after app-level deletion (ghost record / tombstone problem)
- Self-hosted instance operator responsibilities vs. ISLAMU Organization's multi-app ecosystem

**Exclusions**: This report does not cover GDPR legal compliance certification, Keycloak/PDS configuration details, or the implementation of a cross-app ISLAMU Account Service (only its interface contract).

## Claim Boundary

This report is I-VSD design reasoning and traceability, not a fatwa, Sharia certification, product certification, or empirical proof of ethical outcomes. Religious-legal questions about data retention obligations are escalated to qualified scholarly and legal authority.

## Architecture Context

### Authentication Provider Landscape

ISLAMU Event supports three authentication providers, configurable at the **Instance** scope with lock flags for tenant-level override control:

| Provider | Configuration | Identity Ownership |
|---|---|---|
| **Keycloak OIDC** | `PrimaryProviderId` (Keycloak), `KeycloakAuthority`, `KeycloakClientId` | Keycloak realm owner (could be ISLAMU Org or a self-hosting ERP org) |
| **AT Protocol** | `AtprotoLoginEnabled`, `AtprotoPublicUrl` | PDS provider (could be ISLAMU's self-hosted PDS or any external PDS) |
| **Google SSO** | `GoogleSsoEnabled`, `GoogleClientId` | Google (external, no deletion authority) |

### Deployment Models

1. **ISLAMU Organization (first-party)**: Multi-app ecosystem (Event + future Chat, Learning, etc.) sharing a central Keycloak realm and a self-hosted PDS. Users have cross-app identities.
2. **Self-hosted by ERP organization**: An organization deploys ISLAMU Event as one of many apps in their own ecosystem. They connect it to their own Keycloak realm (which has many client applications beyond Event) and/or their own PDS.
3. **Standalone self-hosted**: An organization deploys only ISLAMU Event with its own dedicated Keycloak realm.

### Settings Cascade

The platform uses a hierarchical settings cascade: **Instance → Tenant → Organization → Group → User**. Instance-level settings (including auth provider configuration and deletion policy) can be locked to prevent tenant-level overrides.

## Findings

| # | Finding | Principle | Domain | Severity |
|---|---|---|---|---|
| 1 | **Keycloak account should NOT be deleted by a single app** — in the ISLAMU Organization model, the Keycloak account is the user's cross-ecosystem identity. In the ERP self-hosted model, the Keycloak realm belongs to the ERP org and has many other client apps. In both cases, ISLAMU Event has no authority to delete the Keycloak account. | Rights of People, Non-Harm | Technical, Design, Governance | High |
| 2 | **Email IS PII, but it lives in the identity provider, not in the app** — the app already hard-deletes `UserPii.Email`. The identity provider (Keycloak or PDS) retains the login credential. This is correct separation. | Rights of People, Avoiding Spying | Technical | Concern → Resolved by architecture |
| 3 | **Deletion dialog must adapt to enabled auth providers** — the dialog options depend on which auth providers are enabled for the user's tenancy and whether the instance operator has enabled full-account deletion. A standalone self-hosted instance with its own Keycloak may want to offer both options; an ERP-connected instance should only offer app-level deletion. | Truthfulness, Justice, Rights of People | Design | High |
| 4 | **AT Protocol account deletion is NOT ISLAMU Event's responsibility** — when a user authenticates via AT Protocol, their identity lives on their PDS. The deletion dialog should inform the user that they must contact their PDS provider (or read self-hosting documentation) to delete their AT Protocol identity. | Justice, Trust, Truthfulness | Design, Governance | High |
| 5 | **ISLAMU Organization's self-hosted PDS enables full account deletion** — when ISLAMU operates its own PDS for users, full account deletion (app data + PDS identity) is technically possible because ISLAMU controls both layers. This should be offered as an option when the tenancy uses ISLAMU's PDS. | Rights of People, Trust | Technical, Strategic | Medium |
| 6 | **Re-login after app-level deletion creates a ghost problem** — if the identity provider account is kept and user re-logs into Event, the system finds `AuthProviderId` on a soft-deleted User record. A clear strategy is needed. | Trust, Promise-Keeping | Technical, Operational | High |
| 7 | **Self-hosted instance operators face the same question at their level** — an ERP org that self-hosts ISLAMU Event and connects it to their own Keycloak realm (with many other client apps) must scope deletion to their Event instance only. Their Keycloak account serves many apps, not just Event. | Justice, Trust | Strategic, Governance | High |
| 8 | **`AuthProviderId` on soft-deleted User record is re-identifiable PII** — even after `UserPii` is hard-deleted, the `AuthProviderId` (Keycloak `sub` claim or AT Protocol DID) on the skeletal User record can re-identify the person if the identity provider is queried. | Avoiding Spying, Rights of People | Technical | High |
| 9 | **Deletion scope should be configurable at Instance level** — instance operators should control via Instance Settings whether the UI shows only app-level deletion or both app-level and full-account deletion. This respects the operator's knowledge of their own identity architecture. | Trust, Excellence / Ihsan | Governance, Design | Medium |

## Recommendations

### 1. Configurable Deletion Scope via Instance Settings (Governance + Design Domain)

Add an Instance-level setting to control the deletion dialog behavior:

```
AccountDeletion:Mode = "AppOnly" | "AppAndFullAccount"
```

- **`AppOnly`** (default): The dialog only offers "Delete my Event data." This is the safe default for ERP-connected instances and instances using external identity providers.
- **`AppAndFullAccount`**: The dialog offers both "Delete my Event data" and "Delete my full account." Only appropriate when the instance operator controls the identity provider (e.g., ISLAMU Organization with its own Keycloak realm and PDS).

**Basis**: Trust (operator knows their identity architecture), Justice (don't offer what you can't deliver), Truthfulness (dialog only shows options that are actually actionable).

**Implementation**: Add to the hierarchical settings cascade at Instance scope. Can be locked to prevent tenant override. Resolved at runtime alongside `AuthProviderConfigurationDto`.

### 2. Auth-Provider-Aware Deletion Dialog (Design Domain)

The deletion dialog should dynamically adapt based on the user's authentication provider and the instance's deletion mode:

#### Scenario A: Keycloak (ISLAMU Organization) + `AppAndFullAccount`

```
┌─────────────────────────────────────────────┐
│  Delete Your Data                           │
│                                             │
│  ● Delete my Event data only (recommended)  │
│    Removes your profile, RSVPs, and         │
│    personal data from ISLAMU Event.          │
│    Your ISLAMU Account stays active for      │
│    other ISLAMU services.                    │
│                                             │
│  ○ Delete my entire ISLAMU Account          │
│    Removes your account from ALL ISLAMU      │
│    services. This cannot be undone.          │
│    (Requires separate confirmation flow)     │
│                                             │
│  Type DELETE to confirm: [________]         │
└─────────────────────────────────────────────┘
```

#### Scenario B: Keycloak (ERP self-hosted) or `AppOnly` mode

```
┌─────────────────────────────────────────────┐
│  Delete Your Event Data                     │
│                                             │
│  This removes your profile, RSVPs, and      │
│  personal data from this ISLAMU Event       │
│  instance. Your login account remains       │
│  active for other applications in your      │
│  organization.                              │
│                                             │
│  Type DELETE to confirm: [________]         │
└─────────────────────────────────────────────┘
```

#### Scenario C: AT Protocol (external PDS)

```
┌─────────────────────────────────────────────┐
│  Delete Your Event Data                     │
│                                             │
│  This removes your profile, RSVPs, and      │
│  personal data from ISLAMU Event.           │
│                                             │
│  ⚠ Your AT Protocol identity is managed     │
│  by your PDS provider. To delete your       │
│  AT Protocol account, contact your PDS      │
│  provider or refer to their documentation   │
│  for account deletion.                      │
│                                             │
│  Type DELETE to confirm: [________]         │
└─────────────────────────────────────────────┘
```

#### Scenario D: AT Protocol (ISLAMU's self-hosted PDS) + `AppAndFullAccount`

```
┌─────────────────────────────────────────────┐
│  Delete Your Data                           │
│                                             │
│  ● Delete my Event data only (recommended)  │
│    Removes your profile, RSVPs, and         │
│    personal data from ISLAMU Event.          │
│    Your AT Protocol identity stays active.   │
│                                             │
│  ○ Delete my Event data and AT Protocol     │
│    identity                                 │
│    Removes your Event data and deactivates   │
│    your AT Protocol identity on ISLAMU's     │
│    PDS. This cannot be undone.               │
│                                             │
│  Type DELETE to confirm: [________]         │
└─────────────────────────────────────────────┘
```

**Basis**: Truthfulness (clear about what each option does), Rights of People (user controls scope), Non-Harm (prevents accidental cross-app data loss), Avoiding Gharar (no hidden consequences).

### 3. Clear `AuthProviderId` on Soft-Deleted Record (Technical Domain)

When user chooses "Delete Event data only", the current `DeleteUserCommandHandler` should additionally:

- Set `User.AuthProviderId = null`
- Set `User.AuthProvider = null`

This ensures the soft-deleted skeletal User record has **no link back to any identity provider**, making it truly anonymous and non-re-identifiable — regardless of whether the identity was Keycloak, AT Protocol, or Google.

**Basis**: Avoiding Spying / Tajassus (prevent unjustified re-identification), Rights of People (meaningful erasure), Non-Harm (prevent data leakage through skeletal records).

### 4. Tombstone Table for Deleted Identities (Technical + Operational Domain)

Create a `DeletedUserIdentities` table:

```csharp
public class DeletedUserIdentity
{
    public Guid Id { get; set; }
    public string HashedProviderId { get; set; }  // Hashed AuthProviderId (Keycloak sub or AT Proto DID)
    public string AuthProvider { get; set; }       // e.g., "Keycloak", "AtProto", "Google"
    public string AppName { get; set; }            // e.g., "ISLAMU Event"
    public DateTime DeletedAt { get; set; }
}
```

**Purpose**: When a user re-logs into Event after deletion, the system checks this tombstone. If the hashed provider ID matches a tombstoned entry, a **brand new User record** is created with a new `Guid` ID. No old data, events, RSVPs, or actor associations are restored.

**Note**: The tombstone stores a **hash** of the provider ID, not the plaintext value, to prevent re-identification even from the tombstone table itself.

**Basis**: Promise-Keeping (deletion must be irreversible from user's perspective), Trust (user trusts that "delete" means delete), Rights of People (right to erasure is meaningful only if it's permanent).

### 5. Re-Login After Deletion = Fresh Start (Operational Domain)

When a user with a tombstoned identity logs back into Event:

- **Create a brand new `User` record** with a new `Guid` ID
- **Do NOT restore** any old data, events, RSVPs, or actor associations
- The old soft-deleted record stays as anonymous analytics data only
- The new record is a clean slate — as if a new person signed up

**Basis**: Promise-Keeping (you promised deletion, so the old data must not resurface), Trust (user trusts that "delete" means delete), Rights of People (right to erasure is meaningful only if it's irreversible from the user's perspective).

### 6. Full Account Deletion = Separate Platform Flow (Strategic + Governance Domain)

When user chooses "Delete my entire account" (only available in `AppAndFullAccount` mode):

- For **ISLAMU Organization Keycloak**: Call the ISLAMU Account Service API, which orchestrates deletion across all ISLAMU apps + Keycloak
- For **ISLAMU Organization PDS**: Additionally deactivate the user's AT Protocol identity on ISLAMU's PDS
- Each app gets a deletion notification/event to run its own `DeleteUserCommand`
- After all apps confirm, the identity provider account is deleted/deactivated
- This is a **platform-level concern**, not an app-level concern

**Basis**: Justice (other apps shouldn't be silently destroyed), Trust (each provider has stewardship duties over their own data), Governance (cross-app deletion needs proper orchestration and auditability).

### 7. Self-Hosted Instance Operator Documentation (Strategic Domain)

For organizations that self-host ISLAMU Event:

- Each operator's deletion only affects **their instance**
- If connected to a shared Keycloak realm (ERP model), the operator has **no authority** to touch the Keycloak account
- If connected to an external PDS, the operator has **no authority** to touch the AT Protocol identity
- The `AccountDeletion:Mode` setting should default to `AppOnly` for self-hosted deployments
- Document clearly in operator-facing deployment docs that full-account deletion is only available when the operator controls the identity provider

**Basis**: Justice (fair treatment across apps sharing the identity provider), Trust (clear stewardship boundaries), Excellence / Ihsan (build beyond minimum for ecosystem health).

### 8. AT Protocol PDS Delegation Notice (Design + Governance Domain)

When a user authenticated via AT Protocol and the instance is in `AppOnly` mode (or the PDS is external):

- The deletion dialog must include a **clear notice** that AT Protocol identity deletion is outside ISLAMU Event's control
- Provide actionable guidance: "Contact your PDS provider" or "If you self-host your PDS, refer to your PDS documentation for account deletion"
- For ISLAMU's self-hosted PDS in `AppAndFullAccount` mode, full deletion is possible because ISLAMU controls the PDS

**Basis**: Truthfulness (don't imply you can delete what you can't), Justice (don't overstep authority over external identity systems), Trust (user needs to know where their identity actually lives).

## Common Overlooked Failures And Outcomes

**Feature type**: Account deletion in multi-provider, self-hostable platform with multiple auth providers

**Common overlooked failures**:
- Offering "full account deletion" in the UI when the instance doesn't control the identity provider (broken promise)
- Not adapting the deletion dialog to the user's actual auth provider (Keycloak vs. AT Proto vs. Google)
- Not clearing `AuthProviderId` on soft-deleted records, allowing re-identification via any identity provider
- Re-login silently restoring old data or linking to the soft-deleted record
- No tombstone mechanism, causing duplicate User records for the same identity
- AT Protocol users not being told their identity lives on their PDS, not in ISLAMU Event
- Self-hosted operators not knowing they should set `AccountDeletion:Mode` to `AppOnly`
- ERP-connected instances accidentally offering full-account deletion for a Keycloak realm they don't own
- Google SSO users being offered "full account deletion" (impossible — Google controls the identity)

**Possible bad outcomes**:
- User deletes Event account, loses access to other apps sharing the same Keycloak realm unexpectedly
- AT Protocol user believes their PDS identity was deleted when it wasn't (false sense of erasure)
- User re-registers and sees fragments of old data (broken promise of deletion)
- GDPR/privacy audit finds `AuthProviderId` on soft-deleted records = re-identifiable PII
- Self-hosted operators expose deletion options they can't fulfill (trust violation)
- Multiple soft-deleted records for one identity create analytics confusion

**Positive outcomes if implemented responsibly**:
- Users trust the deletion process because it's transparent about scope and adapted to their auth provider
- Clean separation between identity layer (Keycloak/PDS/Google) and app layer (Event)
- Instance operators have clear control over what deletion options their users see
- AT Protocol users get honest guidance about where their identity lives
- Tombstone mechanism makes deletion cryptographically verifiable
- Self-hosted operators have a reference implementation and clear documentation
- GDPR Article 17 (Right to Erasure) is meaningfully supported at both layers

**Provider questions before implementation**:
1. Who owns the Keycloak instance for each deployment? If ISLAMU operates it centrally, full account deletion is a platform API. If an ERP org owns it, Event must not touch it.
2. Is there an ISLAMU Account Service that orchestrates cross-app deletion, or does each app need to coordinate independently?
3. What's the legal basis for retaining the soft-deleted analytics record? Is this disclosed in the privacy policy?
4. Should the tombstone table (`DeletedUserIdentities`) have a retention period? After N years, should even the tombstone be purged?
5. For AT Protocol: what API does ISLAMU's self-hosted PDS expose for account deactivation/deletion?
6. Should the `AccountDeletion:Mode` setting be lockable at Instance scope to prevent tenant overrides?

## Stakeholders

| Stakeholder | Impact |
|---|---|
| **End user** | Controls scope of deletion; expects deletion to be irreversible; may use multiple apps via the same identity provider |
| **ISLAMU platform operator** | Owns Keycloak realm and PDS; responsible for cross-app deletion orchestration; controls `AccountDeletion:Mode` |
| **Self-hosted instance operator (ERP org)** | Connects Event to their own Keycloak realm with many client apps; must scope deletion to Event only; should use `AppOnly` mode |
| **AT Protocol PDS provider** | Owns the user's AT Protocol identity; ISLAMU Event has no authority over external PDS accounts |
| **Other apps sharing the identity provider** | Affected if identity is deleted without their knowledge (Keycloak realm or PDS) |
| **Analytics/research team** | Retains anonymized skeletal records; must not be able to re-identify via any auth provider |
| **Legal/compliance** | Must validate that deletion flow satisfies GDPR Article 17 and local privacy laws across all auth provider types |

## I-VSD Principles And Domains

| Principle | Relevance |
|---|---|
| **Trust / Amanah** | Stewardship of cross-app and cross-provider identity; deletion must be irreversible from user's perspective |
| **Truthfulness / Sidq** | Dialog must clearly state what each deletion option does, adapted to the user's auth provider; AT Proto users must be told their identity lives on their PDS |
| **Justice / Adl** | Other apps sharing the identity provider shouldn't be silently affected; self-hosted operators must not overstep authority |
| **Non-Harm / La Darar** | Prevent accidental cross-app data loss; prevent re-identification through AuthProviderId |
| **Rights of People** | User controls scope of deletion; right to erasure is meaningful and irreversible |
| **Avoiding Gharar** | No hidden consequences — user knows exactly what happens, including what ISN'T deleted |
| **Promise-Keeping** | "Delete" must mean delete — old data must not resurface on re-login |
| **Avoiding Spying / Tajassus** | Soft-deleted records must be truly de-identified (clear AuthProviderId for all providers) |
| **Excellence / Ihsan** | Build beyond minimum — configurable deletion mode, provider-aware dialog, tombstone mechanism, operator documentation |

| Domain | Key concern |
|---|---|
| **Strategic** | Platform-level vs. app-level deletion authority; self-hosted ecosystem health; AT Protocol federation implications |
| **Design** | Provider-aware dialog with clear explanations; configurable deletion scope; AT Proto delegation notice |
| **Technical** | Tombstone table, AuthProviderId clearing, fresh User on re-login, Instance Settings for deletion mode |
| **Operational** | Cross-app deletion orchestration, operator documentation, re-login policy, PDS deactivation API |
| **Governance** | Who has authority to delete at each layer; Instance Settings control; audit trail for deletion events |
| **Evaluation** | Deletion logs, tombstone integrity checks, re-login audit, provider-aware deletion metrics |

## Validation Gaps

- No evidence of a privacy policy disclosure about retaining anonymized analytics records after deletion
- No evidence of a tombstone mechanism or re-login policy in the current codebase
- No evidence of an ISLAMU Account Service API for cross-app deletion orchestration
- No evidence of self-hosted operator documentation addressing deletion scope
- `AuthProviderId` is currently NOT cleared on soft-deleted User records (verified in `DeleteUserCommandHandler.cs`)
- No `AccountDeletion:Mode` Instance Setting exists yet
- AT Protocol authentication handler is a stub (not yet implemented) — PDS deletion delegation is a future concern
- No PDS account deactivation API specification exists for ISLAMU's self-hosted PDS

## Escalation Needed

- **GDPR Article 17 interpretation**: Whether retaining a soft-deleted analytics record with `AuthProviderId` cleared satisfies "right to erasure" — consult legal counsel for your jurisdiction.
- **Keycloak deletion authority**: Whether a single app has the technical and legal authority to delete a Keycloak account — this is a platform governance decision that differs between ISLAMU Organization and self-hosted deployments.
- **AT Protocol identity deletion**: Whether deactivating a DID on ISLAMU's PDS constitutes "deletion" under AT Protocol specification, or whether the DID must be permanently removed.
- **Tombstone retention period**: Whether the `DeletedUserIdentities` table should have its own retention/expiration policy — consult data protection officer.
- **Google SSO**: Google identity deletion is outside any app's control — confirm that `AppOnly` is the only valid mode for Google-authenticated users.

## Evidence Reviewed

- `Explore.Application/Features/Users/Handlers/Commands/DeleteUserCommandHandler.cs` — current deletion handler implementation
- `Explore.Domain/User.cs` — User entity with `AuthProviderId` and `AuthProvider` fields
- `Explore.Domain/UserPii.cs` — PII extension table (Email, FirstName, LastName)
- `Explore.Domain/ActorPii.cs` — Actor PII (DisplayName, Did, Handle, ProfilePictureUri)
- `Explore.API/Controllers/UserController.cs` — HTTP DELETE endpoint
- `Explore.Blazor.Client/Services/UserService.cs` — BFF client service
- `Event.Application.UnitTests/Features/Users/Commands/DeleteUserCommandHandlerTests.cs` — existing unit tests
- `Explore.Application/DTOs/Onboarding/AuthProviderConfigurationDto.cs` — auth provider configuration (Keycloak, ATProto, Google SSO with lock flags)
- `Explore.Blazor/Authentication/AtprotoAuthenticationHandler.cs` — AT Protocol auth stub handler (FishyFlip OAuth planned)
- `Explore.Blazor/Models/AuthProviderConfigurationResponse.cs` — BFF auth provider model
- `Explore.Domain/Settings/SettingScope.cs` — hierarchical settings cascade (Instance → Tenant → Organization → Group → User)
- `Explore.Domain/AppSetting.cs` — encrypted operational configuration settings
- Keycloak integration evidence: `Explore.ServiceDefaults/HealthChecks/OidcDiscoveryHealthCheck.cs`, `Explore.Infrastructure.Tests/Infrastructure/KeycloakBootstrapServiceTests.cs`
- AT Protocol / PDS infrastructure: `Explore.Infrastructure/Services/Federation/PdsService.cs`, `Explore.API/BackgroundServices/PdsSyncWorker.cs`, `Explore.Domain/Federation/PdsSyncOutbox.cs`

## Missing Evidence

- Privacy policy / terms of service text (not found in repository)
- ISLAMU Account Service API specification (does not exist yet)
- Self-hosted operator deployment documentation
- Data retention schedule for analytics records
- Keycloak realm configuration (shared vs. per-operator)
- AT Protocol PDS account deactivation API specification
- `AccountDeletion:Mode` Instance Setting definition (does not exist yet)
- GDPR Article 17 legal analysis for this specific multi-provider architecture
- Google SSO deletion constraints confirmation

## Context Inventory

- **Repository/workspace docs**: AGENTS.md, docs/QUICK_REFERENCE.md, docs/GOVERNANCE.md, docs/OPERATIONS.md
- **Code/config/tests**: DeleteUserCommandHandler, User entity, UserPii, ActorPii, UserController, UserService, AuthProviderConfigurationDto, AtprotoAuthenticationHandler, PdsService, SettingScope, AppSetting, unit tests
- **User-provided context**: Clarification that "other providers" means self-hosted instances (not tenancies), ERP organizations connecting Event to their own Keycloak realm with multiple client apps, AT Protocol PDS delegation, ISLAMU Organization's self-hosted PDS, configurable deletion scope via Instance Settings
