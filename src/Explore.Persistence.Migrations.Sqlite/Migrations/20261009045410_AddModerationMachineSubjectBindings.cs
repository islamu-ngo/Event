using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    credential_source = table.Column<int>(type: "INTEGER", nullable: false),
                    key_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    authority_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    owner_type = table.Column<int>(type: "INTEGER", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    service_principal_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_machine_subject_bindings", x => x.id);
                    table.CheckConstraint("ck_moderation_machine_binding_owner", "owner_type IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_moderation_machine_binding_revision", "revision > 0");
                    table.CheckConstraint("ck_moderation_machine_binding_source", "credential_source IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_moderation_machine_subject_bindings_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_machine_subject_bindings_service_principals_service_principal_id",
                        column: x => x.service_principal_id,
                        principalTable: "ie_service_principals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_machine_subject_bindings_tenants_authority_tenant_id",
                        column: x => x.authority_tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_machine_subject_bindings_authority_tenant_id",
                table: "ie_moderation_machine_subject_bindings",
                column: "authority_tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_machine_subject_bindings_host_binding_id_credential_source_key_id",
                table: "ie_moderation_machine_subject_bindings",
                columns: new[] { "host_binding_id", "credential_source", "key_id" },
                unique: true);

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
