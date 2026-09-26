# I-VSD Consultancy: Pre-Release Decisions to Prevent Avoidable Breaking Changes

Last Updated: 2026-09-25

## Review Metadata

- Mode: standalone
- Subject: ISLAMU Event pre-v0.1 durable contracts and future optional Asset/Identity integration
- Workstream: none
- Report kind: consultancy-report
- Report status: current
- Disposition: advisory
- Evidence cutoff: 2026-09-25
- Reviewed input: `develop` at `c0b7bb362158f2f4535c6c4aefea3ac5fc839c36` plus the bounded working-tree observations identified below; concurrent unrelated changes were present
- Supersedes: none; complements the launch-readiness consultancy rather than replacing it

## Scope

The objective is **to choose the best foundations while changing them is still inexpensive**, not to preserve the current implementation or prohibit future breaking changes. The founder explicitly permits clean pre-release changes without compatibility baggage.

This report identifies decisions that become expensive once users, organizations, integrations and operators depend on their meaning. It distinguishes:

- **Decide and correct now:** present contracts that could force avoidable data, authority or client changes later.
- **Confirm and protect now:** sound existing choices worth making deliberate and testing.
- **Implement later:** future service adapters, richer features and operational mechanisms that do not need to ship in v0.1.

Two future products are explicit inputs:

1. **ISLAMU Asset:** an optional self-hosted API with advanced file/asset modeling and access APIs, including presigned URLs. When selected, it is intended to handle all Event-managed files instead of Event directly using S3 or a filesystem. Asset can itself use those backends.
2. **ISLAMU Identity:** an optional self-hosted canonical identity graph and profile/consent/provisioning control plane. Keycloak, local credential services and ATProto account authorities retain their respective credential, token and recovery responsibilities. Event must still operate without this additional infrastructure.

The supplied Identity assessment is treated as product intent, not independently verified market or standards research. Its opaque citation markers cannot serve as sources for this report. No competing implementation was examined or copied.

The report does not implement either service, author an execution plan, approve a release, or freeze unresolved choices. Concrete API contracts for Asset and Identity were not supplied. Proposed boundaries below are recommendations for discussion, not claims about those future products.

## Executive Assessment

**Yes: Asset can be introduced as a largely additive integration. Identity can also be additive. Neither result follows merely from the service being optional.**

An integration is additive when existing identities, authorized operations, saved data and published meanings continue to work while a new implementation is selected. It becomes breaking when the new service requires clients to use different IDs, changes who owns or may see data, invalidates stored file references, removes the standalone path, or changes what an existing command promises.

The most valuable pre-release work is therefore **not building Asset or Identity early**. It is removing ambiguities about identity, ownership and lifecycle from Event now.

### The five decisions with the highest leverage

1. **What remains the identity of a person, actor and file when its provider changes?** Choose durable application identities and explicit external bindings, not email addresses, hostnames or storage URLs.
2. **Who is allowed to write each fact?** Separate credential authority, canonical profile authority, Event permissions, scoped consent and immutable historical evidence.
3. **What exactly is a stored file reference?** Separate an Event-facing object identity from its physical target/version and from any temporary delivery URL.
4. **What does deletion or revocation mean across products?** Unlinking an account, withdrawing Event access, deleting one attachment and erasing a person everywhere are different operations.
5. **What does optionality promise?** Asset and Identity must each be independently absent, enabled, unavailable and deliberately removed without silently changing authority or losing business data.

### Ranked decision register

Priority ranks the cost of getting the decision wrong, not the amount of implementation missing.

| Rank | Decision | Current evidence | Pre-release action | Finding |
| --- | --- | --- | --- | --- |
| 1 | Stable identity and authority-qualified linking | Local User/Actor IDs and issuer-qualified OIDC/DID keys already exist; selected verified-email auto-matching also exists. | Preserve the separation; explicitly choose linking/merge rules and external Identity namespace. | IVSD-F001 |
| 2 | Canonical profile writer and Event-owned permissions | Login synchronization and profile commands both write local profile fields. | Decide field ownership and precedence; correct conflicting write semantics before they become promises. | IVSD-F002 |
| 3 | Provider-neutral file identity, target and version | Stable storage IDs exist, but `Uri` has mixed meanings and binding discipline differs by file path. | Normalize reference semantics and extend durable target ownership to all managed file producers. | IVSD-F003 |
| 4 | File access, metadata and shared ownership | Event-resource files have stronger authority/lifecycle rules than generic files. | Define the Asset/Event ownership split and preserve policy-sensitive delivery. | IVSD-F004 |
| 5 | Consent, deletion and retained evidence | Scoped consent snapshots and authority-first erasure already exist. | Define cross-product scopes and prohibit automatic global propagation of local deletion. | IVSD-F005 |
| 6 | Optional-service activation, failure and exit | Standalone and provider ports exist; future distributed handover does not. | Establish independent selection axes and safe transition/failure semantics. | IVSD-F006 |
| 7 | Public and persisted contract boundaries | API, HAL, webhook and configuration versioning foundations exist. | Decide the intended stability surface; remove provider leaks and inconsistent meanings now. | IVSD-F007 |
| 8 | Event/occurrence/provenance and time semantics | Event/session separation, lifecycle rules and explicit end-time types exist. | Confirm the conceptual model with hard examples before registrations and links accumulate. | IVSD-F008 |
| 9 | Historical commitments versus live profile/catalog data | Order-line and consent snapshots, distinct participants and admission credentials exist. | Protect these choices; never replace transaction history with remote live lookups. | IVSD-F009 |

**Recommended first corrections:** storage `Uri` semantics; generic/generated-file target binding; profile-write precedence; deliberate account-correlation rules. The remaining items include important decisions and verification, not a finding that everything needs rewriting.

## Claim Boundary

This is I-VSD design reasoning with bounded implementation traceability. It is not a fatwa, security certification, legal opinion, completed migration design or proof that the future integrations are non-breaking.

Source files, selected tests and repository documentation were inspected. No product build/tests, live application, remote Asset/Identity API, migration rehearsal or stakeholder session was executed. A test's existence is evidence of intended coverage, not a passing result.

Breaking changes remain appropriate when they produce the better design. The goal is to avoid changes caused by preventable ambiguity, not to optimize for a zero-breaking-change statistic.

## What Counts as a Breaking Change Here?

