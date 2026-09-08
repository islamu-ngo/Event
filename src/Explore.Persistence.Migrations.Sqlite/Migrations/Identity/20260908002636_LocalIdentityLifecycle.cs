using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations.Identity
{
    /// <inheritdoc />
    public partial class LocalIdentityLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_local_identity_lifecycle_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    local_subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    personal_actor_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    external_login_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    generation = table.Column<Guid>(type: "TEXT", nullable: false),
                    credential_operation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    pending_address = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    security_stamp = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    result_security_stamp = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    synchronized_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    delivery_state = table.Column<int>(type: "INTEGER", nullable: false),
                    delivery_attempt_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    delivery_attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                    delivery_admitted_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    delivery_completed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
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
                        name: "fk_local_identity_lifecycle_operations_local_identity_users_local_subject_id",
                        column: x => x.local_subject_id,
                        principalTable: "ie_local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_lifecycle_operations_local_subject_id_purpose_expires_at",
                table: "ie_local_identity_lifecycle_operations",
                columns: new[] { "local_subject_id", "purpose", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_local_identity_lifecycle_operations");
        }
    }
}
