ABOUTME: Generated AI agent contract inventory for registry-governed tools.
ABOUTME: Lists tool metadata, approval posture, HAL requirements, and safe invariants without content-bearing AI data.

# AI Agent Contract Inventory

> Generated from `AiToolContractRegistry`. Do not add prompts, provider responses, raw tool payloads, tenant/user identifiers, endpoint URLs, API keys, model secrets, or private event content.

## Manual Notes

<!-- BEGIN MANUAL NOTES -->
_Add local reviewer notes here. This section is preserved by the generator._
<!-- END MANUAL NOTES -->

## Global Invariants

- Registry catalog visibility is advisory and never grants execution authority.
- Mutating tools remain proposal-first and require human confirmation before native CQS commands execute.
- UI mutation affordances must be gated by HAL link presence, not local role or claim inspection.
- MCP adapters must use the same registry contracts and must not write repositories directly.

## Tool Inventory

| Tool | Kind | Risk | Approval | Confirmation | HAL rel | Routes | Workflows | Contexts | Authorization | Mapper/Executor | Provider | MCP |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Apply event session template sync | ApplyEventSessionTemplateSync | High | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-templates | event-session-template-sync-context | islamuevent_custom_property_template:sync_apply | EventSubResourceAiActionMapper | no | yes |
| Apply event template sync | ApplyEventTemplateSync | High | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-templates | event-template-sync-context | islamuevent_custom_property_template:sync_apply | EventSubResourceAiActionMapper | no | yes |
| Assign event team role | AssignEventTeamRole | Medium | HumanConfirmationRequired | Required | team | /calendar, /events/manage, /events/program, /events/{eventId} | event-team | event-team-context | islamuevent_event:manage-team | EventSubResourceAiActionMapper | no | yes |
| Assign session to group | AssignSessionToEventSessionGroup | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-program | event-session-group-context | islamuevent_event_session_group:update | EventSubResourceAiActionMapper | no | yes |
| Create event agenda item | CreateEventAgendaItem | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-agenda | event-agenda-item-context | islamuevent_event_agenda_item:create | EventSubResourceAiActionMapper | no | yes |
| Create event custom property definition | CreateEventCustomPropertyDefinition | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-custom-properties | event-custom-property-context | islamuevent_custom_property_definition:create | EventSubResourceAiActionMapper | no | yes |
| Create event day | CreateEventDay | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-agenda | event-day-context | islamuevent_event_day:create | EventSubResourceAiActionMapper | no | yes |
| Create event draft | CreateEventDraft | Medium | HumanConfirmationRequired | Required | create-event | /calendar, /events, /events/new | event-drafting, event-planning | event, selected-references | islamuevent_event:create | CreateEventDraftAiActionMapper | yes | yes |
| Create event session | CreateEventSession | Medium | HumanConfirmationRequired | Required | add-session | /calendar, /events/manage, /events/program, /events/{eventId} | event-sessions | event-session-context | islamuevent_event_session:create | EventSubResourceAiActionMapper | no | yes |
| Create event session group | CreateEventSessionGroup | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-program | event-session-group-context | islamuevent_event_session_group:create | EventSubResourceAiActionMapper | no | yes |
| Create event session template | CreateEventSessionTemplate | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-templates | event-session-template-context | islamuevent_custom_property_template:create | EventSubResourceAiActionMapper | no | yes |
| Create event template | CreateEventTemplate | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-templates | event-template-context | islamuevent_custom_property_template:create | EventSubResourceAiActionMapper | no | yes |
| Delete event | DeleteEvent | High | HumanConfirmationRequired | Required | delete | /calendar, /events/detail, /events/manage, /events/{eventId} | event-deletion, event-management | event, event-management-context | islamuevent_event:delete | DeleteEventAiActionMapper | no | yes |
| Delete event agenda item | DeleteEventAgendaItem | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-agenda | event-agenda-item-context | islamuevent_event_agenda_item:delete | EventSubResourceAiActionMapper | no | yes |
| Delete event custom property definition | DeleteEventCustomPropertyDefinition | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-custom-properties | event-custom-property-context | islamuevent_custom_property_definition:delete | EventSubResourceAiActionMapper | no | yes |
| Delete event day | DeleteEventDay | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-agenda | event-day-context | islamuevent_event_day:delete | EventSubResourceAiActionMapper | no | yes |
| Delete event Islamic aspect | DeleteEventIslamicAspect | High | HumanConfirmationRequired | Required | edit | /calendar, /events/detail, /events/manage, /events/{eventId} | event-aspects, event-management | event, event-aspect-context, event-management-context | islamuevent_event:update | DeleteEventIslamicAspectAiActionMapper | no | yes |
| Delete event session | DeleteEventSession | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-sessions | event-session-context | islamuevent_event_session:delete | EventSubResourceAiActionMapper | no | yes |
| Delete event session group | DeleteEventSessionGroup | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-program | event-session-group-context | islamuevent_event_session_group:delete | EventSubResourceAiActionMapper | no | yes |
| Delete event session template | DeleteEventSessionTemplate | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-templates | event-session-template-context | islamuevent_custom_property_template:delete | EventSubResourceAiActionMapper | no | yes |
| Delete event Tech aspect | DeleteEventTechAspect | High | HumanConfirmationRequired | Required | edit | /calendar, /events/detail, /events/manage, /events/{eventId} | event-aspects, event-management | event, event-aspect-context, event-management-context | islamuevent_event:update | DeleteEventTechAspectAiActionMapper | no | yes |
| Delete event template | DeleteEventTemplate | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-templates | event-template-context | islamuevent_custom_property_template:delete | EventSubResourceAiActionMapper | no | yes |
| Heavy moderate event | HeavyModerateEvent | Critical | HumanConfirmationRequired | Required | moderate-heavy | /calendar, /events/detail, /events/manage, /events/{eventId} | event-management, event-moderation | event, event-management-context, event-moderation-context | islamuevent_event:moderate-heavy | EventModerationAiActionMapper | no | yes |
| Light moderate event | LightModerateEvent | High | HumanConfirmationRequired | Required | moderate-light | /calendar, /events/detail, /events/manage, /events/{eventId} | event-management, event-moderation | event, event-management-context, event-moderation-context | islamuevent_event:moderate-light | EventModerationAiActionMapper | no | yes |
| Publish event | PublishEvent | High | HumanConfirmationRequired | Required | publish | /calendar, /events/detail, /events/manage, /events/{eventId} | event-management, event-publishing | event, event-management-context, event-publish-readiness | islamuevent_event:update | PublishEventAiActionMapper | no | yes |
| Purge event custom property definition | PurgeEventCustomPropertyDefinition | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-custom-properties | event-custom-property-context | islamuevent_custom_property_definition:delete | EventSubResourceAiActionMapper | no | yes |
| Revoke event team role | RevokeEventTeamRole | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-team | event-team-context | islamuevent_event:manage-team | EventSubResourceAiActionMapper | no | yes |
| Set event custom property multi-values | SetEventCustomPropertyMultiValues | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-custom-properties | event-custom-property-context | islamuevent_custom_property_value:update | EventSubResourceAiActionMapper | no | yes |
| Set event custom property value | SetEventCustomPropertyValue | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-custom-properties | event-custom-property-context | islamuevent_custom_property_value:update | EventSubResourceAiActionMapper | no | yes |
| Unassign session from group | UnassignSessionFromEventSessionGroup | High | HumanConfirmationRequired | Required | delete | /calendar, /events/manage, /events/program, /events/{eventId} | event-program | event-session-group-context | islamuevent_event_session_group:update | EventSubResourceAiActionMapper | no | yes |
| Unmoderate event | UnmoderateEvent | High | HumanConfirmationRequired | Required | unmoderate | /calendar, /events/detail, /events/manage, /events/{eventId} | event-management, event-moderation | event, event-management-context, event-moderation-context | islamuevent_event:unmoderate | EventModerationAiActionMapper | no | yes |
| Update event agenda item | UpdateEventAgendaItem | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-agenda | event-agenda-item-context | islamuevent_event_agenda_item:update | EventSubResourceAiActionMapper | no | yes |
| Update event custom property definition | UpdateEventCustomPropertyDefinition | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-custom-properties | event-custom-property-context | islamuevent_custom_property_definition:update | EventSubResourceAiActionMapper | no | yes |
| Update event day | UpdateEventDay | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-agenda | event-day-context | islamuevent_event_day:update | EventSubResourceAiActionMapper | no | yes |
| Update event draft | UpdateEventDraft | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/detail, /events/manage, /events/{eventId} | event-drafting, event-management | event, event-management-context | islamuevent_event:update | UpdateEventDraftAiActionMapper | no | yes |
| Update event session | UpdateEventSession | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-sessions | event-session-context | islamuevent_event_session:update | EventSubResourceAiActionMapper | no | yes |
| Update event session group | UpdateEventSessionGroup | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-program | event-session-group-context | islamuevent_event_session_group:update | EventSubResourceAiActionMapper | no | yes |
| Update event session template | UpdateEventSessionTemplate | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-templates | event-session-template-context | islamuevent_custom_property_template:update | EventSubResourceAiActionMapper | no | yes |
| Update event template | UpdateEventTemplate | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/manage, /events/program, /events/{eventId} | event-templates | event-template-context | islamuevent_custom_property_template:update | EventSubResourceAiActionMapper | no | yes |
| Upsert event Islamic aspect | UpsertEventIslamicAspect | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/detail, /events/manage, /events/{eventId} | event-aspects, event-management | event, event-aspect-context, event-management-context | islamuevent_event:update | UpsertEventIslamicAspectAiActionMapper | no | yes |
| Upsert event Tech aspect | UpsertEventTechAspect | Medium | HumanConfirmationRequired | Required | edit | /calendar, /events/detail, /events/manage, /events/{eventId} | event-aspects, event-management | event, event-aspect-context, event-management-context | islamuevent_event:update | UpsertEventTechAspectAiActionMapper | no | yes |

