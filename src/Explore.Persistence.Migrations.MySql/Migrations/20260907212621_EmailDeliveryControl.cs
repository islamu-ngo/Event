using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class EmailDeliveryControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_intents_email_policy_revision_nonnegative",
                table: "ie_notification_intents",
                sql: "email_delivery_policy_revision >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_fanout_occurrences_email_revision",
                table: "ie_notification_fanout_occurrences",
                sql: "email_delivery_policy_revision >= 0");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_intents_email_policy_revision_nonnegative",
                table: "ie_notification_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_fanout_occurrences_email_revision",
                table: "ie_notification_fanout_occurrences");

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
        }
    }
}