| Change | Assessment |
| --- | --- |
| Add an Asset adapter while keeping Event object IDs, routes, access rules and lifecycle outcomes | Potentially additive; still requires implementation and migration verification. |
| Add an optional Identity authority while retaining Event subject IDs, membership semantics and credential-provider ownership | Potentially additive; authority handover is still a substantive integration. |
| Add a database table/column or an external binding with a lossless upgrade | Not inherently a public breaking change. A schema migration is normal evolution. |
| Replace every Event `UserId` with an Identity ID, or every file ID with a remote URL | Changes durable identity and references; likely disruptive even if HTTP routes look unchanged. |
| Preserve JSON fields but make a profile update overwrite canonical Identity data or broaden file access | A behavioral/authority break. Matching schemas are not sufficient. |
| Make a formerly standalone workflow require a new daemon or online control plane | A deployment/operational break. |
| Return a new provider/status value to clients promised a closed vocabulary | Potentially breaking for exhaustive consumers; define extension behavior rather than assuming additions are harmless. |
| Rename an internal class without changing persisted/public meaning | Usually an internal refactor, not a reason to create compatibility layers. |

The expensive commitments are semantic identity, ownership, history, access and portability. Framework/package choices and private implementation names are generally easier to change behind a coherent contract.

## Findings

### IVSD-F001: Durable identities and deliberate linking matter more than a shared user table

- **Lifecycle:** open.
- **Severity / claim type:** Very high decision impact; existing strengths plus a concrete correlation-policy choice.
- **Principles / domains:** Amanah, Rights of People, Non-Harm; technical and governance.
- **Stakeholders / controlled decision:** People with several accounts, attendees, operators and other ISLAMU products; identity namespace, account linking, merging and unbinding.
- **Evidence / validation:** Event has a local `User.Id`, a distinct `Actor.Id`, provider-qualified external links, OIDC keys constructed from issuer and subject, and ATProto keys constructed from DID. `SyncUserCommandHandler` can match one existing non-Local-owned account using a provider-verified email for supported providers. Implementation traceability only. [E01-E03]
- **Why costly later:** Replacing local keys spreads through memberships, organizers, registrations, tickets, audit and erasure. Incorrectly merging two people can expose history and is harder to repair than an ordinary schema migration.
- **Mitigation:** **IVSD-M001** - Keep Event business IDs independent of credential and external control-plane IDs. Specify a namespace for an external Identity deployment/authority plus its person ID; a UUID does not by itself identify the trusted issuer. Keep email, handle, display name and current PDS location as attributes, not person identity. Require explicit, proof-backed account linking or a tightly bounded operator migration; recommend removing broad verified-email auto-correlation before release unless its exact trusted-authority policy is deliberately accepted and tested. Email verification demonstrates mailbox control under that provider, not universal continuity of personhood.
- **Decide now / later:** Decide uniqueness, link ownership, merge/split handling and what evidence permits consolidation now. Implement the future Identity connector later. Do not treat all accounts belonging to one real person as automatically entitled to share Event histories.
- **Owner / acceptance:** Identity/security owner with project steward; demonstrate same subject under different issuers remains distinct, an email change does not create a new person, and linking never changes unrelated Event ownership or consent.
- **Escalation:** Cross-product person correlation and disputed account ownership need explicit product/privacy policy. This review does not claim an exploitable account-takeover path was demonstrated.

### IVSD-F002: Choose one writer for canonical profile facts, without exporting Event authorization

- **Lifecycle:** open.
- **Severity / claim type:** Very high; confirmed current write coupling and future authority decision.
- **Principles / domains:** Amanah, Sidq, Rights of People; technical, design and governance.
- **Stakeholders / controlled decision:** Account holders, organizers and administrators; field mastering, profile edits, participation and revocation.
- **Evidence / validation:** `User` exposes local `UserPii` through property wrappers. `UpdateUserCommandHandler` changes names; `SyncUserCommandHandler` also rewrites names, email/verification and Actor display name from incoming account data. `TenantUserProfile` has separate override/preferences fields. `AdminContext` resolves authority from Event repositories; a selected test exercises committed grant changes across independent hosts. [E02-E05]
- **Why costly later:** If an Identity profile update is overwritten at the next login, the system has two masters. Moving Event roles into a generic person profile can also weaken tenant/organization boundaries and revocation freshness.
- **Mitigation:** **IVSD-M002** - Write a field/operation ownership matrix now. When embedded mode is selected, Event's profile authority owns designated fields. When external Identity is selected, that authority owns those same canonical fields and Event uses an explicitly bounded local projection. Credential verification remains with the credential authority; an arbitrary profile edit cannot make an address verified. Tenant display overrides and Event participation/permissions retain their own scopes. Preserve Event's ability to deny an operation using current Event authority, regardless of a remote profile or stale claim.
- **Decide now / later:** Correct unconditional profile-overwrite semantics and identify the application-owned write/read boundary now. Later add the remote implementation and synchronization. Do not require a network call for every rendering of a user's display name, and do not use eventually synchronized display/profile facts to authorize protected actions.
- **Owner / acceptance:** Application/identity owner; a chosen canonical profile edit survives login, a removed organizer cannot act using an old profile/session projection, and identity administration does not confer Event instance-admin authority.
- **Escalation:** If Identity will also become an authorization-policy authority, that is a separate explicit contract—not an automatic consequence of owning the user graph.

### IVSD-F003: Storage has stable IDs, but references and target binding need one coherent meaning

- **Lifecycle:** open.
- **Severity / claim type:** Very high; confirmed generic/resource and URI-semantic differences.
- **Principles / domains:** Amanah, Promise-Keeping, portability; technical and operational.
- **Stakeholders / controlled decision:** Uploaders, attendees, self-hosters and external consumers; logical file identity, location, version and migration.
- **Evidence / validation:** `StorageObject` has `Id`, `Uri`, `ObjectKey`, nullable binding ID and provider version. Generic finalization stores an application content path in `Uri`; the CSV submission sink stores an object key in the same field; federation and Actor profile paths consume that field as a URI. Generic reads/deletes resolve a provider label, while governed resource reads resolve a captured binding and exact version. The database uniqueness is currently `(Provider, ObjectKey)`. [E06-E09]
- **Why costly later:** A URL/key/name that has several meanings becomes difficult to migrate safely. Changing the current S3 bucket or service endpoint must not silently retarget old files or pending deletions.
- **Mitigation:** **IVSD-M003** - Define the Event-facing file identity separately from a bound provider locator and content version. Give `Uri` one explicit meaning or remove it as a persisted universal field; generate authorized delivery links from identity rather than storing provider keys as URLs. Apply immutable target binding, appropriate version identity and deletion targeting to every persistent managed-file producer, not just resource uploads. Define object-key uniqueness within the real storage target instead of assuming a provider kind identifies one permanent bucket/service.
- **Decide now / later:** Correct the current semantics and all relevant producer/read/delete call sites now. Asset-specific endpoint/client/credential details can wait. Adding its provider vocabulary and generated migration later is not itself a public contract failure.
- **Owner / acceptance:** Storage owner; changing the default target cannot redirect old reads/deletes, and a generic image, resource PDF and generated CSV each resolve from a stable Event identity after backend relocation.
- **Escalation:** Existing managed-object collisions or ambiguous old locators require explicit reconciliation during migration; never guess by filename or delete the source merely because a copy request returned success.

