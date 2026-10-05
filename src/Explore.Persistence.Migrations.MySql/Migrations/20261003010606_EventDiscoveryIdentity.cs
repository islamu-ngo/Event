using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class EventDiscoveryIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_event_discovery_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_kind = table.Column<int>(type: "int", nullable: false),
                    source_key = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_key_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_deleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    deleted_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_discovery_identities", x => x.id);
                    table.UniqueConstraint("ak_event_discovery_identity_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_discovery_identity_source_hash", "length(source_key_hash) = 64");
                    table.CheckConstraint("ck_discovery_identity_source_key", "length(source_key) BETWEEN 1 AND 1024");
                    table.CheckConstraint("ck_discovery_identity_source_kind", "source_kind IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_event_discovery_identities_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_discovery_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    identity_epoch = table.Column<long>(type: "bigint", nullable: false),
                    disclosure_epoch = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_discovery_revisions", x => x.id);
                    table.CheckConstraint("ck_discovery_revision_epochs", "identity_epoch >= 0 AND disclosure_epoch >= 0");
                    table.ForeignKey(
                        name: "fk_event_discovery_revisions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_discovery_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    member_identity_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    primary_identity_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    relationship_revision = table.Column<long>(type: "bigint", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reason_code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reviewed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_discovery_aliases", x => x.id);
                    table.CheckConstraint("ck_discovery_alias_not_self", "member_identity_id <> primary_identity_id");
                    table.CheckConstraint("ck_discovery_alias_reason", "length(reason_code) BETWEEN 1 AND 80");
                    table.CheckConstraint("ck_discovery_alias_revision", "relationship_revision > 0");
                    table.ForeignKey(
                        name: "fk_ie_event_discovery_aliases_ie_event_discovery_identi_120bf9bd",
                        columns: x => new { x.tenant_id, x.primary_identity_id },
                        principalTable: "ie_event_discovery_identities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_discovery_aliases_ie_event_discovery_identi_5aad88fc",
                        columns: x => new { x.tenant_id, x.member_identity_id },
                        principalTable: "ie_event_discovery_identities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_aliases_tenant_id_member_identity_id",
                table: "ie_event_discovery_aliases",
                columns: new[] { "tenant_id", "member_identity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_aliases_tenant_id_primary_identity_id",
                table: "ie_event_discovery_aliases",
                columns: new[] { "tenant_id", "primary_identity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_discovery_identities_tenant_id_source_kind__37832492",
                table: "ie_event_discovery_identities",
                columns: new[] { "tenant_id", "source_kind", "source_key_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_revisions_tenant_id",
                table: "ie_event_discovery_revisions",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_event_discovery_aliases");

            migrationBuilder.DropTable(
                name: "ie_event_discovery_revisions");

            migrationBuilder.DropTable(
                name: "ie_event_discovery_identities");
        }
    }
}
