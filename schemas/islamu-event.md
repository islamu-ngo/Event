// ABOUTME: DBML schema reference for the ISLAMU Event platform database.
// ABOUTME: DBML is maintained alongside authoritative EF Core migrations/model snapshot.

Project islamu_event {
  database_type: 'PostgreSQL'
  Note: 'ISLAMU Event multi-tenant platform with ATProto federation and modular event composition. This is the logical unprefixed model: PostgreSQL and SQL Server place these names in the configured schema; SQLite, MariaDB, and MySQL materialize them with the fixed ie_ prefix.'
}

// Generated application lifecycle heads (2026-08-29):
// PostgreSQL 20260829001843, SQLite 20260829002111,
// SQL Server 20260829002119, MariaDB 20260829002127,
// MySQL 20260829002134. Physical identifier shortening is provider-owned;
// this DBML remains the logical unprefixed model.

// ============================================================
// Lookup / Reference Tables (int PK, ValueGeneratedNever)
// ============================================================

Table "actor_types" {
  "id" int [pk, not null]
  "full_name" varchar(200) [not null]
  "master_code" varchar(100) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_actor_types_master_code']
  }

  Note: 'Lookup: classifies global actors. Values: User(1), Organization(2), Bot(3), Group(4), System(5), ExternalUnclassified(6). Seeded.'
}

Table "role_scopes" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_role_scopes_master_code']
  }

  Note: 'Lookup: RBAC role and permission scopes. Values: Platform(0), Tenant(1), Organization(2), Group(3), Event(4). Seeded.'
}

Table "setting_scopes" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_setting_scopes_master_code']
  }

  Note: 'Lookup: configuration hierarchy scopes. Values: System(0), Instance(1), Tenant(2), Organization(3), Group(4), User(5). Seeded.'
}

Table "setting_value_types" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_setting_value_types_master_code']
  }

  Note: 'Lookup: system setting value data types. Values: String(0), Integer(1), Boolean(2), Decimal(3), Json(4), DateTime(5). Seeded.'
}

Table "secret_source_types" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_secret_source_types_master_code']
  }

  Note: 'Lookup: secret value source types. Values: Infisical(0), EnvironmentVariable(1). Seeded.'
}

Table "secret_validation_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_secret_validation_statuses_master_code']
  }

  Note: 'Lookup: secret validation lifecycle. Values: NotValidated(0), Success(1), Failure(2). Seeded.'
}

Table "analytics_providers" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_analytics_providers_master_code']
  }

  Note: 'Lookup: supported analytics engines. Values: PostHog(1), Plausible(2), GoogleAnalytics(3). Seeded.'
}

Table "support_access_audit_event_types" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)
  "is_lifecycle_event" boolean [not null]

  indexes {
    master_code [unique, name: 'ix_support_access_audit_event_types_master_code']
  }

  Note: 'Lookup: support-access audit event categories. Values: Started(1), Stopped(2), Expired(3), Revoked(4), Denied(5), RequestObserved(6), CommandCommitted(7). Seeded.'
}

Table "support_access_end_reasons" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_support_access_end_reasons_master_code']
  }

  Note: 'Lookup: support-access terminal reasons. Values: UserStopped(1), Expired(2), ForceStopped(3), RevokedByPolicy(4), Replaced(5). Seeded.'
}

Table "support_access_modes" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)
  "allows_writes" boolean [not null]

  indexes {
    master_code [unique, name: 'ix_support_access_modes_master_code']
  }

  Note: 'Lookup: support-access permission modes. Values: ReadOnly(1), Write(2). Write mode is separately governed by instance settings.'
}

Table "support_access_session_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)
  "is_terminal" boolean [not null]

  indexes {
    master_code [unique, name: 'ix_support_access_session_statuses_master_code']
  }

  Note: 'Lookup: support-access lifecycle states. Values: PendingApproval(1), Active(2), Stopped(3), Expired(4), Revoked(5). Seeded.'
}

Table "approval_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_approval_statuses_master_code']
  }

  Note: 'Lookup: entity approval lifecycle. Values: Pending(1), Approved(2), Rejected(3), Deferred(4). Seeded.'
}

Table "audience_ages" {
  "id" int [pk, not null]
  "master_code" text [not null]
  "full_name" text [not null]
  "description" text
  "min_age" int
  "max_age" int

  indexes {
    master_code [unique, name: 'ix_audience_ages_master_code']
  }

  Note: 'Lookup: age-group targeting. Values: AllAges(1), Kids(2), Teens(3), Adults(4). Seeded.'
}

Table "audience_genders" {
  "id" int [pk, not null]
  "master_code" text [not null]
  "full_name" text [not null]
  "description" text

  indexes {
    master_code [unique, name: 'ix_audience_genders_master_code']
  }

  Note: 'Lookup: gender-based targeting. Values: Mixed(1), MenOnly(2), WomenOnly(3). Seeded.'
}

Table "category_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_category_types_master_code']
  }

  Note: 'Lookup: classifies categories for different contexts (Events, Organizations, Users). Seeded.'
}

Table "did_custody_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_did_custody_types_master_code']
  }

  Note: 'Lookup: ATProto DID key management model. Values: SelfCustody(1), PlatformManaged(2). Seeded.'
}

Table "event_formats" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_event_formats_master_code']
  }

  Note: 'Lookup: event delivery mode. Values: InPerson(1), Virtual(2), Hybrid(3). Seeded.'
}

Table "participation_handling_modes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_participation_handling_modes_master_code']
  }

  Note: 'Lookup: participation authority. Values: INFORMATION_ONLY(1), WALK_IN(2), EXTERNAL_MANAGED(3), PLATFORM_MANAGED(4). Runtime-seeded.'
}

Table "advance_registration_obligations" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_advance_registration_obligations_master_code']
  }

  Note: 'Lookup: advance-registration requirement. Values: NOT_APPLICABLE(1), OPTIONAL(2), REQUIRED(3). Runtime-seeded.'
}

Table "identity_access_modes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_identity_access_modes_master_code']
  }

  Note: 'Lookup: participation identity access. Values: ACCOUNT_REQUIRED(1), GUEST_ALLOWED(2), CAPABILITY_TOKEN_ALLOWED(3). Runtime-seeded.'
}

Table "ticket_catalog_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_ticket_catalog_statuses_master_code']
  }

  Note: 'Lookup: ticket catalog lifecycle. Values: DRAFT(1), PUBLISHED(2), RETIRED(3). Runtime-seeded.'
}

Table "ticket_pricing_modes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_ticket_pricing_modes_master_code']
  }

  Note: 'Lookup: ticket pricing behavior. Values: FIXED(1), FREE(2), DONATION(3), PAY_WHAT_YOU_CAN(4), SLIDING_SCALE(5). Runtime-seeded.'
}

Table "participant_data_collection_modes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_participant_data_collection_modes_master_code']
  }

  Note: 'Lookup: ticket participant-data collection. Values: NONE(1), LEAD_BOOKER_ONLY(2), PER_TICKET_OPTIONAL(3), PER_TICKET_REQUIRED(4), DEFERRED_ASSIGNMENT(5). Runtime-seeded.'
}

Table "capacity_oversell_policies" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_capacity_oversell_policies_master_code']
  }

  Note: 'Lookup: capacity oversell behavior. Values: DISALLOW(1), ALLOW(2). Runtime-seeded.'
}

Table "capacity_hold_policies" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_capacity_hold_policies_master_code']
  }

  Note: 'Lookup: capacity reservation behavior. Values: NO_HOLD_UNTIL_READY(1), TIMED_HOLD_ON_SELECTION(2), APPROVAL_NO_HOLD(3), WAITLIST_WHEN_FULL(4). Runtime-seeded; migration seeds these rows before legacy pools are backfilled to NO_HOLD_UNTIL_READY(1).'
}

Table "booking_party_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_booking_party_types_master_code']
  }

  Note: 'Lookup: registration-order booking party. Values: INDIVIDUAL(1), HOUSEHOLD(2), ORGANIZATION(3), COMPANY(4), COMMUNITY_GROUP(5). Runtime-seeded.'
}

Table "registration_order_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_order_statuses_master_code']
  }

  Note: 'Lookup: registration-order workflow lifecycle. Values: DRAFT(1) through NEEDS_RECONCILIATION(13). Runtime-seeded.'
}

Table "payment_attempt_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_payment_attempt_statuses_master_code']
  }

  Note: 'Lookup: provider-neutral payment-attempt lifecycle. Values: Created(1), DispatchPending(2), RequiresAction(3), Processing(4), Succeeded(5), Failed(6), Cancelled(7), Unknown(8). Runtime-seeded.'
}

Table "promotion_definition_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_promotion_definition_statuses_master_code']
  }

  Note: 'Lookup: promotion-definition lifecycle. Values: DRAFT(1), PUBLISHED(2), REVOKED(3). Runtime-seeded.'
}

Table "promotion_reservation_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_promotion_reservation_statuses_master_code']
  }

  Note: 'Lookup: promotion-reservation lifecycle. Values: ACTIVE(1), CONSUMED(2), RELEASED(3), EXPIRED(4). Runtime-seeded.'
}

Table "registration_inventory_hold_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_inventory_hold_statuses_master_code']
  }

  Note: 'Lookup: inventory reservation lifecycle. Values: ACTIVE(1), CONSUMED(2), RELEASED(3), EXPIRED(4), CANCELLED(5). Runtime-seeded.'
}

Table "entitlement_scope_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_entitlement_scope_types_master_code']
  }

  Note: 'Lookup: ticket entitlement target. Values: EVENT(1), EVENT_DAY(2), EVENT_SESSION(3). Runtime-seeded.'
}

Table "entitlement_selection_rules" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_entitlement_selection_rules_master_code']
  }

  Note: 'Lookup: ticket entitlement selection. Values: ALL_INCLUDED(1), FIXED_SELECTION(2), CHOOSE_ONE(3), CHOOSE_UP_TO_N(4). Runtime-seeded.'
}

Table "event_provenance_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_event_provenance_types_master_code']
  }

  Note: 'Lookup: event listing provenance. Values: OrganizerCreated(1), CommunityReported(2), TenantCurated(3), Imported(4), Federated(5). Runtime-seeded.'
}

Table "event_public_action_kinds" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_event_public_action_kinds_master_code']
  }

  Note: 'Lookup: moderated external action purpose. Values: OriginalSource(1), ExternalEventPage(2), ExternalRegistration(3), OptionalQuestionnaire(4), Livestream(5), OrganizerContact(6). Runtime-seeded.'
}

Table "event_public_action_health_states" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_event_public_action_health_states_master_code']
  }

  Note: 'Lookup: moderated action health state. Values: PendingReview(1), Active(2), Broken(3), Unsafe(4), Disabled(5), Expired(6). Runtime-seeded.'
}

Table "event_organizer_claim_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_event_organizer_claim_statuses_master_code']
  }

  Note: 'Lookup: organizer claim lifecycle. Values: Pending(1), EvidenceRequired(2), Approved(3), Rejected(4), Withdrawn(5), Expired(6). Runtime-seeded.'
}

Table "event_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_event_statuses_master_code']
  }

  Note: 'Lookup: event lifecycle state. Values: Draft(1), Published(2), Cancelled(3), Completed(4), Archived(5), Moderated(6). Seeded.'
}

Table "event_session_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  Note: 'Lookup: event-session lifecycle state. Values: Draft(1), Submitted(2), UnderReview(3), Approved(4), Published(5), Rejected(6), Cancelled(7), Archived(8), Completed(9), Moderated(10). Seeded. Draft/internal/completed/moderated statuses may be unscheduled or hidden and are not public.'
}

Table "event_types" {
  "id" int [pk, not null]
  "full_name" varchar(500) [not null]
  "master_code" varchar(500) [not null]
  "description" varchar(500)
  "tenant_id" uuid

  indexes {
    tenant_id
    master_code [unique, name: 'ix_event_types_master_code', note: 'filter: tenant_id IS NULL']
    (tenant_id, master_code) [unique, name: 'ix_event_types_tenant_master_code', note: 'filter: tenant_id IS NOT NULL']
  }

  Note: 'Lookup: categorizes events (e.g. Conference, Webinar, Workshop). TenantId null = instance-wide default.'
}

Table "file_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_file_types_master_code']
  }

  Note: 'Lookup: media/document classifications. Values: Image(1), Video(2), Document(3), Archive(4). Seeded.'
}

Table "group_positions" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_group_positions_master_code']
  }

  Note: 'Lookup: formal roles within a group. Values: Lead(1), Admin(2), Moderator(3), Member(4). Seeded.'
}

Table "languages" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_languages_master_code']
  }

  Note: 'Lookup: ISO language codes for localization and content metadata. Seeded.'
}

Table "madhabs" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_madhabs_master_code']
  }

  Note: 'Lookup: Islamic schools of jurisprudence. Values: Hanafi(1), Maliki(2), Shafii(3), Hanbali(4), Other(5). Seeded.'
}

Table "organization_positions" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_organization_positions_master_code']
  }

  Note: 'Lookup: formal roles within an organization. Values: CEO(1), Admin(2), Staff(3), Volunteer(4). Seeded.'
}

Table "owner_types" {
  "id" int [pk, not null]
  "master_code" text [not null]
  "full_name" text [not null]
  "description" text

  indexes {
    master_code [unique, name: 'ix_owner_types_master_code']
  }

  Note: 'Lookup: classifies entities that can own resources. Values: User(1), Organization(2), Group(3), Tenant(4), InstanceAdmin(5). Seeded.'
}

Table "registration_modes" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_modes_master_code']
  }

  Note: 'Lookup: registration policy. Values: Open(1), ApprovalRequired(2), InviteOnly(3), Closed(4). Seeded.'
}

Table "tag_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_tag_types_master_code']
  }

  Note: 'Lookup: classifies tags for different contexts. Seeded.'
}

Table "tenant_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)
  "is_active_state" boolean [not null]

  indexes {
    master_code [unique, name: 'ix_tenant_statuses_master_code']
  }

  Note: 'Lookup: tenant lifecycle. Values: Active(1), Suspended(2), Provisioning(3), Deactivated(4). Seeded.'
}

Table "visibility_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_visibility_types_master_code']
  }

  Note: 'Lookup: discovery level. Values: Public(1), Private(2), Unlisted(3), MembersOnly(4). Seeded.'
}

Table "schedule_item_kinds" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_schedule_item_kinds_master_code']
  }

  Note: 'Lookup: classifies agenda items. Values: Intro(1), Talk(2), QAndA(3), Break(4), Prayer(5), Outro(6), Logistics(7), Custom(8). Seeded.'
}

Table "event_registration_policies" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_event_registration_policies_master_code']
  }

  Note: 'Lookup: defines which registration scopes are allowed for an event. Values: WholeEventOnly(1), WholeDayOnly(2), SessionSelectionOnly(3), WholeEventOrDay(4), WholeEventOrSession(5), Flexible(6). Seeded.'
}

Table "registration_scopes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_scopes_master_code']
  }

  Note: 'Lookup: granularity of a registration intent. Values: Event(1), Day(2), SessionSelection(3). Seeded.'
}

Table "external_api_key_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  "is_usable" boolean [not null]

  indexes {
    (master_code) [unique, name: 'ix_external_api_key_statuses_master_code']
  }

  Note: 'Lookup: external API key state. Values: Active(1), Revoked(2), Expired(3). Seeded.'
}


Table "external_api_key_credit_periods" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_external_api_key_credit_periods_master_code']
  }

  Note: 'Lookup: credit reset frequency. Values: None(1), Daily(2), Weekly(3), Monthly(4). Seeded.'
}

Table "external_api_key_owner_types" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_external_api_key_owner_types_master_code']
  }

  Note: 'Lookup: external API key owner classifiers. Values: User(1), Organization(2), Group(3), Tenant(4), InstanceAdmin(5). Seeded.'
}

Table "notification_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_notification_types_master_code']
  }

  Note: 'Lookup: notification classification. Seeded.'
}

Table "notification_scope_types" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_notification_scope_types_master_code']
  }

  Note: 'Lookup: notification scope classifiers. Values: User(1), Organization(2), Group(4), System(5). Seeded.'
}

Table "notification_entity_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_notification_entity_types_master_code']
  }

  Note: 'Lookup: type of entity a notification relates to. Seeded.'
}

Table "notification_reasons" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_notification_reasons_master_code']
  }

  Note: 'Lookup: why a notification was triggered. Seeded.'
}

Table "actor_subscription_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ux_actor_subscription_statuses_master_code']
  }

  Note: 'Lookup: actor subscription lifecycle. Values: Active(1), Unsubscribed(2), Blocked(3). Seeded.'
}

Table "actor_subscription_notification_levels" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ux_actor_subscription_notification_levels_master_code']
  }

  Note: 'Lookup: per-actor subscription delivery level. Values: None(1), All(2), Personalized(3). Seeded.'
}

Table "event_session_kinds" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_event_session_kinds_master_code']
  }

  Note: 'Lookup: classifying a program item/session (talk, workshop, panel, activity, etc.). Seeded.'
}

// ============================================================
// System / Configuration Tables
// ============================================================

Table "app_settings" {
  "config_key" varchar(256) [pk, not null]
  "encrypted_value" text [not null]
  "key_version" int [not null]
  "encrypted_at" timestamptz [not null]
  "encrypted_by" uuid
  "is_sensitive" boolean [not null]
  "description" varchar(1000)
  "category" varchar(100)
  "value_type" int [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "row_version" bytea [not null]

  indexes {
    category [name: 'ix_app_settings_category']
    is_sensitive [name: 'ix_app_settings_is_sensitive']
  }

  Note: 'Encrypted system settings. Check: CK_AppSettings_NoHighValueSecrets — blocks database/security keys.'
}

Table "system_settings" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "setting_key" varchar(256) [not null]
  "value" text [not null]
  "setting_value_type_id" int [not null]
  "is_locked" boolean [not null]
  "allowed_values" jsonb
  "description" varchar(1000)
  "category" varchar(100)
  "display_order" int [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    setting_key [unique]
  }
}

Table "configuration_change_logs" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "user_id" uuid [not null]
  "timestamp" timestamptz [not null]
  "setting_key" varchar(256) [not null]
  "old_value" text
  "new_value" text [not null]
  "setting_scope_id" int [not null]
  "scope_id" uuid
  "action_type" varchar(50) [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    user_id
    setting_key
    timestamp
    (setting_scope_id, scope_id) [name: 'ix_configuration_change_logs_setting_scope_id_scope_id']
  }
}

Table "secret_bindings" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "setting_key" varchar(256) [not null]
  "qualifier" varchar(128) [not null, default: '']
  "setting_scope_id" int [not null]
  "scope_id" uuid
  "environment_variable_name" varchar(256)
  "infisical_environment" varchar(64)
  "infisical_key" varchar(256)
  "infisical_path" varchar(512)
  "is_locked" boolean [not null]
  "last_validated_at" timestamptz
  "last_validated_by" uuid
  "last_validation_error" varchar(1000)
  "secret_validation_status_id" int [not null]
  "secret_source_type_id" int [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (setting_key, qualifier) [unique, name: 'ix_secret_bindings_setting_key_instance_unique', note: 'filter: scope_id IS NULL']
    (setting_key, scope_id, qualifier) [unique, name: 'ix_secret_bindings_setting_key_scope_id_tenant_unique', note: 'filter: scope_id IS NOT NULL']
    secret_source_type_id [name: 'ix_secret_bindings_secret_source_type_id']
    secret_validation_status_id [name: 'ix_secret_bindings_secret_validation_status_id']
    (setting_scope_id, scope_id) [name: 'ix_secret_bindings_setting_scope_id_scope_id']
  }

  Note: 'Maps application settings to external secrets providers. Qualifier separates same setting/scope bindings for provider-specific secret roles.'
}


// ============================================================
// RBAC (Roles & Permissions)
// ============================================================

Table "roles" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  "role_scope_id" int [not null]
  "is_system" boolean [not null]

  indexes {
    master_code [unique, name: 'ix_roles_mastercode']
    role_scope_id [name: 'ix_roles_role_scope_id']
    (id, role_scope_id) [unique, name: 'ak_roles_id_role_scope_id']
  }
}

Table "permissions" {
  "id" int [pk, not null, note: 'auto-increment']
  "resource_kind" varchar(100) [not null]
  "action" varchar(50) [not null]
  "field_scope" varchar(100)
  "master_code" varchar(200) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  "group_name" varchar(100) [not null]
  "role_scope_id" int [not null]
  "is_system" boolean [not null]
  "is_filtered" boolean [not null]
  "is_active" boolean [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    master_code [unique, name: 'ix_permissions_mastercode']
    (resource_kind, action) [name: 'ix_permissions_resource_action']
    role_scope_id [name: 'ix_permissions_role_scope_id']
  }
}

Table "role_permissions" {
  "role_id" int [pk, not null]
  "permission_id" int [pk, not null]
  "granted_at" timestamptz [not null]
  "granted_by" uuid

  indexes {
    role_id [name: 'ix_rolepermissions_role']
    permission_id [name: 'ix_rolepermissions_permission']
  }
}

// ============================================================
// Federation / AT Protocol
// ============================================================

Table "atproto_records" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "did" varchar(255) [not null]
  "collection" varchar(255) [not null]
  "record_key" varchar(255) [not null]
  "cid" varchar(255)
  "uri" varchar(500)
  "direction" int [not null, note: 'Inbound(1), Outbound(2), Reconciled(3)']
  "provenance" int [not null, note: 'Jetstream(1), LocalLifecycle(2), JetstreamEcho(3)']
  "source_version" bigint [not null]
  "source_cursor" bigint
  "record_json" jsonb
  "record_hash" varchar(64)
  "subject_uri" varchar(500)
  "subject_cid" varchar(255)
  "indexed_at" timestamptz
  "updated_at" timestamptz [not null]
  "tombstoned_at" timestamptz

  indexes {
    (did, collection, record_key) [unique, name: 'ux_atproto_records_identity']
    uri [unique, name: 'ux_atproto_records_uri', note: 'filter: uri IS NOT NULL']
    subject_uri [name: 'ix_atproto_records_subject_uri', note: 'filter: subject_uri IS NOT NULL']
  }

  Note: 'Global canonical AT Protocol record. Tenant visibility and outbound ownership are stored separately.'
}

Table "atproto_event_projections" {
  "atproto_record_id" uuid [pk, not null]
  "name" varchar(240) [not null]
  "description" varchar(4000)
  "created_at" timestamptz [not null]
  "starts_at" timestamptz
  "ends_at" timestamptz
  "mode" varchar(80)
  "status" varchar(80)
  "rsvp_expected" boolean
  "location_summary" varchar(500)
  "source_url" varchar(2048)
  "source_version" bigint [not null]
  "materialized_at" timestamptz [not null]

  indexes {
    (starts_at, atproto_record_id) [name: 'ix_atproto_event_projections_starts_at']
    (created_at, atproto_record_id) [name: 'ix_atproto_event_projections_created_at']
    (name, atproto_record_id) [name: 'ix_atproto_event_projections_name']
  }

  Note: 'Bounded typed public projection materialized atomically with its canonical event record; source URLs are HTTPS-only and tenant presentation is resolved separately.'
}

Table "sync_states" {
  "id" int [pk, not null]
  "service" varchar(500) [not null]
  "cursor" bigint [not null]
  "last_seq_time" timestamptz
  "updated_at" timestamptz [not null]

  indexes {
    service [unique]
  }
}

Table "atproto_record_tenant_presentations" {
  "tenant_id" uuid [pk, not null]
  "atproto_record_id" uuid [pk, not null]
  "is_visible" boolean [not null]
  "source_version" bigint [not null]
  "evaluated_at" timestamptz [not null]

  indexes {
    (tenant_id, is_visible, evaluated_at) [name: 'ix_atproto_record_presentations_visible']
    atproto_record_id [name: 'ix_atproto_record_tenant_presentations_atproto_record_id']
  }

  Note: 'Tenant capability/presentation decision for one global canonical record.'
}

Table "atproto_outbound_record_ownerships" {
  "atproto_record_id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "user_id" uuid [not null]
  "source_entity_type" varchar(100) [not null]
  "source_entity_id" uuid [not null]
  "source_version" uuid [not null]
  "created_at" timestamptz [not null]
  "updated_at" timestamptz [not null]

  indexes {
    (tenant_id, source_entity_type, source_entity_id) [unique, name: 'ux_atproto_outbound_ownership_source']
    (tenant_id, user_id) [name: 'ix_atproto_outbound_ownership_user']
    user_id [name: 'ix_atproto_outbound_record_ownerships_user_id']
  }

  Note: 'Tenant/user/lifecycle authority for a locally published canonical record.'
}

Table "atproto_jetstream_consumer_states" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "service" varchar(500) [not null]
  "cursor" bigint [not null]
  "last_event_at" timestamptz
  "lease_owner" varchar(200)
  "lease_token" uuid
  "lease_expires_at" timestamptz
  "lease_fence" bigint [not null]
  "updated_at" timestamptz [not null]

  indexes {
    service [unique, name: 'ux_atproto_jetstream_consumer_service']
  }

  Note: 'Single global reclaimable and fenced Jetstream cursor authority.'
}

Table "atproto_jetstream_quarantines" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "consumer_state_id" uuid [not null]
  "cursor" bigint [not null]
  "reason_code" varchar(100) [not null]
  "envelope_hash" varchar(64) [not null]
  "record_identity_hash" varchar(64)
  "event_at" timestamptz [not null]
  "quarantined_at" timestamptz [not null]

  indexes {
    (consumer_state_id, cursor) [unique, name: 'ux_atproto_jetstream_quarantine_cursor']
    (reason_code, quarantined_at) [name: 'ix_atproto_jetstream_quarantine_reason']
  }

  Note: 'Payload-free bounded evidence committed atomically with cursor advancement.'
}

Table "pds_sync_outbox" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "user_id" uuid [not null]
  "did" varchar(255) [not null]
  "collection" varchar(255) [not null]
  "record_key" varchar(255) [not null]
  "operation" int [not null]
  "payload" jsonb
  "payload_hash" varchar(64) [not null]
  "idempotency_key" varchar(255) [not null]
  "pds_host" varchar(500) [not null]
  "source_entity_type" varchar(100) [not null]
  "source_entity_id" uuid [not null]
  "source_version" uuid [not null]
  "atproto_record_id" uuid
  "depends_on_atproto_record_id" uuid
  "depends_on_cid" varchar(255) [note: 'event strongRef CID captured for durable terminal-attempt reconciliation suppression']
  "expected_cid" varchar(255)
  "status" int [not null]
  "created_at" timestamptz [not null]
  "processed_at" timestamptz
  "retry_count" int [not null]
  "last_error" varchar(500)
  "next_retry_at" timestamptz
  "max_retries" int [not null, default: 10, note: 'dead-letter threshold']
  "dead_lettered_at" timestamptz [note: 'when entry was quarantined after exhausting retries']
  "lease_owner" varchar(200)
  "lease_token" uuid
  "lease_expires_at" timestamptz
  "lease_fence" bigint [not null]
  "superseded_by_id" uuid
  "superseded_at" timestamptz
  "settled_uri" varchar(500)
  "settled_cid" varchar(255)

  indexes {
    (tenant_id, idempotency_key) [unique, name: 'ux_pds_sync_outbox_idempotency']
    (tenant_id, source_entity_type, source_entity_id, source_version, operation, payload_hash) [unique, name: 'ux_pds_sync_outbox_source_version', note: 'filter: status IN (1, 2) AND superseded_at IS NULL']
    (status, next_retry_at, lease_expires_at, created_at) [name: 'ix_pds_sync_outbox_worker_poll']
    (tenant_id, user_id, status) [name: 'ix_pds_sync_outbox_owner']
    (did, collection, record_key) [name: 'ix_pds_sync_outbox_record_identity']
    atproto_record_id [name: 'ix_pds_sync_outbox_atproto_record_id']
    depends_on_atproto_record_id [name: 'ix_pds_sync_outbox_dependency', note: 'filter: depends_on_atproto_record_id IS NOT NULL']
    superseded_by_id [name: 'ix_pds_sync_outbox_superseded_by_id']
    user_id [name: 'ix_pds_sync_outbox_user_id']
  }

  Note: 'Immutable tenant-owned PDS intent with reclaimable fenced delivery, dependency, supersession, and URI/CID settlement.'
}

// ============================================================
// General Outbox
// ============================================================

Table "outbox_messages" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "aggregate_type" varchar(200) [not null]
  "aggregate_id" uuid [not null]
  "event_type" varchar(200) [not null]
  "payload" jsonb
  "status" int [not null, default: 1]
  "created_at" timestamptz [not null]
  "processed_at" timestamptz
  "retry_count" integer [not null, default: 0]
  "last_error" varchar(2000)
  "next_retry_at" timestamptz
  "max_retries" integer [not null, default: 10]
  "dead_lettered_at" timestamptz

  indexes {
    (status, next_retry_at, created_at) [name: 'IX_OutboxMessages_WorkerPoll']
    (aggregate_type, aggregate_id) [name: 'IX_OutboxMessages_Aggregate']
    (aggregate_type, aggregate_id, event_type, created_at) [name: 'IX_OutboxMessages_Dedup']
  }

  Note: 'General transactional outbox. OutboxProcessor polls for pending messages with exponential backoff retry. Dead-letter after max_retries exhausted.'
}

// ============================================================
// Email Dispatch (Basic Dispatch Mode)
// ============================================================

Table "notification_categories" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
}

Table "notification_ownership_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
}

Table "notification_intent_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
}

Table "notification_recipient_kinds" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
}

Table "notification_preference_channels" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique, note: 'Canonical codes include email, in_app, and push']
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  "sort_order" int [not null]
}

Table "notification_delivery_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique, note: 'PENDING, QUEUED, DELIVERED, SKIPPED, FAILED, DEAD_LETTERED, UNKNOWN, PARKED, SUPERSEDED']
  "full_name" varchar(200) [not null]
  "description" varchar(500)
}

Table "notification_external_delegation_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
}

Table "external_workflow_provider_kinds" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
}

Table "account_authority_kinds" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
}

Table "notification_delivery_policies" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null, unique]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  Note: 'Stable channel-policy codes: registration/event optional email, report case update, report follow-up, required moderation availability, optional moderation context, optional reminder, and required tenant administration.'
}

Table "notification_fanout_occurrences" {
  "id" uuid [pk, not null, note: 'uuidv7() generated before retryable transaction execution']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "session_id" uuid
  "occurred_at" timestamptz [not null]
  "audience_cutoff_at" timestamptz [not null]
  "aggregate_version" uuid [not null]
  "change_set_json" jsonb [not null]
  "safe_before_snapshot_json" jsonb [not null]
  "safe_after_snapshot_json" jsonb [not null]
  "template_key" varchar(160) [not null]
  "template_version" int [not null]
  "delivery_policy_id" int [not null]
  "policy_version" int [not null]
  "priority" int [not null]
  "not_before" timestamptz [not null]
  "source_type" varchar(100) [not null]
  "source_id" uuid [not null]
  "coalescing_key" varchar(300) [not null]
  "coalescing_window_ends_at" timestamptz
  "state" int [not null, note: '1=pending, 2=superseded']
  "superseded_by_occurrence_id" uuid
  "suppression_reason" varchar(100)
  "superseded_at" timestamptz

  indexes {
    (tenant_id, id) [unique, name: 'ak_notification_fanout_occurrences_tenant_id']
    (tenant_id, state, not_before, occurred_at) [name: 'ix_notification_fanout_occurrences_runnable']
    (tenant_id, source_type, source_id, aggregate_version) [name: 'ix_notification_fanout_occurrences_source']
    (tenant_id, coalescing_key, state, occurred_at) [name: 'ix_notification_fanout_occurrences_coalescing']
  }

  Note: 'Immutable event/session change evidence and frozen audience cutoff for resumable recipient fanout. PostgreSQL checks require positive template/policy versions and complete supersession state; the general outbox carries only tenant_id, occurrence_id, and pointer version.'
}

Table "notification_intents" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "category_id" int [not null]
  "ownership_type_id" int [not null]
  "recipient_kind_id" int [not null]
  "status_id" int [not null]
  "template_key" varchar(160) [not null]
  "deduplication_key" varchar(300) [not null]
  "safe_payload_reference" varchar(500)
  "safe_payload_hash" varchar(128)
  "correlation_id" varchar(200)
  "recipient_user_id" uuid [not null]
  "fanout_occurrence_id" uuid
  "event_id" uuid
  "report_id" uuid
  "report_decision_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_notification_intents_tenant_id']
    (tenant_id, id, recipient_user_id) [unique, name: 'ak_notification_intents_tenant_id_recipient']
    (tenant_id, deduplication_key) [unique, name: 'ux_notification_intents_tenant_deduplication_key', note: 'filter: is_deleted = false']
    (tenant_id, recipient_user_id) [name: 'ix_notification_intents_tenant_id_recipient_user_id']
    (tenant_id, status_id, created_at) [name: 'ix_notification_intents_tenant_status_created']
    (tenant_id, fanout_occurrence_id, recipient_user_id) [unique, name: 'ux_notification_intents_tenant_occurrence_recipient']
  }

  Note: 'One logical business occurrence for one required tenant-member recipient. The explicit recipient triple is the principal for email and in-app recipient-equality constraints.'
}

Table "notification_deliveries" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "notification_intent_id" uuid [not null]
  "channel_id" int [not null, note: '1=email, 2=in_app']
  "delivery_policy_id" int [not null]
  "is_required" boolean [not null]
  "policy_version" int [not null]
  "consent_purpose" varchar(100)
  "consent_version" int
  "preference_category_code" varchar(100)
  "preference_enabled" boolean
  "recipient_address_source" int [note: '1=current verified tenant-user email, 2=managed tenant-administrator invitation; null for in-app or unlinked/skipped email']
  "disclosure_level" varchar(100) [not null]
  "template_key" varchar(160) [not null]
  "template_version" int [not null]
  "link_allowed" boolean [not null]
  "notification_id" uuid
  "email_dispatch_outbox_id" uuid
  "status_id" int [not null]
  "provider_message_id" varchar(500)
  "provider_status" varchar(100)
  "failure_category" varchar(100)
  "queued_at" timestamptz
  "completed_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id, notification_intent_id, channel_id) [unique, name: 'ak_notification_deliveries_tenant_id_intent_channel']
    (tenant_id, notification_intent_id, channel_id) [unique, name: 'ux_notification_deliveries_tenant_intent_channel']
    (tenant_id, email_dispatch_outbox_id) [unique, name: 'ux_notification_deliveries_tenant_email_dispatch_outbox', note: 'filter: email_dispatch_outbox_id IS NOT NULL']
    (tenant_id, email_dispatch_outbox_id, notification_intent_id, recipient_address_source) [name: 'ix_notification_deliveries_tenant_id_email_dispatch_outbox_id_']
    (tenant_id, notification_id) [unique, name: 'ux_notification_deliveries_tenant_notification', note: 'filter: notification_id IS NOT NULL']
  }

  Note: 'One channel decision/outcome per intent. ck_notification_deliveries_channel_link forbids dual links, requires linked email source snapshots, and requires null source for in-app and unlinked/skipped email. fk_notification_deliveries_notification_tenant binds a linked notification to the delivery tenant. Migration-authored fk_notification_deliveries_notification_same_intent enforces (tenant_id, notification_id, notification_intent_id) because preserved notifications have a nullable intent principal.'
}

Table "notification_external_delegations" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "notification_intent_id" uuid [not null]
  "provider_kind_id" int [not null]
  "account_authority_kind_id" int
  "status_id" int [not null]
  "recipient_kind_id" int [not null]
  "template_key" varchar(160) [not null]
  "safe_payload_hash" varchar(128)
  "external_provider_id" varchar(200)
  "external_correlation_id" varchar(200)
  "external_delivery_status" varchar(100)
  "failure_category" varchar(100)
  "report_id" uuid
  "report_decision_id" uuid
  "requested_at" timestamptz
  "completed_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, notification_intent_id) [name: 'ix_notification_external_delegations_tenant_intent']
    (tenant_id, provider_kind_id, status_id, created_at) [name: 'ix_notification_external_delegations_tenant_provider_status']
  }

  Note: 'Provider/account-authority delegation audit. The composite tenant-intent FK prevents cross-tenant delegation.'
}

