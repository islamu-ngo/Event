using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationMachineSubjectBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_moderation_machine_subject_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    credential_source = table.Column<int>(type: "int", nullable: false),
                    key_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    authority_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    owner_type = table.Column<int>(type: "int", nullable: false),
                    owner_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    service_principal_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_machine_subject_bindings", x => x.id);
                    table.CheckConstraint("ck_moderation_machine_binding_owner", "owner_type IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_moderation_machine_binding_revision", "revision > 0");
                    table.CheckConstraint("ck_moderation_machine_binding_source", "credential_source IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_machine_subject_bindings_ie_moderation_397f9b57",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_machine_subject_bindings_ie_service_pr_43bd6c64",
                        column: x => x.service_principal_id,
                        principalTable: "ie_service_principals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_machine_subject_bindings_ie_tenants_au_a54b91eb",
                        column: x => x.authority_tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_machine_subject_bindings_host_binding__bbe9ce5f",
                table: "ie_moderation_machine_subject_bindings",
                columns: new[] { "host_binding_id", "credential_source", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_machine_subject_bindings_authority_tenant_id",
                table: "ie_moderation_machine_subject_bindings",
                column: "authority_tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_machine_subject_bindings_service_principal_id",
                table: "ie_moderation_machine_subject_bindings",
                column: "service_principal_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_moderation_machine_subject_bindings");
        }
    }
}
