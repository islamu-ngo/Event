using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventDiscoveryIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_discovery_identities",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<int>(type: "integer", nullable: false),
                    source_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    source_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_discovery_identities", x => x.id);
                    table.UniqueConstraint("ak_event_discovery_identity_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_discovery_identity_source_hash", "length(source_key_hash) = 64");
                    table.CheckConstraint("ck_discovery_identity_source_key", "length(source_key) BETWEEN 1 AND 1024");
                    table.CheckConstraint("ck_discovery_identity_source_kind", "source_kind IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_event_discovery_identities_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_discovery_revisions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_epoch = table.Column<long>(type: "bigint", nullable: false),
                    disclosure_epoch = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_discovery_revisions", x => x.id);
                    table.CheckConstraint("ck_discovery_revision_epochs", "identity_epoch >= 0 AND disclosure_epoch >= 0");
                    table.ForeignKey(
                        name: "fk_event_discovery_revisions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_discovery_aliases",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_revision = table.Column<long>(type: "bigint", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_discovery_aliases", x => x.id);
                    table.CheckConstraint("ck_discovery_alias_not_self", "member_identity_id <> primary_identity_id");
                    table.CheckConstraint("ck_discovery_alias_reason", "length(reason_code) BETWEEN 1 AND 80");
                    table.CheckConstraint("ck_discovery_alias_revision", "relationship_revision > 0");
                    table.ForeignKey(
                        name: "fk_event_discovery_aliases_event_discovery_identit_2c98bbe521a5",
                        columns: x => new { x.tenant_id, x.member_identity_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_discovery_identities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_discovery_aliases_event_discovery_identit_de6ce8bebbe3",
                        columns: x => new { x.tenant_id, x.primary_identity_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_discovery_identities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_aliases_tenant_id_member_identity_id",
                schema: "islamu_event",
                table: "event_discovery_aliases",
                columns: new[] { "tenant_id", "member_identity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_aliases_tenant_id_primary_identity_id",
                schema: "islamu_event",
                table: "event_discovery_aliases",
                columns: new[] { "tenant_id", "primary_identity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_identities_tenant_id_source_kin_f6cdaf3f5c05",
                schema: "islamu_event",
                table: "event_discovery_identities",
                columns: new[] { "tenant_id", "source_kind", "source_key_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_revisions_tenant_id",
                schema: "islamu_event",
                table: "event_discovery_revisions",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_discovery_aliases",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "event_discovery_revisions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "event_discovery_identities",
                schema: "islamu_event");
        }
    }
}