Table "managed_tenant_provisioning_operations" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [note: 'Nullable until the operation succeeds']
  "tenant_administrator_user_id" uuid
  "managed_instance_id" uuid [not null]
  "external_customer_reference" varchar(200) [not null]
  "external_request_id" varchar(100) [not null]
  "request_hash" char(64) [not null]
  "request_json" jsonb
  "current_outbox_message_id" uuid [not null]
  "tenant_slug" varchar(100) [not null]
  "status" varchar(20) [not null]
  "completed_at" timestamptz
  "created_at" timestamptz [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ux_managed_tenant_provisioning_operations_tenant_id']
  }

  Note: 'Tenant remains nullable before success. The partial-lifecycle shape is why migration-authored fk_email_dispatch_outbox_managed_operation_tenant enforces (tenant_id, managed_tenant_provisioning_operation_id) rather than using an EF alternate-key relationship.'
}

Table "configuration_manifest_operations" {
  "id" uuid [pk, not null, note: 'Application-generated UUIDv7']
  "mode" varchar(20) [not null, note: 'ValidateOnly or Bootstrap']
  "api_version" varchar(100) [not null]
  "kind" varchar(100) [not null]
  "manifest_name" varchar(100) [not null]
  "digest" char(64) [not null, note: 'SHA-256 over exact manifest bytes; provenance, not idempotency']
  "status" varchar(20) [not null, note: 'Validated, Applied, or Failed']
  "requested_tenant_count" int [not null]
  "created_tenant_count" int [not null]
  "skipped_existing_tenant_count" int [not null]
  "failed_tenant_count" int [not null]
  "reason_code" varchar(200)
  "reason" varchar(500)
  "started_at" timestamptz [not null]
  "completed_at" timestamptz [not null]

  indexes {
    (digest, mode, completed_at) [name: 'ix_configuration_manifest_operations_digest_mode_completed']
    (status, completed_at) [name: 'ix_configuration_manifest_operations_status_completed']
  }

  Note: 'Append-only deployment audit. Stores bounded contract identity and safe terminal outcome only; never raw manifest bytes, paths, setting/document values, secrets, report content, or PII.'
}

Table "configuration_manifest_tenant_results" {
  "id" uuid [pk, not null, note: 'Application-generated UUIDv7']
  "operation_id" uuid [not null]
  "tenant_id" uuid [not null]
  "status" varchar(24) [not null, note: 'Created or SkippedExisting']
  "reason_code" varchar(200) [not null]
  "completed_at" timestamptz [not null]
  "changed_setting_key_names" varchar(4096) [not null]
  "changed_document_key_names" varchar(4096) [not null]

  indexes {
    operation_id [name: 'ix_configuration_manifest_tenant_results_operation_id']
    (tenant_id, status, completed_at) [name: 'ix_configuration_manifest_results_tenant_status_completed']
    (tenant_id, operation_id) [unique, name: 'ux_configuration_manifest_results_tenant_operation']
  }

  Note: 'Append-only tenant-filtered outcome evidence. Changed-key columns contain sorted canonical names only and never corresponding values.'
}

Table "email_dispatch_outbox" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "publish_event_id" uuid [not null]
  "kind" int [not null]
  "source_type" varchar(100) [not null]
  "source_id" uuid [not null]
  "event_id" uuid
  "registration_order_id" uuid
  "notification_intent_id" uuid [not null]
  "recipient_user_id" uuid [not null]
  "recipient_address_source" int [not null, note: 'Only 1=TenantUserVerifiedEmail or 2=ManagedTenantAdministratorInvitation']
  "managed_tenant_provisioning_operation_id" uuid
  "recipient_email" varchar(320) [not null]
  "subject" varchar(500) [not null]
  "plain_text_body" text
  "html_body" text
  "reply_to" varchar(320)
  "status" int [not null]
  "attempt_count" int [not null]
  "max_attempts" int [not null, default: 5]
  "next_renewal_attempt_at" timestamptz
  "next_sweep_attempt_at" timestamptz
  "processing_started_at" timestamptz
  "processing_lease_token" uuid
  "sent_at" timestamptz
  "dead_lettered_at" timestamptz
  "parked_at" timestamptz
  "unknown_at" timestamptz
  "last_failure_category" varchar(100)
  "last_error" varchar(2000)
  "last_failure_at" timestamptz
  "provider_message_id" varchar(500)
  "correlation_id" varchar(200)
  "rabbit_mq_last_published_at" timestamptz
  "rabbit_mq_last_publish_attempt_at" timestamptz
  "rabbit_mq_publish_attempt_count" int [not null]
  "rabbit_mq_last_publish_failure_category" varchar(100)
  "content_redacted_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    event_id [name: 'ix_email_dispatch_outbox_event_id']
    (tenant_id, registration_order_id) [name: 'ix_email_dispatch_outbox_tenant_id_registration_order_id']
    managed_tenant_provisioning_operation_id [name: 'ix_email_dispatch_outbox_managed_tenant_provisioning_operation']
    (tenant_id, id) [unique, name: 'ak_email_dispatch_outbox_tenant_id']
    (tenant_id, id, notification_intent_id) [unique, name: 'ak_email_dispatch_outbox_tenant_id_intent']
    (tenant_id, id, notification_intent_id, recipient_address_source) [unique, name: 'ak_email_dispatch_outbox_tenant_id_intent_address_source']
    (tenant_id, id, publish_event_id) [unique, name: 'ak_email_dispatch_outbox_tenant_id_publish_event']
    (tenant_id, recipient_user_id) [name: 'ix_email_dispatch_outbox_tenant_id_recipient_user_id']
    (tenant_id, notification_intent_id, recipient_user_id) [name: 'ix_email_dispatch_outbox_tenant_id_notification_intent_id_reci']
    (tenant_id, publish_event_id) [unique, name: 'ux_email_dispatch_outbox_tenant_publish_event']
    (tenant_id, notification_intent_id) [unique, name: 'ux_email_dispatch_outbox_tenant_intent']
    (status, next_attempt_at, created_at) [name: 'ix_email_dispatch_outbox_worker_poll']
    (tenant_id, status, last_failure_at) [name: 'ix_email_dispatch_outbox_tenant_status']
    (tenant_id, content_redacted_at, status, sent_at, last_failure_at, created_at) [name: 'ix_email_dispatch_outbox_retention']
  }

  Note: 'Durable SMTP execution row. One email exists per logical intent. Recipient authority is exactly a current verified tenant member or a same-tenant managed invitation operation; ck_email_dispatch_outbox_recipient_authority rejects every other source. ck_email_dispatch_outbox_redaction_fence requires redacted rows to contain no recipient, subject, body, reply-to, free-text error, provider/correlation identifier, scheduling, or processing material.'
}

Table "email_dispatch_attempts" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "email_dispatch_outbox_id" uuid [not null]
  "attempt_number" int [not null]
  "transport" varchar(50) [not null]
  "provider" varchar(100)
  "outcome" int [not null]
  "started_at" timestamptz [not null]
  "completed_at" timestamptz
  "failure_category" varchar(100)
  "sanitized_error_message" varchar(2000)
  "provider_message_id" varchar(500)
  "correlation_id" varchar(200)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (email_dispatch_outbox_id, attempt_number) [unique, name: 'ux_email_dispatch_attempts_outbox_attempt']
    (tenant_id, email_dispatch_outbox_id) [name: 'ix_email_dispatch_attempts_tenant_id_email_dispatch_outbox_id']
    (tenant_id, started_at) [name: 'ix_email_dispatch_attempts_tenant_started']
  }

  Note: 'Per-attempt immutable delivery ledger for SMTP or future transports.'
}

Table "email_dispatch_receipts" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "publish_event_id" uuid [not null]
  "email_dispatch_outbox_id" uuid [not null]
  "status" int [not null]
  "consumer_id" varchar(200)
  "first_seen_at" timestamptz [not null]
  "processing_started_at" timestamptz
  "completed_at" timestamptz
  "failed_at" timestamptz
  "failure_code" varchar(100)
  "failure_message" varchar(1000)
  "provider_message_id" varchar(500)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (email_dispatch_outbox_id, status) [name: 'ix_email_dispatch_receipts_outbox_status']
    (tenant_id, publish_event_id) [unique, name: 'ux_email_dispatch_receipts_tenant_publish_event']
    (tenant_id, email_dispatch_outbox_id) [unique, name: 'ux_email_dispatch_receipts_tenant_outbox']
    (tenant_id, email_dispatch_outbox_id, publish_event_id) [name: 'ix_email_dispatch_receipts_tenant_id_email_dispatch_outbox_id_']
  }

  Note: 'Idempotency receipt shared by current Basic Mode and future queue-backed dispatch modes.'
}

Table "email_dispatch_tenant_controls" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "is_paused" boolean [not null]
  "pause_reason" varchar(500)
  "paused_at" timestamptz
  "paused_by" uuid
  "smtp_available_tokens" integer [note: 'Remaining tenant SMTP admissions in the current one-minute window. Null only with smtp_refill_at.']
  "smtp_refill_at" timestamptz [note: 'Database-clock boundary for refilling the tenant SMTP bucket. Null only with smtp_available_tokens.']
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    tenant_id [unique, name: 'ux_email_dispatch_tenant_controls_tenant']
    (is_paused, updated_at) [name: 'ix_email_dispatch_tenant_controls_pause_state']
  }

  Note: 'Tenant-scoped operational pause/resume control and shared SMTP rate bucket. CHECK smtp pair is jointly null/non-null; token count is nonnegative.'
}

Table "email_dispatch_processor_states" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "processor_code" varchar(32) [not null]
  "optional_reminders_deferred" boolean [not null]
  "smtp_available_tokens" integer [note: 'Remaining global SMTP admissions in the current one-minute window. Null only with smtp_refill_at.']
  "smtp_refill_at" timestamptz [note: 'Database-clock boundary for refilling the global SMTP bucket. Null only with smtp_available_tokens.']
  "updated_at" timestamptz [not null]

  indexes {
    processor_code [unique, name: 'ux_email_dispatch_processor_states_processor_code']
  }

  Note: 'Cross-replica email processor coordination state. The smtp singleton persists optional-reminder hysteresis and the shared global SMTP rate bucket. CHECK smtp pair is jointly null/non-null; token count is nonnegative.'
}

// ============================================================
// Modules
// ============================================================

Table "module_definitions" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "module_key" varchar(50) [not null]
  "name" varchar(100) [not null]
  "description" varchar(500)
  "wizard_schema_url" varchar(500)
  "icon_name" varchar(50)
  "display_order" int [not null]
  "is_active" boolean [not null]
  "category" varchar(50)
  "created_at" timestamptz [not null]
  "updated_at" timestamptz

  indexes {
    module_key [unique]
  }
}

// ============================================================
// Bootstrap
// ============================================================

Table "instance_bootstrap_states" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "status" int [not null, note: '1 Pending, 2 Superseded, 3 Completed']
  "mode" int [not null, note: '1 Interactive, 2 ConfiguredAdministrator']
  "provider_kind" int [note: '1 Keycloak, 2 Atproto; null for Interactive']
  "deployment_mode" int [not null, note: '1 SingleTenant, 2 MultiTenant']
  "generation" bigint [not null, note: 'positive, monotonic per bootstrap attempt']
  "configuration_fingerprint" char(64)
  "selector_fingerprint" char(64)
  "completed_identity_fingerprint" char(64)
  "created_at" timestamptz [not null]
  "superseded_at" timestamptz
  "completed_at" timestamptz
  "completed_by_user_id" uuid

  indexes {
    generation [unique, name: 'ix_instance_bootstrap_state_generation_unique']
    (status, generation) [name: 'ix_instance_bootstrap_state_status_generation']
  }

  Note: 'A state row starts Pending, becomes Superseded when a newer generation replaces it, and ends Completed once bootstrap succeeds. Only deterministic SHA-256 fingerprints are stored; no raw external identity values are persisted.'
}

Table "instance_policy_sets" {
  "id" uuid [pk, not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "xmin" xid [not null, note: 'rowversion / optimistic concurrency']

  Note: 'Obsolete federation policy-slot columns were retired by migration 20260719213000_RetireLegacyDecentralizationSetting. AT Protocol event federation is governed through the hierarchical federation.atproto_events_enabled setting.'
}

// ============================================================
// Support Access
// ============================================================

Table "support_access_sessions" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "actor_user_id" uuid [not null]
  "target_tenant_id" uuid [not null]
  "target_tenant_user_id" uuid
  "status_id" int [not null]
  "mode_id" int [not null]
  "reason_code" varchar(100) [not null]
  "reason_text" varchar(1000) [not null]
  "ticket_reference" varchar(200) [not null]
  "approved_by_user_id" uuid
  "started_at_utc" timestamptz [not null]
  "expires_at_utc" timestamptz [not null]
  "ended_at_utc" timestamptz
  "end_reason_id" int
  "end_reason_text" varchar(200)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (actor_user_id, status_id, expires_at_utc) [name: 'ix_support_access_sessions_actor_status_expires', note: 'expires_at_utc descending']
    approved_by_user_id [name: 'ix_support_access_sessions_approved_by_user_id']
    end_reason_id [name: 'ix_support_access_sessions_end_reason_id']
    (id, actor_user_id, status_id) [name: 'ix_support_access_sessions_id_actor_status']
    mode_id [name: 'ix_support_access_sessions_mode_id']
    status_id [name: 'ix_support_access_sessions_status_id']
    (target_tenant_id, started_at_utc) [name: 'ix_support_access_sessions_target_tenant_started', note: 'started_at_utc descending']
    target_tenant_user_id [name: 'ix_support_access_sessions_target_tenant_user_id']
    actor_user_id [unique, name: 'ux_support_access_sessions_active_actor', note: 'filter: status_id = 2 AND ended_at_utc IS NULL']
  }

  Note: 'Time-boxed, actor-bound, tenant-bound support-access sessions. Check constraints: expires_at_utc > started_at_utc; ended_at_utc is null or after started_at_utc; end_reason_id presence matches ended_at_utc presence.'
}

Table "support_access_audit_events" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "support_access_session_id" uuid [not null]
  "occurred_at_utc" timestamptz [not null]
  "event_type_id" int [not null]
  "actor_user_id" uuid [not null]
  "target_tenant_id" uuid [not null]
  "target_tenant_user_id" uuid
  "route_name" varchar(200)
  "request_name" varchar(200)
  "resource_kind" varchar(200)
  "resource_id" varchar(200)
  "action" varchar(100)
  "outcome" varchar(100) [not null]
  "http_status_code" int
  "correlation_id" varchar(100)
  "trace_id" varchar(100)
  "sanitized_metadata_json" jsonb

  indexes {
    (actor_user_id, occurred_at_utc) [name: 'ix_support_access_audit_events_actor_occurred', note: 'occurred_at_utc descending']
    event_type_id [name: 'ix_support_access_audit_events_event_type_id']
    (support_access_session_id, occurred_at_utc) [name: 'ix_support_access_audit_events_session_occurred', note: 'occurred_at_utc descending']
    target_tenant_user_id [name: 'ix_support_access_audit_events_target_tenant_user_id']
    (target_tenant_id, occurred_at_utc) [name: 'ix_support_access_audit_events_tenant_occurred', note: 'occurred_at_utc descending']
  }

  Note: 'Append-only support-access audit evidence. Stores bounded request/action metadata only; no raw payloads, cookies, tokens, or provider responses.'
}


// ============================================================
// Tenants
// ============================================================

Table "tenants" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "full_name" varchar(500) [not null]
  "slug" varchar(200) [not null]
  "description" varchar(500)
  "tenant_status_id" int [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    slug [unique]
  }

  Note: 'Multi-tenant root entity. All scoped entities reference this.'
}


Table "tenant_settings_documents" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "document_key" varchar(128) [not null]
  "schema_version" int [not null]
  "defaults_version" varchar(64) [not null]
  "payload_json" jsonb [not null]
  "concurrency_stamp" uuid [not null, note: 'optimistic concurrency token, app-managed']
  "created_at" timestamptz [not null, default: `NOW()`]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    document_key [name: 'ix_tenant_settings_documents_document_key']
    (tenant_id, document_key) [unique, name: 'ix_tenant_settings_documents_tenant_id_document_key']
  }

  Note: 'Tenant-owned typed settings documents. Payload is non-secret JSONB; tenant.branding is provisioned for each tenant.'
}

Table "tenant_setting_overrides"{
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "setting_key" varchar(256) [not null]
  "value" text [not null]
  "is_locked" boolean [not null, default: false]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, setting_key) [unique, name: 'ix_tenant_setting_overrides_tenant_id_setting_key']
  }
}

Table "tenant_navigation_links" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "label" varchar(50) [not null]
  "url" varchar(500) [not null]
  "icon" varchar(100)
  "order" int [not null]
  "open_in_new_tab" boolean [not null]
  "is_active" boolean [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
}

// ============================================================
// Tenant Footer Links
// ============================================================

Table "tenant_footer_link_groups" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid
  "title" varchar(100) [not null]
  "order" int [not null]
  "is_active" boolean [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    tenant_id [name: 'ix_tenant_footer_link_groups_tenant_id']
    (tenant_id, order) [name: 'ix_tenant_footer_link_groups_tenant_id_order']
  }

  Note: 'TenantId null = instance-default group.'
}


Table "tenant_footer_links" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "footer_link_group_id" uuid [not null]
  "label" varchar(200) [not null]
  "url" varchar(1000) [not null]
  "open_in_new_tab" boolean [not null]
  "order" int [not null]
  "is_active" boolean [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (footer_link_group_id, order) [name: 'ix_tenant_footer_links_footer_link_group_id_order']
  }
}


// ============================================================
// Appearance (UI Themes)
// ============================================================

Table "ui_themes" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid
  "theme_key" varchar(128) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(1000)
  "is_active" boolean [not null]
  "is_default" boolean [not null]
  "sort_order" int [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "xmin" xid [not null, note: 'rowversion / optimistic concurrency']

  indexes {
    theme_key [unique, name: 'ix_ui_themes_theme_key', note: 'filter: tenant_id IS NULL']
    (tenant_id, theme_key) [unique, name: 'ix_ui_themes_tenant_id_theme_key', note: 'filter: tenant_id IS NOT NULL']
    is_default [unique, name: 'ix_ui_themes_is_default', note: 'filter: tenant_id IS NULL AND is_default = true']
    (tenant_id, is_default) [unique, name: 'ix_ui_themes_tenant_id_is_default', note: 'filter: tenant_id IS NOT NULL AND is_default = true']
  }
}


Table "ui_theme_presets" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid
  "theme_key" varchar(128) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(1000)
  "is_active" boolean [not null]
  "is_system" boolean [not null]
  "is_editable" boolean [not null]
  "seed_version" int [not null]
  "deprecated_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    theme_key [unique, name: 'ix_ui_theme_presets_theme_key', note: 'filter: tenant_id IS NULL AND is_deleted = false']
    (tenant_id, theme_key) [unique, name: 'ix_ui_theme_presets_tenant_id_theme_key', note: 'filter: tenant_id IS NOT NULL AND is_deleted = false']
    (tenant_id, is_active) [name: 'ix_ui_theme_presets_tenant_id_is_active']
  }
}


Table "tenant_onboarding_states" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "is_completed" boolean [not null]
  "current_step" int [not null]
  "total_steps" int [not null]
  "completed_steps_json" jsonb
  "created_at" timestamptz [not null]
  "completed_at" timestamptz
  "completed_by_user_id" uuid

  indexes {
    tenant_id [unique]
  }
}

Table "tenant_policy_sets" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "xmin" xid [not null, note: 'rowversion / optimistic concurrency']

  indexes {
    tenant_id [unique, name: 'ix_tenant_policy_sets_tenant_id']
  }
}


Table "tenant_lifecycle_logs" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "old_status_id" int
  "new_status_id" int [not null]
  "transitioned_by_user_id" uuid [not null]
  "reason" varchar(1000)
  "transitioned_at" timestamptz [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
}

Table "tenant_capabilities" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "module_id" uuid [not null]
  "is_enabled" boolean [not null]
  "enabled_at" timestamptz [not null]
  "enabled_by" uuid
  "configuration_json" jsonb
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, module_id) [unique]
  }
}

Table "tenant_invitations" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "email" varchar(320) [not null]
  "role_id" int [not null]
  "token" varchar(512) [not null]
  "expires_at" timestamptz [not null]
  "is_accepted" boolean [not null]
  "accepted_at" timestamptz
  "accepted_by_user_id" uuid
  "invited_by_user_id" uuid [not null]
  "allowed_domain" varchar(255)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    token [unique]
    (tenant_id, email)
  }
}

Table "tenant_users" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "user_id" uuid [not null]
  "actor_id" uuid
  "status_id" int [not null, default: 1]
  "joined_at" timestamptz
  "suspended_at" timestamptz
  "suspended_by" uuid
  "ban_expires_at" timestamptz
  "removed_at" timestamptz
  "removed_by" uuid
  "moderation_note" varchar(2000)
  "created_at" timestamptz [not null, default: `NOW()`]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    actor_id [name: 'ix_tenant_users_actor_id']
    user_id [name: 'ix_tenant_users_user_id']
    (tenant_id, actor_id) [unique, name: 'ix_tenantusers_tenant_actor', note: 'filter: actor_id IS NOT NULL']
    (tenant_id, user_id) [unique, name: 'ix_tenantusers_tenant_user']
    (tenant_id, id) [unique, name: 'ak_tenant_users_tenant_id_id']
  }

  Note: 'Tenant-local membership/moderation state for a global user. Check: ck_tenant_users_status (1..4).'
}

Table "tenant_user_role_grants" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "tenant_user_id" uuid [not null]
  "role_id" int [not null]
  "role_scope_id" int [not null, default: 1]
  "granted_at" timestamptz [not null, default: `NOW()`]
  "granted_by" uuid
  "revoked_at" timestamptz
  "revoked_by" uuid
  "revocation_reason" varchar(1000)
  "created_at" timestamptz [not null, default: `NOW()`]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, tenant_user_id, role_id) [unique, name: 'ix_tenant_user_role_grants_active_tenant_user_role', note: 'filter: revoked_at IS NULL']
    (role_id, role_scope_id) [name: 'ix_tenant_user_role_grants_role_id_role_scope_id']
    (tenant_id, role_id) [name: 'ix_tenant_user_role_grants_tenant_role']
  }

  Note: 'Auditable tenant-role grant rooted in TenantUser lifecycle. Check: ck_tenant_user_role_grants_role_scope (role_scope_id = Tenant/1).'
}

Table "tenant_user_profiles" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "tenant_user_id" uuid [not null]
  "display_name_override" varchar(256)
  "contact_email_override" varchar(320)
  "locale" varchar(35)
  "time_zone" varchar(128)
  "preferences_json" jsonb
  "consent_json" jsonb
  "admin_note" varchar(2000)
  "created_at" timestamptz [not null, default: `NOW()`]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    tenant_user_id [unique, name: 'ix_tenantuserprofiles_tenant_user']
    (tenant_id, contact_email_override) [name: 'ix_tenantuserprofiles_tenant_contact_email', note: 'filter: contact_email_override IS NOT NULL']
  }

  Note: 'Tenant-admin-managed local profile overrides layered on top of the global user record.'
}

Table "external_api_keys" {
  "id" uuid [pk, not null]
  "tenant_id" uuid
  "name" varchar(200) [not null]
  "description" varchar(1000)
  "key_id" varchar(64) [not null]
  "secret_hash" varchar(500) [not null]
  "scopes" varchar(1000) [not null]
  "external_api_key_owner_type_id" int [not null]
  "owner_id" uuid [not null]
  "external_api_key_status_id" int [not null]
  "external_api_key_credit_period_id" int [not null]
  "credit_limit" int
  "max_rollover_credits" int
  "expires_at" timestamptz
  "last_used_at" timestamptz
  "last_used_ip" varchar(64)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    key_id [unique, name: 'ix_external_api_keys_key_id']
    external_api_key_owner_type_id [name: 'ix_external_api_keys_external_api_key_owner_type_id']
    (tenant_id, external_api_key_owner_type_id, owner_id) [name: 'ix_external_api_keys_tenant_id_external_api_key_owner_type_id_']
    (tenant_id, external_api_key_status_id) [name: 'ix_external_api_keys_tenant_id_external_api_key_status_id']
  }
}

Table "external_api_key_quotas" {
  "id" uuid [pk, not null]
  "external_api_key_id" uuid [not null]
  "period_start" date [not null]
  "period_end" date [not null]
  "credit_limit" int [not null]
  "credits_used" int [not null]
  "rollover_credits" int [not null]
  "request_count" bigint [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (external_api_key_id, period_start) [unique]
  }
}

Table "external_bindings" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "provider_key" varchar(128) [not null]
  "external_system" varchar(128) [not null]
  "external_type" varchar(128) [not null]
  "external_id" varchar(512) [not null]
  "internal_type" varchar(128) [not null]
  "internal_id" uuid [not null]
  "scope_tenant_id" uuid
  "external_binding_status_id" int [not null, default: 1]
  "metadata_json" jsonb
  "last_seen_at" timestamptz
  "created_at" timestamptz [not null, default: `NOW()`]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    scope_tenant_id [name: 'ix_external_bindings_scope_tenant_id']
    (provider_key, external_system, external_type, external_id) [unique, name: 'ix_external_bindings_external_global_unique', note: 'filter: scope_tenant_id IS NULL']
    (provider_key, external_system, internal_type, internal_id) [unique, name: 'ix_external_bindings_internal_global_unique', note: 'filter: scope_tenant_id IS NULL']
    (provider_key, external_system, external_type, external_id, scope_tenant_id) [unique, name: 'ix_external_bindings_external_tenant_unique', note: 'filter: scope_tenant_id IS NOT NULL']
    (provider_key, external_system, internal_type, internal_id, scope_tenant_id) [unique, name: 'ix_external_bindings_internal_tenant_unique', note: 'filter: scope_tenant_id IS NOT NULL']
  }

  Note: 'Provider-neutral external identity binding. Checks: ck_external_bindings_status (1..3), ck_external_bindings_text_not_blank, ck_external_bindings_registered_pair_scope.'
}


// ============================================================
// Taxonomy (Categories, Tags)
// ============================================================

Table "categories" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "parent_id" uuid
  "tenant_id" uuid [not null]

  Indexes {
    (tenant_id, id) [unique, name: 'ak_categories_tenant_id_id']
    (tenant_id, master_code) [unique]
    (tenant_id, parent_id) [name: 'ix_categories_tenant_id_parent_id']
  }
}

Table "category_type_categories" {
  "id" uuid [pk, not null]
  "category_id" uuid [not null]
  "category_type_id" int [not null]
  "tenant_id" uuid [not null]
}

Table "tags" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "tenant_id" uuid [not null]

  Indexes {
    (tenant_id, id) [unique, name: 'ak_tags_tenant_id_id']
    (tenant_id, master_code) [unique, name: 'ix_tags_tenant_master_code']
  }
}

Table "tag_type_tags" {
  "id" uuid [pk, not null]
  "tag_id" uuid [not null]
  "tag_type_id" int [not null]
  "tenant_id" uuid [not null]
}

// ============================================================
// Custom Properties (EAV)
// ============================================================

Table "custom_property_definitions" {
  "id" uuid [pk, not null]
  "entity_type_name" varchar(50) [not null]
  "tenant_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "property_type" int [not null]
  "is_required" boolean [not null]
  "is_multi" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "exposure_level" int [not null]
  "is_searchable" boolean [not null]
  "is_filterable" boolean [not null]
  "is_exportable" boolean [not null]
  "is_moderation_relevant" boolean [not null]
  "is_analytics_relevant" boolean [not null]
  "is_system_owned" boolean [not null]
  "default_text_value" varchar(1000)
  "default_number_value" decimal(19,4)
  "default_boolean_value" boolean
  "default_date_time_value" timestamptz
  "default_option_id" uuid
  "min_length" int
  "max_length" int
  "regex_pattern" varchar(1000)
  "min_number" decimal(19,4)
  "max_number" decimal(19,4)
  "min_date_time" timestamptz
  "max_date_time" timestamptz
  "allowed_url_schemes" varchar(500)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, entity_type_name, namespace, key) [unique, name: 'ix_cpd_tenant_entity_namespace_key']
    (tenant_id, entity_type_name, is_active) [name: 'ix_cpd_tenant_entity_active']
    (tenant_id, entity_type_name, is_searchable, is_filterable) [name: 'ix_cpd_tenant_entity_search_filter']
  }

  Note: 'Shared custom-property definitions. Check: ck_custom_property_definitions_shared_entity_type allows only registry-approved shared targets.'
}

Table "custom_property_options" {
  "id" uuid [pk, not null]
  "custom_property_definition_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "value" varchar(500) [not null]
  "is_default" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "parent_option_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (custom_property_definition_id, sort_order) [name: 'ix_cpo_definition_sort']
    (custom_property_definition_id, namespace, key) [unique, name: 'ix_cpo_definition_namespace_key']
  }
}

Table "custom_property_values" {
  "id" uuid [pk, not null]
  "custom_property_definition_id" uuid [not null]
  "entity_id" uuid [not null]
  "ordinal" int [not null]
  "text_value" varchar(4000)
  "number_value" decimal(19,4)
  "boolean_value" boolean
  "date_time_value" timestamptz
  "option_id" uuid
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, entity_id) [name: 'ix_cpv_tenant_entity']
    (custom_property_definition_id, entity_id, ordinal) [unique, name: 'ix_cpv_definition_entity_ordinal']
    (tenant_id, custom_property_definition_id) [name: 'ix_cpv_tenant_definition']
  }
}

// ============================================================
// Event Templates & Event Custom Properties
// ============================================================

Table "event_templates" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "template_key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "event_type_id" int
  "version" int [not null]
  "is_published" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, template_key, version) [unique, name: 'ix_event_templates_tenant_key_version']
    (tenant_id, is_published, is_active) [name: 'ix_event_templates_tenant_published_active']
  }
}

Table "event_template_custom_property_definitions" {
  "id" uuid [pk, not null]
  "event_template_id" uuid [not null]
  "tenant_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "property_type" int [not null]
  "is_required" boolean [not null]
  "is_multi" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "exposure_level" int [not null]
  "is_searchable" boolean [not null]
  "is_filterable" boolean [not null]
  "is_exportable" boolean [not null]
  "is_moderation_relevant" boolean [not null]
  "is_analytics_relevant" boolean [not null]
  "is_system_owned" boolean [not null]
  "default_text_value" varchar(1000)
  "default_number_value" decimal(19,4)
  "default_boolean_value" boolean
  "default_date_time_value" timestamptz
  "default_option_id" uuid
  "min_length" int
  "max_length" int
  "regex_pattern" varchar(1000)
  "min_number" decimal(19,4)
  "max_number" decimal(19,4)
  "min_date_time" timestamptz
  "max_date_time" timestamptz
  "allowed_url_schemes" varchar(500)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (event_template_id, namespace, key) [unique, name: 'ix_etcpd_template_namespace_key']
    (tenant_id, is_searchable, is_filterable) [name: 'ix_etcpd_tenant_search_filter']
  }
}

Table "event_template_custom_property_options" {
  "id" uuid [pk, not null]
  "event_template_custom_property_definition_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "value" varchar(500) [not null]
  "is_default" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "parent_option_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (event_template_custom_property_definition_id, sort_order) [name: 'ix_etcpo_definition_sort']
    (event_template_custom_property_definition_id, namespace, key) [unique, name: 'ix_etcpo_definition_namespace_key']
  }
}

Table "event_custom_property_definitions" {
  "id" uuid [pk, not null]
  "event_id" uuid [not null]
  "tenant_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "property_type" int [not null]
  "is_required" boolean [not null]
  "is_multi" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "exposure_level" int [not null]
  "is_searchable" boolean [not null]
  "is_filterable" boolean [not null]
  "is_exportable" boolean [not null]
  "is_moderation_relevant" boolean [not null]
  "is_analytics_relevant" boolean [not null]
  "is_system_owned" boolean [not null]
  "default_text_value" varchar(1000)
  "default_number_value" decimal(19,4)
  "default_boolean_value" boolean
  "default_date_time_value" timestamptz
  "default_option_id" uuid
  "min_length" int
  "max_length" int
  "regex_pattern" varchar(1000)
  "min_number" decimal(19,4)
  "max_number" decimal(19,4)
  "min_date_time" timestamptz
  "max_date_time" timestamptz
  "allowed_url_schemes" varchar(500)
  "source_template_id" uuid
  "source_template_key" varchar(100)
  "source_template_version" int
  "source_template_definition_id" uuid
  "instantiated_at" timestamptz [not null]
  "last_synced_from_template_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (event_id, namespace, key) [unique, name: 'ix_ecpd_event_namespace_key']
    (tenant_id, event_id, is_searchable, is_filterable) [name: 'ix_ecpd_tenant_event_search_filter']
  }
}

Table "event_custom_property_options" {
  "id" uuid [pk, not null]
  "event_custom_property_definition_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "value" varchar(500) [not null]
  "is_default" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "parent_option_id" uuid
  "source_template_option_id" uuid
  "source_template_version" int
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (event_custom_property_definition_id, sort_order) [name: 'ix_ecpo_definition_sort']
    (event_custom_property_definition_id, namespace, key) [unique, name: 'ix_ecpo_definition_namespace_key']
  }
}

Table "event_custom_property_values" {
  "id" uuid [pk, not null]
  "event_custom_property_definition_id" uuid [not null]
  "event_id" uuid [not null]
  "tenant_id" uuid [not null]
  "ordinal" int [not null]
  "text_value" varchar(4000)
  "number_value" decimal(19,4)
  "boolean_value" boolean
  "date_time_value" timestamptz
  "option_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, event_id) [name: 'ix_ecpv_tenant_event']
    (event_custom_property_definition_id, event_id, ordinal) [unique, name: 'ix_ecpv_definition_event_ordinal']
  }
}

Table "event_custom_property_projections" {
  "id" uuid [pk, not null]
  "event_custom_property_definition_id" uuid [not null]
  "event_custom_property_value_id" uuid [not null]
  "event_id" uuid [not null]
  "tenant_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "property_type" int [not null]
  "exposure_level" int [not null]
  "is_searchable" boolean [not null]
  "is_filterable" boolean [not null]
  "is_exportable" boolean [not null]
  "is_moderation_relevant" boolean [not null]
  "is_analytics_relevant" boolean [not null]
  "ordinal" int [not null]
  "option_id" uuid
  "text_value" varchar(4000)
  "number_value" decimal(19,4)
  "boolean_value" boolean
  "date_time_value" timestamptz
  "normalized_value" varchar(4000)
  "updated_at" timestamptz [not null]

  indexes {
    event_custom_property_value_id [unique, name: 'ix_ecpp_value']
    (tenant_id, namespace, key, normalized_value) [name: 'ix_ecpp_tenant_namespace_key_normalized']
    (tenant_id, exposure_level) [name: 'ix_ecpp_tenant_exposure']
    (tenant_id, event_id, namespace, key, ordinal) [name: 'ix_ecpp_tenant_event_namespace_key_ordinal']
  }
}

// ============================================================
// Event Session Templates & Session Custom Properties
// ============================================================

Table "event_session_templates" {
  "id" uuid [pk, not null]
  "event_template_id" uuid [not null]
  "tenant_id" uuid [not null]
  "session_template_key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "version" int [not null]
  "is_published" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (event_template_id, session_template_key, version) [unique, name: 'ix_est_template_key_version']
    (tenant_id, is_published, is_active) [name: 'ix_est_tenant_published_active']
  }
}

Table "event_session_template_custom_property_definitions" {
  "id" uuid [pk, not null]
  "event_session_template_id" uuid [not null]
  "tenant_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "property_type" int [not null]
  "is_required" boolean [not null]
  "is_multi" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "exposure_level" int [not null]
  "is_searchable" boolean [not null]
  "is_filterable" boolean [not null]
  "is_exportable" boolean [not null]
  "is_moderation_relevant" boolean [not null]
  "is_analytics_relevant" boolean [not null]
  "is_system_owned" boolean [not null]
  "default_text_value" varchar(1000)
  "default_number_value" decimal(19,4)
  "default_boolean_value" boolean
  "default_date_time_value" timestamptz
  "default_option_id" uuid
  "min_length" int
  "max_length" int
  "regex_pattern" varchar(1000)
  "min_number" decimal(19,4)
  "max_number" decimal(19,4)
  "min_date_time" timestamptz
  "max_date_time" timestamptz
  "allowed_url_schemes" varchar(500)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (event_session_template_id, namespace, key) [unique, name: 'ix_estcpd_template_namespace_key']
    (tenant_id, is_searchable, is_filterable) [name: 'ix_estcpd_tenant_search_filter']
  }
}

