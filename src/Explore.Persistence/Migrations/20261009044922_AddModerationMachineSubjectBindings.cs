using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationMachineSubjectBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "moderation_machine_subject_bindings",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_source = table.Column<int>(type: "integer", nullable: false),
                    key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authority_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_type = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_machine_subject_bindings", x => x.id);
                    table.CheckConstraint("ck_moderation_machine_binding_owner", "owner_type IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_moderation_machine_binding_revision", "revision > 0");
                    table.CheckConstraint("ck_moderation_machine_binding_source", "credential_source IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_moderation_machine_subject_bindings_moderation__aa20cfc720ec",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_machine_subject_bindings_service_pri_dff04b0fe1e6",
                        column: x => x.service_principal_id,
                        principalSchema: "islamu_event",
                        principalTable: "service_principals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_machine_subject_bindings_tenants_aut_b0ded0592a1e",
                        column: x => x.authority_tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_machine_subject_bindings_authority_tenant_id",
                schema: "islamu_event",
                table: "moderation_machine_subject_bindings",
                column: "authority_tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_machine_subject_bindings_host_bindin_48e4441f27ac",
                schema: "islamu_event",
                table: "moderation_machine_subject_bindings",
                columns: new[] { "host_binding_id", "credential_source", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_machine_subject_bindings_service_principal_id",
                schema: "islamu_event",
                table: "moderation_machine_subject_bindings",
                column: "service_principal_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moderation_machine_subject_bindings",
                schema: "islamu_event");
        }
    }
}
