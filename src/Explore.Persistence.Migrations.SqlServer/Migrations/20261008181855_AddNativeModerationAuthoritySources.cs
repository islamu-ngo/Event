using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeModerationAuthoritySources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "moderation_host_bindings",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    solution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    maintenance_service_principal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    installation_slot = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_host_bindings", x => x.id);
                    table.CheckConstraint("ck_moderation_host_binding_slot", "installation_slot = 1");
                    table.ForeignKey(
                        name: "fk_moderation_host_bindings_service_principals_maintenance_service_principal_id",
                        column: x => x.maintenance_service_principal_id,
                        principalSchema: "islamu_event",
                        principalTable: "service_principals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_qualification_epochs",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_qualification_epochs", x => x.id);
                    table.CheckConstraint("ck_moderation_qualification_revision", "revision > 0");
                    table.ForeignKey(
                        name: "fk_moderation_qualification_epochs_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_epochs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_host_bindings_installation_slot",
                schema: "islamu_event",
                table: "moderation_host_bindings",
                column: "installation_slot",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_host_bindings_maintenance_service_principal_id",
                schema: "islamu_event",
                table: "moderation_host_bindings",
                column: "maintenance_service_principal_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_epochs_host_binding_id_tenant_id",
                schema: "islamu_event",
                table: "moderation_qualification_epochs",
                columns: new[] { "host_binding_id", "tenant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_epochs_tenant_id",
                schema: "islamu_event",
                table: "moderation_qualification_epochs",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moderation_qualification_epochs",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_host_bindings",
                schema: "islamu_event");
        }
    }
}