Table "event_session_template_custom_property_options" {
  "id" uuid [pk, not null]
  "event_session_template_custom_property_definition_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "value" varchar(500) [not null]
  "is_default" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "parent_option_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (event_session_template_custom_property_definition_id, sort_order) [name: 'ix_estcpo_definition_sort']
    (event_session_template_custom_property_definition_id, namespace, key) [unique, name: 'ix_estcpo_definition_namespace_key']
  }
}

Table "event_session_custom_property_definitions" {
  "id" uuid [pk, not null]
  "event_session_id" uuid [not null]
  "tenant_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "property_type" int [not null]
  "is_required" boolean [not null]
  "is_multi" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "exposure_level" int [not null]
  "is_searchable" boolean [not null]
  "is_filterable" boolean [not null]
  "is_exportable" boolean [not null]
  "is_moderation_relevant" boolean [not null]
  "is_analytics_relevant" boolean [not null]
  "is_system_owned" boolean [not null]
  "default_text_value" varchar(1000)
  "default_number_value" decimal(19,4)
  "default_boolean_value" boolean
  "default_date_time_value" timestamptz
  "default_option_id" uuid
  "min_length" int
  "max_length" int
  "regex_pattern" varchar(1000)
  "min_number" decimal(19,4)
  "max_number" decimal(19,4)
  "min_date_time" timestamptz
  "max_date_time" timestamptz
  "allowed_url_schemes" varchar(500)
  "source_template_id" uuid
  "source_template_key" varchar(100)
  "source_template_version" int
  "source_template_definition_id" uuid
  "instantiated_at" timestamptz [not null]
  "last_synced_from_template_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (event_session_id, namespace, key) [unique, name: 'ix_escpd_session_namespace_key']
    (tenant_id, event_session_id, is_searchable, is_filterable) [name: 'ix_escpd_tenant_session_search_filter']
  }
}

Table "event_session_custom_property_options" {
  "id" uuid [pk, not null]
  "event_session_custom_property_definition_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "display_name" varchar(200) [not null]
  "description" varchar(500)
  "value" varchar(500) [not null]
  "is_default" boolean [not null]
  "is_active" boolean [not null]
  "sort_order" int [not null]
  "parent_option_id" uuid
  "source_template_option_id" uuid
  "source_template_version" int
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (event_session_custom_property_definition_id, sort_order) [name: 'ix_escpo_definition_sort']
    (event_session_custom_property_definition_id, namespace, key) [unique, name: 'ix_escpo_definition_namespace_key']
  }
}

Table "event_session_custom_property_values" {
  "id" uuid [pk, not null]
  "event_session_custom_property_definition_id" uuid [not null]
  "event_session_id" uuid [not null]
  "tenant_id" uuid [not null]
  "ordinal" int [not null]
  "text_value" varchar(4000)
  "number_value" decimal(19,4)
  "boolean_value" boolean
  "date_time_value" timestamptz
  "option_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, event_session_id) [name: 'ix_escpv_tenant_session']
    (event_session_custom_property_definition_id, event_session_id, ordinal) [unique, name: 'ix_escpv_definition_session_ordinal']
  }
}

Table "event_session_custom_property_projections" {
  "id" uuid [pk, not null]
  "event_session_custom_property_definition_id" uuid [not null]
  "event_session_custom_property_value_id" uuid [not null]
  "event_session_id" uuid [not null]
  "tenant_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "property_type" int [not null]
  "exposure_level" int [not null]
  "is_searchable" boolean [not null]
  "is_filterable" boolean [not null]
  "is_exportable" boolean [not null]
  "is_moderation_relevant" boolean [not null]
  "is_analytics_relevant" boolean [not null]
  "ordinal" int [not null]
  "option_id" uuid
  "text_value" varchar(4000)
  "number_value" decimal(19,4)
  "boolean_value" boolean
  "date_time_value" timestamptz
  "normalized_value" varchar(4000)
  "updated_at" timestamptz [not null]

  indexes {
    event_session_custom_property_value_id [unique, name: 'ix_escpp_value']
    (tenant_id, namespace, key, normalized_value) [name: 'ix_escpp_tenant_namespace_key_normalized']
    (tenant_id, exposure_level) [name: 'ix_escpp_tenant_exposure']
    (tenant_id, event_session_id, namespace, key, ordinal) [name: 'ix_escpp_tenant_session_namespace_key_ordinal']
  }
}

// ============================================================
// Locations
// ============================================================

Table "location_kinds" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_location_kinds_master_code']
  }

  Note: 'Lookup: descriptive physical-location kind only; it never grants disclosure. Values: Unclassified(1), CommercialVenue(2), PublicSpace(3), CommunityVenue(4), PrivateHome(5).'
}

Table "location_privacy_states" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_location_privacy_states_master_code']
  }

  Note: 'Lookup: physical-location PII lifecycle. Values: NotProvided(1), Active(2), Erased(3). Erased is irreversible.'
}

Table "location_disclosure_audiences" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_location_disclosure_audiences_master_code']
  }

  Note: 'Lookup: EventLocation exact-detail audience. Values: Never(1), AnyCurrentRegistrant(2), ConfirmedParticipant(3).'
}

Table "event_location_privacy_backfill_reversal" {
  "location_id" uuid [pk, not null]
  "previous_privacy_state_id" int [not null]
  "backfilled_privacy_state_id" int [not null]
  "recorded_at_utc" timestamptz [not null]

  Note: 'Migration-owned, PII-free reversal ledger for ELP-230B Location lifecycle changes. Retained only until the contract stage makes rollback invalid.'
}

Table "locations" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "full_name" varchar(500) [not null]
  "display_sort_key" varchar(2000) [not null, note: 'Complete NFC-invariant-uppercase-NFC Unicode text; provider binary collation; SQL Server nvarchar']
  "display_sort_key_version" smallint [not null, note: 'Current normalization revision 2 only; no legacy default']
  "country" varchar(500) [not null]
  "city" varchar(500) [not null]
  "tenant_id" uuid [not null]
  "timezone" varchar(500)
  "location_kind_id" int [not null, default: 1]
  "location_privacy_state_id" int [not null, default: 1]
  "owner_user_id" uuid
  "pii_erased_at_utc" timestamptz
  "pii_erasure_reason" int
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_locations_tenant_id_id']
    tenant_id
    (tenant_id, city) [name: 'ix_locations_tenant_city']
    (tenant_id, country) [name: 'ix_locations_tenant_country']
    location_kind_id [name: 'ix_locations_location_kind_id']
    location_privacy_state_id [name: 'ix_locations_location_privacy_state_id']
    owner_user_id [name: 'ix_locations_owner_user_id']
  }

  Note: 'Tenant-scoped physical place. Checks enforce owner implies PrivateHome and Erased requires cleared owner plus erasure timestamp/reason; optional LocationPii carries exact address data.'
}

Table "location_rooms" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "location_id" uuid [not null]
  "tenant_id" uuid [not null]
  "name" varchar(200) [not null]
  "slug" varchar(200)
  "description" varchar(2000)
  "capacity" int
  "sort_order" int [not null, default: 0]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_location_rooms_tenant_id_id']
    (tenant_id, location_id, id) [unique, name: 'ak_location_rooms_tenant_id_location_id_id']
    (tenant_id, location_id, name) [unique, name: 'ix_location_rooms_tenant_location_name']
    (tenant_id, location_id, sort_order) [name: 'ix_location_rooms_tenant_location_sort']
    location_id [name: 'ix_location_rooms_location_id']
  }

  Note: 'Sub-venue within a location (e.g. Conference Room A, Main Hall). Used for room-based agenda grid layout. Soft-deletable, tenant-scoped.'
}

Table "location_pii" {
  "location_id" uuid [pk, not null, note: 'shared PK with locations']
  "address" varchar(500) [not null]
  "address_substring_key" varchar(2000) [not null, note: 'Removable derived PII; complete normalized Unicode literal-substring text, never truncated']
  "address_substring_key_version" smallint [not null, note: 'Current normalization revision 2 only; unsupported revisions fail closed in suggestions']
  "postcode" varchar(500) [not null]
  "latitude" doubleprecision
  "longitude" doubleprecision

  Note: 'Optional shared-PK exact-address record. CK_LocationPii_CoordinateShape requires latitude/longitude to be jointly null or jointly present within latitude -90..90 and longitude -180..180. Database guards reject attachment to an Erased Location.'
}

Table "event_locations" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "location_id" uuid
  "show_venue_name" boolean [not null]
  "show_city" boolean [not null]
  "show_country" boolean [not null]
  "show_room_name" boolean [not null]
  "show_street_address" boolean [not null]
  "show_postcode" boolean [not null]
  "show_coordinates" boolean [not null]
  "full_details_audience_id" int [not null]
  "reveal_full_details_from_utc" timestamptz
  "needs_privacy_review" boolean [not null]
  "is_to_be_announced" boolean [not null]
  "policy_version" int [not null]
  "last_policy_actor_user_id" uuid
  "last_policy_changed_at_utc" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_locations_tenant_id_id']
    (tenant_id, event_id, id) [unique, name: 'ak_event_locations_tenant_id_event_id_id']
    full_details_audience_id [name: 'ix_event_locations_full_details_audience_id']
    (tenant_id, event_id, is_deleted) [name: 'ix_event_locations_tenant_event_active']
    (tenant_id, location_id) [name: 'ix_event_locations_tenant_id_location_id']
    (tenant_id, event_id, location_id) [unique, name: 'ux_event_locations_active_physical', note: 'partial: is_deleted = false AND is_to_be_announced = false AND location_id IS NOT NULL']
    (tenant_id, event_id) [unique, name: 'ux_event_locations_active_tba', note: 'partial: is_deleted = false AND is_to_be_announced = true']
  }

  Note: 'Canonical per-event location disclosure authority. Checks enforce physical Location XOR explicit TBA, TBA field suppression, positive policy version, and UUIDv7 identity.'
}

Table "event_location_disclosure_audits" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_location_id" uuid [not null]
  "actor_user_id" uuid [not null]
  "previous_fields" int [not null]
  "new_fields" int [not null]
  "previous_audience_id" int [not null]
  "new_audience_id" int [not null]
  "previous_reveal_full_details_from_utc" timestamptz
  "new_reveal_full_details_from_utc" timestamptz
  "previous_policy_version" int [not null]
  "new_policy_version" int [not null]
  "reason" int [not null]
  "occurred_at_utc" timestamptz [not null]

  indexes {
    (tenant_id, event_location_id, occurred_at_utc) [name: 'ix_event_location_disclosure_audits_history']
    new_audience_id [name: 'ix_event_location_disclosure_audits_new_audience_id']
    previous_audience_id [name: 'ix_event_location_disclosure_audits_previous_audience_id']
    (tenant_id, event_location_id, new_policy_version) [unique, name: 'ux_event_location_disclosure_audits_policy_version']
  }

  Note: 'PII-free append-only disclosure-policy history. Checks enforce bounded field flags, a one-version step, typed reasons, and UUIDv7 identity.'
}

Table "event_location_exact_read_audits" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_location_id" uuid [not null]
  "requester_user_id" uuid [not null]
  "purpose" int [not null]
  "was_authorized" boolean [not null]
  "occurred_at_utc" timestamptz [not null]
  "correlation_id" uuid
  "trace_id" uuid

  indexes {
    (tenant_id, event_location_id, occurred_at_utc) [name: 'ix_event_location_exact_read_audits_history']
    (tenant_id, requester_user_id, occurred_at_utc) [name: 'ix_event_location_exact_read_audits_requester']
  }

  Note: 'PII-free append-only security evidence for exceptional exact reads. Checks require a typed purpose, correlation or trace identity, and UUIDv7 identity.'
}

Table "privacy_erasure_replay_checkpoints" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "authority_sequence" bigint [not null]
  "intent_id" uuid [not null, note: 'uuidv7 authority intent']
  "subject_kind" smallint [not null, note: '1 = User; only executable subject kind']
  "subject_id" uuid [not null, note: 'opaque linkable identifier']
  "reason_code" smallint [not null]
  "policy_version" int [not null]
  "previous_checkpoint_id" uuid
  "applied_at_utc" timestamptz [not null]

  indexes {
    authority_sequence [unique, name: 'ux_privacy_erasure_checkpoints_sequence']
    intent_id [unique, name: 'ux_privacy_erasure_checkpoints_intent']
    previous_checkpoint_id [unique, name: 'ux_privacy_erasure_checkpoints_previous']
  }

  Note: 'Application-local append-only replay chain and fact mirror. Checks enforce typed User facts, positive monotonic sequence, a non-forking predecessor, policy version, and UUIDv7 identities.'
}

Table "privacy_erasure_authority"."authority_counter" {
  "singleton" boolean [pk, not null]
  "last_sequence" bigint [not null]
  "retained_floor_sequence" bigint [not null, default: 0]

  Note: 'Singleton sequence allocator and durable compaction floor for typed platform privacy-erasure facts. Checks require 0 <= retained_floor_sequence <= last_sequence. The selected topology migration owns this table.'
}

Table "privacy_erasure_authority"."erasure_intents" {
  "authority_sequence" bigint [pk, not null]
  "intent_id" uuid [not null, note: 'uuidv7 idempotency key']
  "subject_kind" smallint [not null, note: '1 = User; only executable subject kind']
  "subject_id" uuid [not null, note: 'opaque linkable identifier; no user FK by design']
  "reason_code" smallint [not null]
  "policy_version" int [not null]
  "requested_at_utc" timestamptz [not null]
  "recorded_at_utc" timestamptz [not null]
  "retention_expires_at_utc" timestamptz [not null]
  "is_legal_hold_pseudonymized" boolean [not null, default: false]

  indexes {
    intent_id [unique, name: 'ak_privacy_erasure_intents_intent_id']
    (intent_id, subject_kind, policy_version) [unique, name: 'ix_erasure_intents_intent_id_subject_kind_policy_version']
  }

  Note: 'Typed User-erasure facts for all retained-authority placements. Normal facts are immutable and require UUIDv7/RFC identity; maintenance may atomically delete expired prefix rows or replace held intent/subject identifiers with random audit tokens while preserving sequence/timestamps. External runtime access is function-only for append/read/state/evaluate; destructive compact execution belongs only to the migrator role, and direct table SELECT/DML are denied.'
}

// Privacy Erasure Lifecycle
Table "privacy_erasure_sagas" {
  "intent_id" uuid [pk, not null]
  "completed_at_utc" timestamptz
  "completed_provider_work_count" int [not null]
  "concurrency_token" uuid [not null]
  "fence_token" bigint [not null]
  "fenced_at_utc" timestamptz [not null]
  "local_settled_at_utc" timestamptz
  "policy_version" int [not null]
  "provider_work_count" int [not null]
  "receipt_expires_at_utc" timestamptz [not null]
  "receipt_hash" bytea
  "status" smallint [not null]
  "subject_id" uuid [not null]
  "subject_kind" smallint [not null]
  "updated_at_utc" timestamptz [not null]

  indexes {
    receipt_hash [unique, name: 'ix_privacy_erasure_sagas_receipt_hash']
    (subject_kind, subject_id) [unique, name: 'ix_privacy_erasure_sagas_subject_kind_subject_id']
    (intent_id, subject_kind, policy_version) [unique, name: 'ix_privacy_erasure_sagas_intent_id_subject_kind_policy_version']
  }
}

Table "privacy_erasure_provider_work" {
  "id" uuid [pk, not null]
  "action" smallint [not null]
  "attempt_count" int [not null]
  "completed_at_utc" timestamptz
  "created_at_utc" timestamptz [not null]
  "dead_lettered_at_utc" timestamptz
  "intent_id" uuid [not null]
  "last_failure_code" varchar(100)
  "lease_expires_at_utc" timestamptz
  "lease_fence" bigint [not null]
  "lease_owner" varchar(100)
  "lease_token" uuid
  "locator_expires_at_utc" timestamptz [not null]
  "locator_kind" smallint [not null]
  "locator_protection_version" int [not null]
  "next_attempt_at_utc" timestamptz
  "protected_locator" varchar(8192)
  "provider_kind" smallint [not null]
  "status" smallint [not null]
  "subject_id" uuid [not null]
  "subject_kind" smallint [not null]
  "target_id" uuid
  "tenant_id" uuid
  "unknown_at_utc" timestamptz
  "updated_at_utc" timestamptz [not null]

  indexes {
    (status, next_attempt_at_utc, lease_expires_at_utc) [name: 'ix_privacy_erasure_provider_work_status_next_attempt_at_utc_le']
    (intent_id, provider_kind, action, tenant_id, target_id) [unique, name: 'ix_privacy_erasure_provider_work_intent_id_provider_kind_actio']
  }
}

Table "privacy_erasure_policy_coverage" {
  "intent_id" uuid [pk, not null]
  "subject_kind" smallint [pk, not null]
  "policy_version" int [pk, not null]
  "covered_at_utc" timestamptz [not null]
}

// Privacy Erasure Lifecycle Relationships
Ref: "privacy_erasure_sagas"."intent_id" - "privacy_erasure_authority"."erasure_intents"."intent_id" [delete: restrict]
Ref: "privacy_erasure_provider_work"."intent_id" > "privacy_erasure_sagas"."intent_id" [delete: restrict]
Ref: "privacy_erasure_policy_coverage"."intent_id" > "privacy_erasure_authority"."erasure_intents"."intent_id" [delete: restrict]
// ============================================================
// Actors
// ============================================================

Table "actors" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "actor_type_id" int [not null]
  "user_id" uuid
  "organization_id" uuid
  "group_id" uuid
  "external_actor_subject_id" uuid
  "service_principal_id" uuid
  "is_suspended" boolean [not null, default: false]
  "suspended_at" timestamptz
  "suspended_by" uuid
  "moderation_reason_code" text
  "background_color" varchar(50)
  "background_effect" varchar(50)
  "banner_color" varchar(50)
  "description" varchar(500)
  "profile_picture_cid" varchar(500)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (actor_type_id) [name: 'ix_actors_actor_type_id']
    (user_id) [unique, name: 'ix_actors_user_id', note: 'filtered: user_id IS NOT NULL']
    (organization_id) [unique, name: 'ix_actors_organization_id', note: 'filtered: organization_id IS NOT NULL']
    (group_id) [unique, name: 'ix_actors_group_id', note: 'filtered: group_id IS NOT NULL']
    (external_actor_subject_id) [unique, note: 'filtered: external_actor_subject_id IS NOT NULL']
    (service_principal_id) [unique, note: 'filtered: service_principal_id IS NOT NULL']
  }

  Note: 'Global represented subject. ck_actors_exactly_one_owner requires exactly one concrete owner; ck_actors_external_type_matches_owner binds ExternalActorSubject ownership exclusively to ExternalUnclassified(6).'
}


Table "actor_pii" {
  "actor_id" uuid [pk, not null, note: 'shared PK with actors']
  "display_name" varchar(500) [not null]
  "profile_picture_uri" varchar(500)
}

Table "atproto_identities" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "did" varchar(2048) [not null, note: 'exact, case-sensitive C collation']
  "actor_id" uuid [not null]
  "did_custody_type_id" int
  "handle" varchar(253)
  "pds_host" varchar(2048) [not null]
  "signing_key" varchar(2048)
  "is_active" boolean [not null]
  "is_suspended" boolean [not null]
  "suspended_at" timestamptz
  "suspended_by" uuid
  "moderation_reason_code" varchar(128)
  "last_resolved_at" timestamptz [not null]
  "last_seen_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    did [unique, name: 'ix_atproto_identities_did']
    actor_id [name: 'ix_atproto_identities_actor_id']
  }
}

Table "external_actor_subjects" {
  "id" uuid [pk, not null]
  "first_observed_at" timestamptz [not null]
  "last_observed_at" timestamptz [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]
}

Table "service_principals" {
  "id" uuid [pk, not null]
  "code" varchar(128) [not null, unique]
  "display_name" varchar(500) [not null]
  "created_at" timestamptz [not null]
  "concurrency_stamp" uuid [not null]
}

Table "actor_merges" {
  "id" uuid [pk, not null]
  "source_actor_id" uuid [not null, unique]
  "canonical_actor_id" uuid [not null]
  "proof_kind" int [not null]
  "evidence_reference" varchar(2048) [not null]
  "merged_at" timestamptz [not null]
  "merged_by" uuid [not null]
}

Table "actor_key_stores" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "actor_id" uuid [not null]
  "event_provenance_type_id" int [not null]
  "submitted_by_user_id" uuid
  "organizer_actor_id" uuid
  "source_publisher_name" varchar(200)
  "tenant_id" uuid [not null]
  "key_purpose" varchar(50) [not null]
  "private_key_encrypted" text [not null]
  "public_key" varchar(500) [not null]
  "is_active" boolean
  "created_at" timestamptz
}

Table "actor_subscriptions" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "subscriber_tenant_user_id" uuid [not null]
  "subscriber_user_id" uuid [not null]
  "target_actor_id" uuid [not null]
  "target_actor_type_id" int [not null]
  "status_id" int [not null, default: 1]
  "notification_level_id" int [not null, default: 2]
  "subscribed_at" timestamptz [not null, default: `NOW()`]
  "unsubscribed_at" timestamptz
  "created_at" timestamptz [not null, default: `NOW()`]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, subscriber_tenant_user_id, target_actor_id) [unique, name: 'ux_actor_subscriptions_active_row', note: 'filter: is_deleted = false']
    (tenant_id, target_actor_id, status_id, notification_level_id) [name: 'ix_actor_subscriptions_fanout_scan']
    (tenant_id, subscriber_user_id) [name: 'ix_actor_subscriptions_subscriber_user']
  }

  Note: 'Tenant-local subscription from active tenant user to organization/group actor. Checks: target_actor_type_id IN (2,4); status_id IN (1,2,3); notification_level_id IN (1,2,3); unsubscribed status requires unsubscribed_at.'
}

// ============================================================
// Users
// ============================================================

Table "users" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "actor_id" uuid
  "auth_provider" varchar(500)
  "auth_provider_id" varchar(500)
  "default_actor_id" uuid
  "email_verified" boolean
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  Note: 'Platform user identity. PII stored in separate user_pii table.'
}

Table "user_pii" {
  "user_id" uuid [pk, not null, note: 'shared PK with users']
  "email" varchar(320) [not null]
  "first_name" varchar(500) [not null]
  "last_name" varchar(500) [not null]

  indexes {
    email [unique]
  }
}

Table "platform_user_roles" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "user_id" uuid [not null]
  "role_id" int [not null]
  "granted_at" timestamptz [not null]
  "granted_by" uuid

  indexes {
    (user_id, role_id) [unique]
    user_id
  }
}

Table "user_authentication_tokens" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "user_id" uuid [not null]
  "tenant_id" uuid [not null]
  "provider" varchar(500) [not null]
  "subject_did" varchar(2048) [not null]
  "session_ciphertext" bytea [not null]
  "encryption_key_id" varchar(128) [not null]
  "o_auth_client_key_id" varchar(128) [not null]
  "envelope_version" int [not null, default: 1]
  "concurrency_stamp" uuid [not null]
  "pds_host" varchar(2048)
  "expires_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, provider, subject_did) [unique, name: 'ux_user_authentication_tokens_tenant_provider_subject_did']
    user_id [name: 'ix_user_authentication_tokens_user_id']
  }

  Note: 'Complete CarpaNet OAuth session stored only as an AES-256-GCM authenticated envelope; no plaintext token or private DPoP columns.'
}

Table "user_external_logins" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "user_id" uuid [not null]
  "tenant_id" uuid [not null]
  "provider" varchar(255)
  "provider_key" varchar(500)
  "provider_display_name" varchar(500)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
}

Table "user_preferences" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "user_id" uuid [not null]
  "setting_key" varchar(256) [not null]
  "value" text [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, user_id, setting_key) [unique, name: 'ix_user_preferences_tenant_id_user_id_setting_key']
  }
}

Table "user_notification_preferences" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "user_id" uuid [not null]
  "category" varchar(128) [not null]
  "is_enabled" boolean [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, user_id, category) [unique, name: 'ix_user_notification_preferences_tenant_id_user_id_category']
    (user_id) [name: 'ix_user_notification_preferences_user_id']
  }
}


Table "user_appearance_profiles" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "user_id" uuid [not null]
  "tenant_id" uuid
  "name" varchar(200) [not null]
  "is_default" boolean [not null]
  "is_archived" boolean [not null]
  "is_user_editable" boolean [not null]
  "theme_mode" varchar(10) [not null]
  "source_preset_id" uuid
  "source_preset_key" varchar(128)
  "source_preset_seed_version" int
  "cloned_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (user_id, source_preset_id) [name: 'ix_user_appearance_profiles_user_id_source_preset_id']
    (user_id, tenant_id, is_archived) [name: 'ix_user_appearance_profiles_user_id_tenant_id_is_archived']
    (user_id, tenant_id, is_default) [unique, name: 'ix_user_appearance_profiles_user_id_tenant_id_is_default', note: 'filter: is_default = true']
    (user_id, tenant_id, name) [name: 'ix_user_appearance_profiles_user_id_tenant_id_name']
  }
}


Table "user_appearance_preferences" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "user_id" uuid [not null]
  "tenant_id" uuid
  "active_profile_id" uuid
  "direction" varchar(10) [not null]
  "language" varchar(10) [not null]
  "theme_mode" varchar(10) [not null]

  indexes {
    (user_id, tenant_id) [unique, name: 'ix_user_appearance_preferences_user_id_tenant_id']
  }
}


// ============================================================
// Organizations
// ============================================================

Table "organizations" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "website_url" varchar(2048)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  Note: 'Global canonical organization. Tenant approval and local profile state live in organization_tenants.'
}

Table "organization_tenants" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "organization_id" uuid [not null]
  "approval_status_id" int [not null, default: 1]
  "is_visible" boolean [not null]
  "is_organizer_eligible" boolean [not null]
  "is_suspended" boolean [not null]
  "display_name_override" varchar(500)
  "description_override" varchar(5000)
  "website_url_override" varchar(2048)
  "contact_email_override" varchar(500)
  "profile_picture_id" uuid
  "banner_picture_id" uuid
  "background_image_id" uuid
  "approved_at" timestamptz
  "approved_by" uuid
  "approval_notes" text
  "created_at" timestamptz [not null]
  "is_deleted" boolean [not null]
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, organization_id) [unique, note: 'filtered: is_deleted = false']
    (tenant_id, id) [unique]
  }
}

Table "organization_tenant_evidence" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "organization_tenant_id" uuid [not null]
  "document_storage_object_id" uuid [not null]
  "review_status_id" int [not null, default: 1]
  "reviewed_by_user_id" uuid
  "review_notes" varchar(2000)
  "reviewed_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique]
    (tenant_id, organization_tenant_id, document_storage_object_id) [unique]
    (tenant_id, organization_tenant_id, review_status_id)
    (tenant_id, document_storage_object_id)
    review_status_id
    reviewed_by_user_id
  }

  Note: 'Retained private Document evidence for tenant-local Organization participation review. Review does not auto-approve participation.'
}

Table "organization_pii" {
  "organization_id" uuid [pk, not null, note: 'shared PK with organization']
  "full_name" varchar(500) [not null]
  "email" varchar(320)
  "country" varchar(200)
  "city" varchar(200)
  "address" varchar(500)
  "postcode" varchar(50)

  indexes {
    full_name [name: 'ix_organization_pii_name']
  }
}

Table "organization_members" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "organization_tenant_id" uuid [not null]
  "user_id" uuid [not null]
  "role_id" int [not null]
  "organization_position_id" int
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (organization_tenant_id, user_id) [unique, name: 'ix_orgmembers_org_user']
    user_id [name: 'ix_orgmembers_user']
  }
}

Table "organization_reviews" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "organization_tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "user_id" uuid [not null]
  "reviewer_name" varchar(200) [not null]
  "rating" int [not null]
  "comment" varchar(2000)
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
}

Table "organization_setting_overrides" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "organization_id" uuid [not null]
  "setting_key" varchar(256) [not null]
  "value" text [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (organization_tenant_id, setting_key) [unique]
  }
}

Table "organization_policy_sets" {
  "id" uuid [pk, not null]
  "organization_id" uuid [not null]
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "xmin" xid [not null, note: 'rowversion / optimistic concurrency']

  indexes {
    organization_id [unique, name: 'ix_organization_policy_sets_organization_id']
  }
}


// ============================================================
// Groups
// ============================================================

Table "groups" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "full_name" varchar(500) [not null]
  "description" varchar(5000)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  Note: 'Global canonical group. Tenant approval, hierarchy, and local profile state live in group_tenants.'
}

Table "group_tenants" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "group_id" uuid [not null]
  "approval_status_id" int [not null, default: 1]
  "is_visible" boolean [not null]
  "is_organizer_eligible" boolean [not null]
  "is_suspended" boolean [not null]
  "display_name_override" varchar(500)
  "description_override" varchar(5000)
  "profile_picture_id" uuid
  "banner_picture_id" uuid
  "background_image_id" uuid
  "parent_organization_tenant_id" uuid
  "parent_group_tenant_id" uuid
  "approved_at" timestamptz
  "approved_by" uuid
  "approval_notes" text
  "created_at" timestamptz [not null]
  "is_deleted" boolean [not null]
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, group_id) [unique, note: 'filtered: is_deleted = false']
    (tenant_id, id) [unique]
  }

  Note: 'Tenant-local group participation. Checks enforce one parent kind and prevent self-parenting.'
}

Table "group_members" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "group_tenant_id" uuid [not null]
  "user_id" uuid [not null]
  "role_id" int [not null]
  "group_position_id" int
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (group_tenant_id, user_id) [unique]
  }
}

Table "group_setting_overrides" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "group_tenant_id" uuid [not null]
  "setting_key" varchar(256) [not null]
  "value" text [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (group_id, setting_key) [unique]
  }
}

// ============================================================
// Storage
// ============================================================

Table "storage_objects" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "file_type_id" int [not null]
  "uri" varchar(1000) [not null]
  "object_key" varchar(1024)
  "provider" varchar(50) [not null, note: 'local | s3_compatible | legacy_external']
  "full_name" varchar(500) [not null]
  "safe_display_name" varchar(500) [not null]
  "extension" varchar(50) [not null]
  "content_type" varchar(255)
  "sha256_checksum" varchar(64)
  "size" bigint [not null]
  "visibility" varchar(50) [not null, note: 'public_image | authenticated_tenant | private_owner']
  "purpose" varchar(100) [not null, note: 'legacy_image | profile_image | event_image | attachment | document | system_asset']
  "lifecycle_state" varchar(50) [not null, note: 'pending | active | quarantined | delete_requested | deleted']
  "owning_resource_kind" varchar(100)
  "owning_resource_id" uuid
  "quarantined_at" timestamptz
  "quarantined_by" uuid
  "quarantine_reason" varchar(500)
  "tenant_id" uuid [not null]
  "actor_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" bool [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    actor_id
    (tenant_id, provider, lifecycle_state)
    (tenant_id, visibility, purpose)
    (tenant_id, owning_resource_kind, owning_resource_id) [note: 'Filtered when owning resource fields are present.']
    (provider, object_key) [unique, note: 'Filtered when object_key is present.']
  }

  Note: 'Tenant-scoped file metadata. Provider keys are internal; public access resolves through application metadata and visibility. Storage reconciliation uses lifecycle/quarantine/delete metadata to report drift, quarantine unsafe rows, and soft-delete physically removed objects.'
}

Table "storage_upload_sessions" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "user_id" uuid
  "provider" varchar(50) [not null]
  "expected_size_bytes" bigint [not null]
  "reserved_bytes" bigint [not null]
  "content_type" varchar(255) [not null]
  "original_file_name" varchar(500)
  "safe_display_name" varchar(500) [not null]
  "extension" varchar(50)
  "purpose" varchar(100) [not null]
  "visibility" varchar(50) [not null]
  "status" varchar(50) [not null, note: 'reserved | uploading | finalized | canceled | failed | expired']
  "object_key" varchar(1024)
  "sha256_checksum" varchar(64)
  "storage_object_id" uuid
  "owning_resource_kind" varchar(100)
  "owning_resource_id" uuid
  "idempotency_key" varchar(128)
  "failure_code" varchar(100)
  "failure_message" varchar(500)
  "expires_at" timestamptz [not null]
  "upload_started_at" timestamptz
  "finalized_at" timestamptz
  "canceled_at" timestamptz
  "failed_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, status, expires_at)
    (tenant_id, idempotency_key) [unique, note: 'Filtered when idempotency_key is present.']
    (provider, object_key) [note: 'Filtered when object_key is present.']
    storage_object_id
    user_id
    (tenant_id, owning_resource_kind, owning_resource_id) [note: 'Filtered when both owner fields are present.']
  }

  Note: 'Tenant/user scoped upload reservation before bytes are accepted.'
}

Table "storage_usage_counters" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "provider" varchar(50) [not null]
  "used_bytes" bigint [not null]
  "reserved_bytes" bigint [not null]
  "quarantined_bytes" bigint [not null]
  "object_count" bigint [not null]
  "last_recalculated_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, provider) [unique]
  }

  Note: 'Tenant/provider storage usage and reservation aggregate for quota checks.'
}

// ============================================================
// Events
// ============================================================

Table "event_series" {
  "id" uuid [pk, not null]
  "title" varchar(200) [not null]
  "slug" varchar(200)
  "description" varchar(2000)
  "featured_image_id" uuid
  "actor_id" uuid [not null]
  "is_published" boolean [not null]
  "total_views" int [not null]
  "visibility_type_id" int [not null]
  "start_date_utc" timestamptz
  "end_date_utc" timestamptz
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, slug) [unique, name: 'ix_event_series_tenant_id_slug']
    (tenant_id, is_published) [name: 'ix_event_series_tenant_id_is_published']
    (tenant_id, total_views) [name: 'ix_event_series_tenant_id_total_views']
  }
}

// ============================================================
// AI Assistant
// ============================================================

Table "ai_conversations" {
  "id" uuid [pk, not null, default: `uuidv7()`]
  "tenant_id" uuid [not null]
  "user_id" uuid [not null]
  "actor_id" uuid
  "status" int [not null]
  "title" varchar(200)
  "provider" varchar(100)
  "model_id" varchar(200)
  "blocked_reason" varchar(200)
  "last_message_sequence" bigint [not null]
  "created_at" timestamp [not null]
  "created_by" uuid
  "updated_at" timestamp
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamp
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    actor_id [name: 'ix_ai_conversations_actor_id']
    (tenant_id, actor_id, updated_at) [name: 'ix_ai_conversations_tenant_actor_updated_at']
    (tenant_id, user_id, status, updated_at) [name: 'ix_ai_conversations_tenant_user_status_updated_at']
    user_id [name: 'ix_ai_conversations_user_id']
  }

  Note: 'Tenant-scoped private AI assistant conversation aggregate. Status values: Active(1), Running(2), Blocked(3), Archived(4). last_message_sequence is the long cursor for ordered messages.'
}

Table "ai_messages" {
  "id" uuid [pk, not null, default: `uuidv7()`]
  "tenant_id" uuid [not null]
  "conversation_id" uuid [not null]
  "sequence" bigint [not null]
  "role" int [not null]
  "content" varchar(16000) [not null]
  "created_at" timestamp [not null]
  "created_by" uuid

  indexes {
    conversation_id [name: 'ix_ai_messages_conversation_id']
    (tenant_id, conversation_id, created_at) [name: 'ix_ai_messages_tenant_conversation_created_at']
    (tenant_id, conversation_id, sequence) [unique, name: 'ux_ai_messages_tenant_conversation_sequence']
  }

  Note: 'Ordered private conversation messages. Role values: System(1), User(2), Assistant(3), Tool(4). sequence must be positive and is unique per tenant/conversation.'
}

Table "ai_runs" {
  "id" uuid [pk, not null, default: `uuidv7()`]
  "tenant_id" uuid [not null]
  "conversation_id" uuid [not null]
  "status" int [not null]
  "provider" varchar(100) [not null]
  "model_id" varchar(200) [not null]
  "queued_at" timestamp [not null]
  "started_at" timestamp
  "completed_at" timestamp
  "failure_code" varchar(100)
  "failure_message" varchar(1000)

  indexes {
    conversation_id [name: 'ix_ai_runs_conversation_id']
    (tenant_id, conversation_id, queued_at) [name: 'ix_ai_runs_tenant_conversation_queued_at']
    (tenant_id, status, queued_at) [name: 'ix_ai_runs_tenant_status_queued_at']
  }

  Note: 'AI provider run audit row. Status values: Queued(1), InProgress(2), Succeeded(3), Failed(4), Cancelled(5). Failure metadata is bounded and safe for private history.'
}

