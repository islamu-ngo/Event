using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalApiKeyIssuanceReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_api_key_issuance_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_fingerprint = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    input_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_api_key_issuance_receipts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_api_key_issuance_receipts_operation_fingerprint",
                schema: "islamu_event",
                table: "external_api_key_issuance_receipts",
                column: "operation_fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_api_key_issuance_receipts_tenant_id",
                schema: "islamu_event",
                table: "external_api_key_issuance_receipts",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_api_key_issuance_receipts",
                schema: "islamu_event");
        }
    }
}