### IVSD-F004: Asset can own files without owning Event's permission and attachment meaning

- **Lifecycle:** open.
- **Severity / claim type:** Very high; cross-product ownership decision anchored in existing sensitive-file contracts.
- **Principles / domains:** Rights of People, Non-Harm, Haya, Amanah; technical, design and governance.
- **Stakeholders / controlled decision:** Document subjects, uploaders, attendees and other applications sharing an asset; access policy, attachment ownership, metadata, derivatives and disposal.
- **Evidence / validation:** Resource-file access is freshly authorized, version/inspection bound and mediated; generic storage cannot bypass it, even for the uploader. Resource files expose no presigned URLs. Resource lifecycle has durable retirement/tombstones and special handling for shared material and retained evidence. The provider port itself offers byte-level operations. [E06, E08, E10]
- **Why costly later:** Treating a presigned URL as an ordinary permanent link can outlive revocation. Treating an attachment removal as deletion of a shared Asset can damage another event or product. Conversely, treating every deletion as a harmless detach can leave private bytes indefinitely.
- **Mitigation:** **IVSD-M004** - Keep Event responsible for why a file is attached, who may access it in the Event context, and what retention/removal obligation applies. Let Asset own physical objects/versions, asset metadata and its own scoped enforcement. Event must send bounded authority and verify the result; neither a guessed Asset ID nor a broad service credential may substitute for Event authorization. Choose an explicit sole-owner versus shared-reference disposal model. Separate physical metadata and global Asset tags from Event-specific captions, audience, evidence purpose and consent lineage.
- **Decide now / later:** Set these meanings now. Preserve mediated delivery for content requiring current authorization/retention checks; use direct short-lived capabilities only where their lifetime is an accepted policy. A later Asset presign facility need not be exposed to the browser at all. Rich search, taxonomy, transformations and additional delivery modes can wait.
- **Owner / acceptance:** Storage/domain/security owners; revocation cannot be bypassed through another file route, changing a tag does not change Event permission, and detaching a shared object does not erase another valid attachment.
- **Escalation:** Content-safety, shared retention/legal holds and irreversible deletion across products require explicit owner policy. A checksum or successful upload is not a malware-safety verdict.

### IVSD-F005: Central consent and deletion must remain scoped, not become ecosystem-wide side effects

- **Lifecycle:** open.
- **Severity / claim type:** Very high; future privacy-authority decision with strong existing local invariants.
- **Principles / domains:** Rights of People, Avoiding Spying, Amanah, Non-Harm; governance, technical and operational.
- **Stakeholders / controlled decision:** Users, guests, dependents, other adult participants and retaining organizations; consent scope, retention, unlinking, account deletion and service-wide erasure.
- **Evidence / validation:** `RegistrationConsentRecord` pins tenant, event, requirement/form versions, purpose, text and participant/subject lineage. The privacy workflow is authority-first, fences rematerialization and settles provider work separately. Configuration portability explicitly excludes application/subject data. Resource deletion authority can outlive source rows. [E10-E12]
- **Why costly later:** A global `hasConsented` flag cannot represent a particular attendee's agreement to a particular organizer. Removing Event access must not silently delete someone's Identity account or files used elsewhere. A delayed sync must not resurrect erased data.
- **Mitigation:** **IVSD-M005** - Specify distinct operations for unlinking an external account, leaving a tenant, deleting an Event profile, withdrawing a purpose-specific consent, retiring an attachment and requesting wider Identity erasure. Choose one coordinator per operation with durable, scope-qualified requests, retained evidence and observable partial completion. Identity may administer a consent ledger, but moving its storage must preserve Event/recipient/purpose/text/version/subject semantics; it cannot broaden consent. Keep financial/legal evidence separately governed from removable profile data.
- **Decide now / later:** Fix scope and retention ownership now; add remote settlement adapters later. Include the Asset and Identity copies/projections, derived files, exports and independently restored databases in future replay/erasure design.
- **Owner / acceptance:** Privacy/domain owners; deleting a profile does not delete a shared mosque document, withdrawing one consent does not erase unrelated consent evidence, and a late update after erasure cannot recreate readable PII.
- **Escalation:** Legal retention and cross-controller obligations require jurisdiction-specific review; religious-legal questions remain for qualified scholars.

### IVSD-F006: Optional services need explicit handover and failure semantics, not hidden fallback

- **Lifecycle:** open.
- **Severity / claim type:** High; foreseeable distributed-state and self-hosting contract risk, not a claim that the future adapters already fail.
- **Principles / domains:** Promise-Keeping, portability, Amanah, avoidance of excessive uncertainty; strategic, technical and operational.
- **Stakeholders / controlled decision:** Small self-hosters, administrators and users during outages; topology, mode selection, authority transfer and exit.
- **Evidence / validation:** Standalone already combines hosts without requiring every optional service. Authentication-provider selection is its own axis. Storage has an infrastructure port; resource flows and erasure already distinguish local commit from provider settlement. The generic outbox documents at-least-once delivery. [E01, E06, E10, E12, E13]
- **Why costly later:** "Optional" at installation can become mandatory after the first imported profile or remote file. Switching a setting cannot migrate bytes, transfer write authority, preserve credentials or reconcile in-flight work.
- **Mitigation:** **IVSD-M006** - Separate authentication choice, profile/control-plane authority and file provider. Identity is not automatically another login provider. Preserve the four combinations: neither service, Asset only, Identity only, both. For a selected remote owner, define unavailable/pending/conflict states and fail safely for protected operations; do not quietly create a new local user or write to a different file backend during an outage. Enabling/disabling a service is a reviewed handover with preserved IDs, fenced writers, inventory and reconciliation—not an instantaneous Boolean toggle.
- **Decide now / later:** Establish these contracts and a usable embedded implementation now. Build concrete remote retry/event transport and migration tooling when the service is implemented. Reuse durable intent, idempotency and reconciliation patterns rather than introducing cross-database transactions or claiming exactly-once network effects.
- **Owner / acceptance:** Architecture/operations owner; interrupted activation resumes without duplicate people/files, old in-flight work cannot write to a new authority, and a deliberately completed exit leaves a functional supported Event installation.
- **Escalation:** Credential export is not implied by profile portability. Moving back to local credentials may require an explicit enrollment/recovery process; Identity cannot manufacture Keycloak/PDS passwords or reset authority.

