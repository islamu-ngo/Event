using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ManagedActorProfileMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "profile_picture_uri",
                table: "ie_actor_pii",
                newName: "external_profile_picture_uri");

            migrationBuilder.AddColumn<Guid>(
                name: "profile_picture_storage_object_id",
                table: "ie_actor_pii",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_actor_pii_profile_picture_storage_object_id",
                table: "ie_actor_pii",
                column: "profile_picture_storage_object_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_actor_pii_profile_picture_shape",
                table: "ie_actor_pii",
                sql: "(profile_picture_storage_object_id IS NULL OR (profile_picture_storage_object_id <> '00000000-0000-0000-0000-000000000000' AND external_profile_picture_uri IS NULL)) AND (external_profile_picture_uri IS NULL OR (LOWER(external_profile_picture_uri) LIKE 'https://_%' OR LOWER(external_profile_picture_uri) LIKE 'http://_%'))");

            migrationBuilder.AddForeignKey(
                name: "fk_actor_pii_storage_objects_profile_picture_storage_object_id",
                table: "ie_actor_pii",
                column: "profile_picture_storage_object_id",
                principalTable: "ie_storage_objects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_actor_pii_storage_objects_profile_picture_storage_object_id",
                table: "ie_actor_pii");

            migrationBuilder.DropIndex(
                name: "ix_actor_pii_profile_picture_storage_object_id",
                table: "ie_actor_pii");

            migrationBuilder.DropCheckConstraint(
                name: "ck_actor_pii_profile_picture_shape",
                table: "ie_actor_pii");

            migrationBuilder.DropColumn(
                name: "profile_picture_storage_object_id",
                table: "ie_actor_pii");

            migrationBuilder.RenameColumn(
                name: "external_profile_picture_uri",
                table: "ie_actor_pii",
                newName: "profile_picture_uri");
        }
    }
}