## Tool Instructions

### ApplyEventSessionTemplateSync

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose apply event session template sync only, and do not claim side effects happened before confirmation.
- Result card: event-session-template-sync-apply-proposal-card

### ApplyEventTemplateSync

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose apply event template sync only, and do not claim side effects happened before confirmation.
- Result card: event-template-sync-apply-proposal-card

### AssignEventTeamRole

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose assign event team role only, and do not claim side effects happened before confirmation.
- Result card: event-team-role-proposal-card

### AssignSessionToEventSessionGroup

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose assign session to group only, and do not claim side effects happened before confirmation.
- Result card: event-session-group-assignment-proposal-card

### CreateEventAgendaItem

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose create event agenda item only, and do not claim side effects happened before confirmation.
- Result card: event-agenda-item-proposal-card

### CreateEventCustomPropertyDefinition

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose create event custom property definition only, and do not claim side effects happened before confirmation.
- Result card: event-custom-property-definition-proposal-card

### CreateEventDay

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose create event day only, and do not claim side effects happened before confirmation.
- Result card: event-day-proposal-card

### CreateEventDraft

- Availability: Available only when AI tool proposals are enabled and the current API/HAL context allows event creation.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Create a draft proposal only. Put poster-derived date, time, location, gender mode, and primary-session speaker actor references in structured fields instead of prose; keep description as a short summary. The initial event draft may include at most one primary session because event creation creates the first draft session by convention. Use the dedicated event-session draft workflow only after an event exists and the source clearly contains additional sessions. Do not publish, invite attendees, assign roles, or claim the event exists before the user confirms the proposal.
- Result card: event-draft-proposal-card