### IVSD-F007: Define the stability surface before exposing backend details as public contracts

- **Lifecycle:** open.
- **Severity / claim type:** High; existing versioning strengths with exposed shapes that merit pre-release review.
- **Principles / domains:** Sidq, Promise-Keeping, portability; technical, design and governance.
- **Stakeholders / controlled decision:** API/SDK clients, self-hosters and federated consumers; representations, identifiers, extension values and configuration.
- **Evidence / validation:** API versions use media/query/header readers; HAL governs affordances; generated C# has its own source compatibility contract. Webhooks have a schema-versioned envelope. Configuration manifests have an explicit version and exclude application data/secrets. `StorageObjectDto` exposes `Uri` and `Provider`; its shape is already a consumer-facing decision. [E07, E11, E14]
- **Why costly later:** An internal provider rename can become a client break if clients switch on it. A public URL, webhook value, saved manifest or enum can outlive the UI that produced it. Adding a field/status is not universally harmless.
- **Mitigation:** **IVSD-M007** - Classify what is public/stable versus internal: IDs, API fields/nullability, lookup codes, HAL relations, capability formats, webhook schemas, federation identifiers, configuration keys and generated SDK source shapes. Remove accidental implementation details before v0.1; do not add a generic property bag to avoid making domain decisions. Use explicit capabilities for optional behavior and define how consumers handle unknown extension values without defaulting to a more permissive state. Future remote service APIs must have independent version/capability negotiation.
- **Decide now / later:** Review published names and meanings now. Version a genuinely changed contract later rather than keeping misleading aliases. Do not require arbitrary future providers to appear in the v0.1 schema or freeze every internal class.
- **Owner / acceptance:** API/integration owner; a provider addition does not require clients to replace Event IDs or interpret backend locators, and saved webhook/configuration data has an unambiguous versioned meaning.
- **Escalation:** Changes to published federation/capability formats may require protocol-specific review even when ordinary JSON remains unchanged.

### IVSD-F008: Event, occurrence, authority and time are foundational commitments too

- **Lifecycle:** open.
- **Severity / claim type:** High; model-confirmation decision, not a finding that the whole event domain is wrong.
- **Principles / domains:** Sidq, Adl, Promise-Keeping; design, technical and evaluation.
- **Stakeholders / controlled decision:** Organizers, attendees and discovery consumers; what an Event represents, schedule changes, provenance and contextual authority.
- **Evidence / validation:** Accepted ADRs distinguish publishing, organizer and participant authority and retain one object through lifecycle transitions. `EventSession` has nullable start/end and explicit fixed/open-ended/prayer-relative end types; local times are derived by a timezone-aware projection. A separate current consultancy discusses canonical Event/session/series identity and discovery. [E15, E16]
- **Why costly later:** Splitting or merging an Event after orders, URLs, sessions, resources and federation records exist has broader consequences than adding a storage adapter. Changing whether a repeated session is a new event, or whether a time is fixed versus rule-derived, changes attendee expectations.
- **Mitigation:** **IVSD-M008** - Confirm Event/session/day/series meaning using a multi-day conference, repeated course, multi-city program and annual edition. Distinguish reporter, publisher, organizer and payment recipient. Keep structural identity separate from configurable publication completeness. Decide whether each schedule value is an instant, a local civil-time intention or a symbolic rule; define timezone, ambiguity, missing end and rescheduling outcomes without inventing fake dates. Preserve immutable published/transactional versions where needed.
- **Decide now / later:** Resolve meanings that affect current persistence and public contracts now. A future recurrence/prayer calculation engine can wait; an enum value alone is not proof that every symbolic scheduling behavior exists. Reference the dedicated discovery consultation rather than creating a competing model here.
- **Owner / acceptance:** Event-domain/product owner with organizer examples; one intended event remains recognizable through edit/cancel/reschedule, and DST/timezone/unknown-end cases have explicit outcomes.
- **Escalation:** Religious timing interpretations require qualified domain/scholarly input where contested. This report does not select a prayer-calculation policy.

### IVSD-F009: Keep transaction and consent history independent of live remote profiles

- **Lifecycle:** open.
- **Severity / claim type:** High; existing design strength to preserve across future integration.
- **Principles / domains:** Amanah, Rights of People, Promise-Keeping, avoidance of excessive uncertainty; technical and governance.
- **Stakeholders / controlled decision:** Purchasers, participants, organizers and auditors; historical meaning of purchases, admission, consent and evidence.
- **Evidence / validation:** Registration separates purchaser, participant, assignment and admission credential. `RegistrationOrderLine` snapshots catalog, name, currency, amount and fee-policy facts. Consent records bind text/version/subject. Admission display IDs are not bearer credentials. [E11, E17]
- **Why costly later:** If an old purchase points only to today's Identity profile or mutable Asset document, a subsequent edit can change who appeared to consent or what was sold. Making every participant an Identity user would also alter guest/dependent participation and data minimization.
- **Mitigation:** **IVSD-M009** - Keep local immutable transaction facts and minimal necessary identity/consent/evidence snapshots, each with its retention policy. Remote profile data may help populate a new form but must not rewrite a concluded order or another adult's consent. Keep opaque credentials distinct from stable business IDs. Continue integer minor-unit money with explicit currency and explicit purchase/payment/refund/admission states.
- **Decide now / later:** Confirm snapshot versus live-reference rules now; do not rebuild already suitable order models. Later connectors may enrich current presentation through authorized projections without becoming the historical authority.
- **Owner / acceptance:** Registration/payments/privacy owners; changing a name, email, asset title/version or catalog cannot change the historical purchase or consent; guests remain valid without a global account.
- **Escalation:** Retention of transaction evidence versus erasure is a policy/legal decision, not a justification for retaining every profile field forever.