Table "ai_conversation_references" {
  "id" uuid [pk, not null, default: `uuidv7()`]
  "tenant_id" uuid [not null]
  "conversation_id" uuid [not null]
  "kind" int [not null]
  "reference_id" uuid [not null]
  "display_name" varchar(500) [not null]
  "summary" varchar(2000)
  "created_at" timestamp [not null]
  "created_by" uuid

  indexes {
    conversation_id [name: 'ix_ai_conversation_references_conversation_id']
    (tenant_id, conversation_id, kind, reference_id) [unique, name: 'ux_ai_conversation_references_identity']
  }

  Note: 'Server-selected references attached to an AI conversation. Kind values: Event(1), EventSession(2), Actor(3), Organization(4).'
}

Table "ai_proposed_actions" {
  "id" uuid [pk, not null, default: `uuidv7()`]
  "tenant_id" uuid [not null]
  "conversation_id" uuid [not null]
  "message_id" uuid
  "kind" int [not null]
  "status" int [not null]
  "payload_json" jsonb [not null]
  "confirmed_by" uuid
  "confirmed_at" timestamp
  "rejected_by" uuid
  "rejected_at" timestamp
  "result_resource_id" uuid
  "failure_code" varchar(100)
  "failure_message" varchar(1000)
  "created_at" timestamp [not null]
  "created_by" uuid

  indexes {
    conversation_id [name: 'ix_ai_proposed_actions_conversation_id']
    message_id [name: 'ix_ai_proposed_actions_message_id']
    (tenant_id, conversation_id, status, created_at) [name: 'ix_ai_proposed_actions_tenant_conversation_status_created_at']
    (tenant_id, status, kind, created_at) [name: 'ix_ai_proposed_actions_tenant_status_kind_created_at']
  }

  Note: 'Typed provider proposal requiring explicit user confirmation before side effects. Kind values currently allow CreateEventDraft(1). Status values: Proposed(1), Confirmed(2), Rejected(3), Executed(4), Failed(5). payload_json must be a JSON object.'
}

Table "ai_tool_executions" {
  "id" uuid [pk, not null, default: `uuidv7()`]
  "tenant_id" uuid [not null]
  "proposed_action_id" uuid [not null]
  "tool_name" varchar(200) [not null]
  "started_at" timestamp [not null]
  "completed_at" timestamp
  "succeeded" boolean [not null]
  "failure_code" varchar(100)
  "failure_message" varchar(1000)

  indexes {
    proposed_action_id [name: 'ix_ai_tool_executions_proposed_action_id']
    (tenant_id, proposed_action_id, started_at) [name: 'ix_ai_tool_executions_tenant_action_started_at']
    (tenant_id, tool_name, started_at) [name: 'ix_ai_tool_executions_tenant_tool_started_at']
  }

  Note: 'Audit rows for future confirmed AI tool execution attempts. No automatic tool execution is enabled by the MVP send flow.'
}

Ref: "ai_conversations"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "ai_conversations"."user_id" > "users"."id" [delete: restrict]
Ref: "ai_conversations"."actor_id" > "actors"."id" [delete: set null]
Ref: "ai_messages"."conversation_id" > "ai_conversations"."id" [delete: cascade]
Ref: "ai_runs"."conversation_id" > "ai_conversations"."id" [delete: cascade]
Ref: "ai_conversation_references"."conversation_id" > "ai_conversations"."id" [delete: cascade]
Ref: "ai_proposed_actions"."conversation_id" > "ai_conversations"."id" [delete: cascade]
Ref: "ai_proposed_actions"."message_id" > "ai_messages"."id" [delete: set null]
Ref: "ai_tool_executions"."proposed_action_id" > "ai_proposed_actions"."id" [delete: cascade]

Table "events" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "event_type_id" int
  "title" varchar(200) [not null]
  "subtitle" varchar(200)
  "description" varchar(150)
  "content" varchar(5000)
  "audience_gender_id" int
  "audience_age_id" int
  "actor_id" uuid [not null]
  "featured_image_id" uuid
  "total_views" int [not null]
  "madhab_id" int
  "tenant_id" uuid [not null]
  "slug" varchar(200)
  "visibility_type_id" int [not null]
  "session_count" int
  "event_status_id" int [not null]
  "first_session_date" date
  "last_session_date" date
  "timezone" varchar(100)
  "first_session_start_utc" timestamptz
  "last_session_start_utc" timestamptz
  "event_time_zone_id" varchar(100)
  "event_series_id" uuid
  "series_order" int
  "registration_policy_id" int [note: 'FK to event_registration_policies. Null = Flexible.']
  "event_format_id" int [not null]
  "atproto_record_id" uuid
  "instantiated_from_template_at" timestamptz
  "last_synced_from_template_at" timestamptz
  "source_template_id" uuid
  "source_template_key" text
  "source_template_version" int
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]
  "background_color" varchar(50)
  "background_effect" varchar(50)
  "background_image_id" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_events_tenant_id_id']
    (tenant_id, is_deleted, event_status_id) [name: 'ix_events_tenant_active_status']
    (tenant_id, actor_id, created_at) [name: 'ix_events_tenant_actor_created']
    (tenant_id, first_session_date, last_session_date) [name: 'ix_events_tenant_daterange']
    (tenant_id, event_type_id) [name: 'ix_events_tenant_eventtype']
    (tenant_id, slug) [name: 'ix_events_tenant_slug']
    (tenant_id, visibility_type_id) [name: 'ix_events_tenant_visibility']
    (atproto_record_id) [name: 'ix_events_atproto_record_id']
    (audience_age_id) [name: 'ix_events_audience_age_id']
    (audience_gender_id) [name: 'ix_events_audience_gender_id']
    (background_image_id) [name: 'ix_events_background_image_id']
    (featured_image_id) [name: 'ix_events_featured_image_id']
    (event_format_id) [name: 'ix_events_event_format_id']
    (event_series_id) [name: 'ix_events_event_series_id']
    (event_status_id) [name: 'ix_events_event_status_id']
    (event_type_id) [name: 'ix_events_event_type_id']
    (madhab_id) [name: 'ix_events_madhab_id']
    (registration_policy_id) [name: 'ix_events_registration_policy_id']
    (visibility_type_id) [name: 'ix_events_visibility_type_id']
  }

  Note: 'Core aggregate. Checks: CK_Event_SessionDateRange, CK_Event_SessionStartUtcRange, CK_Event_TimeZoneIdNotBlank. Soft-deletable, tenant-scoped, concurrency-protected. Event graph child rows use (tenant_id, id) as the principal key. UTC session starts are source of truth; local dates are server-owned projections.'
}


Table "event_islamic_aspects" {
  "id" uuid [pk, not null, note: 'shared PK with events']
  "madhab_id" int
  "reference_prayer" int
  "prayer_time_offset" int
  "gender_mode" int [not null]
  "includes_quran_recitation" boolean [not null]
  "primary_language_id" int
}

Table "event_tech_aspects" {
  "id" uuid [pk, not null, note: 'shared PK with events']
  "github_repo_url" varchar(2048)
  "hackathon_track" varchar(200)
  "skill_level" int [not null]
  "tech_stack_tags" varchar(1000)
  "requires_laptop" boolean [not null]
  "is_coding_competition" boolean [not null]
  "max_team_size" int
  "prize_pool" decimal(19,4)
  "prize_currency_code" varchar(3)
}

Table "event_days" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "event_id" uuid [not null]
  "tenant_id" uuid [not null]
  "local_date" date [not null, note: 'calendar date in event timezone']
  "label" varchar(200)
  "description" varchar(2000)
  "banner_text" varchar(500)
  "banner_image_id" uuid
  "is_published" boolean [not null, default: false]
  "sort_order" int [not null, default: 0]
  "allows_day_scope_registration" boolean [not null, default: false]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_days_tenant_id_id']
    (tenant_id, event_id, id) [unique, name: 'ak_event_days_tenant_id_event_id_id']
    (tenant_id, event_id, local_date) [unique, name: 'ix_event_days_tenant_event_local_date', note: 'partial: is_deleted = false']
    (tenant_id, event_id, sort_order) [name: 'ix_event_days_tenant_event_sort']
    (tenant_id, event_id, is_published) [name: 'ix_event_days_tenant_event_published']
  }

  Note: 'Calendar-day grouping for multi-day events.'
}

Table "event_agenda_items" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "event_id" uuid [not null]
  "event_day_id" uuid
  "tenant_id" uuid [not null]
  "title" varchar(500) [not null]
  "description" varchar(2000)
  "start_time" timestamptz [not null]
  "end_time" timestamptz [not null]
  "local_start_date" date [not null]
  "local_end_date" date [not null]
  "local_start_time" time [not null]
  "local_end_time" time [not null]
  "local_start_minute_of_day" int [not null]
  "local_end_minute_of_day" int [not null]
  "event_location_id" uuid
  "location_id" uuid
  "room_id" uuid
  "kind_id" int
  "sort_order" int [not null, default: 0]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, event_id, event_day_id) [name: 'ix_event_agenda_items_tenant_id_event_id_event_day_id']
    (tenant_id, event_id, event_location_id, location_id) [name: 'ix_event_agenda_items_elp_consistency']
    (tenant_id, location_id) [name: 'ix_event_agenda_items_tenant_id_location_id']
    (tenant_id, location_id, room_id) [name: 'ix_event_agenda_items_tenant_id_location_id_room_id']
    (tenant_id, event_id, local_start_date, local_start_minute_of_day) [name: 'ix_event_agenda_items_tenant_event_local_start']
    (tenant_id, event_id, sort_order) [name: 'ix_event_agenda_items_tenant_event_sort']
  }

  Note: 'Non-session schedule entries (breaks, prayers). Checks: CK_EventAgendaItem_EndAfterStart, CK_EventAgendaItem_LocalDateRange, CK_EventAgendaItem_RoomRequiresLocation, CK_EventAgendaItem_LocalStartMinuteRange, CK_EventAgendaItem_LocalEndMinuteRange, CK_EventAgendaItem_LocalStartMinuteMatchesTime, CK_EventAgendaItem_LocalEndMinuteMatchesTime. UTC times are source of truth; local fields are server-owned projections.'
}

Table "event_sessions" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "event_id" uuid [not null]
  "event_day_id" uuid
  "event_session_kind_id" int
  "event_session_status_id" int [not null, default: 1, note: 'FK to event_session_statuses. Default/backfill = Draft.']
  "start_time" timestamptz
  "end_time" timestamptz
  "end_time_type" int [not null, default: 0, note: 'Values: Fixed(0), OpenEnded(1), RelativeToPrayer(2)']
  "event_location_id" uuid
  "location_id" uuid
  "room_id" uuid
  "title" varchar(500)
  "featured_image_id" uuid
  "tenant_id" uuid [not null]
  "slug" varchar(200)
  "max_audience_attendees" int
  "current_audience_attendees" int
  "registration_mode_id" int
  "description" varchar(500)
  "sort_order" int [not null, default: 0]
  "local_start_date" date
  "local_end_date" date
  "local_start_time" time
  "local_end_time" time
  "local_start_minute_of_day" int
  "local_end_minute_of_day" int
  "instantiated_from_template_at" timestamptz
  "last_synced_from_template_at" timestamptz
  "source_template_id" uuid
  "source_template_key" text
  "source_template_version" int
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_sessions_tenant_id_id']
    (tenant_id, event_id, id) [unique, name: 'ak_event_sessions_tenant_id_event_id_id']
    (tenant_id, event_id, event_day_id) [name: 'ix_event_sessions_tenant_id_event_id_event_day_id']
    (tenant_id, event_id, event_location_id, location_id) [name: 'ix_event_sessions_elp_consistency']
    (tenant_id, location_id) [name: 'ix_event_sessions_tenant_id_location_id']
    (tenant_id, event_id, local_start_date, local_start_minute_of_day) [name: 'ix_event_sessions_tenant_event_local_start']
    (tenant_id, location_id, room_id, start_time, end_time) [name: 'ix_event_sessions_tenant_location_room_time']
    (tenant_id, event_day_id, sort_order) [name: 'ix_event_sessions_tenant_day_sort']
    (event_session_kind_id) [name: 'ix_event_sessions_event_session_kind_id']
    (event_session_status_id) [name: 'ix_event_sessions_event_session_status_id']
    (registration_mode_id) [name: 'ix_event_sessions_registration_mode_id']
    (featured_image_id) [name: 'ix_event_sessions_featured_image_id']
  }

  Note: 'Draft-capable program item. Checks: CK_EventSession_EndAfterStart (conditional when scheduled), CK_EventSession_LocalDateRange (conditional when both local dates are present), CK_EventSession_RoomRequiresLocation, CK_EventSession_LocalStartMinuteRange, CK_EventSession_LocalEndMinuteRange, CK_EventSession_LocalStartMinuteMatchesTime, CK_EventSession_LocalEndMinuteMatchesTime. Nullable start/end/local projection fields represent unscheduled draft/internal sessions. Model-owned exclusion: EX_EventSession_RoomNoOverlap prevents overlapping active scheduled sessions in the same tenant/location/room using tstzrange(start_time, end_time, ''[)'') and ignores rows where start_time or end_time is null. UTC times are source of truth when present; local fields are server-owned projections.'
}

Table "event_session_groups" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "event_id" uuid [not null]
  "name" varchar(200) [not null]
  "slug" varchar(200)
  "description" varchar(1000)
  "event_location_id" uuid
  "location_id" uuid
  "room_id" uuid
  "color" varchar(50)
  "sort_order" int [not null, default: 0]
  "is_published" boolean [not null]
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_session_groups_tenant_id_id']
    (tenant_id, event_id, id) [unique, name: 'ak_event_session_groups_tenant_id_event_id_id']
    (tenant_id, event_id, event_location_id, location_id) [name: 'ix_event_session_groups_elp_consistency']
    (tenant_id, location_id) [name: 'ix_event_session_groups_tenant_id_location_id']
    (tenant_id, location_id, room_id) [name: 'ix_event_session_groups_tenant_id_location_id_room_id']
    (tenant_id, event_id, sort_order) [name: 'ix_event_session_groups_tenant_event_sort']
  }

  Note: 'Tenant-scoped program section/track/devroom grouping for sessions inside an event. Check: CK_EventSessionGroup_RoomRequiresLocation.'
}

Table "event_session_group_sessions" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "event_session_group_id" uuid [not null]
  "event_session_id" uuid [not null]
  "event_id" uuid [not null]
  "is_primary" boolean [not null]
  "sort_order" int [not null, default: 0]
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, event_id, event_session_group_id, event_session_id) [unique, name: 'ix_event_session_group_sessions_tenant_event_group_session', note: 'filter: is_deleted = false']
    (tenant_id, event_id, event_session_id, is_primary) [unique, name: 'ix_event_session_group_sessions_tenant_event_session_primary', note: 'filter: is_primary = true AND is_deleted = false']
    (tenant_id, event_session_group_id, sort_order) [name: 'ix_event_session_group_sessions_tenant_group_sort']
  }

  Note: 'Explicit join assigning EventSession program items to tracks/devrooms/sections.'
}

Table "event_session_islamic_aspects" {
  "event_session_id" uuid [pk, not null, note: 'shared PK with event_sessions']
  "start_time_type" int [not null]
  "reference_prayer" int
  "offset_minutes" int
  "end_reference_prayer" int
  "end_offset_minutes" int
  "requires_wudu" boolean [not null, default: false]
  "ritual_requirements_json" jsonb

  Note: 'Checks: CK_EventSessionIslamicAspect_StartTimeState, CK_EventSessionIslamicAspect_OffsetRange, CK_EventSessionIslamicAspect_ReferencePrayerRange, CK_EventSessionIslamicAspect_EndOffsetRange, CK_EventSessionIslamicAspect_EndReferencePrayerRange, CK_EventSessionIslamicAspect_EndTimeState. Fixed start requires null prayer fields; prayer-relative start requires reference_prayer and offset_minutes (-180..180). Fixed end requires null end prayer fields; prayer-relative end requires end_reference_prayer and end_offset_minutes (-180..180).'
}

Table "event_session_agenda_items" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "event_session_id" uuid [not null]
  "start_time" timestamptz [not null]
  "end_time" timestamptz [not null]
  "title" varchar(500) [not null]
  "description" varchar(500)
  "event_location_id" uuid
  "location_id" uuid
  "tenant_id" uuid [not null]

  indexes {
    (tenant_id, event_session_id, event_location_id, location_id) [name: 'ix_event_session_agenda_items_elp_consistency']
    (tenant_id, event_location_id) [name: 'ix_event_session_agenda_items_tenant_id_event_location_id']
    (tenant_id, location_id) [name: 'ix_event_session_agenda_items_tenant_id_location_id']
  }
}

Table "event_session_languages" {
  "id" int [pk, not null, note: 'auto-increment']
  "event_session_id" uuid [not null]
  "language_id" int [not null]
  "tenant_id" uuid [not null]

  indexes {
    (tenant_id, event_session_id, language_id) [unique, name: 'ix_eventsessionlanguages_session_language']
  }
}

Table "event_session_speakers" {
  "id" uuid [pk, not null]
  "actor_id" uuid [not null]
  "event_session_id" uuid [not null]
  "tenant_id" uuid [not null]

  indexes {
    (tenant_id, event_session_id, actor_id) [unique, name: 'ix_event_session_speakers_tenant_session_actor']
    (tenant_id, actor_id) [name: 'ix_event_session_speakers_tenant_id_actor_id']
  }
}

Table "event_categories" {
  "id" uuid [pk, not null]
  "event_id" uuid [not null]
  "category_id" uuid [not null]
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, event_id, category_id) [unique, name: 'ix_event_categories_tenant_event_category']
    (tenant_id, category_id) [name: 'ix_event_categories_tenant_id_category_id']
  }
}

Table "event_tags" {
  "id" uuid [pk, not null]
  "event_id" uuid [not null]
  "tag_id" uuid [not null]
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, event_id, tag_id) [unique, name: 'ix_event_tags_tenant_event_tag']
    (tenant_id, tag_id) [name: 'ix_event_tags_tenant_id_tag_id']
  }
}

Table "event_participation_configurations" {
  "id" uuid [pk, not null, note: 'Shared primary key with events.id']
  "tenant_id" uuid [not null]
  "participation_handling_mode_id" int [not null]
  "advance_registration_obligation_id" int [not null]
  "identity_access_mode_id" int
  "guest_recovery_policy" int [note: 'Nullable scalar enum; intentionally not a lookup table']
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    advance_registration_obligation_id [name: 'ix_event_participation_configurations_advance_registration_obl']
    identity_access_mode_id [name: 'ix_event_participation_configurations_identity_access_mode_id']
    participation_handling_mode_id [name: 'ix_event_participation_configurations_participation_handling_m']
    (tenant_id, id) [unique, name: 'ix_event_participation_configurations_tenant_id_id']
  }

  Note: 'Tenant-scoped 1:1 Event participation policy. Identity and recovery apply only when the typed handling mode permits them.'
}

Table "event_capacity_pools" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "name" varchar(200) [not null]
  "maximum_quantity" int
  "hold_duration_seconds" int [not null]
  "capacity_hold_policy_id" int [not null]
  "capacity_oversell_policy_id" int [not null]
  "is_active" boolean [not null]
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_capacity_pools_tenant_id_id']
    capacity_hold_policy_id [name: 'ix_event_capacity_pools_capacity_hold_policy_id']
    capacity_oversell_policy_id [name: 'ix_event_capacity_pools_capacity_oversell_policy_id']
    (tenant_id, event_id, name) [unique, name: 'ix_event_capacity_pools_tenant_id_event_id_name', note: 'filter: is_deleted = false']
  }

  Note: 'Tenant-scoped shared capacity definition. Soft-deletable, audited, and concurrency-protected.'
}

Table "event_ticket_catalog_versions" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "currency_code" varchar(3) [not null]
  "version_number" int [not null]
  "ticket_catalog_status_id" int [not null]
  "merchant_disclosure_text" varchar(2000)
  "refund_policy_disclosure_text" varchar(2000)
  "support_contact_disclosure_text" varchar(2000)
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_ticket_catalog_versions_tenant_id_id']
    ticket_catalog_status_id [name: 'ix_event_ticket_catalog_versions_ticket_catalog_status_id']
    (tenant_id, event_id) [unique, name: 'ix_event_ticket_catalog_versions_tenant_id_event_id', note: 'filter: ticket_catalog_status_id = 2 AND is_deleted = false']
    (tenant_id, event_id, version_number) [unique, name: 'ix_event_ticket_catalog_versions_tenant_id_event_id_version_nu', note: 'filter: is_deleted = false']
  }

  Note: 'Versioned Event ticket catalog. At most one non-deleted published catalog exists per tenant and Event.'
}

Table "event_ticket_types" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "catalog_id" uuid [not null]
  "name" varchar(200) [not null]
  "currency_code" varchar(3) [not null]
  "ticket_pricing_mode_id" int [not null]
  "fixed_price_minor" bigint
  "minimum_price_minor" bigint
  "suggested_price_minor" bigint
  "participant_data_collection_mode_id" int [not null]
  "capacity_pool_id" uuid
  "minimum_age" int
  "maximum_age" int
  "requires_guardian" boolean [not null]
  "requires_approval" boolean [not null]
  "per_order_limit" int
  "per_account_limit" int
  "per_verified_contact_limit" int
  "per_booking_party_limit" int
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_ticket_types_tenant_id_id']
    participant_data_collection_mode_id [name: 'ix_event_ticket_types_participant_data_collection_mode_id']
    ticket_pricing_mode_id [name: 'ix_event_ticket_types_ticket_pricing_mode_id']
    (tenant_id, capacity_pool_id) [name: 'ix_event_ticket_types_tenant_id_capacity_pool_id']
    (tenant_id, catalog_id) [name: 'ix_event_ticket_types_tenant_id_catalog_id']
  }

  Note: 'Ticket type within one catalog version. Monetary values are nullable bigint minor units selected by pricing mode. CK_EventTicketType_MoneyNonnegative rejects negative fixed, minimum, or suggested minor-unit values when present.'
}

Table "ticket_type_entitlements" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "ticket_type_id" uuid [not null]
  "tenant_id" uuid [not null]
  "target_event_id" uuid [not null]
  "entitlement_scope_type_id" int [not null]
  "event_day_id" uuid
  "event_session_id" uuid
  "included_quantity" int [not null]
  "entitlement_selection_rule_id" int [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_ticket_type_entitlements_tenant_id_id']
    entitlement_scope_type_id [name: 'ix_ticket_type_entitlements_entitlement_scope_type_id']
    entitlement_selection_rule_id [name: 'ix_ticket_type_entitlements_entitlement_selection_rule_id']
    (tenant_id, ticket_type_id) [name: 'ix_ticket_type_entitlements_tenant_id_ticket_type_id']
    (tenant_id, target_event_id, event_day_id) [name: 'ix_ticket_type_entitlements_tenant_id_target_event_id_event_da']
    (tenant_id, target_event_id, event_session_id) [name: 'ix_ticket_type_entitlements_tenant_id_target_event_id_event_se']
  }

  Note: 'Ticket access entitlement targeting the Event, one Event day, or one Event session.'
}

Table "registration_orders" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "account_user_id" uuid
  "purchaser_actor_id" uuid
  "booking_party_type_id" int [not null]
  "registration_order_status_id" int [not null]
  "ticket_catalog_version_id" uuid [not null]
  "participation_configuration_version_snapshot" uuid [not null]
  "participation_handling_mode_id_snapshot" int [not null]
  "advance_registration_obligation_id_snapshot" int [not null]
  "identity_access_mode_id_snapshot" int
  "guest_recovery_policy_snapshot" int
  "registration_order_participation_configuration_version_snapshot" uuid [not null]
  "registration_workflow_version_id" uuid
  "guest_access_token_hash" varchar(44)
  "currency_code" varchar(3) [not null]
  "expires_at" timestamptz
  "submitted_at" timestamptz
  "confirmed_at" timestamptz
  "rejected_at" timestamptz
  "cancelled_at" timestamptz
  "organizer_directed_total_minor_snapshot" bigint [not null]
  "platform_fee_total_minor_snapshot" bigint [not null]
  "organizer_earnings_total_minor_snapshot" bigint [not null]
  "platform_contribution_total_minor_snapshot" bigint [not null]
  "pre_discount_organizer_directed_total_minor_snapshot" bigint [not null, default: 0]
  "promotion_discount_total_minor_snapshot" bigint [not null, default: 0]
  "post_discount_organizer_directed_total_minor_snapshot" bigint [not null, default: 0]
  "applied_promotion_definition_version_id_snapshot" uuid
  "applied_promotion_code_id_snapshot" uuid
  "applied_promotion_display_label_snapshot" varchar(16)
  "active_promotion_reservation_id" uuid
  "total_due_minor_snapshot" bigint [not null]
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_orders_tenant_id_id']
    booking_party_type_id [name: 'ix_registration_orders_booking_party_type_id']
    registration_order_status_id [name: 'ix_registration_orders_registration_order_status_id']
    (tenant_id, event_id, registration_order_status_id) [name: 'ix_registration_orders_tenant_id_event_id_registration_order_s']
    (tenant_id, applied_promotion_code_id_snapshot) [name: 'ix_registration_orders_tenant_id_applied_promotion_code_id_sna']
    (tenant_id, applied_promotion_definition_version_id_snapshot) [name: 'ix_registration_orders_tenant_id_applied_promotion_definition_']
    (tenant_id, expires_at) [name: 'ix_registration_orders_tenant_id_expires_at']
    (tenant_id, ticket_catalog_version_id) [name: 'ix_registration_orders_tenant_id_ticket_catalog_version_id']
  }

  Note: 'Tenant-scoped checkout aggregate with immutable participation, catalog, money, and access snapshots.'
}

Table "registration_inventory_holds" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "registration_order_id" uuid [not null]
  "capacity_pool_id" uuid [not null]
  "ticket_type_id" uuid [not null]
  "tenant_id" uuid [not null]
  "quantity" int [not null]
  "registration_inventory_hold_status_id" int [not null]
  "expires_at" timestamptz [not null]
  "consumed_at" timestamptz
  "released_at" timestamptz
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_inventory_holds_tenant_id_id']
    registration_inventory_hold_status_id [name: 'ix_registration_inventory_holds_registration_inventory_hold_st']
    (tenant_id, capacity_pool_id, registration_inventory_hold_status_id) [name: 'ix_registration_inventory_holds_tenant_id_capacity_pool_id_reg']
    (tenant_id, registration_inventory_hold_status_id, expires_at) [name: 'ix_registration_inventory_holds_tenant_id_registration_invento']
    (tenant_id, registration_order_id) [name: 'ix_registration_inventory_holds_tenant_id_registration_order_id']
    (tenant_id, ticket_type_id) [name: 'ix_registration_inventory_holds_tenant_id_ticket_type_id']
  }

  Note: 'Tenant-scoped, audited, soft-deletable capacity reservation. Status controls whether its quantity is allocated.'
}

Table "registration_order_lines" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "registration_order_id" uuid [not null]
  "tenant_id" uuid [not null]
  "ticket_type_id" uuid [not null]
  "quantity" int [not null]
  "unit_price_amount_snapshot" bigint [not null]
  "chosen_unit_price_amount_snapshot" bigint
  "currency_code_snapshot" varchar(3) [not null]
  "line_subtotal_snapshot" bigint [not null]
  "pre_discount_line_subtotal_minor_snapshot" bigint [not null, default: 0]
  "promotion_discount_amount_minor_snapshot" bigint [not null, default: 0]
  "post_discount_line_subtotal_minor_snapshot" bigint [not null, default: 0]
  "ticket_type_name_snapshot" varchar(200) [not null]
  "ticket_pricing_mode_snapshot" int [not null]
  "minimum_price_amount_snapshot" bigint
  "suggested_price_amount_snapshot" bigint
  "ticket_catalog_version_id" uuid [not null]
  "platform_fee_policy_version_snapshot" int
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_order_lines_tenant_id_id']
    (tenant_id, registration_order_id, id) [unique, name: 'ak_registration_order_lines_tenant_id_registration_order_id_id']
    (tenant_id, registration_order_id, ticket_type_id) [unique, name: 'ix_registration_order_lines_tenant_id_registration_order_id_ti']
    (tenant_id, ticket_catalog_version_id) [name: 'ix_registration_order_lines_tenant_id_ticket_catalog_version_id']
    (tenant_id, ticket_type_id) [name: 'ix_registration_order_lines_tenant_id_ticket_type_id']
  }

  Note: 'Immutable selected-ticket and price snapshots for one registration order.'
}

Table "promotion_definitions" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "definition_group_id" uuid [not null]
  "tenant_id" uuid [not null]
  "version_number" int [not null]
  "promotion_definition_status_id" int [not null]
  "scope_metadata" text [not null]
  "display_label" varchar(200) [not null]
  "eligibility" text [not null]
  "discount_rule" text [not null]
  "starts_at_utc" timestamptz [not null]
  "ends_at_utc" timestamptz [not null]
  "total_redemption_limit" int
  "per_verified_purchaser_limit" int
  "published_at_utc" timestamptz
  "revoked_at_utc" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "scope_currency_code" varchar(3) [not null]
  "scope_event_id" uuid [not null]
  "scope_ticket_catalog_version_id" uuid [not null]
  "scope_ticket_catalog_version_number" int [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_promotion_definitions_tenant_id_id']
    promotion_definition_status_id [name: 'ix_promotion_definitions_promotion_definition_status_id']
    (tenant_id, definition_group_id, version_number) [unique, name: 'ix_promotion_definitions_tenant_id_definition_group_id_version']
    (tenant_id, scope_event_id, scope_ticket_catalog_version_id, promotion_definition_status_id) [name: 'ix_promotion_definitions_tenant_id_scope_event_id_scope_ticket']
  }

  Note: 'Tenant-scoped versioned promotion definition with scope, eligibility, discount, lifecycle, and redemption-limit metadata.'
}

Table "promotion_codes" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "promotion_definition_version_id" uuid [not null]
  "scope_metadata" text [not null]
  "display_label" varchar(16) [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_active" boolean [not null, default: true]
  "lookup_digest" varchar(128) [not null, note: 'Opaque digest only; raw promotion codes are never stored.']
  "lookup_key_version" int [not null]
  "retired_at_utc" timestamptz
  "scope_currency_code" varchar(3) [not null]
  "scope_event_id" uuid [not null]
  "scope_ticket_catalog_version_id" uuid [not null]
  "scope_ticket_catalog_version_number" int [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_promotion_codes_tenant_id_id']
    (tenant_id, promotion_definition_version_id, is_active) [name: 'ix_promotion_codes_tenant_id_promotion_definition_version_id_i']
    (tenant_id, scope_event_id, scope_ticket_catalog_version_id, lookup_key_version) [name: 'ix_promotion_codes_tenant_id_scope_event_id_scope_ticket_catal']
    (tenant_id, scope_event_id, scope_ticket_catalog_version_id, lookup_key_version, lookup_digest) [unique, name: 'ix_promotion_codes_tenant_id_scope_event_id_scope_ticket_catal1']
  }

  Note: 'Tenant-scoped promotion code identity using versioned lookup digests and display-only labels.'
}

Table "promotion_reservations" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "promotion_definition_version_id" uuid [not null]
  "promotion_code_id" uuid [not null]
  "promotion_reservation_status_id" int [not null]
  "order_reservation_slot" uuid [not null]
  "reserved_at_utc" timestamptz [not null]
  "consumed_at_utc" timestamptz
  "released_at_utc" timestamptz
  "expired_at_utc" timestamptz
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_promotion_reservations_tenant_id_id']
    promotion_reservation_status_id [name: 'ix_promotion_reservations_promotion_reservation_status_id']
    (registration_order_id, order_reservation_slot) [unique, name: 'ix_promotion_reservations_registration_order_id_order_reservat']
    (tenant_id, promotion_code_id) [name: 'ix_promotion_reservations_tenant_id_promotion_code_id']
    (tenant_id, promotion_definition_version_id, promotion_reservation_status_id) [name: 'ix_promotion_reservations_tenant_id_promotion_definition_versi']
    (tenant_id, registration_order_id) [name: 'ix_promotion_reservations_tenant_id_registration_order_id']
  }

  Note: 'Tenant-scoped promotion usage reservation. Checks: active rows use the zero slot; consumed/released/expired rows use id as slot, and exactly the matching terminal timestamp is set.'
}

Table "participant_types" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" text

  indexes {
    master_code [unique, name: 'ix_participant_types_master_code']
  }

  Note: 'Lookup: Adult(1), Child(2), Dependent(3), Employee(4), Guest(5), Unnamed(6). Seeded.'
}

Table "assignment_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(50) [not null]
  "full_name" varchar(100) [not null]
  "description" text

  indexes {
    master_code [unique, name: 'ix_assignment_statuses_master_code']
  }

  Note: 'Lookup: Unassigned(1), Assigned(2), Deferred(3). Seeded.'
}

Table "registration_requirement_criticalities" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_requirement_criticalities_master_code']
  }

  Note: 'Lookup: Required(1), Optional(2), Informational(3), Post-registration(4). Seeded.'
}

Table "registration_requirement_completion_effects" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_requirement_completion_effects_master_code']
  }

  Note: 'Lookup: Blocks registration(1), Enriches registration(2), No registration effect(3). Seeded.'
}

Table "registration_answer_sync_modes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_answer_sync_modes_master_code']
  }

  Note: 'Lookup: None(1), Completion only(2), Selected fields(3), Full canonical(4), Mirror only(5). Seeded.'
}

Table "registration_requirement_subject_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_requirement_subject_types_master_code']
  }

  Note: 'Lookup: All orders(1), Specific ticket type(2), Every participant(3), Lead booker only(4), Child participants(5), Specific session selection(6). Seeded.'
}


Table "registration_answer_subject_types" {
  "id" int [pk, not null]
  "master_code" text [not null]
  "full_name" text [not null]
  "description" text

  Note: 'Lookup: answer/consent subject identity. Values: RegistrationOrder(1), Purchaser(2), Participant(3), TicketAssignment(4), SessionSelection(5). Runtime-seeded.'
}

Table "registration_attempt_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_attempt_statuses_master_code']
  }

  Note: 'Lookup: registration-attempt lifecycle. Values include Issued, Consumed, Expired, Superseded. Runtime-seeded.'
}

Table "registration_submission_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ix_registration_submission_statuses_master_code']
  }

  Note: 'Lookup: submission lifecycle. Values include Received, Finalized, EvidenceOnly. Runtime-seeded.'
}

Table "registration_provider_kinds" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  indexes {
    master_code [unique, name: 'ix_registration_provider_kinds_master_code']
  }
}

Table "registration_provider_deployment_kinds" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  indexes {
    master_code [unique, name: 'ix_registration_provider_deployment_kinds_master_code']
  }
}

Table "registration_provider_schema_authorities" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  indexes {
    master_code [unique, name: 'ix_registration_provider_schema_authorities_master_code']
  }
}

Table "registration_provider_presentation_modes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  indexes {
    master_code [unique, name: 'ix_registration_provider_presentation_modes_master_code']
  }
}

Table "registration_provider_collection_modes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  indexes {
    master_code [unique, name: 'ix_registration_provider_collection_modes_master_code']
  }
}

Table "registration_provider_completion_modes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  indexes {
    master_code [unique, name: 'ix_registration_provider_completion_modes_master_code']
  }
}

Table "registration_provider_trust_levels" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  indexes {
    master_code [unique, name: 'ix_registration_provider_trust_levels_master_code']
  }
}

Table "registration_provider_drift_classes" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  indexes {
    master_code [unique, name: 'ix_registration_provider_drift_classes_master_code']
  }
}

Table "registration_provider_binding_states" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  indexes {
    master_code [unique, name: 'ix_registration_provider_binding_states_master_code']
  }
}

Table "registration_workflows" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "purpose" varchar(100) [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, event_id, id) [unique, name: 'ak_registration_workflows_tenant_id_event_id_id']
    (tenant_id, event_id, purpose) [unique, name: 'ix_registration_workflows_tenant_id_event_id_purpose']
  }

  Note: 'One tenant/event registration workflow per stable purpose.'
}

