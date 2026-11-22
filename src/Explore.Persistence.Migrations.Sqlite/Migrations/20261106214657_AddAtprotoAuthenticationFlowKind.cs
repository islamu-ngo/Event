using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAtprotoAuthenticationFlowKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_atproto_transients_tenant_purpose",
                table: "ie_atproto_transient_records");

            migrationBuilder.AddColumn<int>(
                name: "flow_kind",
                table: "ie_atproto_transient_records",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_atproto_transients_tenant_purpose",
                table: "ie_atproto_transient_records",
                sql: "(purpose = 3 AND tenant_id IS NULL AND flow_kind IS NULL) OR (purpose IN (1, 2) AND tenant_id IS NOT NULL AND flow_kind IS NOT NULL AND flow_kind = 1) OR (purpose = 1 AND tenant_id IS NULL AND flow_kind IS NOT NULL AND flow_kind = 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_atproto_transients_tenant_purpose",
                table: "ie_atproto_transient_records");

            migrationBuilder.DropColumn(
                name: "flow_kind",
                table: "ie_atproto_transient_records");

            migrationBuilder.AddCheckConstraint(
                name: "ck_atproto_transients_tenant_purpose",
                table: "ie_atproto_transient_records",
                sql: "(purpose = 3 AND tenant_id IS NULL) OR (purpose IN (1, 2) AND tenant_id IS NOT NULL)");
        }
    }
}