## ISLAMU Asset: Recommended Boundary

### Ownership

| Concern | Recommended owner |
| --- | --- |
| Physical file, immutable content/version, technical metadata and Asset-native tags/categories | Asset when selected; embedded/local provider when absent |
| Event-facing object reference and its remote binding | Event |
| Why an event/registration/evidence record attaches the file | Event |
| Current Event audience, attendee entitlement and moderation disclosure | Event, enforced before granting the relevant delivery capability |
| Event-specific caption/alt text, purpose and context | Event; reusable Asset defaults may be projected, not silently substituted |
| Asset-wide access controls and safe handling | Asset, in addition to—not instead of—the Event decision |
| Whether bytes can be physically removed when other products reference them | Explicit ownership/reference/retention agreement between the systems |

Advanced modeling in both products does **not** require identical domain classes, database tables or taxonomies. An Asset category can describe a document collection while an Event category describes a program. Share a stable vocabulary only where meanings actually coincide; local integer lookup IDs are not automatically cross-product identifiers.

### What "all files" should mean

The recommended scope is all **persistent business-managed files**: images, avatars and branding media, resource PDFs/documents, registration answer uploads, retained CSV/export artifacts, imported/cached media where Event owns a copy, and retained evidence attachments. Every producer needs a deliberate route. The CSV sink proves that replacing only the browser uploader is insufficient.

Inventory generated and background producers before claiming coverage. A bounded temporary spool that is deleted after validation is not a second persistent file backend. Application binaries, database files, secret keys, audit infrastructure and disaster-recovery backups should remain under their respective infrastructure/secret authorities unless separately designed otherwise. This scope interpretation needs explicit acceptance; "all files" should not accidentally make Asset responsible for its own only recoverable backup.

If Asset is selected for all business files, distinguish **new-write routing** from **completed adoption**. Existing files must also be migrated and verified, or the transition must honestly remain incomplete. Per-object bindings enable a safe transition; they are not a reason to silently leave half the promised file estate elsewhere forever.

### A non-breaking future adoption scenario

1. An Event image or resource references Event storage object `S`; its current target binding points to local/S3 bytes.
2. Copy the exact content to the selected Asset authority, receiving asset `A` and immutable version `V`; verify the required content identity and safety metadata.
3. Commit the new binding of `S` to that authority/asset/version under concurrency control. Existing Event, registration and resource references still identify `S`.
4. Keep delivery through the Event contract or its policy-approved capability flow. Asset presigning can remain server-to-server plumbing.
5. Retire the old physical copy only after reference, hold, pending-write and recovery obligations are settled; retain retryable deletion authority.

This is a **target contract**, not an implemented migration. Neither a simple provider-name switch nor copying a bucket proves it.

## ISLAMU Identity: Recommended Boundary

### Authentication is not profile mastering

Keep these independently selectable:

```text
Credential/authentication authority: Local | Keycloak | ATProto account authority
Canonical profile/control-plane authority: Embedded Event | ISLAMU Identity
File provider: Local | S3-compatible | ISLAMU Asset
```

These are conceptual axes, not proposed configuration keys or a requirement to expand the current enums now. Identity may later offer additional interfaces, but its supplied role does not require it to become the login/token issuer.

### Ownership

| Concern | Recommended owner |
| --- | --- |
| Passwords, MFA, login tokens, provider verification and reset/security email | The selected credential/account authority |
| Canonical person graph, externally linked authorities and designated global profile fields | Embedded profile authority or Identity, with one selected writer |
| Local Event subject/Actor IDs and Event references | Event |
| Event tenant/organization/group participation, organizer authority, bans and grants | Event by default; remote provisioning requests do not bypass local authorization |
| Tenant-specific display/contact overrides | Their explicit tenant authority, with defined precedence and verification semantics |
| Event registration, purchaser/participant state, orders and admission | Event |
| Notification preferences and consent | Explicitly scoped records; centralized administration must preserve product, purpose, recipient and subject boundaries |
| Historical purchase/consent/audit snapshots | Their originating domain and retention policy, not the current editable profile |

Identity can provision membership information without making every global identity-group membership an Event permission. Likewise, a person's canonical profile can exist without granting visibility of that person's participation across unrelated mosques or applications.

### How far should the standalone model align?

**Align meanings and application boundaries now; do not copy the whole future Identity product into Event.**

The embedded implementation should support the same essential account/profile operations and ownership rules Event needs. The remote implementation can later satisfy those application ports. Local EF entities remain local persistence; do not turn navigation properties into synchronous remote calls or replace repository interfaces with raw Identity DTOs.

A richer Identity service can expose capabilities that embedded Event does not provide. Advertise those differences explicitly. Equal business outcomes for common operations do not require identical deployment, schema, feature breadth or timing.

Do not move all current fields merely because they contain "user." A ticket participant, consent subject, purchaser, public Actor, tenant member and credential account are related concepts, not synonyms.

### A non-breaking future adoption scenario

An Event installation has `User U`, personal `Actor P`, memberships and historical registrations. Identity authority `I` introduces canonical person `C`. Event records an authenticated, reviewed mapping `(I, C) -> U`; existing event-domain foreign keys retain `U` and `P`.

The profile authority then hands over deliberately. Login still resolves the validated external credential account to the correct local Event subject. New canonical profile writes go to the selected owner; Event projections carry provenance/version and never become a competing writer. Existing scoped consent and transaction snapshots do not change.

Disabling Identity requires an export/handover of the fields Event needs, preserved mappings/history and an explicitly supported login path. It is not safe to drop the mapping and hope email correlation reconstructs the account. A remote outage must not trigger that handover automatically.

## Recommendations

### Make a small set of binding decisions before implementation work

Record the answers as domain/architecture decisions and adjust the existing implementation where it contradicts them:

1. **Identity/reference contract:** Which IDs remain Event-local, how external authorities are namespaced, which links are permitted, and what merging may change.
2. **Authority matrix:** One writer for each canonical profile field; separate credentials, Event access, scoped consent, historical facts and file ownership.
3. **File contract:** Stable reference versus location/version/delivery URL; complete producer inventory; access and deletion semantics across shared use.
4. **Lifecycle/optionality contract:** Activation, interruption, revocation, erasure, restore and deliberate exit for each optional service.
5. **Public meaning contract:** Event/session/time/provenance, stable API/lookup/webhook/configuration values and immutable transactional snapshots.

