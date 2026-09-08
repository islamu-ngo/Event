using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class EmailOptionalSelfHostingIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                table: "ie_local_identity_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                table: "ie_instance_bootstrap_states");

            migrationBuilder.AddColumn<DateTime>(
                name: "registration_content_retention_until_utc",
                table: "ie_storage_objects",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "anonymous_pii_retention_until_utc",
                table: "ie_registration_orders",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "guest_status_access_until_utc",
                table: "ie_registration_orders",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "email_delivery_policy_revision",
                table: "ie_notification_intents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "email_delivery_policy_revision",
                table: "ie_notification_fanout_occurrences",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "delivery_policy_revision",
                table: "ie_email_dispatch_tenant_controls",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "optional_suppressed_through_revision",
                table: "ie_email_dispatch_tenant_controls",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "optional_suppressed_through_utc",
                table: "ie_email_dispatch_tenant_controls",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "delivery_policy_revision",
                table: "ie_email_dispatch_processor_states",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "optional_suppressed_through_revision",
                table: "ie_email_dispatch_processor_states",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "optional_suppressed_through_utc",
                table: "ie_email_dispatch_processor_states",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "park_reason",
                table: "ie_email_dispatch_outbox",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ie_anonymous_challenge_event_quotas",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    window_minute = table.Column<long>(type: "bigint", nullable: false),
                    issued = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_anonymous_challenge_event_quotas", x => new { x.tenant_id, x.event_id });
                    table.CheckConstraint("ck_anon_challenge_event_count", "issued >= 0 AND issued <= 10000");
                    table.CheckConstraint("ck_anon_challenge_event_minute", "window_minute >= 0");
                    table.ForeignKey(
                        name: "fk_ie_anonymous_challenge_event_quotas_ie_events_tenant_10cb0b0f",
                        columns: x => new { x.tenant_id, x.event_id },
                        principalTable: "ie_events",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_anonymous_challenge_tenant_quotas",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    window_minute = table.Column<long>(type: "bigint", nullable: false),
                    issued = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_anonymous_challenge_tenant_quotas", x => x.tenant_id);
                    table.CheckConstraint("ck_anon_challenge_tenant_count", "issued >= 0 AND issued <= 10000");
                    table.CheckConstraint("ck_anon_challenge_tenant_minute", "window_minute >= 0");
                    table.ForeignKey(
                        name: "fk_anonymous_challenge_tenant_quotas_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_local_identity_credential_operation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    kind = table.Column<int>(type: "int", nullable: false),
                    stage = table.Column<int>(type: "int", nullable: false),
                    initiating_application_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    local_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    personal_actor_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    external_login_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    previous_operation_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    previous_operation_concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reset_reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    verified_by_application_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    verified_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_local_identity_credential_operation", x => x.id);
                    table.CheckConstraint("ck_local_credential_operation_kind", "kind BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_local_credential_operation_reset_metadata", "(kind = 1 AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL) OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL AND TRIM(reset_reason) <> '' AND stage <> 1)");
                    table.CheckConstraint("ck_local_credential_operation_stage", "stage BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_local_credential_operation_timestamps", "((kind = 1 AND verified_at >= created_at) OR (kind = 2 AND verified_at <= created_at)) AND (updated_at IS NULL OR updated_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_ie_local_identity_credential_operation_ie_local_iden_1288010b",
                        column: x => x.local_subject_id,
                        principalTable: "ie_local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_local_identity_lifecycle_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    local_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    personal_actor_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    external_login_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    purpose = table.Column<int>(type: "int", nullable: false),
                    generation = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    credential_operation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pending_address = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    security_stamp = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    result_security_stamp = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    synchronized_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    delivery_state = table.Column<int>(type: "int", nullable: false),
                    delivery_attempt_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    delivery_attempt_count = table.Column<int>(type: "int", nullable: false),
                    delivery_admitted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    delivery_completed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_local_identity_lifecycle_operations", x => x.id);
                    table.CheckConstraint("ck_local_lifecycle_consumption", "(consumed_at IS NULL AND result_security_stamp IS NULL AND synchronized_at IS NULL) OR (consumed_at IS NOT NULL AND result_security_stamp IS NOT NULL AND consumed_at >= created_at AND consumed_at < expires_at AND (synchronized_at IS NULL OR synchronized_at >= consumed_at))");
                    table.CheckConstraint("ck_local_lifecycle_delivery_attempt", "(delivery_attempt_count = 0 AND delivery_attempt_id IS NULL AND delivery_admitted_at IS NULL AND delivery_completed_at IS NULL AND delivery_state IN (0,3)) OR (delivery_attempt_count > 0 AND delivery_attempt_id IS NOT NULL AND delivery_admitted_at IS NOT NULL AND delivery_admitted_at >= created_at AND delivery_admitted_at < expires_at AND (delivery_completed_at IS NULL OR delivery_completed_at >= delivery_admitted_at) AND (delivery_state <> 1 OR delivery_completed_at IS NULL) AND (delivery_state <> 2 OR delivery_completed_at IS NOT NULL))");
                    table.CheckConstraint("ck_local_lifecycle_delivery_state", "delivery_state BETWEEN 0 AND 3 AND delivery_attempt_count BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_local_lifecycle_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_local_lifecycle_purpose", "purpose BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_ie_local_identity_lifecycle_operations_ie_local_iden_466d171c",
                        column: x => x.local_subject_id,
                        principalTable: "ie_local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_intents_email_policy_revision_nonnegative",
                table: "ie_notification_intents",
                sql: "email_delivery_policy_revision >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_fanout_occurrences_email_revision",
                table: "ie_notification_fanout_occurrences",
                sql: "email_delivery_policy_revision >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                table: "ie_local_identity_users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                table: "ie_instance_bootstrap_states",
                sql: "provider_kind IS NULL OR provider_kind IN (1, 2, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_tenant_controls_revision_nonnegative",
                table: "ie_email_dispatch_tenant_controls",
                sql: "delivery_policy_revision >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_tenant_controls_suppression_revision",
                table: "ie_email_dispatch_tenant_controls",
                sql: "optional_suppressed_through_revision IS NULL OR optional_suppressed_through_revision BETWEEN 0 AND delivery_policy_revision");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_processor_states_revision_nonnegative",
                table: "ie_email_dispatch_processor_states",
                sql: "delivery_policy_revision >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_processor_states_suppression_revision",
                table: "ie_email_dispatch_processor_states",
                sql: "optional_suppressed_through_revision IS NULL OR optional_suppressed_through_revision BETWEEN 0 AND delivery_policy_revision");

            migrationBuilder.AddCheckConstraint(
                name: "ck_email_dispatch_outbox_park_reason",
                table: "ie_email_dispatch_outbox",
                sql: "park_reason IS NULL OR park_reason IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_credential_operation_local_subject_id",
                table: "ie_local_identity_credential_operation",
                column: "local_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_local_identity_lifecycle_operations_local_subject_1510baf4",
                table: "ie_local_identity_lifecycle_operations",
                columns: new[] { "local_subject_id", "purpose", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_anonymous_challenge_event_quotas");

            migrationBuilder.DropTable(
                name: "ie_anonymous_challenge_tenant_quotas");

            migrationBuilder.DropTable(
                name: "ie_local_identity_credential_operation");

            migrationBuilder.DropTable(
                name: "ie_local_identity_lifecycle_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_intents_email_policy_revision_nonnegative",
                table: "ie_notification_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_fanout_occurrences_email_revision",
                table: "ie_notification_fanout_occurrences");

            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                table: "ie_local_identity_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                table: "ie_instance_bootstrap_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_tenant_controls_revision_nonnegative",
                table: "ie_email_dispatch_tenant_controls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_tenant_controls_suppression_revision",
                table: "ie_email_dispatch_tenant_controls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_processor_states_revision_nonnegative",
                table: "ie_email_dispatch_processor_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_processor_states_suppression_revision",
                table: "ie_email_dispatch_processor_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_email_dispatch_outbox_park_reason",
                table: "ie_email_dispatch_outbox");

            migrationBuilder.DropColumn(
                name: "registration_content_retention_until_utc",
                table: "ie_storage_objects");

            migrationBuilder.DropColumn(
                name: "anonymous_pii_retention_until_utc",
                table: "ie_registration_orders");

            migrationBuilder.DropColumn(
                name: "guest_status_access_until_utc",
                table: "ie_registration_orders");

            migrationBuilder.DropColumn(
                name: "email_delivery_policy_revision",
                table: "ie_notification_intents");

            migrationBuilder.DropColumn(
                name: "email_delivery_policy_revision",
                table: "ie_notification_fanout_occurrences");

            migrationBuilder.DropColumn(
                name: "delivery_policy_revision",
                table: "ie_email_dispatch_tenant_controls");

            migrationBuilder.DropColumn(
                name: "optional_suppressed_through_revision",
                table: "ie_email_dispatch_tenant_controls");

            migrationBuilder.DropColumn(
                name: "optional_suppressed_through_utc",
                table: "ie_email_dispatch_tenant_controls");

            migrationBuilder.DropColumn(
                name: "delivery_policy_revision",
                table: "ie_email_dispatch_processor_states");

            migrationBuilder.DropColumn(
                name: "optional_suppressed_through_revision",
                table: "ie_email_dispatch_processor_states");

            migrationBuilder.DropColumn(
                name: "optional_suppressed_through_utc",
                table: "ie_email_dispatch_processor_states");

            migrationBuilder.DropColumn(
                name: "park_reason",
                table: "ie_email_dispatch_outbox");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                table: "ie_local_identity_users",
                column: "normalized_email");

            migrationBuilder.AddCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                table: "ie_instance_bootstrap_states",
                sql: "provider_kind IS NULL OR provider_kind BETWEEN 1 AND 2");
        }
    }
}