Table "registration_requirements" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_workflow_id" uuid [not null]
  "ordinal" int [not null]
  "criticality_id" int [not null]
  "completion_effect_id" int [not null]
  "answer_sync_mode_id" int [not null]
  "applies_to_subject_type_id" int [not null]
  "applies_to_subject_id" uuid
  "can_skip" boolean [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, event_id, registration_workflow_id, id) [unique, name: 'ak_registration_requirements_tenant_id_event_id_registration_w']
    (registration_workflow_id, ordinal) [unique, name: 'ix_registration_requirements_registration_workflow_id_ordinal']
    (tenant_id, event_id) [name: 'ix_registration_requirements_tenant_id_event_id']
  }

  Note: 'Workflow-owned requirement with ALL-at-workflow evaluation and typed applicability.'
}

Table "participation_requirement_attachments" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "participation_configuration_id" uuid [not null]
  "registration_workflow_id" uuid [not null]
  "registration_requirement_id" uuid [not null]
  "registration_form_id" uuid
  "registration_form_version_id" uuid
  "is_standalone_questionnaire" boolean [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (participation_configuration_id, registration_requirement_id) [unique, note: 'filter: is_deleted = false']
    (participation_configuration_id, is_standalone_questionnaire) [unique, note: 'filter: is_deleted = false AND is_standalone_questionnaire = true']
    (tenant_id, event_id)
  }

  Note: 'Participation-owned attachment. CHECK event_id = participation_configuration_id; standalone rows require one published form version while non-standalone rows carry no form identity.'
}

Table "registration_channels" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_workflow_id" uuid [not null]
  "registration_requirement_id" uuid [not null]
  "ordinal" int [not null]
  "registration_provider_binding_id" uuid
  "is_native" boolean [not null]
  "registration_provider_binding_key" uuid [not null, note: 'computed: COALESCE(registration_provider_binding_id, zero uuid)']
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, event_id, registration_workflow_id, registration_requirement_id, id) [unique, name: 'ak_registration_channels_tenant_id_event_id_registration_workf']
    (tenant_id, event_id, registration_workflow_id, registration_requirement_id, id, registration_provider_binding_key) [unique, name: 'ak_registration_channels_tenant_id_event_id_registration_workf1']
    (registration_requirement_id, ordinal) [unique, name: 'ix_registration_channels_registration_requirement_id_ordinal']
    registration_provider_binding_id [name: 'ix_registration_channels_registration_provider_binding_id']
    (tenant_id, registration_provider_binding_id) [name: 'ix_registration_channels_tenant_id_registration_provider_bindi']
    (tenant_id, event_id) [name: 'ix_registration_channels_tenant_id_event_id']
  }

  Note: 'Requirement-owned alternative channel. Check ck_registration_channels_provider_shape: native rows require null provider binding and zero binding key; provider rows require binding_id = binding_key.'
}

Table "registration_form_statuses" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique]
  }
}

Table "registration_field_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique]
  }
}

Table "registration_organizer_visibilities" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique]
  }
}

Table "registration_retention_policies" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)
  "duration_days" int
  "is_legal_hold" boolean [not null, default: false]

  indexes {
    master_code [unique]
  }
}

Table "contact_share_consent_subject_types" {
  "id" int [pk, not null]
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique]
  }
}

Table "registration_forms" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "name" varchar(200) [not null]
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, event_id, id) [unique]
    (tenant_id, event_id, namespace, key) [unique, note: 'filter: is_deleted = false']
  }
}

Table "registration_form_versions" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "version" int [not null]
  "status_id" int [not null]
  "language_tag" varchar(35) [not null]
  "schema_hash" varchar(64) [note: 'lowercase SHA-256 of the complete canonical schema bundle']
  "data_schema_artifact" text
  "ui_schema_artifact" text
  "logic_schema_artifact" text
  "mapping_artifact" text
  "published_at" timestamptz
  "retired_at" timestamptz
  "source_template_form_id" uuid
  "source_template_version_id" uuid
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, event_id, registration_form_id, id) [unique]
    (tenant_id, event_id, registration_form_id, version) [unique, note: 'filter: is_deleted = false']
    (tenant_id, event_id, registration_form_id, status_id, language_tag)
  }

  Note: 'ck_registration_form_versions_schema_artifacts: drafts have no pinned artifacts/hash; published and retired versions require all four artifacts and hash'
}

Table "registration_form_sections" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "ordinal" int [not null]
  "title" varchar(200) [not null]
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, event_id, registration_form_id, registration_form_version_id, id) [unique]
    (tenant_id, event_id, registration_form_version_id, ordinal) [unique, note: 'filter: is_deleted = false']
  }
}

Table "registration_form_fields" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "registration_form_section_id" uuid [not null]
  "ordinal" int [not null]
  "namespace" varchar(100) [not null]
  "key" varchar(100) [not null]
  "label" varchar(500) [not null]
  "field_type_id" int [not null]
  "retention_policy_id" int [not null]
  "organizer_visibility_id" int [not null]
  "requires_explicit_consent" boolean [not null]
  "consent_purpose_code" character varying(100)
  "consent_text_version" character varying(100)
  "is_provider_transfer_allowed" boolean [not null]
  "is_exportable" boolean [not null]
  "export_purpose_code" character varying(100)
  "is_required" boolean [not null]
  "is_multi" boolean [not null]
  "min_length" int
  "max_length" int
  "regex_pattern" varchar(1000)
  "min_number" numeric
  "max_number" numeric
  "min_date_time" timestamptz
  "max_date_time" timestamptz
  "allowed_url_schemes" varchar(200)
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  Note: 'ck_registration_form_fields_consent_metadata: explicit-consent fields require nonblank purpose code and text version; other fields require both values null'

  indexes {
    (tenant_id, event_id, registration_form_id, registration_form_version_id, registration_form_section_id, id) [unique]
    (tenant_id, event_id, registration_form_version_id, registration_form_section_id, ordinal) [unique, note: 'filter: is_deleted = false']
    (tenant_id, event_id, registration_form_version_id, namespace, key) [unique, note: 'filter: is_deleted = false']
  }
}

Table "registration_form_field_options" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "registration_form_section_id" uuid [not null]
  "registration_form_field_id" uuid [not null]
  "ordinal" int [not null]
  "key" varchar(100) [not null]
  "label" varchar(500) [not null]
  "retired_at" timestamptz
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, event_id, registration_form_id, registration_form_version_id, registration_form_section_id, registration_form_field_id, id) [unique]
    (tenant_id, event_id, registration_form_version_id, registration_form_field_id, ordinal) [unique, note: 'filter: is_deleted = false']
    (tenant_id, event_id, registration_form_version_id, registration_form_field_id, key) [unique, note: 'filter: is_deleted = false']
  }
}

Table "registration_form_rules" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "ordinal" int [not null, note: 'CHECK ordinal > 0']
  "target_namespace" varchar(100) [not null]
  "target_key" varchar(100) [not null]
  "effect" int [not null, note: 'CHECK 1..4: show, hide, require, make optional']
  "condition" text [not null, note: 'closed typed nine-operator condition AST serialized by an EF converter']
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, event_id, registration_form_id, registration_form_version_id, id) [unique]
    (tenant_id, event_id, registration_form_version_id, ordinal) [unique, note: 'filter: is_deleted = false']
    (tenant_id, event_id, registration_form_version_id, target_namespace, target_key)
  }
}

Table "registration_participants" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "linked_user_id" uuid
  "participant_type_id" int [not null]
  "guardian_participant_id" uuid
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_participants_tenant_id_id']
    (tenant_id, registration_order_id, id) [unique, name: 'ak_registration_participants_tenant_id_registration_order_id_id']
    (tenant_id, registration_order_id) [name: 'ix_registration_participants_tenant_id_registration_order_id']
    (tenant_id, linked_user_id) [name: 'ix_registration_participants_tenant_id_linked_user_id']
    (tenant_id, guardian_participant_id) [name: 'ix_registration_participants_tenant_id_guardian_participant_id']
    (tenant_id, registration_order_id, guardian_participant_id) [name: 'ix_registration_participants_tenant_id_registration_order_id_guardian_participant_id']
  }

  Note: 'Tenant-scoped named or unnamed participant owned by one registration order; PII is split.'
}

Table "registration_participant_pii" {
  "registration_participant_id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "display_name" varchar(200)
  "email" varchar(320)
  "normalized_email" varchar(320)
  "phone" varchar(50)
  "retention_until" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, registration_participant_id) [unique, name: 'ak_registration_participant_pii_tenant_id_registration_participant_id']
    (tenant_id, normalized_email) [name: 'ix_registration_participant_pii_tenant_id_normalized_email']
  }

  Note: 'Removable one-to-one PII extension for a registration participant.'
}

Table "registration_ticket_assignments" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "registration_order_line_id" uuid [not null]
  "participant_id" uuid
  "ordinal" int [not null, note: 'positive; unique active slot within concrete order line']
  "assignment_status_id" int [not null]
  "assignment_deadline" timestamptz
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_ticket_assignments_tenant_id_id']
    (tenant_id, registration_order_id) [name: 'ix_registration_ticket_assignments_tenant_id_registration_order_id']
    (tenant_id, participant_id) [name: 'ix_registration_ticket_assignments_tenant_id_participant_id']
    (tenant_id, registration_order_id, participant_id) [name: 'ix_registration_ticket_assignments_tenant_id_registration_order_id_participant_id']
    (tenant_id, registration_order_line_id, ordinal) [unique, name: 'ix_registration_ticket_assignments_tenant_id_registration_order_line_id_ordinal', note: 'filter: is_deleted = false']
  }

  Note: 'Concrete ticket-unit slot. PostgreSQL trigger locks the order line and rejects active assignment count above its quantity.'
}


// ============================================================
// Registration Runtime Evidence and Provider Framework (Phase 8/9)
// ============================================================

Table "registration_provider_connections" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "name" varchar(120) [not null]
  "provider_kind_id" int [not null]
  "deployment_kind_id" int [not null]
  "api_token_secret_binding_id" uuid
  "webhook_secret_binding_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_provider_connections_tenant_id_id']
    deployment_kind_id [name: 'ix_registration_provider_connections_deployment_kind_id']
    provider_kind_id [name: 'ix_registration_provider_connections_provider_kind_id']
    (tenant_id, api_token_secret_binding_id) [name: 'ix_registration_provider_connections_tenant_id_api_token_secre']
    (tenant_id, webhook_secret_binding_id) [name: 'ix_registration_provider_connections_tenant_id_webhook_secret_']
    (tenant_id, name) [unique, name: 'ix_registration_provider_connections_tenant_id_name']
  }

  Note: 'Tenant-local provider connection. Secret FKs point to SecretBinding by (tenant_id scope_id, id); no provider credential plaintext is stored here.'
}

Table "registration_provider_approved_origins" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_provider_connection_id" uuid [not null]
  "origin" varchar(300) [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, registration_provider_connection_id, id) [unique, name: 'ak_registration_provider_approved_origins_tenant_id_registrati']
    (tenant_id, registration_provider_connection_id, origin) [unique, name: 'ix_registration_provider_approved_origins_tenant_id_registrati']
  }
}

Table "registration_provider_schema_revisions" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_provider_connection_id" uuid [not null]
  "schema_authority_id" int [not null]
  "revision_hash" varchar(44) [not null]
  "observed_at" timestamptz [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]

  indexes {
    schema_authority_id [name: 'ix_registration_provider_schema_revisions_schema_authority_id']
    (tenant_id, registration_provider_connection_id, revision_hash) [unique, name: 'ix_registration_provider_schema_revisions_tenant_id_registrati']
  }
}

Table "registration_provider_bindings" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_provider_connection_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "presentation_mode_id" int [not null]
  "collection_mode_id" int [not null]
  "completion_mode_id" int [not null]
  "trust_level_id" int [not null]
  "drift_class_id" int [not null]
  "state_id" int [not null]
  "published_mapping_revision_hash" varchar(44)
  "published_mapping_revision_hash_key" varchar(44) [not null, default: '']
  "published_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_provider_bindings_tenant_id_id']
    (tenant_id, id, published_mapping_revision_hash_key) [unique, name: 'ak_registration_provider_bindings_tenant_id_id_published_mappi']
    collection_mode_id [name: 'ix_registration_provider_bindings_collection_mode_id']
    completion_mode_id [name: 'ix_registration_provider_bindings_completion_mode_id']
    drift_class_id [name: 'ix_registration_provider_bindings_drift_class_id']
    presentation_mode_id [name: 'ix_registration_provider_bindings_presentation_mode_id']
    state_id [name: 'ix_registration_provider_bindings_state_id']
    (tenant_id, registration_form_id, registration_form_version_id) [name: 'ix_registration_provider_bindings_tenant_id_registration_form_']
    (tenant_id, registration_provider_connection_id, registration_form_version_id) [unique, name: 'ix_registration_provider_bindings_tenant_id_registration_provi']
    trust_level_id [name: 'ix_registration_provider_bindings_trust_level_id']
  }

  Note: 'Provider binding pins one provider connection to one form version. Check ck_registration_provider_bindings_publication: published state requires published_mapping_revision_hash and published_at; non-published rows do not.'
}

Table "registration_provider_subscription_states" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_provider_binding_id" uuid [not null]
  "provider_event_type" varchar(120) [not null]
  "watch_id" varchar(200) [not null]
  "watch_expires_at" timestamptz [not null]
  "response_checkpoint" varchar(1024)
  "last_notification_at" timestamptz
  "pending_notification_at" timestamptz
  "last_sweep_success_at" timestamptz
  "last_renewal_attempt_at" timestamptz
  "last_renewal_success_at" timestamptz
  "next_attempt_at" timestamptz
  "failure_category" varchar(80)
  "processing_generation" bigint [not null]
  "lease_token" uuid
  "lease_expires_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_provider_subscription_states_tenant_id_id']
    (pending_notification_at, next_sweep_attempt_at, lease_expires_at) [name: 'ix_registration_provider_subscription_states_sweep_poll']
    (watch_expires_at, lease_expires_at) [name: 'ix_registration_provider_subscription_states_renewal_poll']
    (tenant_id, registration_provider_binding_id, provider_event_type) [unique, name: 'ux_registration_provider_subscription_states_binding_event']
  }

  Note: 'Durable tenant-scoped provider watch state. Checks: processing_generation >= 0 and watch_expires_at > created_at. PendingNotificationAt is cleared by successful checkpoint settlement; NextRenewalAttemptAt gates renewal retry and NextSweepAttemptAt gates response-recovery retry independently. Workers claim with lease_token plus processing_generation fence.'
}

Table "registration_provider_capabilities" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_provider_binding_id" uuid [not null]
  "provider_code" varchar(100) [not null]
  "deployment_kind" varchar(100) [not null]
  "api_version" varchar(100) [not null]
  "adapter_policy_version" varchar(100) [not null]
  "conformance_evidence_revision" varchar(200) [not null]
  "capability_code" varchar(100) [not null]
  "is_deleted" boolean [not null, default: false]

  indexes {
    (registration_provider_binding_id, provider_code, deployment_kind, api_version, adapter_policy_version, conformance_evidence_revision, capability_code) [unique, name: 'ix_registration_provider_capabilities_registration_provider_bi']
    (tenant_id, registration_provider_binding_id) [name: 'ix_registration_provider_capabilities_tenant_id_registration_p']
  }

  Note: 'Exact provider capability tuple. Provider/deployment/API/adapter/evidence/capability columns are all part of the uniqueness contract.'
}

Table "registration_provider_field_mappings" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_provider_binding_id" uuid [not null]
  "platform_field_key" varchar(200) [not null]
  "provider_field_key" varchar(200) [not null]
  "is_required" boolean [not null]
  "is_deleted" boolean [not null, default: false]

  indexes {
    (tenant_id, registration_provider_binding_id, id) [unique, name: 'ak_registration_provider_field_mappings_tenant_id_registration']
    (registration_provider_binding_id, platform_field_key) [unique, name: 'ix_registration_provider_field_mappings_registration_provider_']
    (registration_provider_binding_id, provider_field_key) [unique, name: 'ix_registration_provider_field_mappings_registration_provider_1']
  }
}

Table "registration_provider_option_mappings" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_provider_binding_id" uuid [not null]
  "registration_provider_field_mapping_id" uuid [not null]
  "platform_option_key" varchar(200) [not null]
  "provider_option_key" varchar(200) [not null]
  "is_deleted" boolean [not null, default: false]

  indexes {
    (registration_provider_field_mapping_id, platform_option_key) [unique, name: 'ix_registration_provider_option_mappings_registration_provider']
    (tenant_id, registration_provider_binding_id, registration_provider_field_mapping_id) [name: 'ix_registration_provider_option_mappings_tenant_id_registratio']
  }
}

Table "registration_sensitive_answer_values" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "ciphertext" varchar(131072) [not null]
  "key_version" int [not null]
  "retention_until" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_sensitive_answer_values_tenant_id_id']
    (tenant_id, key_version) [name: 'ix_registration_sensitive_answer_values_tenant_id_key_version']
  }

  Note: 'Ciphertext-only sensitive answer payload. Check ck_registration_sensitive_answer_values_shape: key_version > 0 and ciphertext is nonblank.'
}

Table "registration_attempts" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "registration_workflow_id" uuid [not null]
  "registration_requirement_id" uuid [not null]
  "registration_channel_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "registration_provider_binding_id" uuid
  "provider_mapping_revision_hash" varchar(44)
  "capability_token_hash" varchar(44) [not null]
  "expires_at" timestamptz [not null]
  "consumed_at" timestamptz
  "submission_consumption_claim_id" uuid
  "superseded_at" timestamptz
  "superseded_by_registration_attempt_id" uuid
  "supersession_reason" varchar(500)
  "status_id" int [not null]
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "provider_mapping_revision_hash_key" varchar(44) [not null, note: 'computed: COALESCE(provider_mapping_revision_hash, empty string)']
  "registration_provider_binding_key" uuid [not null, note: 'computed: COALESCE(registration_provider_binding_id, zero uuid)']

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_attempts_tenant_id_id']
    (tenant_id, event_id, registration_order_id, registration_workflow_id, registration_requirement_id, registration_channel_id, registration_form_id, id) [unique, name: 'ak_registration_attempts_tenant_id_event_id_registration_order']
    (tenant_id, event_id, registration_order_id, registration_workflow_id, registration_requirement_id, registration_channel_id, registration_form_id, registration_form_version_id, id) [unique, name: 'ak_registration_attempts_tenant_id_event_id_registration_order1']
    status_id [name: 'ix_registration_attempts_status_id']
    (tenant_id, capability_token_hash) [unique, name: 'ix_registration_attempts_tenant_id_capability_token_hash']
    (tenant_id, event_id, registration_form_id, registration_form_version_id) [name: 'ix_registration_attempts_tenant_id_event_id_registration_form_']
    (tenant_id, event_id, registration_order_id, registration_workflow_id, registration_requirement_id, registration_channel_id, registration_form_id, superseded_by_registration_attempt_id) [name: 'ix_registration_attempts_tenant_id_event_id_registration_order']
    (tenant_id, event_id, registration_workflow_id, registration_order_id) [name: 'ix_registration_attempts_tenant_id_event_id_registration_workf']
    (tenant_id, event_id, registration_workflow_id, registration_requirement_id, registration_channel_id, registration_provider_binding_key) [name: 'ix_registration_attempts_tenant_id_event_id_registration_workf1']
    (tenant_id, registration_provider_binding_id, provider_mapping_revision_hash_key) [name: 'ix_registration_attempts_tenant_id_registration_provider_bindi']
    (tenant_id, status_id, expires_at) [name: 'ix_registration_attempts_tenant_id_status_id_expires_at']
  }

  Note: 'Capability-token attempt. Checks: provider binding and mapping revision must be both null or both present; provider key mirrors binding or zero uuid; expires_at > created_at; consumed and superseded statuses require their lifecycle columns. Provider FK pins to registration_provider_bindings(tenant_id,id,published_mapping_revision_hash_key).'
}

Table "registration_submissions" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "registration_workflow_id" uuid [not null]
  "registration_requirement_id" uuid [not null]
  "registration_channel_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "registration_attempt_id" uuid [not null]
  "attempt_status_at_receipt_id" int [not null]
  "business_deduplication_key" varchar(71) [not null]
  "received_evidence_hash" varchar(44) [not null]
  "http_idempotency_key_hash" varchar(44)
  "registration_provider_binding_id" uuid
  "provider_mapping_revision_hash" varchar(44)
  "provider_submission_id" varchar(200)
  "provider_response_revision" varchar(200)
  "provider_subject_id" varchar(200)
  "provider_correlation_id" varchar(200)
  "received_at" timestamptz [not null]
  "finalized_at" timestamptz
  "attempt_consumption_claim_id" uuid
  "is_finalizable" boolean [not null]
  "status_id" int [not null]
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, event_id, id) [unique, name: 'ak_registration_submissions_tenant_id_event_id_id']
    (tenant_id, event_id, registration_order_id, registration_workflow_id, registration_requirement_id, registration_form_id, registration_form_version_id, registration_attempt_id, id) [unique, name: 'ak_registration_submissions_tenant_id_event_id_registration_or']
    attempt_status_at_receipt_id [name: 'ix_registration_submissions_attempt_status_at_receipt_id']
    http_idempotency_key_hash [name: 'ix_registration_submissions_http_idempotency_key_hash']
    status_id [name: 'ix_registration_submissions_status_id']
    (tenant_id, event_id, registration_order_id, registration_workflow_id, registration_requirement_id, registration_channel_id, registration_form_id, registration_form_version_id, registration_attempt_id) [name: 'ix_registration_submissions_tenant_id_event_id_registration_or']
    (tenant_id, registration_attempt_id, received_at) [name: 'ix_registration_submissions_tenant_id_registration_attempt_id_']
    (tenant_id, registration_attempt_id, business_deduplication_key) [unique, name: 'ux_registration_submissions_native_identity', note: 'filter: provider_submission_id IS NULL']
    (tenant_id, registration_provider_binding_id, provider_submission_id, provider_response_revision) [unique, name: 'ux_registration_submissions_provider_identity', note: 'filter: provider_submission_id IS NOT NULL']
  }

  Note: 'Immutable native/provider submission evidence. Headless provider submissions may pin binding and mapping lineage before a provider response ID exists. Checks keep provider response ID and revision paired; finalization shape ties status to is_finalizable, attempt_consumption_claim_id, and finalized_at.'
}

Table "registration_submission_revisions" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_submission_id" uuid [not null]
  "revision_number" int [not null]
  "received_evidence_hash" varchar(44) [not null]
  "provider_revision_id" varchar(200)
  "received_at" timestamptz [not null]
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, event_id, registration_submission_id) [name: 'ix_registration_submission_revisions_tenant_id_event_id_regist']
    (tenant_id, registration_submission_id, revision_number) [unique, name: 'ux_registration_submission_revisions_submission_revision_number']
  }

  Note: 'Append-only submission revision evidence. Check: revision_number > 0.'
}

Table "registration_submission_issues" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_attempt_id" uuid [not null]
  "registration_submission_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "registration_form_field_id" uuid
  "code" varchar(100) [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, event_id, registration_submission_id) [name: 'ix_registration_submission_issues_tenant_id_event_id_registrat']
    (tenant_id, registration_submission_id) [name: 'ix_registration_submission_issues_tenant_id_registration_submi']
  }

  Note: 'Safe issue-code evidence only; no rejected raw answer value or free-form attendee content is persisted.'
}

Table "registration_answers" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "registration_attempt_id" uuid [not null]
  "registration_submission_id" uuid [not null]
  "registration_workflow_id" uuid [not null]
  "registration_requirement_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "registration_form_section_id" uuid [not null]
  "registration_form_field_id" uuid [not null]
  "field_type_id" int [not null]
  "requirement_subject_type_id" int [not null]
  "requirement_subject_id" uuid
  "requirement_subject_key" uuid [not null, note: 'computed: COALESCE(requirement_subject_id, zero uuid)']
  "answer_subject_type_id" int [not null]
  "order_subject_id" uuid
  "purchaser_subject_id" uuid
  "participant_subject_id" uuid
  "ticket_assignment_subject_id" uuid
  "ticket_assignment_order_line_id" uuid
  "session_selection_subject_id" uuid
  "effective_subject_identity" uuid [not null, note: 'computed: first non-null concrete subject id']
  "ordinal" int [not null]
  "text_value" varchar(10000)
  "integer_value" bigint
  "decimal_value" decimal(19,4)
  "boolean_value" boolean
  "date_value" date
  "time_value" time
  "instant_value" timestamptz
  "selected_option_id" uuid
  "sensitive_answer_value_id" uuid
  "retention_until" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    answer_subject_type_id [name: 'ix_registration_answers_answer_subject_type_id']
    (tenant_id, event_id, registration_form_id, registration_form_version_id, registration_form_section_id, registration_form_field_id, field_type_id) [name: 'ix_registration_answers_tenant_id_event_id_registration_form_i']
    (tenant_id, event_id, registration_form_id, registration_form_version_id, registration_form_section_id, registration_form_field_id, selected_option_id) [name: 'ix_registration_answers_tenant_id_event_id_registration_form_i1']
    (tenant_id, event_id, registration_order_id, registration_workflow_id, registration_requirement_id, registration_form_id, registration_form_version_id, registration_attempt_id, registration_submission_id) [name: 'ix_registration_answers_tenant_id_event_id_registration_order_']
    (tenant_id, event_id, registration_workflow_id, registration_requirement_id, requirement_subject_type_id, requirement_subject_key) [name: 'ix_registration_answers_tenant_id_event_id_registration_workfl']
    (tenant_id, registration_order_id) [name: 'ix_registration_answers_tenant_id_registration_order_id']
    (tenant_id, registration_order_id, participant_subject_id) [name: 'ix_registration_answers_tenant_id_registration_order_id_partic']
    (tenant_id, registration_order_id, ticket_assignment_order_line_id, requirement_subject_id) [name: 'ix_registration_answers_tenant_id_registration_order_id_ticket']
    (tenant_id, registration_order_id, ticket_assignment_subject_id, ticket_assignment_order_line_id) [name: 'ix_registration_answers_tenant_id_registration_order_id_ticket1']
    (tenant_id, sensitive_answer_value_id) [unique, name: 'ix_registration_answers_tenant_id_sensitive_answer_value_id']
    (tenant_id, registration_submission_id, registration_form_field_id, answer_subject_type_id, effective_subject_identity, ordinal) [unique, name: 'ux_registration_answers_durable_identity']
  }

  Note: 'Atomic typed answer. Checks: exactly one value column; value shape must match registration field type; exactly one subject identity; ordinal > 0.'
}

Table "registration_consent_records" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "registration_attempt_id" uuid [not null]
  "registration_submission_id" uuid [not null]
  "registration_workflow_id" uuid [not null]
  "registration_requirement_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "registration_form_version" int [not null]
  "registration_form_section_id" uuid [not null]
  "registration_form_field_id" uuid [not null]
  "field_type_id" int [not null]
  "requirement_subject_type_id" int [not null]
  "requirement_subject_id" uuid
  "requirement_subject_key" uuid [not null, note: 'computed: COALESCE(requirement_subject_id, zero uuid)']
  "purpose_code" varchar(100) [not null]
  "consent_text_snapshot" varchar(4000) [not null]
  "consent_text_version" varchar(100) [not null]
  "language_tag" varchar(35) [not null]
  "answer_subject_type_id" int [not null]
  "order_subject_id" uuid
  "purchaser_subject_id" uuid
  "participant_subject_id" uuid
  "ticket_assignment_subject_id" uuid
  "ticket_assignment_order_line_id" uuid
  "session_selection_subject_id" uuid
  "effective_subject_identity" uuid [not null, note: 'computed: first non-null concrete subject id']
  "granted_at" timestamptz [not null]
  "withdrawn_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    answer_subject_type_id [name: 'ix_registration_consent_records_answer_subject_type_id']
    (tenant_id, answer_subject_type_id, effective_subject_identity, withdrawn_at) [name: 'ix_registration_consent_records_subject']
    (tenant_id, event_id, registration_workflow_id, registration_requirement_id, requirement_subject_type_id, requirement_subject_key) [name: 'ix_registration_consent_records_tenant_id_event_id_registratio']
    (tenant_id, event_id, registration_form_id, registration_form_version_id, registration_form_section_id, registration_form_field_id, field_type_id) [name: 'ix_registration_consent_records_tenant_id_event_id_registratio1']
    (tenant_id, event_id, registration_order_id, registration_workflow_id, registration_requirement_id, registration_form_id, registration_form_version_id, registration_attempt_id, registration_submission_id) [name: 'ix_registration_consent_records_tenant_id_event_id_registratio2']
    (tenant_id, registration_order_id, participant_subject_id) [name: 'ix_registration_consent_records_tenant_id_registration_order_i']
    (tenant_id, registration_order_id, ticket_assignment_order_line_id, requirement_subject_id) [name: 'ix_registration_consent_records_tenant_id_registration_order_i1']
    (tenant_id, registration_order_id, ticket_assignment_subject_id, ticket_assignment_order_line_id) [name: 'ix_registration_consent_records_tenant_id_registration_order_i2']
    (tenant_id, registration_submission_id, registration_form_field_id, answer_subject_type_id, effective_subject_identity) [unique, name: 'ux_registration_consent_records_evidence']
  }

  Note: 'Immutable consent evidence snapshot. Check ck_registration_consent_records_subject_shape mirrors answer subject identity rules.'
}

Table "registration_requirement_fulfillments" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "registration_workflow_id" uuid [not null]
  "registration_requirement_id" uuid [not null]
  "subject_type_id" int [not null]
  "subject_id" uuid [not null]
  "source_registration_submission_id" uuid
  "is_skipped" boolean [not null]
  "recorded_at" timestamptz [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_requirement_fulfillments_tenant_id_id']
    subject_type_id [name: 'ix_registration_requirement_fulfillments_subject_type_id']
    (tenant_id, event_id, registration_order_id) [name: 'ix_registration_requirement_fulfillments_tenant_id_event_id_re']
    (tenant_id, event_id, registration_workflow_id, registration_requirement_id) [name: 'ix_registration_requirement_fulfillments_tenant_id_event_id_re1']
    (tenant_id, event_id, source_registration_submission_id) [name: 'ix_registration_requirement_fulfillments_tenant_id_event_id_so']
    (tenant_id, registration_order_id, registration_requirement_id, subject_type_id, subject_id, is_skipped) [unique, name: 'ux_registration_requirement_fulfillments_identity']
  }

  Note: 'Durable requirement outcome. Check ck_registration_requirement_fulfillments_outcome: skipped rows have no source submission; fulfilled rows require one.'
}

Table "registration_finalization_effects" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "status" int [not null, note: 'OutboxMessageStatus: Pending(1), Processing(2), Completed(3), Failed(4)']
  "attempt_count" int [not null]
  "processing_fence" bigint [not null]
  "processing_lease_owner" varchar(200)
  "processing_lease_token" uuid
  "processing_lease_expires_at" timestamptz
  "next_attempt_at" timestamptz
  "completed_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_finalization_effects_tenant_id_id']
    (tenant_id, event_id, registration_order_id) [name: 'ix_registration_finalization_effects_tenant_id_event_id_regist']
    (status, next_attempt_at, created_at) [name: 'ix_registration_finalization_effects_worker_poll']
    (tenant_id, registration_order_id) [unique, name: 'ux_registration_finalization_effects_order']
  }

  Note: 'Fenced finalization effect/outbox row. Checks: attempt_count and processing_fence nonnegative; state check requires lease columns only while Processing and completed_at only while Completed.'
}

Table "registration_provider_submission_write_effects" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "registration_attempt_id" uuid [not null]
  "registration_submission_id" uuid [not null]
  "registration_provider_binding_id" uuid [not null]
  "status" int [not null, note: 'OutboxMessageStatus: Pending(1), Processing(2), Completed(3), Failed(4), DeadLettered(5)']
  "attempt_count" int [not null]
  "processing_fence" bigint [not null]
  "processing_lease_owner" varchar(200)
  "processing_lease_token" uuid
  "processing_lease_expires_at" timestamptz
  "next_attempt_at" timestamptz
  "completed_at" timestamptz
  "dead_lettered_at" timestamptz
  "parked_at" timestamptz
  "failure_code" varchar(120)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_provider_submission_write_effects_tenant_id_id']
    (tenant_id, event_id, registration_order_id) [name: 'ix_registration_provider_submission_write_effects_tenant_order']
    (status, next_attempt_at, created_at) [name: 'ix_registration_provider_submission_write_effects_worker_poll']
    (tenant_id, registration_submission_id) [unique, name: 'ux_registration_provider_submission_write_effects_submission']
  }

  Note: 'Identifiers-only fenced post-commit provider write intent. Canonical answers are rebuilt after claim. Retryable pre-handoff failures back off; permanent failures dead-letter; ambiguous post-handoff failures park without automatic retry.'
}

Table "registration_answer_files" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "registration_submission_id" uuid [not null]
  "registration_form_id" uuid [not null]
  "registration_form_version_id" uuid [not null]
  "registration_form_section_id" uuid [not null]
  "registration_form_field_id" uuid [not null]
  "field_type_id" int [not null]
  "storage_object_id" uuid [not null]
  "safe_display_name" varchar(500) [not null]
  "content_type" varchar(255) [not null]
  "extension" varchar(50) [not null]
  "sha256checksum" varchar(64)
  "size" bigint [not null]
  "quarantine_state" varchar(20) [not null]
  "scan_status" varchar(20) [not null]
  "quarantined_at" timestamptz [not null]
  "released_at" timestamptz
  "released_by" uuid
  "concurrency_stamp" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_answer_files_tenant_id_id']
    (tenant_id, event_id, registration_form_id, registration_form_version_id, registration_form_section_id, registration_form_field_id, field_type_id) [name: 'ix_registration_answer_files_tenant_id_event_id_registration_f']
    (tenant_id, event_id, registration_submission_id) [name: 'ix_registration_answer_files_tenant_id_event_id_registration_s']
    (tenant_id, registration_submission_id, registration_form_field_id, storage_object_id) [unique, name: 'ix_registration_answer_files_tenant_id_registration_submission']
    (tenant_id, storage_object_id) [unique, name: 'ix_registration_answer_files_tenant_id_storage_object_id']
    (tenant_id, storage_object_id, quarantine_state) [name: 'ix_registration_answer_files_tenant_id_storage_object_id_quara']
  }

  Note: 'Quarantined file-answer metadata. Checks: field_type_id = File(18), size >= 0, quarantine_state in quarantined/released, scan_status = not_scanned, release columns required only for released rows.'
}

Table "registration_answer_file_releases" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "registration_answer_file_id" uuid [not null]
  "released_by" uuid [not null]
  "released_at" timestamptz [not null]
  "reason" varchar(500) [not null]
  "previous_quarantine_state" varchar(20) [not null]
  "new_quarantine_state" varchar(20) [not null]

  indexes {
    (tenant_id, registration_answer_file_id) [unique, name: 'ix_registration_answer_file_releases_tenant_id_registration_an']
  }

  Note: 'One manual release audit for a quarantined registration file. Check: previous_quarantine_state = quarantined and new_quarantine_state = released.'
}

Table "registration_order_pii" {
  "registration_order_id" uuid [pk, not null, note: 'shared PK with registration_orders']
  "tenant_id" uuid [not null]
  "contact_name" varchar(200)
  "email" varchar(320)
  "normalized_email" varchar(320)
  "phone" varchar(50)
  "organization_name" varchar(200)
  "is_email_verified" boolean [not null, default: false]
  "retention_until" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, registration_order_id) [unique, name: 'ak_registration_order_pii_tenant_id_registration_order_id']
    (tenant_id, normalized_email) [name: 'ix_registration_order_pii_tenant_id_normalized_email']
  }

  Note: 'Optional buyer contact PII split from durable registration-order workflow and accounting state.'
}

Table "registration_order_platform_contributions" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "registration_order_id" uuid [not null]
  "tenant_id" uuid [not null]
  "platform_contribution_setting_id_snapshot" uuid [not null]
  "platform_contribution_setting_version_snapshot" int [not null]
  "contribution_basis_points_snapshot" int [not null]
  "amount_minor" bigint [not null]
  "currency_code" varchar(3) [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_registration_order_platform_contributions_tenant_id_id']
    (tenant_id, registration_order_id) [unique, name: 'ix_registration_order_platform_contributions_tenant_id_registr']
  }

  Note: 'Optional instance-directed contribution snapshot kept separate from organizer-directed order totals.'
}