These are decision deliverables, not five new platforms. A short explicit ownership table with counterexamples is more useful than a large speculative interface hierarchy.

### Current changes with the clearest justification

- Eliminate the mixed meaning of `StorageObject.Uri` across finalization, generated CSVs and consumers; choose a consistent ID-based delivery contract.
- Extend or deliberately unify target/version binding and disposal obligations across generic files and resource files. Preserve different access policies rather than flattening sensitive resources into generic downloads.
- Decide profile-write precedence before adding more profile fields: incoming login claims must not silently compete with the chosen canonical writer.
- Decide whether the current verified-email auto-match policy is acceptable; prefer explicit proof-backed linking for the intended multi-authority future.
- Review configuration and public DTO semantics that currently expose provider details; do not build an Asset/Identity connector merely to prove the boundary exists.

These are recommendations for subsequent authorized implementation. No product files were changed by this consultancy.

### What can wait

- The Asset HTTP client, rich tags/categories/search, transformations and optional direct-upload/presign optimizations.
- The Identity deployment, administrative UI, SCIM connectors, advanced provisioning and ATProto orchestration beyond Event's current need.
- A universal multi-product domain library, generic plugin framework, global distributed transaction coordinator or new message broker.
- Concrete cross-service migration tools until the remote contract exists—provided the durable identity/ownership seams above are selected now.
- Supporting every conceivable future provider or pre-allocating arbitrary nullable fields for them.

SCIM or another protocol may be a useful later integration transport; selecting it does not decide person identity, field ownership, consent scope or Event permissions.

### Rejected alternatives

- **Preserve today's code because it might become public:** directly contradicts the greenfield objective.
- **Build the entire Asset/Identity roadmap before v0.1:** delays learning and duplicates future product design unnecessarily.
- **Use one canonical remote ID as every product's primary key:** couples migration, restores and account merges to every consumer's historical records.
- **Share the full EF model/database between products:** couples releases and lets one application's write bypass another's domain invariants.
- **Treat Identity as another auth-provider enum member:** confuses the stated control-plane role with token authority.
- **Trust presigned URLs everywhere:** weakens current sensitive-resource revocation and disclosure semantics.
- **Automatically fall back to local storage/profile writes during a remote outage:** creates split ownership and misleading success.
- **Assume optional means non-breaking:** ignores data, deployment, authority and removal contracts.
- **Remove all snapshots to avoid duplication:** confuses deliberate historical evidence with competing editable masters.

## Pre-Release Acceptance Scenarios

These are recommended proofs for follow-up implementation, **not tests run in this report**. Put them in the existing relevant test suites and exercise real provider paths when introduced.

| Scenario | Required outcome | Finding |
| --- | --- | --- |
| Two credential authorities issue the same subject/email | No accidental account merge or transfer of existing rights. | IVSD-F001 |
| A canonical profile edit is followed by login from another linked provider | Selected ownership/precedence is preserved; verification provenance is not fabricated. | IVSD-F002 |
| An organizer is revoked while a remote profile projection is stale | Event denies new protected actions according to its current authority contract. | IVSD-F002 |
| Default storage target changes, including two targets with the same key spelling | Old objects and pending deletions keep their exact original target until explicit migration. | IVSD-F003 |
| Image, PDF/resource, avatar and generated CSV move to Asset | Event references and allowed delivery behavior remain coherent for every class. | IVSD-F003 |
| A private attachment is revoked after another delivery route was discovered | No generic route or overlong capability defeats the promised restriction. | IVSD-F004 |
| One of two valid attachments is retired | Other valid use survives; sole-owner deletion still eventually disposes eligible bytes. | IVSD-F004 |
| A user leaves Event or withdraws one purpose-specific consent | Other product accounts/data/consents are not implicitly destroyed or broadened. | IVSD-F005 |
| A delayed profile/file event arrives after erasure or retirement | It cannot resurrect readable data or redirect deletion to a new target. | IVSD-F005 |
| An optional service disappears after accepting a command | Outcome is durable pending/unknown/failure as specified; no duplicate person/file or silent local fallback. | IVSD-F006 |
| Activation/exit crashes halfway through | Reconciliation preserves local IDs and explicit ownership; it never guesses identity from email. | IVSD-F006 |
| Older supported client encounters an added provider capability | It follows the agreed unsupported/unknown contract without corrupting state or widening access. | IVSD-F007 |
| A course spans cities, DST and an open-ended session | Event identity, occurrence matching and time interpretation remain explicit and truthful. | IVSD-F008 |
| A user/profile/catalog/asset description changes after purchase or consent | Historical evidence still describes the original scoped commitment. | IVSD-F009 |

For v0.1, prove the applicable local invariants and correct current couplings. Remote end-to-end scenarios become activation gates when those integrations ship; they are not a demand to deploy nonexistent services now.

## Stakeholders

- Users with several credential accounts, people seeking pseudonymity, guests and independently consenting participants.
- Mosques, groups and organizers whose authority and attendee data must remain separated.
- People appearing in images/documents, including non-users and dependents.
- Self-hosters who need predictable installation, backup, upgrade and exit behavior.
- Maintainers of Event, Asset and Identity who need independent releases and clear ownership.
- API/federation consumers and future products relying on durable references.

## I-VSD Principles And Domains

| Principle/domain | Application to these decisions |
| --- | --- |
| Amanah / technical and operational | Retain correct identity, target, authority and recovery meaning through integration and migration. |
| Rights of People / governance and design | Linking is not consent; local removal is not global erasure; file reuse does not erase another person's rights. |
| Sidq / strategic and design | State what optionality, completed migration, privacy and deletion actually mean. |
| Non-Harm / technical and operational | Prevent cross-account exposure, stale authority, wrong-target deletion and orphaned private files. |
| Adl / design and evaluation | Do not force guest attendees into a global identity account or make small self-hosters fund unnecessary infrastructure. |
| Avoiding Spying / governance | A canonical user graph is not permission to correlate participation across all communities/products. |
| Promise-Keeping / strategic and operational | Preserve the supported standalone path and a credible exit from optional services. |
| Ihsan / evaluation | Test the difficult counterexamples before users bear the cost, without constructing speculative frameworks. |

## Common Overlooked Failures And Outcomes

