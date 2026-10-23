using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deployment_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    authority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    authority_profile_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    required_replacements = table.Column<string>(type: "text", nullable: false),
                    activations = table.Column<string>(type: "text", nullable: false),
                    fence = table.Column<string>(type: "text", nullable: true),
                    cleaned_setting_count = table.Column<int>(type: "integer", nullable: true),
                    cleaned_binding_count = table.Column<int>(type: "integer", nullable: true)
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
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authentication_credential_cutover_heads", x => x.instance_id);
                    table.ForeignKey(
                        name: "fk_authentication_credential_cutover_heads_authent_6202fed73daa",
                        columns: x => new { x.instance_id, x.current_operation_id },
                        principalSchema: "islamu_event",
                        principalTable: "authentication_credential_cutover_receipts",
                        principalColumns: new[] { "instance_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_authentication_credential_cutover_heads_instanc_ef2fabeb7905",
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