### CreateEventSession

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose create event session only, and do not claim side effects happened before confirmation.
- Result card: event-session-proposal-card

### CreateEventSessionGroup

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose create event session group only, and do not claim side effects happened before confirmation.
- Result card: event-session-group-proposal-card

### CreateEventSessionTemplate

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose create event session template only, and do not claim side effects happened before confirmation.
- Result card: event-session-template-proposal-card

### CreateEventTemplate

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose create event template only, and do not claim side effects happened before confirmation.
- Result card: event-template-proposal-card

### DeleteEvent

- Availability: Available only when the current API/HAL event context exposes the delete affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read event management context first, use its concurrency stamp, require explicit destructive confirmation metadata, and do not claim the event was deleted before the user confirms the proposal.
- Result card: event-delete-proposal-card

### DeleteEventAgendaItem

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose delete event agenda item only, and do not claim side effects happened before confirmation.
- Result card: event-agenda-item-delete-proposal-card

### DeleteEventCustomPropertyDefinition

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose delete event custom property definition only, and do not claim side effects happened before confirmation.
- Result card: event-custom-property-definition-delete-proposal-card

### DeleteEventDay

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose delete event day only, and do not claim side effects happened before confirmation.
- Result card: event-day-delete-proposal-card

### DeleteEventIslamicAspect

- Availability: Available only when the current API/HAL event context exposes the edit affordance for event aspect management.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read event management context first, use its concurrency stamp, require explicit destructive confirmation metadata, and do not claim the Islamic aspect was deleted before the user confirms the proposal.
- Result card: event-islamic-aspect-delete-proposal-card

### DeleteEventSession

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose delete event session only, and do not claim side effects happened before confirmation.
- Result card: event-session-delete-proposal-card

### DeleteEventSessionGroup

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose delete event session group only, and do not claim side effects happened before confirmation.
- Result card: event-session-group-delete-proposal-card

### DeleteEventSessionTemplate

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose delete event session template only, and do not claim side effects happened before confirmation.
- Result card: event-session-template-delete-proposal-card

### DeleteEventTechAspect

