using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeModerationAuthoritySources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_moderation_host_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    solution_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    instance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    maintenance_service_principal_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    installation_slot = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_host_bindings", x => x.id);
                    table.CheckConstraint("ck_moderation_host_binding_slot", "installation_slot = 1");
                    table.ForeignKey(
                        name: "fk_moderation_host_bindings_service_principals_maintenance_service_principal_id",
                        column: x => x.maintenance_service_principal_id,
                        principalTable: "ie_service_principals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_qualification_epochs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_qualification_epochs", x => x.id);
                    table.CheckConstraint("ck_moderation_qualification_revision", "revision > 0");
                    table.ForeignKey(
                        name: "fk_moderation_qualification_epochs_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_epochs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_host_bindings_installation_slot",
                table: "ie_moderation_host_bindings",
                column: "installation_slot",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_host_bindings_maintenance_service_principal_id",
                table: "ie_moderation_host_bindings",
                column: "maintenance_service_principal_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_epochs_host_binding_id_tenant_id",
                table: "ie_moderation_qualification_epochs",
                columns: new[] { "host_binding_id", "tenant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_epochs_tenant_id",
                table: "ie_moderation_qualification_epochs",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_moderation_qualification_epochs");

            migrationBuilder.DropTable(
                name: "ie_moderation_host_bindings");
        }
    }
}