Table "platform_fee_policies" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "version_number" int [not null]
  "is_active" boolean [not null]
  "is_enabled" boolean [not null]
  "fee_basis_points" int [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    is_active [unique, name: 'ix_platform_fee_policies_is_active', note: 'filter: is_active = true']
    version_number [unique, name: 'ix_platform_fee_policies_version_number']
  }

  Note: 'Versioned instance platform fee policy. At most one active version.'
}

Table "platform_fee_fixed_charges" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "currency_code" varchar(3) [not null]
  "amount_minor" bigint [not null]
  "platform_fee_policy_id" uuid

  indexes {
    (platform_fee_policy_id, currency_code) [unique, name: 'ix_platform_fee_fixed_charges_platform_fee_policy_id_currency_']
  }

  Note: 'Per-currency fixed minor-unit component of a platform fee policy.'
}

Table "platform_contribution_settings" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "version_number" int [not null]
  "is_active" boolean [not null]
  "is_enabled" boolean [not null]
  "heading" varchar(200) [not null]
  "body" varchar(2000) [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    is_active [unique, name: 'ix_platform_contribution_settings_is_active', note: 'filter: is_active = true']
    version_number [unique, name: 'ix_platform_contribution_settings_version_number']
  }

  Note: 'Versioned instance contribution prompt and option set. At most one active version.'
}

Table "platform_contribution_options" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "contribution_basis_points" int [not null]
  "sort_order" int [not null]
  "is_default" boolean [not null]
  "platform_contribution_setting_id" uuid

  indexes {
    (platform_contribution_setting_id, contribution_basis_points) [unique, name: 'ix_platform_contribution_options_platform_contribution_setting']
    (platform_contribution_setting_id, sort_order) [unique, name: 'ix_platform_contribution_options_platform_contribution_setting1']
  }

  Note: 'Ordered basis-point contribution option belonging to a versioned contribution setting.'
}

// ============================================================
// Payment Policies & Provider Connections
// ============================================================

Table "paid_event_policy_versions" {
  "id" uuid [pk, not null]
  "tenant_id" uuid
  "policy_scope_key" varchar(48) [not null]
  "version_number" int [not null]
  "is_active" boolean [not null]
  "active_uniqueness_slot" int [not null]
  "is_payments_enabled" boolean [not null]
  "requires_local_verification" boolean [not null]
  "default_currency_code" varchar(3)
  "requires_first_paid_event_review" boolean [not null]
  "far_future_review_threshold_days" int
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (policy_scope_key, id) [unique, name: 'ak_paid_event_policy_versions_policy_scope_key_id']
    (policy_scope_key, active_uniqueness_slot) [unique, name: 'ix_paid_event_policy_versions_policy_scope_key_active_uniquene']
    (policy_scope_key, version_number) [unique, name: 'ix_paid_event_policy_versions_policy_scope_key_version_number']
    tenant_id [name: 'ix_paid_event_policy_versions_tenant_id']
  }

  Note: 'Versioned paid-event policy. The instance scope is the ceiling and a tenant scope can only narrow it. Domain transitions and the unfiltered unique slot index enforce one active version per scope; the generated composite alternate key is (policy_scope_key, id).'
}

Table "paid_event_policy_allowed_organizer_kinds" {
  "policy_scope_key" varchar(48) [not null]
  "paid_event_policy_version_id" uuid [not null]
  "ordinal" int [not null]
  "tenant_id" uuid
  "actor_type_id" int [not null]

  indexes {
    (policy_scope_key, paid_event_policy_version_id, actor_type_id) [unique, name: 'ix_paid_event_policy_allowed_organizer_kinds_policy_scope_key_']
  }

  Note: 'Allowed organizer kinds for a paid-event policy version.'
}

Table "paid_event_policy_allowed_currencies" {
  "policy_scope_key" varchar(48) [not null]
  "paid_event_policy_version_id" uuid [not null]
  "ordinal" int [not null]
  "tenant_id" uuid
  "currency_code" varchar(3) [not null]

  indexes {
    (policy_scope_key, paid_event_policy_version_id, currency_code) [unique, name: 'ix_paid_event_policy_allowed_currencies_policy_scope_key_paid_']
  }

  Note: 'Allowed currencies for a paid-event policy version.'
}

Table "paid_event_policy_refund_protections" {
  "policy_scope_key" varchar(48) [not null]
  "paid_event_policy_version_id" uuid [not null]
  "ordinal" int [not null]
  "tenant_id" uuid
  "refund_protection_id" int [not null]

  indexes {
    (policy_scope_key, paid_event_policy_version_id, refund_protection_id) [unique, name: 'ix_paid_event_policy_refund_protections_policy_scope_key_paid_']
  }

  Note: 'Allowed refund protections for a paid-event policy version.'
}

Table "paid_event_policy_currency_risk_limits" {
  "policy_scope_key" varchar(48) [not null]
  "paid_event_policy_version_id" uuid [not null]
  "ordinal" int [not null]
  "tenant_id" uuid
  "currency_code" varchar(3) [not null]
  "per_event_sales_ceiling_minor" bigint
  "rolling_organizer_sales_ceiling_minor" bigint
  "high_value_review_threshold_minor" bigint

  indexes {
    (policy_scope_key, paid_event_policy_version_id, currency_code) [unique, name: 'ix_paid_event_policy_currency_risk_limits_policy_scope_key_pai']
  }

  Note: 'Per-currency risk ceilings for a paid-event policy version.'
}

Table "organizer_payment_provider_connections" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "organizer_actor_id" uuid [not null]
  "provider_code" varchar(40) [not null]
  "connect_platform_id" varchar(120) [not null]
  "external_account_id" varchar(200) [not null]
  "active_scope_key" varchar(232) [not null]
  "active_uniqueness_slot" varchar(48) [not null]
  "status_id" int [not null]
  "merchant_country_code" varchar(2)
  "charge_capability_state_id" int [not null]
  "requirements_state_id" int [not null]
  "last_readiness_observed_at" timestamptz
  "last_readiness_evidence_revision" varchar(120)
  "replaces_connection_id" uuid
  "replaced_by_connection_id" uuid
  "replaced_at" timestamptz
  "disabled_at" timestamptz
  "disabled_reason_code" varchar(80)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_organizer_payment_provider_connections_tenant_id_id']
    (active_scope_key, active_uniqueness_slot) [unique, name: 'ix_organizer_payment_provider_connections_active_scope_key_act']
    organizer_actor_id [name: 'ix_organizer_payment_provider_connections_organizer_actor_id']
    (provider_code, connect_platform_id, external_account_id) [unique, name: 'ix_organizer_payment_provider_connections_provider_code_connec']
    (tenant_id, organizer_actor_id, provider_code, connect_platform_id, status_id) [name: 'ix_organizer_payment_provider_connections_tenant_id_organizer_']
    (tenant_id, replaced_by_connection_id) [name: 'ix_organizer_payment_provider_connections_tenant_id_replaced_b']
    (tenant_id, replaces_connection_id) [name: 'ix_organizer_payment_provider_connections_tenant_id_replaces_c']
  }

  Note: 'Organizer payment provider connection scoped to one tenant, organizer actor, provider, and platform. Checks enforce status/charge-capability/requirements ranges; active uniqueness uses the portable slot column.'
}

Table "organizer_payment_provider_account_operations" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "organizer_actor_id" uuid [not null]
  "provider_code" varchar(40) [not null]
  "connect_platform_id" varchar(120) [not null]
  "provider_idempotency_key" varchar(80) [not null]
  "status_id" int [not null]
  "active_scope_key" varchar(232) [not null]
  "active_uniqueness_slot" varchar(80) [not null]
  "external_account_id" varchar(200)
  "connection_id" uuid
  "failure_code" varchar(120)
  "provider_request_id" varchar(120)
  "resolution_reason" varchar(160)
  "requested_at" timestamptz [not null]
  "manual_reconciliation_required_at" timestamptz
  "bound_at" timestamptz
  "no_provider_account_confirmed_at" timestamptz
  "provider_rejected_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_organizer_payment_provider_account_operations_tenant_id_id']
    (active_scope_key, active_uniqueness_slot) [unique, name: 'ix_organizer_payment_provider_account_operations_active_scope_']
    organizer_actor_id [name: 'ix_organizer_payment_provider_account_operations_organizer_act']
    provider_idempotency_key [unique, name: 'ix_organizer_payment_provider_account_operations_provider_idem']
    (tenant_id, connection_id) [name: 'ix_organizer_payment_provider_account_operations_tenant_id_con']
    (tenant_id, organizer_actor_id, provider_code, connect_platform_id, status_id) [name: 'ix_organizer_payment_provider_account_operations_tenant_id_org']
  }

  Note: 'Durable organizer payment account-create operation fence. Checks enforce status_id BETWEEN 1 AND 5; provider idempotency is globally unique, and active scope-slot uniqueness permits one unresolved operation per tenant/organizer/provider/platform scope.'
}

Table "organizer_payment_provider_connection_supported_currencies" {
  "tenant_id" uuid [not null]
  "organizer_payment_provider_connection_id" uuid [not null]
  "ordinal" int [not null]
  "currency_code" varchar(3) [not null]

  indexes {
    (tenant_id, organizer_payment_provider_connection_id, currency_code) [unique, name: 'ix_organizer_payment_provider_connection_supported_currencies_']
  }

  Note: 'Supported currencies for an organizer payment provider connection.'
}

Table "incoming_webhook_messages" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "webhook_consumer_provider_binding_id" uuid
  "provider" varchar(100) [not null]
  "provider_message_id" varchar(256) [not null]
  "idempotency_key" varchar(256)
  "event_type" varchar(200)
  "headers_json" jsonb
  "payload_bytes" bytea
  "payload_hash" varchar(71) [not null]
  "payload_byte_length" bigint [not null]
  "payload_provenance_id" int [not null]
  "content_type" varchar(200) [not null]
  "content_encoding" varchar(50) [not null]
  "payload_retention_until" timestamptz [not null]
  "payload_cleared_at" timestamptz
  "retention_policy_version" varchar(200) [not null]
  "processing_attempt_retention_until" timestamptz [not null]
  "dead_letter_evidence_retention_until" timestamptz [not null]
  "replay_window_until" timestamptz [not null]
  "operational_log_retention_until" timestamptz [not null]
  "status_id" int [not null]
  "processing_generation" int [not null]
  "processing_fence" bigint [not null]
  "attempt_count" int [not null]
  "processing_lease_owner" varchar(200)
  "processing_lease_token" uuid
  "processing_lease_expires_at" timestamptz
  "processing_started_at" timestamptz
  "next_attempt_at" timestamptz
  "received_at" timestamptz [not null]
  "verified_at" timestamptz [not null]
  "processed_at" timestamptz
  "ignored_at" timestamptz
  "rejected_at" timestamptz
  "dead_lettered_at" timestamptz
  "payload_conflict_at" timestamptz
  "failure_category" varchar(100)
  "safe_detail" varchar(1024)
  "settled_by_effect_receipt_id" uuid
  "settled_effect_kind" varchar(200)
  "settlement_source_id" int
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_incoming_webhook_messages_tenant_id']
    (tenant_id, provider, provider_message_id) [unique, name: 'ux_incoming_webhook_messages_tenant_provider_message']
    (tenant_id, provider, idempotency_key) [unique, name: 'ix_incoming_webhook_messages_tenant_provider_idempotency', note: 'Filtered to non-null idempotency keys']
    (tenant_id, status_id, next_attempt_at, processing_lease_expires_at) [name: 'ix_incoming_webhook_messages_claim_due']
    (tenant_id, status_id, received_at) [name: 'ix_incoming_webhook_messages_tenant_status_received']
    (tenant_id, webhook_consumer_provider_binding_id, received_at) [name: 'ix_incoming_webhook_messages_tenant_binding_received']
    (tenant_id, payload_retention_until, replay_window_until) [name: 'ix_incoming_webhook_messages_tenant_payload_retention']
    (tenant_id, processing_attempt_retention_until) [name: 'ix_incoming_webhook_messages_tenant_attempt_retention']
    (tenant_id, dead_letter_evidence_retention_until) [name: 'ix_incoming_webhook_messages_tenant_dead_letter_retention']
  }

  Note: 'Exact bytes are verified before capture. Stripe payment callbacks retain only a normalized identifiers envelope while payload_hash remains the exact signed-body SHA-256. Provider event and event-type/object identities are unique at database authority.'
}
Table "paid_order_acceptance_snapshots" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "event_id" uuid [not null]
  "composition_revision" varchar(80) [not null]
  "disclosure_revision" varchar(80) [not null]
  "acceptance_template_identifier" varchar(80) [not null]
  "acceptance_template_text" varchar(2000) [not null]
  "organizer_actor_id" uuid [not null]
  "organizer_payment_provider_connection_id" uuid [not null]
  "connect_platform_id" varchar(120) [not null]
  "external_account_id" varchar(200) [not null]
  "merchant_country_code" varchar(2) [not null]
  "merchant_disclosure_text" varchar(2000) [not null]
  "tenant_directory_operator_document_id" uuid [not null]
  "tenant_directory_operator_revision_id" uuid [not null]
  "tenant_directory_operator_public_name" varchar(200) [not null]
  "tenant_directory_operator_legal_name" varchar(300) [not null]
  "tenant_directory_operator_kind_code" varchar(80) [not null]
  "tenant_directory_operator_country_code" varchar(2) [not null]
  "tenant_directory_operator_registration_identifier" varchar(120)
  "tenant_directory_operator_public_contact_email" varchar(320) [not null]
  "tenant_directory_operator_legal_notice_url" varchar(500) [not null]
  "tenant_directory_operator_terms_url" varchar(500) [not null]
  "tenant_directory_operator_privacy_url" varchar(500) [not null]
  "operator_id" uuid [not null]
  "operator_display_name" varchar(200) [not null]
  "operator_legal_name" varchar(300) [not null]
  "operator_kind_code" varchar(80) [not null]
  "operator_registration_identifier" varchar(120)
  "is_official_instance" boolean [not null]
  "official_origin" varchar(500) [not null]
  "operator_region_code" varchar(8) [not null]
  "operator_website_url" varchar(500) [not null]
  "operator_legal_notice_url" varchar(500) [not null]
  "operator_terms_url" varchar(500) [not null]
  "operator_privacy_url" varchar(500) [not null]
  "complaint_contact" varchar(320) [not null]
  "complaint_owner" varchar(200) [not null]
  "refund_owner" varchar(200) [not null]
  "dispute_owner" varchar(200) [not null]
  "reconciliation_owner" varchar(200) [not null]
  "activation_status" varchar(32) [not null]
  "delivery_starts_at_utc" timestamptz [not null]
  "delivery_ends_at_utc" timestamptz [not null]
  "event_time_zone_id" varchar(100) [not null]
  "currency_code" varchar(3) [not null]
  "organizer_amount_minor" bigint [not null]
  "platform_fee_minor" bigint [not null]
  "platform_contribution_minor" bigint [not null]
  "total_minor" bigint [not null]
  "instance_policy_version_id" uuid [not null]
  "tenant_policy_version_id" uuid
  "refund_policy_version" int [not null]
  "refund_policy_text" varchar(2000) [not null]
  "refund_policy_language_tag" varchar(35) [not null]
  "support_contact" varchar(320) [not null]
  "provider_code" varchar(40) [not null]
  "provider_profile_code" varchar(40) [not null]
  "charge_type" varchar(40) [not null]
  "statement_descriptor" varchar(22) [not null]
  "provider_environment" varchar(16) [not null]
  "provider_credential_owner" varchar(80) [not null]
  "accepted_at" timestamptz [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_paid_order_acceptance_snapshots_tenant_id_id']
    (tenant_id, registration_order_id, disclosure_revision) [unique, name: 'ix_paid_order_acceptance_snapshots_tenant_order_disclosure']
    (tenant_id, event_id, accepted_at) [name: 'ix_paid_order_acceptance_snapshots_tenant_event_accepted']
  }

  Note: 'Immutable exact buyer-accepted template, organizer merchant, tenant directory document/revision, instance operator, delivery, line/money, refund, complaint, provider, and statement facts. No historical payment attempt is backfilled.'
}

Table "paid_order_acceptance_lines" {
  "tenant_id" uuid [not null]
  "paid_order_acceptance_snapshot_id" uuid [not null]
  "ordinal" int [not null]
  "order_line_id" uuid [not null]
  "name" varchar(300) [not null]
  "quantity" int [not null]
  "unit_amount_minor" bigint [not null]
  "discount_amount_minor" bigint [not null]
  "line_total_minor" bigint [not null]

  indexes {
    (tenant_id, paid_order_acceptance_snapshot_id, ordinal) [pk]
    (tenant_id, paid_order_acceptance_snapshot_id, order_line_id) [unique, name: 'ix_paid_order_acceptance_lines_tenant_acceptance_order_line']
  }

  Note: 'Normalized immutable order-line facts accepted by the buyer; amounts satisfy quantity and discount shape constraints.'
}

Table "payment_attempts" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "paid_order_acceptance_snapshot_id" uuid
  "recipient_tenant_id" uuid [not null]
  "recipient_organizer_actor_id" uuid [not null]
  "recipient_connection_id" uuid [not null]
  "recipient_provider_code" varchar(40) [not null]
  "recipient_connect_platform_id" varchar(120) [not null]
  "recipient_external_account_id" varchar(200) [not null]
  "recipient_merchant_country_code" varchar(2) [not null]
  "recipient_currency_code" varchar(3) [not null]
  "recipient_profile_code" varchar(40) [not null]
  "recipient_instance_policy_version_id" uuid [not null]
  "recipient_tenant_policy_version_id" uuid
  "recipient_snapshotted_at" timestamptz [not null]
  "provider_code" varchar(40) [not null]
  "profile_code" varchar(40) [not null]
  "provider_api_revision" varchar(80) [not null]
  "composition_revision" varchar(80) [not null]
  "currency_code" varchar(3) [not null]
  "organizer_amount_minor" bigint [not null]
  "platform_fee_minor" bigint [not null]
  "platform_contribution_minor" bigint [not null]
  "total_minor" bigint [not null]
  "provider_idempotency_key" varchar(160) [not null]
  "active_scope_key" varchar(170) [not null]
  "active_uniqueness_slot" varchar(80) [not null]
  "provider_checkout_session_id" varchar(200)
  "provider_payment_id" varchar(200)
  "payment_attempt_status_id" int [not null]
  "authoritative_status_floor_id" int [not null]
  "expires_at" timestamptz
  "last_status_observed_at" timestamptz [not null]
  "last_provider_request_id" varchar(120)
  "dispatch_pending_at" timestamptz
  "requires_action_at" timestamptz
  "processing_at" timestamptz
  "succeeded_at" timestamptz
  "failed_at" timestamptz
  "cancelled_at" timestamptz
  "unknown_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_payment_attempts_tenant_id_id']
    (active_scope_key, active_uniqueness_slot) [unique, name: 'ix_payment_attempts_active_scope_key_active_uniqueness_slot']
    provider_idempotency_key [unique, name: 'ix_payment_attempts_provider_idempotency_key']
    (tenant_id, registration_order_id, payment_attempt_status_id) [name: 'ix_payment_attempts_tenant_id_registration_order_id_payment_attempt_status_id']
    (tenant_id, paid_order_acceptance_snapshot_id) [name: 'ix_payment_attempts_tenant_acceptance']
  }

  Note: 'Durable registration checkout attempt. Checks enforce status/floor ranges, nonnegative money composition, and portable one-active-attempt scope-slot uniqueness; recipient and amount facts are snapshotted locally before provider I/O.'
}

Table "checkout_dispatch_effects" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "payment_attempt_id" uuid [not null]
  "status" int [not null]
  "attempt_count" int [not null]
  "processing_fence" bigint [not null]
  "processing_lease_owner" varchar(200)
  "processing_lease_token" uuid
  "processing_lease_expires_at" timestamptz
  "next_attempt_at" timestamptz
  "completed_at" timestamptz
  "parked_at" timestamptz
  "unknown_at" timestamptz
  "last_failure_code" varchar(120)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_checkout_dispatch_effects_tenant_id_id']
    (tenant_id, payment_attempt_id) [unique, name: 'ix_checkout_dispatch_effects_tenant_id_payment_attempt_id']
    (status, next_attempt_at, created_at) [name: 'ix_checkout_dispatch_effects_worker_poll']
  }

  Note: 'Identifiers-only post-commit checkout dispatch effect. Checks enforce nonnegative attempt/fence counters and lease-token state shape for Pending, Failed, Processing, Completed, DeadLettered, and Unknown parked worker outcomes.'
}

Table "payment_reconciliation_effects" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "payment_attempt_id" uuid [not null]
  "source_incoming_webhook_message_id" uuid
  "checkout_dispatch_effect_id" uuid
  "checkout_dispatch_unknown_at" timestamptz
  "checkout_dispatch_processing_fence" bigint
  "checkout_dispatch_attempt_count" int
  "status" int [not null]
  "attempt_count" int [not null]
  "processing_fence" bigint [not null]
  "processing_lease_owner" varchar(200)
  "processing_lease_token" uuid
  "processing_lease_expires_at" timestamptz
  "next_attempt_at" timestamptz
  "completed_at" timestamptz
  "parked_at" timestamptz
  "unknown_at" timestamptz
  "last_failure_code" varchar(120)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_payment_reconciliation_effects_tenant_id_id']
    (tenant_id, payment_attempt_id) [unique, name: 'ix_payment_reconciliation_effects_tenant_id_payment_attempt_id']
    (tenant_id, source_incoming_webhook_message_id) [name: 'ix_payment_reconciliation_effects_tenant_id_source_incoming_webhook_message_id']
    (tenant_id, checkout_dispatch_effect_id) [name: 'ix_payment_reconciliation_effects_tenant_id_checkout_dispatch_effect_id']
    (status, next_attempt_at, created_at, id) [name: 'ix_payment_reconciliation_effects_worker_poll']
  }

  Note: 'Identifiers-only durable due work for authoritative connected-account Checkout and PaymentIntent retrieval. Lease token and monotonic fence protect retry, interruption recovery, completion, and parking.'
}

Table "payment_succeeded_observations" {
  "id" uuid [pk, not null]
  "tenant_id" uuid [not null]
  "registration_order_id" uuid [not null]
  "payment_attempt_id" uuid [not null]
  "source_incoming_webhook_message_id" uuid
  "provider_checkout_session_id" varchar(200) [not null]
  "provider_payment_id" varchar(200) [not null]
  "provider_request_id" varchar(120)
  "observed_at" timestamptz [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_payment_succeeded_observations_tenant_id_id']
    (tenant_id, payment_attempt_id) [unique, name: 'ix_payment_succeeded_observations_tenant_id_payment_attempt_id']
    (tenant_id, source_incoming_webhook_message_id) [name: 'ix_payment_succeeded_observations_tenant_id_source_incoming_webhook_message_id']
  }

  Note: 'Identifiers-only duplicate-safe evidence that authoritative provider retrieval proved payment success. This row is downstream input and does not finalize an order.'
}

Table "event_public_actions" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "event_public_action_kind_id" int [not null]
  "health_state_id" int [not null]
  "url" varchar(2048) [not null]
  "destination_domain" varchar(253) [not null]
  "label" varchar(200)
  "sort_order" int [not null]
  "is_primary" boolean [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_public_actions_tenant_id_id']
    (tenant_id, event_id) [name: 'ix_event_public_actions_tenant_event']
  }

  Note: 'Tenant-scoped moderated external actions. Application handlers enforce at most one active primary action per event inside a serializable EF transaction.'
}

Table "event_organizer_claims" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "claimant_actor_id" uuid [not null]
  "status_id" int [not null]
  "evidence_type" varchar(100) [not null]
  "evidence_reference" varchar(2048) [not null]
  "reviewer_user_id" uuid
  "decision_reason_code" varchar(100)
  "decided_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_organizer_claims_tenant_id_id']
  }

  Note: 'Tenant-scoped organizer authority claims. Approval grants future organizer authority only and does not grant historical attendee data.'
}

Table "event_registrations" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "event_id" uuid [not null, note: 'denormalized from EventSession for same-event composite FK enforcement']
  "linked_user_id" uuid
  "event_session_id" uuid [not null]
  "coverage_established_at" timestamptz [not null, default: `NOW()`]
  "registration_order_id" uuid
  "registration_order_line_id" uuid
  "registration_participant_id" uuid [not null]
  "ticket_type_entitlement_id" uuid
  "entitlement_ordinal" int
  "approval_status_id" int
  "tenant_id" uuid [not null]
  "atproto_record_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, event_id, event_session_id, linked_user_id) [name: 'ix_eventregistrations_session_user', note: 'filter: is_deleted = false']
    linked_user_id [name: 'ix_eventregistrations_user']
    (tenant_id, event_session_id, registration_participant_id) [unique, name: 'ix_eventregistrations_session_participant', note: 'filter: is_deleted = false']
    (tenant_id, registration_order_id) [name: 'ix_event_registrations_tenant_id_registration_order_id']
    (tenant_id, ticket_type_entitlement_id) [name: 'ix_event_registrations_tenant_id_ticket_type_entitlement_id']
    (tenant_id, registration_order_line_id, ticket_type_entitlement_id, event_session_id, entitlement_ordinal) [unique, name: 'ix_eventregistrations_order_admission', note: 'filter: registration_order_line_id IS NOT NULL AND ticket_type_entitlement_id IS NOT NULL AND entitlement_ordinal IS NOT NULL AND is_deleted = false']
  }
}

Table "event_session_categories" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "event_session_id" uuid [not null]
  "category_id" uuid [not null]
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, category_id) [name: 'ix_event_session_categories_tenant_id_category_id']
    (tenant_id, event_session_id, category_id) [unique, name: 'ix_event_session_categories_tenant_session_category']
  }
}


Table "event_session_tags" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "event_session_id" uuid [not null]
  "tag_id" uuid [not null]
  "tenant_id" uuid [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, tag_id) [name: 'ix_event_session_tags_tenant_id_tag_id']
    (tenant_id, event_session_id, tag_id) [unique, name: 'ix_event_session_tags_tenant_session_tag']
  }
}


Table "event_contact_share_consents" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "subject_type_id" int [not null]
  "subject_id" uuid [not null]
  "user_subject_id" uuid
  "registration_purchaser_order_id" uuid
  "registration_participant_id" uuid
  "guest_contact_order_id" uuid
  "recipient_actor_id" uuid [not null]
  "purpose_code" varchar(100) [not null]
  "status" int [not null]
  "email_snapshot" varchar(320) [not null]
  "email_normalized_snapshot" varchar(320) [not null]
  "consent_text_snapshot" text [not null]
  "consent_ui_version" varchar(100) [not null]
  "granted_at" timestamptz [not null]
  "withdrawn_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, subject_type_id, subject_id, recipient_actor_id, purpose_code) [unique, name: 'ux_event_contact_share_consents_current_scope']
    (tenant_id, recipient_actor_id, status) [name: 'ix_event_contact_share_consents_recipient_status']
    (tenant_id, subject_type_id, subject_id, status) [name: 'ix_event_contact_share_consents_subject_status']
  }

  Note: 'Current typed contact-share consent. Check ck_event_contact_share_consents_subject_shape requires exactly one subject-specific FK.'
}

Table "event_contact_share_consent_history" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "consent_id" uuid [not null]
  "operation_id" int [not null]
  "status_snapshot" int [not null]
  "subject_type_id" int [not null]
  "subject_id" uuid [not null]
  "recipient_actor_id" uuid [not null]
  "purpose_code_snapshot" varchar(100) [not null]
  "email_snapshot" varchar(320) [not null]
  "email_normalized_snapshot" varchar(320) [not null]
  "consent_text_snapshot" varchar(4000) [not null]
  "consent_ui_version_snapshot" varchar(100) [not null]
  "source_event_id" uuid
  "source_registration_order_id" uuid
  "actor_id" uuid
  "user_id" uuid
  "occurred_at" timestamptz [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, consent_id, occurred_at) [name: 'ix_event_contact_share_consent_history_tenant_id_consent_id_occurred_at']
  }
}

Table "event_contact_share_exports" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "recipient_actor_id" uuid [not null]
  "event_id" uuid
  "exported_by_user_id" uuid [not null]
  "format" varchar(20) [not null]
  "purpose_code" varchar(100) [not null]
  "status_id" int [not null]
  "requested_field_keys_snapshot" varchar(2000) [not null]
  "included_field_keys_snapshot" varchar(2000) [not null]
  "policy_version" varchar(100) [not null]
  "content_hash" varchar(64)
  "failure_category_id" int
  "completed_at" timestamptz
  "failed_at" timestamptz
  "row_count" int [not null]
  "created_at" timestamptz [not null]

  indexes {
    (tenant_id, recipient_actor_id, created_at) [name: 'ix_eventcontactshareexports_recipient_date']
    (tenant_id, event_id) [name: 'ix_event_contact_share_exports_tenant_id_event_id']
    (exported_by_user_id) [name: 'ix_event_contact_share_exports_exported_by_user_id']
  }
}


Table "event_contact_share_export_items" {
  "export_id" uuid [pk, not null]
  "consent_id" uuid [pk, not null]
  "exported_field_snapshot" varchar(4000) [not null]

  indexes {
    (consent_id) [name: 'ix_event_contact_share_export_items_consent_id']
  }
}


// ============================================================
// Event Reporting & Moderation Review
// ============================================================

Table "event_reports" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "reporter_user_id" uuid
  "reporter_actor_id" uuid
  "reporter_kind" int [not null]
  "source_kind" int [not null]
  "reason_code" varchar(100) [not null]
  "subcategory_code" varchar(100)
  "status" int [not null]
  "priority" int [not null]
  "severity_hint" int
  "duplicate_group_id" uuid
  "report_case_updates_consent" boolean [not null]
  "report_follow_up_contact_consent" boolean [not null]
  "reporter_locale" varchar(10)
  "reporter_ip_hash" varchar(64)
  "reporter_user_agent_hash" varchar(64)
  "closed_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null, default: false]
  "deleted_at" timestamptz
  "deleted_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_reports_tenant_id_id']
    (tenant_id, event_id, id) [unique, name: 'ak_event_reports_tenant_id_event_id_id']
    reporter_user_id [name: 'ix_event_reports_reporter_user_id']
    (tenant_id, reporter_actor_id) [name: 'ix_event_reports_tenant_id_reporter_actor_id']
    (tenant_id, duplicate_group_id) [name: 'ix_event_reports_tenant_duplicate_group', note: 'filter: duplicate_group_id IS NOT NULL']
    (tenant_id, event_id, status, created_at) [name: 'ix_event_reports_tenant_event_status_created', note: 'descending: created_at']
    (tenant_id, priority, status, created_at) [name: 'ix_event_reports_tenant_priority_status_created', note: 'descending: created_at']
    (tenant_id, reporter_user_id, event_id, reason_code, created_at) [name: 'ix_event_reports_tenant_reporter_event_reason_created', note: 'filter: reporter_user_id IS NOT NULL; descending: created_at']
  }

  Note: 'Tenant-scoped event-report aggregate with separate case-update and follow-up-contact consent authorities. Checks enforce non-blank reason/subcategory/fingerprint values when present, enum ranges for reporter/source/status/priority/severity, and closed_at only for terminal statuses.'
}

Table "event_report_cases" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "report_id" uuid [not null]
  "queue_code" varchar(50) [not null]
  "status" int [not null]
  "priority" int [not null]
  "assigned_moderator_user_id" uuid
  "sla_due_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_report_cases_tenant_id_id']
    (tenant_id, report_id, id) [unique, name: 'ak_event_report_cases_tenant_id_report_id_id']
    assigned_moderator_user_id [name: 'ix_event_report_cases_assigned_moderator_user_id']
    (tenant_id, assigned_moderator_user_id, status, updated_at) [name: 'ix_event_report_cases_tenant_assignee_status_updated', note: 'filter: assigned_moderator_user_id IS NOT NULL; descending: updated_at']
    (tenant_id, queue_code, status, priority, created_at) [name: 'ix_event_report_cases_tenant_queue_status_priority_created', note: 'descending: created_at']
    (tenant_id, sla_due_at) [name: 'ix_event_report_cases_tenant_sla_due_at', note: 'filter: sla_due_at IS NOT NULL']
  }

  Note: 'Local moderation queue case for an event report. Checks enforce non-blank queue code and status/priority ranges; concurrency_stamp is the optimistic workflow guard.'
}

Table "event_report_evidence" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "report_id" uuid [not null]
  "evidence_kind" int [not null]
  "text_body_encrypted" text
  "storage_object_id" uuid
  "content_hash" varchar(128)
  "classification" int [not null]
  "retention_until" timestamptz
  "created_by_user_id" uuid
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    created_by_user_id [name: 'ix_event_report_evidence_created_by_user_id']
    (tenant_id, storage_object_id) [name: 'ix_event_report_evidence_tenant_id_storage_object_id']
    (tenant_id, content_hash) [name: 'ix_event_report_evidence_tenant_content_hash', note: 'filter: content_hash IS NOT NULL']
    (tenant_id, report_id, evidence_kind, created_at) [name: 'ix_event_report_evidence_tenant_report_kind_created', note: 'descending: created_at']
    (tenant_id, retention_until) [name: 'ix_event_report_evidence_tenant_retention_until', note: 'filter: retention_until IS NOT NULL']
  }

  Note: 'Sensitive report evidence. Reporter-text evidence stores encrypted text only; checks enforce evidence/classification ranges, required encrypted reporter text for text evidence, and non-blank content_hash when present.'
}

Table "event_report_targets" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "report_id" uuid [not null]
  "target_kind" int [not null]
  "target_id" uuid [not null]
  "field_path" varchar(200)
  "storage_object_id" uuid

  indexes {
    (tenant_id, storage_object_id) [name: 'ix_event_report_targets_tenant_id_storage_object_id']
    (tenant_id, report_id, target_kind, target_id) [name: 'ix_event_report_targets_tenant_report_target']
    (tenant_id, target_kind, target_id) [name: 'ix_event_report_targets_tenant_target']
  }

  Note: 'Report target references for event/session/field/storage-object targets. Checks enforce target_kind range and non-blank field_path when present.'
}

Table "event_report_signals" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "report_id" uuid
  "event_id" uuid [not null]
  "provider" int [not null]
  "signal_type" varchar(100) [not null]
  "policy_code" varchar(100) [not null]
  "score" numeric(5,4)
  "verdict" int [not null]
  "recommended_action" int
  "safe_summary" varchar(500)
  "external_signal_id" varchar(200)
  "correlation_id" varchar(100) [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, event_id, report_id) [name: 'ix_event_report_signals_tenant_id_event_id_report_id']
    (tenant_id, event_id, provider, created_at) [name: 'ix_event_report_signals_tenant_event_provider_created', note: 'descending: created_at']
    (tenant_id, report_id, provider, created_at) [name: 'ix_event_report_signals_tenant_report_provider_created', note: 'filter: report_id IS NOT NULL; descending: created_at']
    (tenant_id, provider, correlation_id) [unique, name: 'ux_event_report_signals_tenant_provider_correlation']
    (tenant_id, provider, external_signal_id) [unique, name: 'ux_event_report_signals_tenant_provider_external_signal', note: 'filter: external_signal_id IS NOT NULL']
  }

  Note: 'Bounded moderation provider signal metadata. Checks enforce provider/verdict/recommended_action ranges, score 0..1, non-blank signal/policy/correlation IDs, and no raw provider payload storage.'
}

Table "event_report_decisions" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "case_id" uuid [not null]
  "report_id" uuid [not null]
  "decision_source" int [not null]
  "decision_kind" int [not null]
  "duplicate_group_id" uuid
  "reason_code" varchar(100) [not null]
  "safe_note" varchar(1000)
  "moderator_user_id" uuid
  "external_decision_id" varchar(200)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, report_id, id) [unique, name: 'ak_event_report_decisions_tenant_id_report_id_id']
    moderator_user_id [name: 'ix_event_report_decisions_moderator_user_id']
    (tenant_id, report_id, case_id) [name: 'ix_event_report_decisions_tenant_id_report_id_case_id']
    (tenant_id, case_id, created_at) [name: 'ix_event_report_decisions_tenant_case_created', note: 'descending: created_at']
    (tenant_id, report_id, created_at) [name: 'ix_event_report_decisions_tenant_report_created', note: 'descending: created_at']
    (tenant_id, decision_source, external_decision_id) [unique, name: 'ux_event_report_decisions_tenant_source_external', note: 'filter: external_decision_id IS NOT NULL']
  }

  Note: 'Review decision before enforcement. Duplicate decisions require duplicate_group_id and every other kind forbids it. Checks also enforce source/kind ranges, non-blank reason/safe-note/external IDs when present, and local moderator identity for local decisions.'
}

