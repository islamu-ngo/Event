using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.CoLocatedPrivacyErasureAuthority
{
    /// <inheritdoc />
    public partial class RestoreCoLocatedAuthorityRecoveryCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "recovery_generation",
                schema: "islamu_event",
                table: "authority_counter",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                schema: "islamu_event",
                table: "authority_counter",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "islamu_event",
                table: "authority_counter",
                type: "uuid",
                nullable: false,
                defaultValueSql: "uuidv7()");

            migrationBuilder.AddCheckConstraint(
                name: "ck_privacy_erasure_authority_counter_recovery",
                schema: "islamu_event",
                table: "authority_counter",
                sql: "revision > 0 AND recovery_generation > 0 AND substring(store_id::text from 15 for 1) = '7' AND substring(store_id::text from 20 for 1) IN ('8', '9', 'a', 'b')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_privacy_erasure_authority_counter_recovery",
                schema: "islamu_event",
                table: "authority_counter");

            migrationBuilder.DropColumn(
                name: "recovery_generation",
                schema: "islamu_event",
                table: "authority_counter");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "islamu_event",
                table: "authority_counter");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "islamu_event",
                table: "authority_counter");
        }
    }
}
