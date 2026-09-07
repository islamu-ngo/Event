using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations.Identity
{
    /// <inheritdoc />
    public partial class LocalAdministratorBootstrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                table: "ie_local_identity_users");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                table: "ie_local_identity_users",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_local_identity_users_normalized_email",
                table: "ie_local_identity_users");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_users_normalized_email",
                table: "ie_local_identity_users",
                column: "normalized_email");
        }
    }
}
