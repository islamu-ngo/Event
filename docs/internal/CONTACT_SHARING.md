ABOUTME: Documents implemented contact share consent behavior and privacy boundaries.
ABOUTME: Prevents unsupported claims about permanent deletion, mailing-list removal, or server-to-server sharing.

# Contact Sharing

> **Audience:** Admins | Integrators | Contributors
> **Status:** Implemented
> **Owner:** Product/Admin
> **Last Verified:** 2026-08-12
> **Source Anchors:** `Explore.Application/Features/ContactShareConsents/`, `Explore.API/Controllers/ContactShareConsentController.cs`, `Explore.Application/Services/ContactShareConsentService.cs`, `Explore.Domain/EventContactShareConsent.cs`, `Explore.Domain/EventContactShareConsentHistory.cs`, `Explore.Persistence/Repositories/EventContactShareConsentRepository.cs`

Contact sharing records a subject's explicit consent to share registration contact details with an approved organization for a named purpose. The implemented behavior is typed consent storage, append-only lifecycle evidence, withdrawal, organization read access, and audited browser-download export; it is not an email fanout or external mailing-list integration.

## Consent Model

`EventContactShareConsent` is the current-state row scoped by tenant, typed subject, recipient actor, and purpose:

| Field Family | Behavior |
|---|---|
| Recipient | Approved organization actor receiving access. |
| Purpose | `ORGANIZER_FUTURE_COMMUNICATIONS`. |
| Subject | `User`, `RegistrationPurchaser`, `RegistrationParticipant`, or `GuestContact`, represented by `SubjectTypeId` and `SubjectId` with exactly one matching nullable FK. |
| Source event / registration order | Stored in append-only history as provenance; not the consent identity boundary. |
| Snapshot data | Email, normalized email, consent text, and UI version are stored as snapshots. |
| Status | Granted or withdrawn; withdrawal sets `WithdrawnAt`. |
| History | Every grant, regrant, and withdrawal appends an immutable `EventContactShareConsentHistory` snapshot in the same persistence operation. |

Persistence enforces uniqueness for `(TenantId, SubjectTypeId, SubjectId, RecipientActorId, PurposeCode)` and a check constraint requiring exactly one typed subject FK. Do not describe consent as event-scoped; source events are provenance and may be used to narrow an export without changing consent identity.

## Capture Flow

Registration handling calls `IContactShareConsentService.ProcessRegistrationConsent(...)` for current-account consent. The service records consent only when:

1. The registrant opts in.
2. The event publisher resolves to an approved organization.
3. The user has an email snapshot to share.

The purpose is `ORGANIZER_FUTURE_COMMUNICATIONS`. Consent text and UI version are stored with the snapshot, and the service supplies defaults when those values are omitted by the registration flow.

Native participant submissions preserve participant independence. Consent is created only for the exact submitted participant subject; purchaser consent is not copied to another participant. Marketing consent for a child participant is rejected with `CHILD_MARKETING_CONSENT_DISABLED`, while an adult participant in the same order can consent independently. Operational consent for a dependent is a separate future policy and must not be inferred from purchaser or guardian marketing consent.

The registration UI displays the consent checkbox and links users to settings where they can review connected apps.

## API Surface

| Action | Endpoint | Boundary |
|---|---|---|
| List my consents | `GET /api/contactshareconsent/my` | Authenticated user. |
| Check recipient consent | `GET /api/contactshareconsent/check/{recipientActorId}` | Authenticated user. |
| Withdraw consent | `POST /api/contactshareconsent/withdraw/{id}` | Authenticated user for their own consent. |
| List organization shared contacts | `GET /api/contactshareconsent/organization/{recipientActorId}` | Organization shared-contact authorization. |
| Export organization shared contacts | `POST /api/contactshareconsent/organization/{recipientActorId}/export` | Organization shared-contact export authorization. |

Organization read and export requests use application resource authorization for the `event_contact_share_consent` resource kind and the `ViewSharedContacts` or `ExportSharedContacts` action.

## Privacy Boundary

- Organization access reads stored contact snapshots for granted consents.
- Export selects only active consent matching `ORGANIZER_FUTURE_COMMUNICATIONS`; an optional event ID requires matching consent-history provenance for that event.
- Export writes `EventContactShareExport` plus per-consent `EventContactShareExportItem` audit evidence with purpose, policy version, included fields, row count, content hash, exporter, and snapshots.
- Withdrawal changes future application reads by moving the consent to withdrawn status.
- Withdrawal is not a permanent-delete operation and does not prove removal from external mailing lists.
- Paid-event merchant, refund, and support disclosures are catalog publication data, not contact-sharing consent or a basis to export attendee contact details.

Do not document legal or privacy guarantees beyond these source-backed behaviors.

## Verified Export Behavior

Anonymous registration names and answers have a separate event-purpose retention
boundary. Their expiry does not revoke, regrant or rewrite a
`RegistrationConsentRecord`, `EventContactShareConsentHistory` or audited
contact-export receipt. A legal hold preserves required storage but does not grant
continued operational access to expired anonymous names or answers. Generated
registration CSV is not a consent export and remains subject to its own stored
content deadline. See [anonymous retention](DOMAIN.md#anonymous-registration-retention).

Exports are generated as browser-download files in CSV or TSV format. Spreadsheet-formula prefixes are neutralized and file-name segments are sanitized. Withdrawn or wrong-purpose consent is excluded by the repository query. The inspected source does not show server-to-server sharing, outbound email fanout, or external marketing-provider synchronization.

## Admin And User Surfaces

| Surface | Source |
|---|---|
| Registration consent checkbox | `Explore.Blazor.Client/Pages/Events/Components/EventRegistration.razor` |
| User connected-app withdrawal | `Explore.Blazor.Client/Pages/User/Components/SettingsConnectedApps.razor` |
| Organization shared contacts list/export | `Explore.Blazor.Client/Pages/Organizations/OrganizationSharedContacts.razor` |
| Event attendee export | `Explore.Blazor.Client/Pages/Studio/StudioEventAttendees.razor` |

The Studio attendee page renders **Export consented contacts** only when the management Event resource contains the server-authored `export-attendees` HAL relation. The relation itself is permission-bound to `ExportSharedContacts` on the verified organizer organization and routes through the same audited export command with event provenance. It remains an action inside Attendees, not a sidebar capability.

The user settings UI warns that withdrawing consent does not guarantee removal from external mailing lists. Keep that warning unless a future source-backed integration proves stronger behavior.

## Related Documentation

- [ADMIN_GUIDE.md](ADMIN_GUIDE.md) - organization admin workflow context.
- [API.md](API.md) - API conventions and error shape.
- [API_COOKBOOK.md](API_COOKBOOK.md) - integration guidance.
- [SECURITY_OVERVIEW.md](SECURITY_OVERVIEW.md) - authorization action catalog.