- Availability: Available only when the current API/HAL event context exposes the edit affordance for event aspect management.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read event management context first, use its concurrency stamp, require explicit destructive confirmation metadata, and do not claim the Tech aspect was deleted before the user confirms the proposal.
- Result card: event-tech-aspect-delete-proposal-card

### DeleteEventTemplate

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose delete event template only, and do not claim side effects happened before confirmation.
- Result card: event-template-delete-proposal-card

### HeavyModerateEvent

- Availability: Available only when the current API/HAL event management context exposes the required moderation affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read event management context first, use the current concurrency stamp, require the matching moderation HAL affordance, propose heavy moderate event only, and do not claim moderation happened before confirmation.
- Result card: event-moderation-heavy-proposal-card

### LightModerateEvent

- Availability: Available only when the current API/HAL event management context exposes the required moderation affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read event management context first, use the current concurrency stamp, require the matching moderation HAL affordance, propose light moderate event only, and do not claim moderation happened before confirmation.
- Result card: event-moderation-light-proposal-card

### PublishEvent

- Availability: Available only when the current API/HAL event context exposes the publish affordance and publish readiness is ready.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read event management context and publish readiness first, use the current concurrency stamp, propose publishing only when readiness is ready, and do not claim the event was published before the user confirms the proposal.
- Result card: event-publish-proposal-card

### PurgeEventCustomPropertyDefinition

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose purge event custom property definition only, and do not claim side effects happened before confirmation.
- Result card: event-custom-property-definition-purge-proposal-card

### RevokeEventTeamRole

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose revoke event team role only, and do not claim side effects happened before confirmation.
- Result card: event-team-role-revoke-proposal-card

### SetEventCustomPropertyMultiValues

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose set event custom property multi-values only, and do not claim side effects happened before confirmation.
- Result card: event-custom-property-multi-value-proposal-card

### SetEventCustomPropertyValue

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose set event custom property value only, and do not claim side effects happened before confirmation.
- Result card: event-custom-property-value-proposal-card

### UnassignSessionFromEventSessionGroup

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: ShowWarningsBeforeConfirmation
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose unassign session from group only, and do not claim side effects happened before confirmation.
- Result card: event-session-group-unassign-proposal-card

### UnmoderateEvent

- Availability: Available only when the current API/HAL event management context exposes the required moderation affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read event management context first, use the current concurrency stamp, require the matching moderation HAL affordance, propose unmoderate event only, and do not claim moderation happened before confirmation.
- Result card: event-unmoderation-proposal-card

### UpdateEventAgendaItem

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose update event agenda item only, and do not claim side effects happened before confirmation.
- Result card: event-agenda-item-update-proposal-card

### UpdateEventCustomPropertyDefinition

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose update event custom property definition only, and do not claim side effects happened before confirmation.
- Result card: event-custom-property-definition-update-proposal-card

### UpdateEventDay

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose update event day only, and do not claim side effects happened before confirmation.
- Result card: event-day-update-proposal-card

### UpdateEventDraft

- Availability: Available only when the current API/HAL event context exposes the edit affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read event management context first, use its concurrency stamp, propose a draft update only, and do not claim the event was updated before the user confirms the proposal.
- Result card: event-draft-update-proposal-card

### UpdateEventSession

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose update event session only, and do not claim side effects happened before confirmation.
- Result card: event-session-update-proposal-card

### UpdateEventSessionGroup

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose update event session group only, and do not claim side effects happened before confirmation.
- Result card: event-session-group-update-proposal-card

### UpdateEventSessionTemplate

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose update event session template only, and do not claim side effects happened before confirmation.
- Result card: event-session-template-update-proposal-card

### UpdateEventTemplate

- Availability: Available only when the current API/HAL context exposes the required event-management affordance.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read the current management context first, use server-issued identifiers and concurrency stamps, propose update event template only, and do not claim side effects happened before confirmation.
- Result card: event-template-update-proposal-card

### UpsertEventIslamicAspect

- Availability: Available only when the current API/HAL event context exposes the edit affordance for event aspect management.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read event management context first, use its concurrency stamp, include the Islamic aspect module context, and do not claim the aspect was changed before the user confirms the proposal.
- Result card: event-islamic-aspect-upsert-proposal-card

### UpsertEventTechAspect

- Availability: Available only when the current API/HAL event context exposes the edit affordance for event aspect management.
- Follow-up policy: AskClarifyingQuestionBeforeProposal
- Safe action instructions: Read event management context first, use its concurrency stamp, include the Tech aspect module context, and do not claim the aspect was changed before the user confirms the proposal.
- Result card: event-tech-aspect-upsert-proposal-card

