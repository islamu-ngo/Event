using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class LocalAdministratorBootstrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_event",
                table: "local_identity_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                schema: "islamu_event",
                table: "instance_bootstrap_states");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_event",
                table: "local_identity_users",
                column: "normalized_email",
                unique: true,
                filter: "[normalized_email] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                schema: "islamu_event",
                table: "instance_bootstrap_states",
                sql: "provider_kind IS NULL OR provider_kind IN (1, 2, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_event",
                table: "local_identity_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                schema: "islamu_event",
                table: "instance_bootstrap_states");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_event",
                table: "local_identity_users",
                column: "normalized_email");

            migrationBuilder.AddCheckConstraint(
                name: "ck_instance_bootstrap_states_provider_kind",
                schema: "islamu_event",
                table: "instance_bootstrap_states",
                sql: "provider_kind IS NULL OR provider_kind BETWEEN 1 AND 2");
        }
    }
}
