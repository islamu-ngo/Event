using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ExternalApiKeyIssuanceReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_external_api_key_issuance_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    operation_fingerprint = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    input_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    external_api_key_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_external_api_key_issuance_receipts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_api_key_issuance_receipts_operation_fingerprint",
                table: "ie_external_api_key_issuance_receipts",
                column: "operation_fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_api_key_issuance_receipts_tenant_id",
                table: "ie_external_api_key_issuance_receipts",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_external_api_key_issuance_receipts");
        }
    }
}