Table "event_report_decision_executions" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "report_id" uuid [not null]
  "decision_id" uuid [not null]
  "state" int [not null]
  "enforcement_receipt_kind" int [not null]
  "enforcement_receipt_id" uuid
  "processing_lease_token" uuid
  "processing_lease_expires_at_utc" timestamptz
  "attempt_count" int [not null]
  "last_failure_code" varchar(100)
  "last_failure_at_utc" timestamptz
  "enforcement_completed_at_utc" timestamptz
  "completed_at_utc" timestamptz
  "created_at" timestamptz [not null]
  "updated_at" timestamptz
  "version" bigint [not null]

  indexes {
    (tenant_id, id) [unique, name: 'ak_event_report_decision_executions_tenant_id_id']
    (tenant_id, decision_id) [unique, name: 'ux_event_report_decision_executions_tenant_decision']
    (tenant_id, report_id, decision_id) [unique, name: 'ux_event_report_decision_executions_tenant_report_decision']
    (state, processing_lease_expires_at_utc, created_at) [name: 'ix_event_report_decision_executions_runnable']
  }

  Note: 'One durable execution per captured report decision. Checks fence the Requested/InProgress/CompletionPending/Completed state shapes, paired leases, exact light/heavy receipt IDs, and bounded failure metadata.'
}

Table "event_report_external_links" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "report_id" uuid [not null]
  "case_id" uuid
  "provider" int [not null]
  "provider_case_id" varchar(200)
  "provider_signal_id" varchar(200)
  "provider_url" varchar(500)
  "sync_state" int [not null]
  "last_synced_at" timestamptz
  "last_error_category" varchar(100)
  "retry_count" int [not null]
  "correlation_id" varchar(100) [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, report_id, case_id) [name: 'ix_event_report_external_links_tenant_id_report_id_case_id']
    (tenant_id, provider, sync_state, created_at) [name: 'ix_event_report_external_links_tenant_provider_state_created', note: 'descending: created_at']
    (tenant_id, provider, provider_case_id) [unique, name: 'ux_event_report_external_links_tenant_provider_case', note: 'filter: provider_case_id IS NOT NULL']
    (tenant_id, provider, correlation_id) [unique, name: 'ux_event_report_external_links_tenant_provider_correlation']
    (tenant_id, provider, provider_signal_id) [unique, name: 'ux_event_report_external_links_tenant_provider_signal', note: 'filter: provider_signal_id IS NOT NULL']
  }

  Note: 'External provider sync marker for report/case/signal mirroring. Checks enforce provider/sync_state ranges, retry_count >= 0, non-blank external IDs/URLs/error categories/correlation IDs when present.'
}


// ============================================================
// Audit & Notifications
// ============================================================

Table "audit_logs" {
  "id" uuid [pk, not null, note: 'uuidv7 app-side']
  "entity_type" varchar(200) [not null]
  "entity_id" varchar(200) [not null]
  "action" varchar(50) [not null]
  "old_values" jsonb
  "new_values" jsonb
  "affected_columns" jsonb
  "actor_id" uuid
  "timestamp" timestamptz [not null]
  "tenant_id" uuid [not null]

  indexes {
    (entity_type, entity_id) [name: 'IX_audit_logs_entity_type_entity_id']
    tenant_id [name: 'IX_audit_logs_tenant_id']
    (tenant_id, entity_type, entity_id, timestamp) [name: 'ix_audit_logs_desc', note: 'descending: timestamp']
  }
}

Table "notifications" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "user_id" uuid [not null]
  "notification_intent_id" uuid [note: 'Nullable for preserved inbox rows created before explicit intent linkage']
  "notification_type_id" int [not null]
  "notification_reason_id" int
  "notification_entity_type_id" int
  "source_actor_id" uuid
  "recipient_context_actor_id" uuid
  "notification_scope_id" int [not null]
  "entity_id" varchar(200)
  "title" varchar(500) [not null]
  "body" varchar(2000)
  "deduplication_key" varchar(500) [not null]
  "snoozed_until" timestamptz
  "is_read" boolean [not null]
  "read_at" timestamptz
  "is_archived" boolean [not null]
  "archived_at" timestamptz
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "is_deleted" boolean [not null]
  "deleted_at" timestamptz
  "deleted_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_notifications_tenant_id']
    (tenant_id, id, notification_intent_id) [unique, name: 'ux_notifications_tenant_id_intent_link']
    (tenant_id, notification_intent_id) [unique, name: 'ux_notifications_tenant_notification_intent', note: 'filter: notification_intent_id IS NOT NULL AND is_deleted = false']
    (tenant_id, notification_intent_id, user_id) [name: 'ix_notifications_tenant_id_notification_intent_id_user_id']
    notification_entity_type_id [name: 'ix_notifications_notification_entity_type_id']
    notification_reason_id [name: 'ix_notifications_notification_reason_id']
    notification_scope_id [name: 'ix_notifications_notification_scope_id']
    notification_type_id [name: 'ix_notifications_notification_type_id']
    recipient_context_actor_id [name: 'ix_notifications_recipient_context_actor_id']
    source_actor_id [name: 'ix_notifications_source_actor_id']
    (tenant_id, user_id, is_read, created_at) [name: 'ix_notifications_tenant_user_unread', note: 'descending: created_at']
    (tenant_id, user_id, created_at) [name: 'ix_notifications_unread_by_user', note: 'descending: created_at', unique]
    (tenant_id, notification_type_id) [name: 'ix_notifications_tenant_type']
    (user_id, notification_scope_id, is_read) [name: 'ix_notifications_user_scope']
    (user_id, is_archived, created_at) [name: 'ix_notifications_user_archived', note: 'descending: created_at']
    (tenant_id, user_id, deduplication_key) [unique, name: 'ux_notifications_tenant_user_deduplication_key']
  }

  Note: 'User notification inbox row. New linked rows enforce tenant, intent, and recipient equality; preserved pre-1.0 rows remain valid with a null intent link. Check ck_notifications_entity_reference_shape keeps polymorphic references null/null or Guid-shaped.'
}

Table "notification_fanout_runs" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "fanout_kind" varchar(100) [not null]
  "notification_entity_type_id" int [not null]
  "entity_id" uuid [not null]
  "source_actor_id" uuid [not null]
  "status" varchar(50) [not null, default: 'pending']
  "cursor_subscriber_tenant_user_id" uuid
  "cursor_first_eligible_registration_created_at" timestamptz
  "cursor_user_id" uuid
  "fanout_occurrence_id" uuid
  "processing_lease_owner" varchar(200)
  "processing_lease_token" uuid
  "processing_lease_expires_at" timestamptz
  "processing_generation" int [not null]
  "processing_fence" bigint [not null]
  "heartbeat_at" timestamptz
  "processed_count" int [not null]
  "created_notification_count" int [not null]
  "started_at" timestamptz
  "completed_at" timestamptz
  "failed_at" timestamptz
  "last_error" varchar(2000)
  "created_at" timestamptz [not null, default: `NOW()`]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid
  "concurrency_stamp" uuid [not null]

  indexes {
    (tenant_id, fanout_kind, notification_entity_type_id, entity_id, source_actor_id) [unique, name: 'ux_notification_fanout_runs_source', note: 'filter: fanout_occurrence_id IS NULL']
    (tenant_id, fanout_occurrence_id) [unique, name: 'ux_notification_fanout_runs_occurrence']
    (status, processing_lease_expires_at, created_at) [name: 'ix_notification_fanout_runs_worker_poll']
  }

  Note: 'Legacy and occurrence fanout progress. Occurrence runs use a fenced renewable lease plus compound timestamp/user checkpoint. Checks enforce nonnegative counts/generation/fence, paired cursor fields, and complete occurrence lease state.'
}


Table "policy_change_outbox" {
  "id" uuid [pk, not null]
  "scope" int [not null]
  "scope_id" uuid
  "operation" int [not null]
  "status" int [not null]
  "created_at" timestamptz [not null]
  "created_by" varchar(200)
  "processed_at" timestamptz
  "retry_count" int [not null]
  "last_error" varchar(2000)
  "next_retry_at" timestamptz

  indexes {
    (status, next_retry_at) [name: 'ix_policy_change_outbox_status_retry']
  }
}

Table "atproto_transient_records" {
  "id" uuid [pk, not null, note: 'Application-assigned UUIDv7; immutable candidate identity']
  "purpose" int [not null, note: '1 OAuthState, 2 TenantHandoff, 3 internal HealthProbe']
  "token_digest" char(64) [not null, note: 'Lowercase SHA-256 locator digest; never the raw state or handoff code']
  "tenant_id" uuid [note: 'Mandatory nonempty binding for authentication purposes; null only for internal health probes']
  "protected_payload" varchar(65536) [not null, note: 'BFF-protected ciphertext; application boundary also enforces 64 KiB UTF-8']
  "expires_at_unix_milliseconds" bigint [not null]

  indexes {
    (purpose, token_digest) [unique]
    expires_at_unix_milliseconds
  }

  Note: 'Instance-owned authentication infrastructure, not a tenant-filtered business aggregate. Rows are insert-only and conditionally deleted using candidate identity, purpose, digest, expected tenant and fresh expiry. Exactly one committed delete may return ciphertext. Tenant-purpose shape is database constrained; live eligibility does not depend on cleanup.'
}

Table "atproto_transient_assertion_replays" {
  "id" uuid [pk, not null, note: 'Application-assigned UUIDv7']
  "assertion_digest" char(64) [not null, note: 'SHA-256 digest of a transient-service assertion identifier; no raw assertion']
  "expires_at_unix_milliseconds" bigint [not null, note: 'Retains the claim through the full assertion acceptance and clock-skew window']

  indexes {
    assertion_digest [unique]
    expires_at_unix_milliseconds
  }

  Note: 'Instance-owned, insert-only replay authority for machine requests before tenant restoration. Separate from tenant-scoped bootstrap replay and ordinary idempotency records. Bounded cleanup physically deletes expired claims.'
}

Table "idempotency_records" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "key" varchar(128) [not null]
  "tenant_id" uuid [not null]
  "user_id" varchar(256)
  "request_method" varchar(16) [not null]
  "request_target" varchar(512) [not null]
  "request_content_type" varchar(256)
  "request_body_hash" varchar(64) [not null]
  "principal_fingerprint" varchar(64) [not null]
  "expires_at" timestamptz [not null]
  "status_code" int
  "content_type" varchar(256)
  "response_body" text
  "created_at" timestamptz [not null]

  indexes {
    (key, tenant_id) [unique, name: 'IX_IdempotencyRecords_Key_TenantId']
    expires_at [name: 'IX_IdempotencyRecords_ExpiresAt']
  }

  Note: 'Ephemeral write-retry replay cache. Request fingerprint columns reject same-key reuse for a different write request or principal fingerprint.'
}

Table "incoming_webhook_effect_outbox" {
  "id" uuid [pk, not null, default: `uuidv7()`]
  "tenant_id" uuid [not null]
  "incoming_webhook_message_id" uuid [not null]
  "provider" varchar(100) [not null]
  "provider_decision_id" varchar(256) [not null]
  "effect_kind" varchar(200) [not null]
  "payload_sha256" varchar(71) [not null]
  "status" int [not null]
  "processing_generation" int [not null, default: 1]
  "processing_fence" bigint [not null, default: 0]
  "attempt_count" int [not null, default: 0]
  "processing_lease_owner" varchar(200)
  "processing_lease_token" uuid
  "processing_lease_expires_at" timestamptz
  "processing_started_at" timestamptz
  "next_attempt_at" timestamptz
  "completed_at" timestamptz
  "dead_lettered_at" timestamptz
  "failure_category" varchar(100)
  "safe_detail" varchar(1024)
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_incoming_webhook_effect_outbox_tenant_id']
    (tenant_id, provider, provider_decision_id, effect_kind) [unique, name: 'ux_incoming_webhook_effect_outbox_provider_decision']
    (tenant_id, incoming_webhook_message_id, effect_kind) [unique, name: 'ux_incoming_webhook_effect_outbox_message_effect']
    (status, next_attempt_at, created_at) [name: 'ix_incoming_webhook_effect_outbox_worker_poll']
  }

  Note: 'Durable pointer from a verified Coop decision callback to deferred local effect execution. Stores provider identity, SHA-256 evidence, fenced lease/retry state, and bounded safe failure evidence only; raw callback bytes remain on the tenant-matched incoming_webhook_messages row. The composite inbox foreign key and RESTRICT deletion retain those bytes until terminal effect settlement and replay retention permit cleanup. Checks: payload_sha256 matches ^sha256:[0-9a-f]{64}$; processing_generation >= 1; processing_fence and attempt_count >= 0; failure_category is null or lowercase ASCII letters/digits/underscore.'
}


// ============================================================
// Webhook Provider Capability Authority
// ============================================================

Table "webhook_provider_capabilities" {
  "id" int [pk, not null, note: 'Stable individual flag value; ValueGeneratedNever']
  "master_code" varchar(100) [not null]
  "full_name" varchar(200) [not null]
  "description" varchar(500)

  indexes {
    master_code [unique, name: 'ux_webhook_provider_capabilities_master_code']
  }

  Note: 'Normalized metadata for the twelve individually addressable provider capability flags (1..2048). Rows are inserted idempotently by the runtime lookup seeder; combinations remain persisted as bounded bitmasks.'
}

Table "webhook_consumer_provider_bindings" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "webhook_consumer_id" uuid [not null]
  "instance_id" uuid [not null]
  "provider_kind_id" int [not null]
  "provider_version" varchar(100) [not null]
  "provider_environment" varchar(500) [not null]
  "normalized_environment" varchar(500) [not null]
  "application_uid" varchar(500) [not null]
  "normalized_application_uid" varchar(500) [not null]
  "external_application_id" varchar(500)
  "normalized_external_application_id" varchar(500)
  "verification_state_id" int [not null]
  "verified_tenant_id" uuid
  "verified_webhook_consumer_id" uuid
  "verified_at_utc" timestamptz
  "capabilities" bigint [not null]
  "governance_allowed_capabilities" bigint [not null]
  "capability_resolution_version" varchar(100) [not null]
  "capabilities_resolved_at_utc" timestamptz [not null]
  "is_enabled" boolean [not null]
  "concurrency_version" bigint [not null]
  "verification_fence" bigint [not null]
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    (tenant_id, id) [unique, name: 'ak_webhook_consumer_provider_bindings_tenant_id_id']
    (tenant_id, webhook_consumer_id, provider_kind_id, normalized_environment) [unique, name: 'ux_webhook_provider_bindings_tenant_consumer_provider_environment']
    (tenant_id, provider_kind_id, normalized_environment, normalized_external_application_id) [unique, name: 'ux_webhook_provider_bindings_tenant_provider_environment_external_app', note: 'filter: normalized_external_application_id IS NOT NULL']
    (provider_kind_id, normalized_environment, normalized_external_application_id, normalized_application_uid) [unique, name: 'ux_webhook_provider_bindings_provider_application_identity', note: 'filter: normalized_external_application_id IS NOT NULL']
    (tenant_id, provider_kind_id, normalized_environment, normalized_application_uid) [unique, name: 'ux_webhook_provider_bindings_tenant_provider_environment_application_uid']
  }

  Note: 'Tenant-scoped provider ownership proof. Checks require concurrency_version and verification_fence > 0 and constrain capabilities/governance_allowed_capabilities to known masks 0..4095. Effective authority is capabilities & governance_allowed_capabilities; EF enforces restrictive tenant/consumer and normalized lookup foreign keys.'
}


// ============================================================
// Views
// ============================================================

Table "custom_property_projection_dirty_scope" {
  "id" bigint [pk, not null, note: 'bigint identity']
  "projection_name" varchar(100) [not null]
  "projection_version" int [not null]
  "tenant_id" uuid [not null]
  "scope_type" varchar(50) [not null]
  "scope_id" uuid
  "definition_id" uuid
  "reason" varchar(200) [not null]
  "created_at" timestamptz [not null]
  "drained_at" timestamptz

  indexes {
    tenant_id [name: 'ix_custom_property_projection_dirty_scope_tenant_id']
    (projection_name, projection_version, tenant_id) [name: 'ix_dirty_scope_pending', note: 'filter: drained_at IS NULL']
    (projection_name, projection_version, tenant_id, scope_type, scope_id, definition_id) [unique, name: 'ix_dirty_scope_unique']
  }
}

Table "custom_property_projection_status" {
  "projection_name" varchar(100) [not null]
  "projection_version" int [not null]
  "tenant_id" uuid [not null]
  "state" varchar(50) [not null]
  "last_rebuild_started_at" timestamptz
  "last_rebuild_completed_at" timestamptz
  "rows_processed" bigint [not null]
  "rows_failed" bigint [not null]
  "last_checkpoint" varchar(200)
  "last_error_message" varchar(2000)
  "concurrency_stamp" uuid [not null]

  indexes {
    tenant_id [name: 'ix_custom_property_projection_status_tenant_id']
  }

  Note: 'PK is (projection_name, projection_version, tenant_id).'
}

Table "event_role_assignments" {
  "id" uuid [pk, not null, note: 'uuidv7()']
  "tenant_id" uuid [not null]
  "event_id" uuid [not null]
  "user_id" uuid [not null]
  "role_id" int [not null]
  "status" int [not null]
  "starts_at_utc" timestamptz [not null]
  "expires_at_utc" timestamptz
  "revoked_at_utc" timestamptz
  "revoked_by_user_id" uuid
  "version" bigint [not null, note: 'concurrency token']
  "created_at" timestamptz [not null]
  "created_by" uuid
  "updated_at" timestamptz
  "updated_by" uuid

  indexes {
    event_id [name: 'ix_event_role_assignments_event_id']
    role_id [name: 'ix_event_role_assignments_role_id']
    user_id [name: 'ix_event_role_assignments_user_id']
    (tenant_id, event_id, role_id, status) [name: 'ix_event_role_assignments_tenant_event_role_status']
    (tenant_id, event_id, user_id, role_id) [unique, name: 'ix_event_role_assignments_unique_pending_active', note: 'filter: status IN (1, 2)']
    (tenant_id, event_id, user_id, status) [name: 'ix_event_role_assignments_tenant_event_user_status']
    (tenant_id, user_id, event_id, status) [name: 'ix_event_role_assignments_tenant_user_event_status']
  }

  Note: 'Check: ck_event_role_assignments_validity_window (expires_at_utc IS NULL OR expires_at_utc > starts_at_utc).'
}


// ============================================================
// Relationships (Foreign Keys)
// ============================================================

// Tenants & Setup
Ref: "instance_bootstrap_states"."completed_by_user_id" > "users"."id" [delete: restrict]
Ref: "tenants"."tenant_status_id" > "tenant_statuses"."id" [delete: restrict]
Ref: "tenant_settings_documents"."tenant_id" - "tenants"."id" [delete: cascade]
Ref: "tenant_setting_overrides"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "configuration_manifest_tenant_results"."operation_id" > "configuration_manifest_operations"."id" [delete: restrict]
Ref: "configuration_manifest_tenant_results"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "tenant_navigation_links"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "tenant_footer_link_groups"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "tenant_footer_links"."footer_link_group_id" > "tenant_footer_link_groups"."id" [delete: cascade]
Ref: "tenant_onboarding_states"."tenant_id" - "tenants"."id" [delete: cascade]
Ref: "tenant_policy_sets"."tenant_id" - "tenants"."id" [delete: cascade]
Ref: "tenant_capabilities"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "tenant_capabilities"."module_id" > "module_definitions"."id" [delete: restrict]
Ref: "tenant_invitations"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "tenant_users"."tenant_id" > "tenants"."id" [delete: cascade]
Ref: "tenant_users"."user_id" > "users"."id" [delete: cascade]
Ref: "tenant_users"."actor_id" > "actors"."id" [delete: set null]
Ref: "tenant_user_role_grants"."tenant_id" > "tenants"."id" [delete: cascade]
Ref: "tenant_user_role_grants".("tenant_id", "tenant_user_id") > "tenant_users".("tenant_id", "id") [delete: cascade]
Ref: "tenant_user_role_grants".("role_id", "role_scope_id") > "roles".("id", "role_scope_id") [delete: restrict]
Ref: "tenant_user_profiles"."tenant_id" > "tenants"."id" [delete: cascade]
Ref: "tenant_user_profiles"."tenant_user_id" - "tenant_users"."id" [delete: cascade]

// Users & Roles
Ref: "user_pii"."user_id" - "users"."id" [delete: cascade]
Ref: "platform_user_roles"."user_id" > "users"."id" [delete: restrict]
Ref: "platform_user_roles"."role_id" > "roles"."id" [delete: restrict]
Ref: "roles"."role_scope_id" > "role_scopes"."id" [delete: restrict]
Ref: "permissions"."role_scope_id" > "role_scopes"."id" [delete: restrict]
Ref: "role_permissions"."role_id" > "roles"."id" [delete: cascade]
Ref: "role_permissions"."permission_id" > "permissions"."id" [delete: cascade]
Ref: "user_authentication_tokens"."user_id" > "users"."id" [delete: cascade]
Ref: "user_authentication_tokens"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "user_external_logins"."user_id" > "users"."id" [delete: restrict]
Ref: "user_preferences"."user_id" > "users"."id" [delete: restrict]
Ref: "user_notification_preferences"."user_id" > "users"."id" [delete: restrict]
Ref: "user_appearance_profiles"."user_id" > "users"."id" [delete: restrict]
Ref: "user_appearance_preferences"."user_id" > "users"."id" [delete: restrict]
Ref: "user_appearance_preferences"."active_profile_id" > "user_appearance_profiles"."id" [delete: restrict]
Ref: "external_bindings"."scope_tenant_id" > "tenants"."id" [delete: restrict]

// Support Access
Ref: "support_access_sessions"."actor_user_id" > "users"."id" [delete: restrict]
Ref: "support_access_sessions"."approved_by_user_id" > "users"."id" [delete: restrict]
Ref: "support_access_sessions"."target_tenant_id" > "tenants"."id" [delete: restrict]
Ref: "support_access_sessions"."target_tenant_user_id" > "tenant_users"."id" [delete: restrict]
Ref: "support_access_sessions"."status_id" > "support_access_session_statuses"."id" [delete: restrict]
Ref: "support_access_sessions"."mode_id" > "support_access_modes"."id" [delete: restrict]
Ref: "support_access_sessions"."end_reason_id" > "support_access_end_reasons"."id" [delete: restrict]
Ref: "support_access_audit_events"."support_access_session_id" > "support_access_sessions"."id" [delete: restrict]
Ref: "support_access_audit_events"."event_type_id" > "support_access_audit_event_types"."id" [delete: restrict]
Ref: "support_access_audit_events"."actor_user_id" > "users"."id" [delete: restrict]
Ref: "support_access_audit_events"."target_tenant_id" > "tenants"."id" [delete: restrict]
Ref: "support_access_audit_events"."target_tenant_user_id" > "tenant_users"."id" [delete: restrict]

// Actors & Identity
Ref: "actors"."actor_type_id" > "actor_types"."id" [delete: restrict]
Ref: "actors"."user_id" - "users"."id" [delete: restrict]
Ref: "actors"."organization_id" - "organizations"."id" [delete: restrict]
Ref: "actors"."group_id" - "groups"."id" [delete: restrict]
Ref: "actors"."external_actor_subject_id" - "external_actor_subjects"."id" [delete: restrict]
Ref: "actors"."service_principal_id" - "service_principals"."id" [delete: restrict]
Ref: "actor_pii"."actor_id" - "actors"."id" [delete: cascade]
Ref: "atproto_identities"."actor_id" > "actors"."id" [delete: restrict]
Ref: "atproto_identities"."did_custody_type_id" > "did_custody_types"."id" [delete: restrict]
Ref: "actor_merges"."source_actor_id" > "actors"."id" [delete: restrict]
Ref: "actor_merges"."canonical_actor_id" > "actors"."id" [delete: restrict]
Ref: "actor_key_stores"."actor_id" > "actors"."id" [delete: cascade]
Ref: "actor_subscriptions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "actor_subscriptions".("tenant_id", "subscriber_tenant_user_id") > "tenant_users".("tenant_id", "id") [delete: restrict]
Ref: "actor_subscriptions"."subscriber_user_id" > "users"."id" [delete: restrict]
Ref: "actor_subscriptions"."target_actor_id" > "actors"."id" [delete: restrict]
Ref: "actor_subscriptions"."target_actor_type_id" > "actor_types"."id" [delete: restrict]
Ref: "actor_subscriptions"."status_id" > "actor_subscription_statuses"."id" [delete: restrict]
Ref: "actor_subscriptions"."notification_level_id" > "actor_subscription_notification_levels"."id" [delete: restrict]

// Payment Policies & Provider Connections
Ref: "paid_event_policy_versions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "paid_event_policy_allowed_organizer_kinds".("policy_scope_key", "paid_event_policy_version_id") > "paid_event_policy_versions".("policy_scope_key", "id") [delete: cascade]
Ref: "paid_event_policy_allowed_currencies".("policy_scope_key", "paid_event_policy_version_id") > "paid_event_policy_versions".("policy_scope_key", "id") [delete: cascade]
Ref: "paid_event_policy_refund_protections".("policy_scope_key", "paid_event_policy_version_id") > "paid_event_policy_versions".("policy_scope_key", "id") [delete: cascade]
Ref: "paid_event_policy_currency_risk_limits".("policy_scope_key", "paid_event_policy_version_id") > "paid_event_policy_versions".("policy_scope_key", "id") [delete: cascade]
Ref: "organizer_payment_provider_connections"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "organizer_payment_provider_connections"."organizer_actor_id" > "actors"."id" [delete: restrict]
Ref: "organizer_payment_provider_connections".("tenant_id", "replaced_by_connection_id") > "organizer_payment_provider_connections".("tenant_id", "id") [delete: restrict]
Ref: "organizer_payment_provider_connections".("tenant_id", "replaces_connection_id") > "organizer_payment_provider_connections".("tenant_id", "id") [delete: restrict]
Ref: "organizer_payment_provider_account_operations"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "organizer_payment_provider_account_operations"."organizer_actor_id" > "actors"."id" [delete: restrict]
Ref: "organizer_payment_provider_account_operations".("tenant_id", "connection_id") > "organizer_payment_provider_connections".("tenant_id", "id") [delete: restrict]
Ref: "organizer_payment_provider_connection_supported_currencies".("tenant_id", "organizer_payment_provider_connection_id") > "organizer_payment_provider_connections".("tenant_id", "id") [delete: cascade]
Ref: "paid_order_acceptance_snapshots"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "paid_order_acceptance_snapshots".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "paid_order_acceptance_snapshots".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "paid_order_acceptance_lines".("tenant_id", "paid_order_acceptance_snapshot_id") > "paid_order_acceptance_snapshots".("tenant_id", "id") [delete: restrict]
Ref: "payment_attempts"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "payment_attempts".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "payment_attempts".("tenant_id", "paid_order_acceptance_snapshot_id") - "paid_order_acceptance_snapshots".("tenant_id", "id") [delete: restrict]
Ref: "payment_attempts"."payment_attempt_status_id" > "payment_attempt_statuses"."id" [delete: restrict]
Ref: "payment_attempts"."authoritative_status_floor_id" > "payment_attempt_statuses"."id" [delete: restrict]
Ref: "checkout_dispatch_effects"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "checkout_dispatch_effects".("tenant_id", "payment_attempt_id") > "payment_attempts".("tenant_id", "id") [delete: restrict]
Ref: "payment_reconciliation_effects"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "payment_reconciliation_effects".("tenant_id", "payment_attempt_id") > "payment_attempts".("tenant_id", "id") [delete: restrict]
Ref: "payment_reconciliation_effects".("tenant_id", "source_incoming_webhook_message_id") > "incoming_webhook_messages".("tenant_id", "id") [delete: restrict]
Ref: "payment_succeeded_observations"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "payment_succeeded_observations".("tenant_id", "payment_attempt_id") > "payment_attempts".("tenant_id", "id") [delete: restrict]
Ref: "payment_succeeded_observations".("tenant_id", "source_incoming_webhook_message_id") > "incoming_webhook_messages".("tenant_id", "id") [delete: restrict]

// Organizations & Groups
Ref: "organization_pii"."organization_id" - "organizations"."id" [delete: cascade]
Ref: "organization_tenants"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "organization_tenants"."organization_id" > "organizations"."id" [delete: restrict]
Ref: "organization_tenants"."approval_status_id" > "approval_statuses"."id" [delete: restrict]
Ref: "organization_tenant_evidence".("tenant_id", "organization_tenant_id") > "organization_tenants".("tenant_id", "id") [delete: restrict]
Ref: "organization_tenant_evidence".("tenant_id", "document_storage_object_id") > "storage_objects".("tenant_id", "id") [delete: restrict]
Ref: "organization_tenant_evidence"."review_status_id" > "approval_statuses"."id" [delete: restrict]
Ref: "organization_tenant_evidence"."reviewed_by_user_id" > "users"."id" [delete: restrict]
Ref: "organization_tenant_evidence"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "organization_members".("tenant_id", "organization_tenant_id") > "organization_tenants".("tenant_id", "id") [delete: cascade]
Ref: "organization_members"."user_id" > "users"."id" [delete: restrict]
Ref: "organization_members"."organization_position_id" > "organization_positions"."id" [delete: restrict]
Ref: "organization_reviews"."organization_id" > "organizations"."id" [delete: restrict]
Ref: "organization_reviews"."event_id" > "events"."id" [delete: restrict]
Ref: "organization_setting_overrides".("tenant_id", "organization_tenant_id") > "organization_tenants".("tenant_id", "id") [delete: cascade]
Ref: "organization_policy_sets"."organization_id" - "organizations"."id" [delete: cascade]
Ref: "group_tenants"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "group_tenants"."group_id" > "groups"."id" [delete: restrict]
Ref: "group_tenants"."approval_status_id" > "approval_statuses"."id" [delete: restrict]
Ref: "group_tenants".("tenant_id", "parent_group_tenant_id") > "group_tenants".("tenant_id", "id") [delete: restrict]
Ref: "group_tenants".("tenant_id", "parent_organization_tenant_id") > "organization_tenants".("tenant_id", "id") [delete: restrict]
Ref: "group_members".("tenant_id", "group_tenant_id") > "group_tenants".("tenant_id", "id") [delete: cascade]
Ref: "group_members"."user_id" > "users"."id" [delete: restrict]
Ref: "group_members"."group_position_id" > "group_positions"."id" [delete: restrict]
Ref: "group_setting_overrides".("tenant_id", "group_tenant_id") > "group_tenants".("tenant_id", "id") [delete: cascade]

// Taxonomy
// Tenant-scoped event-graph FKs below intentionally include tenant_id in the physical database model.
// This prevents a tenant-owned row from linking to a parent aggregate owned by another tenant.
Ref: "categories".("tenant_id", "parent_id") > "categories".("tenant_id", "id") [delete: restrict]
Ref: "category_type_categories"."category_id" > "categories"."id" [delete: restrict]
Ref: "category_type_categories"."category_type_id" > "category_types"."id" [delete: restrict]
Ref: "tag_type_tags"."tag_id" > "tags"."id" [delete: restrict]
Ref: "tag_type_tags"."tag_type_id" > "tag_types"."id" [delete: restrict]

// Storage
Ref: "storage_objects"."file_type_id" > "file_types"."id" [delete: restrict]
Ref: "storage_objects"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "storage_objects"."actor_id" > "actors"."id" [delete: set null]
Ref: "storage_upload_sessions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "storage_upload_sessions"."user_id" > "users"."id" [delete: restrict]
Ref: "storage_upload_sessions"."storage_object_id" > "storage_objects"."id" [delete: restrict]
Ref: "storage_usage_counters"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "locations"."location_kind_id" > "location_kinds"."id" [delete: restrict]
Ref: "locations"."location_privacy_state_id" > "location_privacy_states"."id" [delete: restrict]
Ref: "locations"."owner_user_id" > "users"."id" [delete: restrict]
Ref: "location_pii"."location_id" - "locations"."id" [delete: cascade]
Ref: "location_rooms".("tenant_id", "location_id") > "locations".("tenant_id", "id") [delete: cascade]
Ref: "event_locations"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_locations".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_locations".("tenant_id", "location_id") > "locations".("tenant_id", "id") [delete: restrict]
Ref: "event_locations"."full_details_audience_id" > "location_disclosure_audiences"."id" [delete: restrict]
Ref: "event_location_disclosure_audits"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_location_disclosure_audits".("tenant_id", "event_location_id") > "event_locations".("tenant_id", "id") [delete: restrict]
Ref: "event_location_disclosure_audits"."previous_audience_id" > "location_disclosure_audiences"."id" [delete: restrict]
Ref: "event_location_disclosure_audits"."new_audience_id" > "location_disclosure_audiences"."id" [delete: restrict]
Ref: "event_location_exact_read_audits"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_location_exact_read_audits".("tenant_id", "event_location_id") > "event_locations".("tenant_id", "id") [delete: restrict]
Ref: "privacy_erasure_replay_checkpoints"."previous_checkpoint_id" > "privacy_erasure_replay_checkpoints"."id" [delete: restrict]

// Federation / AT Protocol
Ref: "atproto_record_tenant_presentations"."tenant_id" > "tenants"."id" [delete: cascade]
Ref: "atproto_record_tenant_presentations"."atproto_record_id" > "atproto_records"."id" [delete: cascade]
Ref: "atproto_event_projections"."atproto_record_id" - "atproto_records"."id" [delete: cascade]
Ref: "atproto_outbound_record_ownerships"."atproto_record_id" - "atproto_records"."id" [delete: cascade]
Ref: "atproto_outbound_record_ownerships"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "atproto_outbound_record_ownerships"."user_id" > "users"."id" [delete: restrict]
Ref: "atproto_outbound_record_ownerships".("tenant_id", "user_id") > "tenant_users".("tenant_id", "user_id") [delete: restrict]
Ref: "atproto_jetstream_quarantines"."consumer_state_id" > "atproto_jetstream_consumer_states"."id" [delete: cascade]
Ref: "pds_sync_outbox"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "pds_sync_outbox"."user_id" > "users"."id" [delete: restrict]
Ref: "pds_sync_outbox".("tenant_id", "user_id") > "tenant_users".("tenant_id", "user_id") [delete: restrict]
Ref: "pds_sync_outbox"."atproto_record_id" > "atproto_records"."id" [delete: restrict]
Ref: "pds_sync_outbox"."depends_on_atproto_record_id" > "atproto_records"."id" [delete: restrict]
Ref: "pds_sync_outbox"."superseded_by_id" > "pds_sync_outbox"."id" [delete: restrict]