| Overlooked failure | Harm | Better outcome |
| --- | --- | --- |
| Shared SMTP or SSO is treated as shared identity ownership | Profile/credential authority becomes confused; recovery actions cross boundaries. | Account authorities retain security flows; Identity coordinates only authorized operations. |
| Asset thumbnails/derived documents are omitted from retention | Source deletion leaves readable derivatives. | The disposal contract identifies versions, derivatives and retained exceptions. |
| Asset-native tags become Event access policy implicitly | Classification edits unexpectedly disclose documents. | Classification and access remain explicit, independently governed facts. |
| Global profile updates rewrite order/consent snapshots | Evidence no longer describes what was agreed. | Immutable scoped history survives profile changes. |
| Restoring Event alone replays old writes into newer Identity/Asset state | Erased profiles or retired files reappear. | Fences, versioned intents and independently retained authority govern replay. |
| Service credentials are broad and end-user scope is inferred from a file/person ID | One application becomes an unintended cross-product data gateway. | Bounded service authority plus local resource authorization and auditable scope. |
| Identical UUID shapes are assumed to imply identical namespace | Restores/imports bind to the wrong authority. | References name the issuing authority and preserve explicit mappings. |
| An optional service is removable only by abandoning data | Hidden operational lock-in. | Documented, tested authority handover and data export. |

## Validation Gaps

- Asset and Identity interface/schema, versioning, capabilities, credentials and operational contracts were not available.
- This is not an exhaustive inventory of every file producer, profile read/write, erasure worker or externally consumed field.
- Selected implementation seams and test source were reviewed; no current green test/build claim is made.
- Cross-product privacy/consent expectations and migration UX have not been validated with users/operators.
- Concurrent repository work was present. The source-bound observations below are not a whole-working-tree review.
- Future distributed correctness cannot be established from current in-process behavior. The report identifies contracts and activation proofs rather than asserting they already exist.

## Escalation Needed

- **Project steward/architect:** accept or replace the recommended ownership, identity, linking, file-scope and standalone promises before dependent implementation.
- **Security/privacy owners:** review identity consolidation, credential recovery, cross-product authorization freshness, consent scopes and erasure/restore coordination.
- **Product/domain owner:** settle Event/session/series/time semantics and publication/organizer distinctions with concrete examples.
- **Legal/privacy adviser:** determine actual controller/processor responsibilities, retention and export/erasure obligations when data crosses products.
- **Qualified Sunni scholars:** address any contested religious-legal finance, consent/obligation or prayer-timing questions. This report makes no ruling or certification claim.

None of these escalations requires abandoning greenfield freedom. They identify who owns a consequential decision, not approval gates for routine report work.

## Evidence Reviewed

Locators refer to the inspected revision/working-tree snapshot. Documentation is distinguished from current code; older ADR implementation-status paragraphs are not treated as current feature inventories.

| ID | Source | Evidence used |
| --- | --- | --- |
| E01 | [Authentication invariants](../../docs/internal/AUTHENTICATION.md):6-16; [Principal identity key construction](../../src/Explore.Application/Authentication/PlatformIdentityPrincipalExtensions.cs):196-253; [External login model](../../src/Explore.Domain/UserExternalLogin.cs); [Persistence mapping](../../src/Explore.Persistence/Configurations/Entities/UserExternalLoginConfiguration.cs):11-34 | Existing separation of login providers, instance-global bindings, issuer/subject/DID keys and Event subjects. |
| E02 | [User synchronization](../../src/Explore.Application/Features/Users/Handlers/Commands/SyncUserCommandHandler.cs):71-165, 207-273, 363-366 | Verified-email matching policy, Local-owned-account exclusions and provider-driven profile writes. |
| E03 | [User](../../src/Explore.Domain/User.cs):7-43; [Actor](../../src/Explore.Domain/Actor.cs):8-76; [Global actor ADR](../../docs/internal/adr/ADR-020-global-actor-and-concrete-tenant-participation.md) | Distinct User/Actor ownership and concrete tenant participation; global means within the Event instance, not automatically one cross-installation identity. |
| E04 | [Profile update handler](../../src/Explore.Application/Features/Users/Handlers/Commands/UpdateUserCommandHandler.cs):53-98; [Tenant profile](../../src/Explore.Domain/TenantUserProfile.cs) | Local profile writes, image URI assignment and separate tenant override fields. |
| E05 | [AdminContext](../../src/Explore.Infrastructure/Identity/AdminContext.cs):11-140; [Authority freshness tests](../../tests/Event.API.IntegrationTests/Features/AdminAuthorityFreshnessTests.cs):28-111 | Database-owned Event authority and source assertions for committed changes across independent hosts; tests not run. |
| E06 | [Storage design](../../docs/internal/STORAGE.md), implementation/upload/delivery sections; [Storage object](../../src/Explore.Domain/StorageObject.cs):5-53; [Provider port](../../src/Explore.Application/Contracts/Infrastructure/IFileStorageProvider.cs) | Stable storage identity, private locator fields, lifecycle and existing infrastructure abstraction. |
| E07 | [Generic finalization](../../src/Explore.Application/Features/StorageObjects/Handlers/Commands/FinalizeStorageUploadSessionCommandHandler.cs):358-389; [CSV producer](../../src/Explore.Infrastructure/Services/Registration/Providers/SubmissionSinks/CsvRegistrationProviderSubmissionSink.cs):54-103; [Storage DTO](../../src/Explore.Application/DTOs/StorageObject/StorageObjectDto.cs); [Federation snapshot](../../src/Explore.Application/Features/Federation/Atproto/Services/AtprotoEventPublicationSnapshotFactory.cs):867-890 | Different `Uri` meanings, non-browser producer and public/consumer-facing metadata. |
| E08 | [Generic reader](../../src/Explore.Application/Services/StorageObjectContentReader.cs):39-122; [Generic deletion](../../src/Explore.Infrastructure/StorageObjectDeletionService.cs):51-89; [Resource reader](../../src/Explore.Application/Services/EventResourceContentService.cs):14-49; [Binding service contract](../../src/Explore.Application/Contracts/Infrastructure/IStorageProviderBindingService.cs) | Provider-label versus captured-binding reads/deletes and exact-version resource access. |
| E09 | [Provider binding](../../src/Explore.Domain/StorageProviderBinding.cs); [Storage mapping](../../src/Explore.Persistence/Configurations/Entities/StorageObjectConfiguration.cs):11-20, 54-72; [Binding persistence tests](../../tests/Explore.Infrastructure.Tests/Infrastructure/StorageProviderBindingPersistenceTests.cs):11-73 | Immutable target coordinates/references, current provider vocabulary and object-key uniqueness; selected test source. |
| E10 | [Event resources](../../docs/internal/EVENT_RESOURCES.md):242-373; [Presign handler](../../src/Explore.Application/Features/StorageObjects/Handlers/Commands/IssuePresignedDownloadUrlCommandHandler.cs):82-118 | Sensitive resource delivery, safety binding, shared material/retained evidence and durable disposal; presign restrictions. |
| E11 | [Consent record](../../src/Explore.Domain/RegistrationConsentRecord.cs):12-129; [Configuration manifest](../../docs/internal/CONFIGURATION_MANIFEST.md):50-83 | Scoped evidence and versioned configuration—not application-data portability. |
| E12 | [Privacy erasure](../../docs/internal/PRIVACY_ERASURE.md):17-83; [Account-email authority](../../docs/internal/SECURITY-MODEL.md), Keycloak Identity Email Boundary and ATProto/PDS Identity Email Boundary | Authority-first erasure, topology/replay limits and separation of security emails from product notifications. |
| E13 | [Architecture](../../docs/internal/ARCHITECTURE.md):74-147; [Outbox](../../docs/internal/OUTBOX_PATTERN.md):8-64 | Independent host composition, durable key ownership and documented at-least-once delivery. |
| E14 | [API versioning implementation](../../src/Explore.API/Extensions/ApiVersioningExtensions.cs):18-44; [API contract](../../docs/internal/API.md), Concurrency Request Headers and API Versioning; [Generated-client contract](../../docs/internal/RECORD_CONTRACTS.md):116-158; [Webhook envelopes](../../docs/internal/WEBHOOKS.md):109-127 | Existing public protocol and generated-client stability surfaces. |
| E15 | [Participation ADR](../../docs/internal/adr/ADR-017-event-participation-authority-model.md), Decision; [Lifecycle ADR](../../docs/internal/adr/ADR-026-domain-owned-lifecycle-and-contextual-completeness.md):58-148; [EventSession](../../src/Explore.Domain/EventSession.cs):24-46; [Schedule projection](../../src/Explore.Domain/Services/Scheduling/EventScheduleProjectionCalculator.cs); [Timezone resolver](../../src/Explore.Domain/Services/Scheduling/ScheduleTimeZoneResolver.cs); [End-time types](../../src/Explore.Domain/Enums/SessionEndTimeType.cs) | Ownership, lifecycle and time semantics worth confirming; not evidence that all future recurrence/prayer behavior is implemented. |
| E16 | [Canonical discovery consultation](i-vsd-event-publication-and-canonical-discovery-consultation.md):143-179 | Related working-tree proposal on Event/session/series identity and discovery; contextual recommendation, not implemented proof. |
| E17 | [Registration ADR](../../docs/internal/adr/ADR-018-registration-order-ticketing-aggregate.md):24-60; [Order line](../../src/Explore.Domain/RegistrationOrderLine.cs):17-86; [Admission ADR](../../docs/internal/adr/ADR-023-admission-credential-check-in-transfer-recovery.md):22-55 | Immutable commercial snapshots, participant distinctions and credential/display-ID separation. |
| E18 | Founder's request of 2026-09-25; [I-VSD architecture heuristics](../../.agents/skills/i-vsd/resources/architecture-heuristics.md); [Report contract](../../.agents/skills/i-vsd/resources/report-contract.md) | Greenfield objective, future product roles, provider-responsibility criteria and evidence limits. |

