using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmailOptionalSelfHostingIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_event",
                table: "local_identity_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                schema: "islamu_event",
                table: "instance_bootstrap_states");

            migrationBuilder.AddColumn<DateTime>(
                name: "registration_content_retention_until_utc",
                schema: "islamu_event",
                table: "storage_objects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "anonymous_pii_retention_until_utc",
                schema: "islamu_event",
                table: "registration_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "guest_status_access_until_utc",
                schema: "islamu_event",
                table: "registration_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "email_delivery_policy_revision",
                schema: "islamu_event",
                table: "notification_intents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "email_delivery_policy_revision",
                schema: "islamu_event",
                table: "notification_fanout_occurrences",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "delivery_policy_revision",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "optional_suppressed_through_revision",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "optional_suppressed_through_utc",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "delivery_policy_revision",
                schema: "islamu_event",
                table: "email_dispatch_processor_states",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "optional_suppressed_through_revision",
                schema: "islamu_event",
                table: "email_dispatch_processor_states",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "optional_suppressed_through_utc",
                schema: "islamu_event",
                table: "email_dispatch_processor_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "park_reason",
                schema: "islamu_event",
                table: "email_dispatch_outbox",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "anonymous_challenge_event_quotas",
                schema: "islamu_event",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    window_minute = table.Column<long>(type: "bigint", nullable: false),
                    issued = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anonymous_challenge_event_quotas", x => new { x.tenant_id, x.event_id });
                    table.CheckConstraint("ck_anon_challenge_event_count", "issued >= 0 AND issued <= 10000");
                    table.CheckConstraint("ck_anon_challenge_event_minute", "window_minute >= 0");
                    table.ForeignKey(
                        name: "fk_anonymous_challenge_event_quotas_events_tenant_id_event_id",
                        columns: x => new { x.tenant_id, x.event_id },
                        principalSchema: "islamu_event",
                        principalTable: "events",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "anonymous_challenge_tenant_quotas",
                schema: "islamu_event",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    window_minute = table.Column<long>(type: "bigint", nullable: false),
                    issued = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anonymous_challenge_tenant_quotas", x => x.tenant_id);
                    table.CheckConstraint("ck_anon_challenge_tenant_count", "issued >= 0 AND issued <= 10000");
                    table.CheckConstraint("ck_anon_challenge_tenant_minute", "window_minute >= 0");
                    table.ForeignKey(
                        name: "fk_anonymous_challenge_tenant_quotas_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "local_identity_credential_operation",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    initiating_application_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personal_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_login_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_operation_concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    reset_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    verified_by_application_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_identity_credential_operation", x => x.id);
                    table.CheckConstraint("ck_local_credential_operation_kind", "kind BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_local_credential_operation_reset_metadata", "(kind = 1 AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL) OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL AND TRIM(reset_reason) <> '' AND stage <> 1)");
                    table.CheckConstraint("ck_local_credential_operation_stage", "stage BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_local_credential_operation_timestamps", "((kind = 1 AND verified_at >= created_at) OR (kind = 2 AND verified_at <= created_at)) AND (updated_at IS NULL OR updated_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_local_identity_credential_operation_local_ident_082ba06f3f19",
                        column: x => x.local_subject_id,
                        principalSchema: "islamu_event",
                        principalTable: "local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "local_identity_lifecycle_operations",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personal_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_login_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pending_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    result_security_stamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    synchronized_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivery_state = table.Column<int>(type: "integer", nullable: false),
                    delivery_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    delivery_admitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivery_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_identity_lifecycle_operations", x => x.id);
                    table.CheckConstraint("ck_local_lifecycle_consumption", "(consumed_at IS NULL AND result_security_stamp IS NULL AND synchronized_at IS NULL) OR (consumed_at IS NOT NULL AND result_security_stamp IS NOT NULL AND consumed_at >= created_at AND consumed_at < expires_at AND (synchronized_at IS NULL OR synchronized_at >= consumed_at))");
                    table.CheckConstraint("ck_local_lifecycle_delivery_attempt", "(delivery_attempt_count = 0 AND delivery_attempt_id IS NULL AND delivery_admitted_at IS NULL AND delivery_completed_at IS NULL AND delivery_state IN (0,3)) OR (delivery_attempt_count > 0 AND delivery_attempt_id IS NOT NULL AND delivery_admitted_at IS NOT NULL AND delivery_admitted_at >= created_at AND delivery_admitted_at < expires_at AND (delivery_completed_at IS NULL OR delivery_completed_at >= delivery_admitted_at) AND (delivery_state <> 1 OR delivery_completed_at IS NULL) AND (delivery_state <> 2 OR delivery_completed_at IS NOT NULL))");
                    table.CheckConstraint("ck_local_lifecycle_delivery_state", "delivery_state BETWEEN 0 AND 3 AND delivery_attempt_count BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_local_lifecycle_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_local_lifecycle_purpose", "purpose BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_local_identity_lifecycle_operations_local_ident_9863bfa80451",
                        column: x => x.local_subject_id,
                        principalSchema: "islamu_event",
                        principalTable: "local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_intents_email_policy_revision_nonnegative",
                schema: "islamu_event",
                table: "notification_intents",
                sql: "email_delivery_policy_revision >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_fanout_occurrences_email_revision",
                schema: "islamu_event",
                table: "notification_fanout_occurrences",
                sql: "email_delivery_policy_revision >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_event",
                table: "local_identity_users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                schema: "islamu_event",
                table: "instance_bootstrap_states",
                sql: "provider_kind IS NULL OR provider_kind IN (1, 2, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_tenant_controls_revision_nonnegative",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls",
                sql: "delivery_policy_revision >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_tenant_controls_suppression_revision",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls",
                sql: "optional_suppressed_through_revision IS NULL OR optional_suppressed_through_revision BETWEEN 0 AND delivery_policy_revision");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_processor_states_revision_nonnegative",
                schema: "islamu_event",
                table: "email_dispatch_processor_states",
                sql: "delivery_policy_revision >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_processor_states_suppression_revision",
                schema: "islamu_event",
                table: "email_dispatch_processor_states",
                sql: "optional_suppressed_through_revision IS NULL OR optional_suppressed_through_revision BETWEEN 0 AND delivery_policy_revision");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_outbox_park_reason",
                schema: "islamu_event",
                table: "email_dispatch_outbox",
                sql: "park_reason IS NULL OR park_reason IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_credential_operation_local_subject_id",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                column: "local_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_lifecycle_operations_local_subje_40075b197509",
                schema: "islamu_event",
                table: "local_identity_lifecycle_operations",
                columns: new[] { "local_subject_id", "purpose", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anonymous_challenge_event_quotas",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "anonymous_challenge_tenant_quotas",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "local_identity_credential_operation",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "local_identity_lifecycle_operations",
                schema: "islamu_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_intents_email_policy_revision_nonnegative",
                schema: "islamu_event",
                table: "notification_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_fanout_occurrences_email_revision",
                schema: "islamu_event",
                table: "notification_fanout_occurrences");

            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_event",
                table: "local_identity_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                schema: "islamu_event",
                table: "instance_bootstrap_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_tenant_controls_revision_nonnegative",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_tenant_controls_suppression_revision",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_processor_states_revision_nonnegative",
                schema: "islamu_event",
                table: "email_dispatch_processor_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_processor_states_suppression_revision",
                schema: "islamu_event",
                table: "email_dispatch_processor_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_outbox_park_reason",
                schema: "islamu_event",
                table: "email_dispatch_outbox");

            migrationBuilder.DropColumn(
                name: "registration_content_retention_until_utc",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropColumn(
                name: "anonymous_pii_retention_until_utc",
                schema: "islamu_event",
                table: "registration_orders");

            migrationBuilder.DropColumn(
                name: "guest_status_access_until_utc",
                schema: "islamu_event",
                table: "registration_orders");

            migrationBuilder.DropColumn(
                name: "email_delivery_policy_revision",
                schema: "islamu_event",
                table: "notification_intents");

            migrationBuilder.DropColumn(
                name: "email_delivery_policy_revision",
                schema: "islamu_event",
                table: "notification_fanout_occurrences");

            migrationBuilder.DropColumn(
                name: "delivery_policy_revision",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls");

            migrationBuilder.DropColumn(
                name: "optional_suppressed_through_revision",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls");

            migrationBuilder.DropColumn(
                name: "optional_suppressed_through_utc",
                schema: "islamu_event",
                table: "email_dispatch_tenant_controls");

            migrationBuilder.DropColumn(
                name: "delivery_policy_revision",
                schema: "islamu_event",
                table: "email_dispatch_processor_states");

            migrationBuilder.DropColumn(
                name: "optional_suppressed_through_revision",
                schema: "islamu_event",
                table: "email_dispatch_processor_states");

            migrationBuilder.DropColumn(
                name: "optional_suppressed_through_utc",
                schema: "islamu_event",
                table: "email_dispatch_processor_states");

            migrationBuilder.DropColumn(
                name: "park_reason",
                schema: "islamu_event",
                table: "email_dispatch_outbox");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_event",
                table: "local_identity_users",
                column: "normalized_email");

            migrationBuilder.AddCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                schema: "islamu_event",
                table: "instance_bootstrap_states",
                sql: "provider_kind IS NULL OR provider_kind BETWEEN 1 AND 2");
        }
    }
}