// Events Core
Ref: "events"."event_type_id" > "event_types"."id" [delete: restrict]
Ref: "events".("tenant_id", "actor_id") > "actors".("tenant_id", "id") [delete: restrict]
Ref: "events"."event_provenance_type_id" > "event_provenance_types"."id" [delete: restrict]
Ref: "events"."submitted_by_user_id" > "users"."id" [delete: restrict]
Ref: "events"."organizer_actor_id" > "actors"."id" [delete: restrict]
Ref: "events"."event_status_id" > "event_statuses"."id" [delete: restrict]
Ref: "events"."event_format_id" > "event_formats"."id" [delete: restrict]
Ref: "events"."visibility_type_id" > "visibility_types"."id" [delete: restrict]
Ref: "events"."registration_policy_id" > "event_registration_policies"."id" [delete: restrict]
Ref: "events"."audience_gender_id" > "audience_genders"."id" [delete: restrict]
Ref: "events"."audience_age_id" > "audience_ages"."id" [delete: restrict]
Ref: "events"."madhab_id" > "madhabs"."id" [delete: restrict]
Ref: "events"."atproto_record_id" > "atproto_records"."id" [delete: set null]
Ref: "events"."event_series_id" > "event_series"."id" [delete: restrict]
Ref: "event_participation_configurations"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_participation_configurations".("tenant_id", "id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_participation_configurations"."participation_handling_mode_id" > "participation_handling_modes"."id" [delete: restrict]
Ref: "event_participation_configurations"."advance_registration_obligation_id" > "advance_registration_obligations"."id" [delete: restrict]
Ref: "event_participation_configurations"."identity_access_mode_id" > "identity_access_modes"."id" [delete: restrict]
Ref: "event_capacity_pools"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_capacity_pools".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "event_capacity_pools"."capacity_hold_policy_id" > "capacity_hold_policies"."id" [delete: restrict]
Ref: "event_capacity_pools"."capacity_oversell_policy_id" > "capacity_oversell_policies"."id" [delete: restrict]
Ref: "event_ticket_catalog_versions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_ticket_catalog_versions".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "event_ticket_catalog_versions"."ticket_catalog_status_id" > "ticket_catalog_statuses"."id" [delete: restrict]
Ref: "event_ticket_types".("tenant_id", "catalog_id") > "event_ticket_catalog_versions".("tenant_id", "id") [delete: restrict]
Ref: "event_ticket_types".("tenant_id", "capacity_pool_id") > "event_capacity_pools".("tenant_id", "id") [delete: restrict]
Ref: "event_ticket_types"."ticket_pricing_mode_id" > "ticket_pricing_modes"."id" [delete: restrict]
Ref: "event_ticket_types"."participant_data_collection_mode_id" > "participant_data_collection_modes"."id" [delete: restrict]
Ref: "ticket_type_entitlements".("tenant_id", "ticket_type_id") > "event_ticket_types".("tenant_id", "id") [delete: restrict]
Ref: "ticket_type_entitlements".("tenant_id", "target_event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "ticket_type_entitlements".("tenant_id", "target_event_id", "event_day_id") > "event_days".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "ticket_type_entitlements".("tenant_id", "target_event_id", "event_session_id") > "event_sessions".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "ticket_type_entitlements"."entitlement_scope_type_id" > "entitlement_scope_types"."id" [delete: restrict]
Ref: "ticket_type_entitlements"."entitlement_selection_rule_id" > "entitlement_selection_rules"."id" [delete: restrict]
Ref: "registration_orders"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_orders".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "registration_orders".("tenant_id", "ticket_catalog_version_id") > "event_ticket_catalog_versions".("tenant_id", "id") [delete: restrict]
Ref: "registration_orders"."booking_party_type_id" > "booking_party_types"."id" [delete: restrict]
Ref: "registration_orders"."registration_order_status_id" > "registration_order_statuses"."id" [delete: restrict]
Ref: "registration_orders".("tenant_id", "applied_promotion_code_id_snapshot") > "promotion_codes".("tenant_id", "id") [delete: restrict]
Ref: "registration_orders".("tenant_id", "applied_promotion_definition_version_id_snapshot") > "promotion_definitions".("tenant_id", "id") [delete: restrict]
Ref: "registration_inventory_holds"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_inventory_holds".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "registration_inventory_holds".("tenant_id", "capacity_pool_id") > "event_capacity_pools".("tenant_id", "id") [delete: restrict]
Ref: "registration_inventory_holds".("tenant_id", "ticket_type_id") > "event_ticket_types".("tenant_id", "id") [delete: restrict]
Ref: "registration_inventory_holds"."registration_inventory_hold_status_id" > "registration_inventory_hold_statuses"."id" [delete: restrict]
Ref: "registration_order_lines".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "registration_order_lines".("tenant_id", "ticket_type_id") > "event_ticket_types".("tenant_id", "id") [delete: restrict]
Ref: "registration_order_lines".("tenant_id", "ticket_catalog_version_id") > "event_ticket_catalog_versions".("tenant_id", "id") [delete: restrict]
Ref: "promotion_definitions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "promotion_definitions"."promotion_definition_status_id" > "promotion_definition_statuses"."id" [delete: restrict]
Ref: "promotion_codes"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "promotion_codes".("tenant_id", "promotion_definition_version_id") > "promotion_definitions".("tenant_id", "id") [delete: restrict]
Ref: "promotion_reservations"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "promotion_reservations".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "promotion_reservations".("tenant_id", "promotion_definition_version_id") > "promotion_definitions".("tenant_id", "id") [delete: restrict]
Ref: "promotion_reservations".("tenant_id", "promotion_code_id") > "promotion_codes".("tenant_id", "id") [delete: restrict]
Ref: "promotion_reservations"."promotion_reservation_status_id" > "promotion_reservation_statuses"."id" [delete: restrict]
Ref: "registration_participants"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_participants".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "registration_participants"."linked_user_id" > "users"."id" [delete: restrict]
Ref: "registration_participants"."participant_type_id" > "participant_types"."id" [delete: restrict]
Ref: "registration_participants".("tenant_id", "registration_order_id", "guardian_participant_id") > "registration_participants".("tenant_id", "registration_order_id", "id") [delete: restrict]
Ref: "registration_participant_pii".("tenant_id", "registration_participant_id") - "registration_participants".("tenant_id", "id") [delete: restrict]
Ref: "registration_participant_pii"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_ticket_assignments"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_ticket_assignments".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "registration_ticket_assignments".("tenant_id", "registration_order_id", "registration_order_line_id") > "registration_order_lines".("tenant_id", "registration_order_id", "id") [delete: restrict]
Ref: "registration_ticket_assignments".("tenant_id", "registration_order_id", "participant_id") > "registration_participants".("tenant_id", "registration_order_id", "id") [delete: restrict]
Ref: "registration_ticket_assignments"."assignment_status_id" > "assignment_statuses"."id" [delete: restrict]
Ref: "registration_workflows"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_workflows".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "registration_requirements"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_requirements".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "registration_requirements".("tenant_id", "event_id", "registration_workflow_id") > "registration_workflows".("tenant_id", "event_id", "id") [delete: cascade]
Ref: "registration_requirements"."criticality_id" > "registration_requirement_criticalities"."id" [delete: restrict]
Ref: "registration_requirements"."completion_effect_id" > "registration_requirement_completion_effects"."id" [delete: restrict]

Ref: "registration_provider_connections"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_provider_connections"."provider_kind_id" > "registration_provider_kinds"."id" [delete: restrict]
Ref: "registration_provider_connections"."deployment_kind_id" > "registration_provider_deployment_kinds"."id" [delete: restrict]
Ref: "registration_provider_connections"."api_token_secret_binding_id" > "secret_bindings"."id" [delete: restrict]
Ref: "registration_provider_connections"."webhook_secret_binding_id" > "secret_bindings"."id" [delete: restrict]
Ref: "registration_provider_approved_origins"."registration_provider_connection_id" > "registration_provider_connections"."id" [delete: cascade]
Ref: "registration_provider_schema_revisions"."registration_provider_connection_id" > "registration_provider_connections"."id" [delete: restrict]
Ref: "registration_provider_schema_revisions"."schema_authority_id" > "registration_provider_schema_authorities"."id" [delete: restrict]
Ref: "registration_provider_bindings"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_provider_bindings"."registration_provider_connection_id" > "registration_provider_connections"."id" [delete: restrict]
Ref: "registration_provider_bindings"."registration_form_version_id" > "registration_form_versions"."id" [delete: restrict]
Ref: "registration_provider_bindings"."presentation_mode_id" > "registration_provider_presentation_modes"."id" [delete: restrict]
Ref: "registration_provider_bindings"."collection_mode_id" > "registration_provider_collection_modes"."id" [delete: restrict]
Ref: "registration_provider_bindings"."completion_mode_id" > "registration_provider_completion_modes"."id" [delete: restrict]
Ref: "registration_provider_bindings"."trust_level_id" > "registration_provider_trust_levels"."id" [delete: restrict]
Ref: "registration_provider_bindings"."drift_class_id" > "registration_provider_drift_classes"."id" [delete: restrict]
Ref: "registration_provider_bindings"."state_id" > "registration_provider_binding_states"."id" [delete: restrict]
Ref: "registration_provider_subscription_states"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_provider_subscription_states".("tenant_id", "registration_provider_binding_id") > "registration_provider_bindings".("tenant_id", "id") [delete: restrict]
Ref: "registration_provider_capabilities"."registration_provider_binding_id" > "registration_provider_bindings"."id" [delete: cascade]
Ref: "registration_provider_field_mappings"."registration_provider_binding_id" > "registration_provider_bindings"."id" [delete: cascade]
Ref: "registration_provider_option_mappings"."registration_provider_binding_id" > "registration_provider_bindings"."id" [delete: cascade]
Ref: "registration_provider_option_mappings"."registration_provider_field_mapping_id" > "registration_provider_field_mappings"."id" [delete: cascade]
Ref: "registration_sensitive_answer_values"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_attempts"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_attempts"."event_id" > "events"."id" [delete: restrict]
Ref: "registration_attempts"."registration_order_id" > "registration_orders"."id" [delete: restrict]
Ref: "registration_attempts"."registration_workflow_id" > "registration_workflows"."id" [delete: restrict]
Ref: "registration_attempts"."registration_requirement_id" > "registration_requirements"."id" [delete: restrict]
Ref: "registration_attempts"."registration_channel_id" > "registration_channels"."id" [delete: restrict]
Ref: "registration_attempts"."registration_form_id" > "registration_forms"."id" [delete: restrict]
Ref: "registration_attempts"."registration_form_version_id" > "registration_form_versions"."id" [delete: restrict]
Ref: "registration_attempts"."registration_provider_binding_id" > "registration_provider_bindings"."id" [delete: restrict]
Ref: "registration_attempts"."superseded_by_registration_attempt_id" > "registration_attempts"."id" [delete: restrict]
Ref: "registration_attempts"."status_id" > "registration_attempt_statuses"."id" [delete: restrict]
Ref: "registration_submissions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_submissions"."event_id" > "events"."id" [delete: restrict]
Ref: "registration_submissions"."registration_attempt_id" > "registration_attempts"."id" [delete: restrict]
Ref: "registration_submissions"."attempt_status_at_receipt_id" > "registration_attempt_statuses"."id" [delete: restrict]
Ref: "registration_submissions"."status_id" > "registration_submission_statuses"."id" [delete: restrict]
Ref: "registration_submission_revisions"."registration_submission_id" > "registration_submissions"."id" [delete: restrict]
Ref: "registration_submission_issues"."registration_submission_id" > "registration_submissions"."id" [delete: restrict]
Ref: "registration_answers"."registration_submission_id" > "registration_submissions"."id" [delete: restrict]
Ref: "registration_answers"."registration_requirement_id" > "registration_requirements"."id" [delete: restrict]
Ref: "registration_answers"."registration_form_field_id" > "registration_form_fields"."id" [delete: restrict]
Ref: "registration_answers"."selected_option_id" > "registration_form_field_options"."id" [delete: restrict]
Ref: "registration_answers"."participant_subject_id" > "registration_participants"."id" [delete: restrict]
Ref: "registration_answers"."ticket_assignment_subject_id" > "registration_ticket_assignments"."id" [delete: restrict]
Ref: "registration_answers"."answer_subject_type_id" > "registration_answer_subject_types"."id" [delete: restrict]
Ref: "registration_answers"."sensitive_answer_value_id" > "registration_sensitive_answer_values"."id" [delete: restrict]
Ref: "registration_consent_records"."registration_submission_id" > "registration_submissions"."id" [delete: restrict]
Ref: "registration_consent_records"."registration_requirement_id" > "registration_requirements"."id" [delete: restrict]
Ref: "registration_consent_records"."registration_form_field_id" > "registration_form_fields"."id" [delete: restrict]
Ref: "registration_consent_records"."participant_subject_id" > "registration_participants"."id" [delete: restrict]
Ref: "registration_consent_records"."ticket_assignment_subject_id" > "registration_ticket_assignments"."id" [delete: restrict]
Ref: "registration_consent_records"."answer_subject_type_id" > "registration_answer_subject_types"."id" [delete: restrict]
Ref: "registration_requirement_fulfillments"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_requirement_fulfillments"."registration_order_id" > "registration_orders"."id" [delete: restrict]
Ref: "registration_requirement_fulfillments"."registration_requirement_id" > "registration_requirements"."id" [delete: restrict]
Ref: "registration_requirement_fulfillments"."source_registration_submission_id" > "registration_submissions"."id" [delete: restrict]
Ref: "registration_requirement_fulfillments"."subject_type_id" > "registration_answer_subject_types"."id" [delete: restrict]
Ref: "registration_finalization_effects"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_finalization_effects"."registration_order_id" > "registration_orders"."id" [delete: restrict]
Ref: "registration_provider_submission_write_effects"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_provider_submission_write_effects".("tenant_id", "event_id", "registration_order_id") > "registration_orders".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "registration_answer_files"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_answer_files"."registration_submission_id" > "registration_submissions"."id" [delete: restrict]
Ref: "registration_answer_files"."registration_form_field_id" > "registration_form_fields"."id" [delete: restrict]
Ref: "registration_answer_files"."storage_object_id" > "storage_objects"."id" [delete: restrict]
Ref: "registration_answer_file_releases"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_answer_file_releases"."registration_answer_file_id" > "registration_answer_files"."id" [delete: restrict]
Ref: "registration_channels"."registration_provider_binding_id" > "registration_provider_bindings"."id" [delete: restrict]

Ref: "registration_requirements"."answer_sync_mode_id" > "registration_answer_sync_modes"."id" [delete: restrict]
Ref: "registration_requirements"."applies_to_subject_type_id" > "registration_requirement_subject_types"."id" [delete: restrict]
Ref: "participation_requirement_attachments".("tenant_id", "participation_configuration_id") > "event_participation_configurations".("tenant_id", "id") [delete: cascade]
Ref: "participation_requirement_attachments".("tenant_id", "event_id", "registration_workflow_id") > "registration_workflows".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "participation_requirement_attachments".("tenant_id", "event_id", "registration_workflow_id", "registration_requirement_id") > "registration_requirements".("tenant_id", "event_id", "registration_workflow_id", "id") [delete: restrict]
Ref: "participation_requirement_attachments".("tenant_id", "event_id", "registration_form_id", "registration_form_version_id") > "registration_form_versions".("tenant_id", "event_id", "registration_form_id", "id") [delete: restrict]
Ref: "registration_channels"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_channels".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "registration_channels".("tenant_id", "event_id", "registration_workflow_id", "registration_requirement_id") > "registration_requirements".("tenant_id", "event_id", "registration_workflow_id", "id") [delete: cascade]
Ref: "registration_forms"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_forms".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "registration_form_versions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_form_versions".("tenant_id", "event_id", "registration_form_id") > "registration_forms".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "registration_form_versions"."status_id" > "registration_form_statuses"."id" [delete: restrict]
Ref: "registration_form_sections"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_form_sections".("tenant_id", "event_id", "registration_form_id", "registration_form_version_id") > "registration_form_versions".("tenant_id", "event_id", "registration_form_id", "id") [delete: restrict]
Ref: "registration_form_fields"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_form_fields".("tenant_id", "event_id", "registration_form_id", "registration_form_version_id", "registration_form_section_id") > "registration_form_sections".("tenant_id", "event_id", "registration_form_id", "registration_form_version_id", "id") [delete: restrict]
Ref: "registration_form_fields"."field_type_id" > "registration_field_types"."id" [delete: restrict]
Ref: "registration_form_fields"."organizer_visibility_id" > "registration_organizer_visibilities"."id" [delete: restrict]
Ref: "registration_form_fields"."retention_policy_id" > "registration_retention_policies"."id" [delete: restrict]
Ref: "registration_form_field_options"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_form_field_options".("tenant_id", "event_id", "registration_form_id", "registration_form_version_id", "registration_form_section_id", "registration_form_field_id") > "registration_form_fields".("tenant_id", "event_id", "registration_form_id", "registration_form_version_id", "registration_form_section_id", "id") [delete: restrict]
Ref: "registration_form_rules"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "registration_form_rules".("tenant_id", "event_id", "registration_form_id", "registration_form_version_id") > "registration_form_versions".("tenant_id", "event_id", "registration_form_id", "id") [delete: restrict]
Ref: "registration_order_pii".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "registration_order_platform_contributions".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "platform_fee_fixed_charges"."platform_fee_policy_id" > "platform_fee_policies"."id" [delete: restrict]
Ref: "platform_contribution_options"."platform_contribution_setting_id" > "platform_contribution_settings"."id" [delete: restrict]
Ref: "event_public_actions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_public_actions".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "event_public_actions"."event_public_action_kind_id" > "event_public_action_kinds"."id" [delete: restrict]
Ref: "event_public_actions"."health_state_id" > "event_public_action_health_states"."id" [delete: restrict]
Ref: "event_organizer_claims"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_organizer_claims".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "event_organizer_claims"."claimant_actor_id" > "actors"."id" [delete: restrict]
Ref: "event_organizer_claims"."status_id" > "event_organizer_claim_statuses"."id" [delete: restrict]
Ref: "event_organizer_claims"."reviewer_user_id" > "users"."id" [delete: restrict]

// Event Extensions (1:1)
Ref: "event_islamic_aspects"."id" - "events"."id" [delete: cascade]
Ref: "event_tech_aspects"."id" - "events"."id" [delete: cascade]

// Event Structure
Ref: "event_days".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_agenda_items".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_agenda_items".("tenant_id", "event_id", "event_day_id") > "event_days".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "event_agenda_items".("tenant_id", "event_id", "event_location_id") > "event_locations".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "event_agenda_items".("tenant_id", "location_id") > "locations".("tenant_id", "id") [delete: restrict]
Ref: "event_agenda_items".("tenant_id", "location_id", "room_id") > "location_rooms".("tenant_id", "location_id", "id") [delete: restrict]
Ref: "event_agenda_items"."kind_id" > "schedule_item_kinds"."id" [delete: restrict]
Ref: "event_sessions".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_sessions".("tenant_id", "event_id", "event_day_id") > "event_days".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "event_sessions".("tenant_id", "event_id", "event_location_id") > "event_locations".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "event_sessions".("tenant_id", "location_id") > "locations".("tenant_id", "id") [delete: restrict]
Ref: "event_sessions".("tenant_id", "location_id", "room_id") > "location_rooms".("tenant_id", "location_id", "id") [delete: restrict]
Ref: "event_sessions"."event_session_status_id" > "event_session_statuses"."id" [delete: restrict]
Ref: "event_sessions"."registration_mode_id" > "registration_modes"."id" [delete: restrict]
Ref: "event_session_groups".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_session_groups".("tenant_id", "event_id", "event_location_id") > "event_locations".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "event_session_groups".("tenant_id", "location_id") > "locations".("tenant_id", "id") [delete: restrict]
Ref: "event_session_groups".("tenant_id", "location_id", "room_id") > "location_rooms".("tenant_id", "location_id", "id") [delete: restrict]
Ref: "event_session_group_sessions".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_session_group_sessions".("tenant_id", "event_id", "event_session_group_id") > "event_session_groups".("tenant_id", "event_id", "id") [delete: cascade]
Ref: "event_session_group_sessions".("tenant_id", "event_id", "event_session_id") > "event_sessions".("tenant_id", "event_id", "id") [delete: cascade]
Ref: "event_session_islamic_aspects"."event_session_id" - "event_sessions"."id" [delete: cascade]
Ref: "event_session_agenda_items".("tenant_id", "event_session_id") > "event_sessions".("tenant_id", "id") [delete: cascade]
Ref: "event_session_agenda_items".("tenant_id", "event_location_id") > "event_locations".("tenant_id", "id") [delete: restrict]
Ref: "event_session_agenda_items".("tenant_id", "location_id") > "locations".("tenant_id", "id") [delete: restrict]
Ref: "event_session_languages".("tenant_id", "event_session_id") > "event_sessions".("tenant_id", "id") [delete: cascade]
Ref: "event_session_languages"."language_id" > "languages"."id" [delete: restrict]
Ref: "event_session_speakers".("tenant_id", "event_session_id") > "event_sessions".("tenant_id", "id") [delete: cascade]
Ref: "event_session_speakers".("tenant_id", "actor_id") > "actors".("tenant_id", "id") [delete: cascade]
Ref: "event_categories".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_categories".("tenant_id", "category_id") > "categories".("tenant_id", "id") [delete: cascade]
Ref: "event_tags".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_tags".("tenant_id", "tag_id") > "tags".("tenant_id", "id") [delete: cascade]
Ref: "event_session_categories".("tenant_id", "event_session_id") > "event_sessions".("tenant_id", "id") [delete: cascade]
Ref: "event_session_categories".("tenant_id", "category_id") > "categories".("tenant_id", "id") [delete: cascade]
Ref: "event_session_tags".("tenant_id", "event_session_id") > "event_sessions".("tenant_id", "id") [delete: cascade]
Ref: "event_session_tags".("tenant_id", "tag_id") > "tags".("tenant_id", "id") [delete: cascade]

// Registration
Ref: "event_registrations".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: cascade]
Ref: "event_registrations".("tenant_id", "event_id", "event_session_id") > "event_sessions".("tenant_id", "event_id", "id") [delete: cascade]
Ref: "event_registrations"."linked_user_id" > "users"."id" [delete: restrict]
Ref: "event_registrations".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "event_registrations".("tenant_id", "registration_order_line_id") > "registration_order_lines".("tenant_id", "id") [delete: restrict]
Ref: "event_registrations".("tenant_id", "ticket_type_entitlement_id") > "ticket_type_entitlements".("tenant_id", "id") [delete: restrict]
Ref: "event_registrations".("tenant_id", "registration_order_id", "registration_participant_id") > "registration_participants".("tenant_id", "registration_order_id", "id") [delete: restrict]
Ref: "event_registrations"."atproto_record_id" > "atproto_records"."id" [delete: set null]

// Email Dispatch
Ref: "incoming_webhook_effect_outbox"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "incoming_webhook_effect_outbox".("tenant_id", "incoming_webhook_message_id") > "incoming_webhook_messages".("tenant_id", "id") [delete: restrict]

Ref: "email_dispatch_outbox"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "email_dispatch_outbox"."event_id" > "events"."id" [delete: restrict]
Ref: "email_dispatch_outbox".("tenant_id", "registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "email_dispatch_outbox".("tenant_id", "recipient_user_id") > "tenant_users".("tenant_id", "user_id") [delete: restrict]
Ref: "email_dispatch_outbox".("tenant_id", "notification_intent_id", "recipient_user_id") > "notification_intents".("tenant_id", "id", "recipient_user_id") [delete: restrict]
Ref: "email_dispatch_outbox"."managed_tenant_provisioning_operation_id" > "managed_tenant_provisioning_operations"."id" [delete: restrict]
Ref: "email_dispatch_outbox".("tenant_id", "managed_tenant_provisioning_operation_id") > "managed_tenant_provisioning_operations".("tenant_id", "id") [delete: restrict]
Ref: "email_dispatch_attempts"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "email_dispatch_attempts".("tenant_id", "email_dispatch_outbox_id") > "email_dispatch_outbox".("tenant_id", "id") [delete: cascade]
Ref: "email_dispatch_receipts"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "email_dispatch_receipts".("tenant_id", "email_dispatch_outbox_id", "publish_event_id") > "email_dispatch_outbox".("tenant_id", "id", "publish_event_id") [delete: cascade]
Ref: "email_dispatch_tenant_controls"."tenant_id" > "tenants"."id" [delete: restrict]

Ref: "notification_intents"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "notification_intents".("tenant_id", "recipient_user_id") > "tenant_users".("tenant_id", "user_id") [delete: restrict]
Ref: "notification_intents".("tenant_id", "fanout_occurrence_id") > "notification_fanout_occurrences".("tenant_id", "id") [delete: restrict]
Ref: "notification_intents"."category_id" > "notification_categories"."id" [delete: restrict]
Ref: "notification_intents"."ownership_type_id" > "notification_ownership_types"."id" [delete: restrict]
Ref: "notification_intents"."recipient_kind_id" > "notification_recipient_kinds"."id" [delete: restrict]
Ref: "notification_intents"."status_id" > "notification_intent_statuses"."id" [delete: restrict]
Ref: "notification_deliveries".("tenant_id", "notification_intent_id") > "notification_intents".("tenant_id", "id") [delete: restrict]
Ref: "notification_deliveries"."channel_id" > "notification_preference_channels"."id" [delete: restrict]
Ref: "notification_deliveries"."delivery_policy_id" > "notification_delivery_policies"."id" [delete: restrict]
Ref: "notification_deliveries"."status_id" > "notification_delivery_statuses"."id" [delete: restrict]
Ref: "notification_deliveries".("tenant_id", "email_dispatch_outbox_id", "notification_intent_id", "recipient_address_source") > "email_dispatch_outbox".("tenant_id", "id", "notification_intent_id", "recipient_address_source") [delete: restrict]
Ref: "notification_deliveries".("tenant_id", "notification_id") > "notifications".("tenant_id", "id") [delete: restrict]
Ref: "notification_deliveries".("tenant_id", "notification_id", "notification_intent_id") > "notifications".("tenant_id", "id", "notification_intent_id") [delete: restrict]
Ref: "notification_external_delegations".("tenant_id", "notification_intent_id") > "notification_intents".("tenant_id", "id") [delete: restrict]
Ref: "notification_external_delegations"."provider_kind_id" > "external_workflow_provider_kinds"."id" [delete: restrict]
Ref: "notification_external_delegations"."account_authority_kind_id" > "account_authority_kinds"."id" [delete: restrict]
Ref: "notification_external_delegations"."status_id" > "notification_external_delegation_statuses"."id" [delete: restrict]
Ref: "notification_external_delegations"."recipient_kind_id" > "notification_recipient_kinds"."id" [delete: restrict]
Ref: "notification_fanout_occurrences"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "notification_fanout_occurrences".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "notification_fanout_occurrences".("tenant_id", "session_id") > "event_sessions".("tenant_id", "id") [delete: restrict]
Ref: "notification_fanout_occurrences"."delivery_policy_id" > "notification_delivery_policies"."id" [delete: restrict]
Ref: "notification_fanout_occurrences".("tenant_id", "superseded_by_occurrence_id") > "notification_fanout_occurrences".("tenant_id", "id") [delete: restrict]

// Contact Share
Ref: "event_contact_share_consents"."subject_type_id" > "contact_share_consent_subject_types"."id" [delete: restrict]
Ref: "event_contact_share_consents"."user_subject_id" > "users"."id" [delete: restrict]
Ref: "event_contact_share_consents".("tenant_id", "registration_purchaser_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "event_contact_share_consents".("tenant_id", "guest_contact_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "event_contact_share_consents".("tenant_id", "registration_participant_id") > "registration_participants".("tenant_id", "id") [delete: restrict]
Ref: "event_contact_share_consents".("tenant_id", "recipient_actor_id") > "actors".("tenant_id", "id") [delete: restrict]
Ref: "event_contact_share_consent_history".("tenant_id", "consent_id") > "event_contact_share_consents".("tenant_id", "id") [delete: restrict]
Ref: "event_contact_share_consent_history".("tenant_id", "source_event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "event_contact_share_consent_history".("tenant_id", "source_registration_order_id") > "registration_orders".("tenant_id", "id") [delete: restrict]
Ref: "event_contact_share_consent_history"."actor_id" > "actors"."id" [delete: restrict]
Ref: "event_contact_share_consent_history"."user_id" > "users"."id" [delete: restrict]
Ref: "event_contact_share_exports".("tenant_id", "recipient_actor_id") > "actors".("tenant_id", "id") [delete: restrict]
Ref: "event_contact_share_exports".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "event_contact_share_exports"."exported_by_user_id" > "users"."id" [delete: restrict]
Ref: "event_contact_share_export_items"."export_id" > "event_contact_share_exports"."id" [delete: cascade]
Ref: "event_contact_share_export_items"."consent_id" > "event_contact_share_consents"."id" [delete: restrict]

// Event Reporting & Moderation Review
Ref: "event_reports"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_reports".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "event_reports"."reporter_user_id" > "users"."id" [delete: restrict]
Ref: "event_reports".("tenant_id", "reporter_actor_id") > "actors".("tenant_id", "id") [delete: restrict]
Ref: "event_report_cases"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_report_cases".("tenant_id", "report_id") > "event_reports".("tenant_id", "id") [delete: restrict]
Ref: "event_report_cases"."assigned_moderator_user_id" > "users"."id" [delete: restrict]
Ref: "event_report_evidence"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_report_evidence".("tenant_id", "report_id") > "event_reports".("tenant_id", "id") [delete: restrict]
Ref: "event_report_evidence".("tenant_id", "storage_object_id") > "storage_objects".("tenant_id", "id") [delete: restrict]
Ref: "event_report_evidence"."created_by_user_id" > "users"."id" [delete: restrict]
Ref: "event_report_targets"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_report_targets".("tenant_id", "report_id") > "event_reports".("tenant_id", "id") [delete: restrict]
Ref: "event_report_targets".("tenant_id", "storage_object_id") > "storage_objects".("tenant_id", "id") [delete: restrict]
Ref: "event_report_signals"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_report_signals".("tenant_id", "event_id") > "events".("tenant_id", "id") [delete: restrict]
Ref: "event_report_signals".("tenant_id", "event_id", "report_id") > "event_reports".("tenant_id", "event_id", "id") [delete: restrict]
Ref: "event_report_decisions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_report_decisions".("tenant_id", "report_id") > "event_reports".("tenant_id", "id") [delete: restrict]
Ref: "event_report_decisions".("tenant_id", "report_id", "case_id") > "event_report_cases".("tenant_id", "report_id", "id") [delete: restrict]
Ref: "event_report_decisions"."moderator_user_id" > "users"."id" [delete: restrict]
Ref: "event_report_decision_executions"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_report_decision_executions".("tenant_id", "report_id") > "event_reports".("tenant_id", "id") [delete: restrict]
Ref: "event_report_decision_executions".("tenant_id", "report_id", "decision_id") > "event_report_decisions".("tenant_id", "report_id", "id") [delete: restrict]
Ref: "event_report_external_links"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "event_report_external_links".("tenant_id", "report_id") > "event_reports".("tenant_id", "id") [delete: restrict]
Ref: "event_report_external_links".("tenant_id", "report_id", "case_id") > "event_report_cases".("tenant_id", "report_id", "id") [delete: restrict]

// Custom Properties (EAV)
Ref: "custom_property_definitions"."default_option_id" > "custom_property_options"."id" [delete: cascade]
Ref: "custom_property_options"."custom_property_definition_id" > "custom_property_definitions"."id" [delete: cascade]
Ref: "custom_property_options"."parent_option_id" > "custom_property_options"."id" [delete: cascade]
Ref: "custom_property_values"."custom_property_definition_id" > "custom_property_definitions"."id" [delete: restrict]
Ref: "custom_property_values"."option_id" > "custom_property_options"."id" [delete: cascade]

// Event EAV
Ref: "event_template_custom_property_definitions"."event_template_id" > "event_templates"."id" [delete: cascade]
Ref: "event_template_custom_property_definitions"."default_option_id" > "event_template_custom_property_options"."id" [delete: cascade]
Ref: "event_template_custom_property_options"."event_template_custom_property_definition_id" > "event_template_custom_property_definitions"."id" [delete: cascade]
Ref: "event_template_custom_property_options"."parent_option_id" > "event_template_custom_property_options"."id" [delete: cascade]
Ref: "event_custom_property_definitions"."event_id" > "events"."id" [delete: cascade]
Ref: "event_custom_property_definitions"."source_template_id" > "event_templates"."id" [delete: restrict]
Ref: "event_custom_property_definitions"."default_option_id" > "event_custom_property_options"."id" [delete: cascade]
Ref: "event_custom_property_options"."event_custom_property_definition_id" > "event_custom_property_definitions"."id" [delete: cascade]
Ref: "event_custom_property_options"."parent_option_id" > "event_custom_property_options"."id" [delete: cascade]
Ref: "event_custom_property_values"."event_id" > "events"."id" [delete: cascade]
Ref: "event_custom_property_values"."event_custom_property_definition_id" > "event_custom_property_definitions"."id" [delete: restrict]
Ref: "event_custom_property_values"."option_id" > "event_custom_property_options"."id" [delete: cascade]
Ref: "event_custom_property_projections"."event_id" > "events"."id" [delete: cascade]
Ref: "event_custom_property_projections"."event_custom_property_definition_id" > "event_custom_property_definitions"."id" [delete: cascade]
Ref: "event_custom_property_projections"."event_custom_property_value_id" - "event_custom_property_values"."id" [delete: cascade]

// Event Session EAV
Ref: "event_session_templates"."event_template_id" > "event_templates"."id" [delete: cascade]
Ref: "event_session_template_custom_property_definitions"."event_session_template_id" > "event_session_templates"."id" [delete: cascade]
Ref: "event_session_template_custom_property_definitions"."default_option_id" > "event_session_template_custom_property_options"."id" [delete: cascade]
Ref: "event_session_template_custom_property_options"."event_session_template_custom_property_definition_id" > "event_session_template_custom_property_definitions"."id" [delete: cascade]
Ref: "event_session_template_custom_property_options"."parent_option_id" > "event_session_template_custom_property_options"."id" [delete: cascade]
Ref: "event_session_custom_property_definitions"."event_session_id" > "event_sessions"."id" [delete: cascade]
Ref: "event_session_custom_property_definitions"."default_option_id" > "event_session_custom_property_options"."id" [delete: cascade]
Ref: "event_session_custom_property_options"."event_session_custom_property_definition_id" > "event_session_custom_property_definitions"."id" [delete: cascade]
Ref: "event_session_custom_property_options"."parent_option_id" > "event_session_custom_property_options"."id" [delete: cascade]
Ref: "event_session_custom_property_values"."event_session_id" > "event_sessions"."id" [delete: cascade]
Ref: "event_session_custom_property_values"."event_session_custom_property_definition_id" > "event_session_custom_property_definitions"."id" [delete: restrict]
Ref: "event_session_custom_property_values"."option_id" > "event_session_custom_property_options"."id" [delete: cascade]
Ref: "event_session_custom_property_projections"."event_session_id" > "event_sessions"."id" [delete: cascade]
Ref: "event_session_custom_property_projections"."event_session_custom_property_definition_id" > "event_session_custom_property_definitions"."id" [delete: cascade]
Ref: "event_session_custom_property_projections"."event_session_custom_property_value_id" - "event_session_custom_property_values"."id" [delete: cascade]

// Notifications
Ref: "notifications"."notification_type_id" > "notification_types"."id" [delete: restrict]
Ref: "notifications"."notification_reason_id" > "notification_reasons"."id" [delete: restrict]
Ref: "notifications"."notification_entity_type_id" > "notification_entity_types"."id" [delete: restrict]
Ref: "notifications"."notification_scope_id" > "notification_scope_types"."id" [delete: restrict]
Ref: "notifications"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "notifications"."user_id" > "users"."id" [delete: cascade]
Ref: "notifications".("tenant_id", "notification_intent_id", "user_id") > "notification_intents".("tenant_id", "id", "recipient_user_id") [delete: restrict]
Ref: "notifications"."source_actor_id" > "actors"."id" [delete: set null]
Ref: "notifications"."recipient_context_actor_id" > "actors"."id" [delete: set null]
Ref: "notification_fanout_runs"."tenant_id" > "tenants"."id" [delete: restrict]
Ref: "notification_fanout_runs"."notification_entity_type_id" > "notification_entity_types"."id" [delete: restrict]
Ref: "notification_fanout_runs".("tenant_id", "source_actor_id") > "actors".("tenant_id", "id") [delete: restrict]
Ref: "notification_fanout_runs".("tenant_id", "fanout_occurrence_id") > "notification_fanout_occurrences".("tenant_id", "id") [delete: restrict]

// API Keys
Ref: "external_api_keys"."external_api_key_owner_type_id" > "external_api_key_owner_types"."id" [delete: restrict]
Ref: "external_api_keys"."external_api_key_status_id" > "external_api_key_statuses"."id" [delete: restrict]
Ref: "external_api_keys"."external_api_key_credit_period_id" > "external_api_key_credit_periods"."id" [delete: restrict]
Ref: "external_api_key_quotas"."external_api_key_id" > "external_api_keys"."id" [delete: cascade]

// Configuration & Secrets
Ref: "system_settings"."setting_value_type_id" > "setting_value_types"."id" [delete: restrict]
Ref: "configuration_change_logs"."setting_scope_id" > "setting_scopes"."id" [delete: restrict]
Ref: "secret_bindings"."setting_scope_id" > "setting_scopes"."id" [delete: restrict]
Ref: "secret_bindings"."secret_source_type_id" > "secret_source_types"."id" [delete: restrict]
Ref: "secret_bindings"."secret_validation_status_id" > "secret_validation_statuses"."id" [delete: restrict]
Ref: "payment_reconciliation_effects".("tenant_id", "checkout_dispatch_effect_id") > "checkout_dispatch_effects".("tenant_id", "id") [delete: restrict]