### Working-tree evidence binding

HEAD remained `c0b7bb362158f2f4535c6c4aefea3ac5fc839c36` during collection. Concurrent edits to storage binding/resource initialization were inspected and changed formatting, not the cited target-binding distinction. The API documentation addition concerned Keycloak operation DTOs, not the versioning/concurrency sections used here. Other concurrent changes were not reviewed or modified.

Key source SHA-256 snapshots:

| File | SHA-256 |
| --- | --- |
| `src/Explore.Domain/StorageProviderBinding.cs` | `44c491d3addc8faf27ab32b802f88b8fcf96bf516fa57c653fe4413c0c65bb2d` |
| `src/Explore.Application/Features/Users/Handlers/Commands/SyncUserCommandHandler.cs` | `7455272fca5e332901359b1545b19fcb51a835075adb10fc4c92159521d20e62` |
| `src/Explore.Application/Authentication/PlatformIdentityPrincipalExtensions.cs` | `349a1e0ea6d6507c3f09d8f20d6077f9a731a1095898fd397f3c9046feab4d93` |
| `src/Explore.Application/Services/StorageObjectContentReader.cs` | `8c19b9f17437dc8c154a0750293b69217769e865f531b32457abd367336e49ba` |

## Missing Evidence

- Agreed Asset and Identity external contracts and ownership matrices.
- Accepted account-link/merge policy and exact scope of canonical versus tenant/product profile data.
- Complete persistent-file producer, derivative, attachment/reference and deletion inventory.
- The intended scope of cross-product consent administration and Identity-wide erasure.
- Tested activation/exit/data-portability workflows for the future services.
- Stakeholder acceptance of identity correlation, shared asset ownership and service-removal behavior.

These are the decisions/evidence to obtain, not a requirement to implement both services before release.

## Context Inventory

- Current repository architecture/domain/API/operator docs, application/domain/infrastructure code and selected existing test source.
- Two independent read-only evidence tracks for storage and identity; parent verification of decisive source seams and related domain contracts.
- Knowledge-graph discovery found no available tool. C# workspace-symbol lookup timed out while processing unrelated workspace projects; bounded native search/read supplied the relevant source evidence instead.
- Earlier launch consultancy remains operational context; the canonical-discovery consultation remains a separate domain-design source.
- No external product/source research, provider calls, dependency additions or implementation edits. The supplied Identity product narrative was not reproduced as verified research.
- The shared working tree contained other work. Only this new report and one catalogue entry are authored by this task.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
| --- | --- | --- | --- | --- |
| 2026-09-25 | none | current | New pre-release decision consultancy for optional Asset/Identity and other durable contracts | Reviewed HEAD, bounded working-tree sources and E01-E18; advisory, not implementation or release approval |

Refresh affected findings when canonical IDs, profile mastering, file binding/delivery, consent/erasure ownership, optional-service semantics or proposed remote contracts change. Preserve the finding/mitigation IDs during that review. Future breaking changes remain valid when justified by the better design.
