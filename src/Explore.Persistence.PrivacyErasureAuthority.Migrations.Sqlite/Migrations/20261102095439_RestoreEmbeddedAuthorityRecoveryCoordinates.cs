using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RestoreEmbeddedAuthorityRecoveryCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "store_id",
                table: "ie_authority_counter",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "(lower(printf('%08x-%04x-7%s-%s%s-%s',\n    cast(unixepoch('subsec') * 1000 as integer) >> 16,\n    cast(unixepoch('subsec') * 1000 as integer) & 65535,\n    substr(hex(randomblob(2)), 2),\n    substr('89ab', (random() & 3) + 1, 1),\n    substr(hex(randomblob(2)), 2), hex(randomblob(6)))))",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_authority_counter_recovery",
                table: "ie_authority_counter",
                sql: "revision > 0 AND recovery_generation > 0 AND length(store_id) = 36 AND substr(store_id, 15, 1) = '7' AND lower(substr(store_id, 20, 1)) IN ('8', '9', 'a', 'b')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_authority_counter_recovery",
                table: "ie_authority_counter");

            migrationBuilder.AlterColumn<Guid>(
                name: "store_id",
                table: "ie_authority_counter",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldDefaultValueSql: "(lower(printf('%08x-%04x-7%s-%s%s-%s',\n    cast(unixepoch('subsec') * 1000 as integer) >> 16,\n    cast(unixepoch('subsec') * 1000 as integer) & 65535,\n    substr(hex(randomblob(2)), 2),\n    substr('89ab', (random() & 3) + 1, 1),\n    substr(hex(randomblob(2)), 2), hex(randomblob(6)))))");
        }
    }
}
