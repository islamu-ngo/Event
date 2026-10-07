using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AuthenticationCredentialCutoverReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authentication_credential_cutover_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deadline = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deployment_revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    authority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    authority_profile_revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    required_replacements = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    activations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    fence = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    cleaned_setting_count = table.Column<int>(type: "int", nullable: true),
                    cleaned_binding_count = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authentication_credential_cutover_receipts", x => x.id);
                    table.UniqueConstraint("ak_authentication_credential_cutover_receipts_instance_id_id", x => new { x.instance_id, x.id });
                    table.CheckConstraint("ck_authenticationcutover_authority", "authority IN ('Environment','Infisical','UserSecrets')");
                    table.CheckConstraint("ck_authenticationcutover_counts", "(cleaned_setting_count IS NULL OR cleaned_setting_count >= 0) AND (cleaned_binding_count IS NULL OR cleaned_binding_count >= 0)");
                    table.CheckConstraint("ck_authenticationcutover_state", "state IN ('Prepared','Collecting','ReadyForCleanup','Cleaned','Failed','Expired','Superseded')");
                });

            migrationBuilder.CreateTable(
                name: "authentication_credential_cutover_heads",
                schema: "islamu_event",
                columns: table => new
                {
                    instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    current_operation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authentication_credential_cutover_heads", x => x.instance_id);
                    table.ForeignKey(
                        name: "fk_authentication_credential_cutover_heads_authentication_credential_cutover_receipts_instance_id_current_operation_id",
                        columns: x => new { x.instance_id, x.current_operation_id },
                        principalSchema: "islamu_event",
                        principalTable: "authentication_credential_cutover_receipts",
                        principalColumns: new[] { "instance_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_authentication_credential_cutover_heads_instance_id_current_operation_id",
                schema: "islamu_event",
                table: "authentication_credential_cutover_heads",
                columns: new[] { "instance_id", "current_operation_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authentication_credential_cutover_heads",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "authentication_credential_cutover_receipts",
                schema: "islamu_event");
        }
    }
}
