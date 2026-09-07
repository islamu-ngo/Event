using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations.Identity
{
    /// <inheritdoc />
    public partial class LocalAdministratorBootstrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_identity",
                table: "local_identity_users");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_identity",
                table: "local_identity_users",
                column: "normalized_email",
                unique: true,
                filter: "[normalized_email] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_identity",
                table: "local_identity_users");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                schema: "islamu_identity",
                table: "local_identity_users",
                column: "normalized_email");
        }
    }
}
