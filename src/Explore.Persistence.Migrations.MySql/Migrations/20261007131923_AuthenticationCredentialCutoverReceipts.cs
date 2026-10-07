using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AuthenticationCredentialCutoverReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_authentication_credential_cutover_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    instance_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    deadline = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    deployment_revision = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    authority = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    authority_profile_revision = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    state = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    required_replacements = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    activations = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fence = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    cleaned_setting_count = table.Column<int>(type: "int", nullable: true),
                    cleaned_binding_count = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_authentication_credential_cutover_receipts", x => x.id);
                    table.UniqueConstraint("ak_authentication_credential_cutover_receipts_instance_id_id", x => new { x.instance_id, x.id });
                    table.CheckConstraint("ck_authenticationcutover_authority", "authority IN ('Environment','Infisical','UserSecrets')");
                    table.CheckConstraint("ck_authenticationcutover_counts", "(cleaned_setting_count IS NULL OR cleaned_setting_count >= 0) AND (cleaned_binding_count IS NULL OR cleaned_binding_count >= 0)");
                    table.CheckConstraint("ck_authenticationcutover_state", "state IN ('Prepared','Collecting','ReadyForCleanup','Cleaned','Failed','Expired','Superseded')");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_authentication_credential_cutover_heads",
                columns: table => new
                {
                    instance_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    current_operation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_authentication_credential_cutover_heads", x => x.instance_id);
                    table.ForeignKey(
                        name: "fk_ie_authentication_credential_cutover_heads_ie_authen_1c172cd6",
                        columns: x => new { x.instance_id, x.current_operation_id },
                        principalTable: "ie_authentication_credential_cutover_receipts",
                        principalColumns: new[] { "instance_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_authentication_credential_cutover_heads_instance__676f8f64",
                table: "ie_authentication_credential_cutover_heads",
                columns: new[] { "instance_id", "current_operation_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_authentication_credential_cutover_heads");

            migrationBuilder.DropTable(
                name: "ie_authentication_credential_cutover_receipts");
        }
    }
}
