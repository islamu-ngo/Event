using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class EventDiscoveryTraversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_created_at_atproto_record_id",
                table: "ie_atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_name_atproto_record_id",
                table: "ie_atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_starts_at_atproto_record_id",
                table: "ie_atproto_event_projections");

            migrationBuilder.AddColumn<string>(
                name: "discovery_source_sort_key",
                table: "ie_events",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                collation: "ascii_bin")
                .Annotation("MySql:CharSet", "ascii");

            migrationBuilder.AddColumn<string>(
                name: "discovery_title_sort_key",
                table: "ie_events",
                type: "varchar(800)",
                unicode: false,
                maxLength: 800,
                nullable: false,
                defaultValue: "",
                collation: "ascii_bin")
                .Annotation("MySql:CharSet", "ascii");

            migrationBuilder.AddColumn<string>(
                name: "discovery_source_sort_key",
                table: "ie_atproto_event_projections",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                collation: "ascii_bin")
                .Annotation("MySql:CharSet", "ascii");

            migrationBuilder.AddColumn<string>(
                name: "discovery_title_sort_key",
                table: "ie_atproto_event_projections",
                type: "varchar(960)",
                unicode: false,
                maxLength: 960,
                nullable: false,
                defaultValue: "",
                collation: "ascii_bin")
                .Annotation("MySql:CharSet", "ascii");

            migrationBuilder.CreateTable(
                name: "ie_event_discovery_snapshot_reservations",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_discovery_snapshot_reservations", x => x.tenant_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_discovery_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    criteria_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    identity_epoch = table.Column<long>(type: "bigint", nullable: false),
                    disclosure_epoch = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    item_count = table.Column<int>(type: "int", nullable: false),
                    truncated = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    local_source_complete = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    remote_source_complete = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_discovery_snapshots", x => x.id);
                    table.UniqueConstraint("ak_event_discovery_snapshots_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_discovery_snapshot_count", "item_count BETWEEN 0 AND 1000");
                    table.CheckConstraint("ck_discovery_snapshot_epochs", "identity_epoch >= 0 AND disclosure_epoch >= 0");
                    table.CheckConstraint("ck_discovery_snapshot_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_discovery_snapshot_hash", "length(criteria_hash) = 64");
                    table.ForeignKey(
                        name: "fk_ie_event_discovery_snapshots_ie_event_discovery_snap_21aba2f1",
                        column: x => x.tenant_id,
                        principalTable: "ie_event_discovery_snapshot_reservations",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_discovery_snapshot_items",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    snapshot_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<long>(type: "bigint", nullable: false),
                    source_kind = table.Column<int>(type: "int", nullable: false),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    canonical_kind = table.Column<int>(type: "int", nullable: false),
                    canonical_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    matching_session_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_discovery_snapshot_items_tenant_id_snapshot_89bf56b6", x => new { x.tenant_id, x.snapshot_id, x.ordinal });
                    table.CheckConstraint("ck_discovery_snapshot_item_canonical", "canonical_kind IN (1, 2, 3)");
                    table.CheckConstraint("ck_discovery_snapshot_item_ordinal", "ordinal BETWEEN 0 AND 999");
                    table.CheckConstraint("ck_discovery_snapshot_item_source", "source_kind IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_ie_event_discovery_snapshot_items_ie_event_discovery_f029775e",
                        columns: x => new { x.tenant_id, x.snapshot_id },
                        principalTable: "ie_event_discovery_snapshots",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_events_discovery_source_sort_key",
                table: "ie_events",
                column: "discovery_source_sort_key");

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_created_at_discovery_source_sort_key",
                table: "ie_events",
                columns: new[] { "tenant_id", "created_at", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_total_views_discovery_source_sort_key",
                table: "ie_events",
                columns: new[] { "tenant_id", "total_views", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_events_tenant_id_discovery_title_sort_key_discove_13363d56",
                table: "ie_events",
                columns: new[] { "tenant_id", "discovery_title_sort_key", "discovery_source_sort_key" })
                .Annotation("MySql:IndexPrefixLength", new[] { 0, 512, 0 });

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_discovery_source_sort_key",
                table: "ie_atproto_event_projections",
                column: "discovery_source_sort_key");

            migrationBuilder.CreateIndex(
                name: "ix_ie_atproto_event_projections_created_at_discovery_so_8483aff5",
                table: "ie_atproto_event_projections",
                columns: new[] { "created_at", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_atproto_event_projections_discovery_title_sort_ke_9fa0b9db",
                table: "ie_atproto_event_projections",
                columns: new[] { "discovery_title_sort_key", "discovery_source_sort_key" })
                .Annotation("MySql:IndexPrefixLength", new[] { 512, 0 });

            migrationBuilder.CreateIndex(
                name: "ix_ie_atproto_event_projections_starts_at_discovery_sou_e9a41e4a",
                table: "ie_atproto_event_projections",
                columns: new[] { "starts_at", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_discovery_snapshot_items_tenant_id_snapshot_1698ad69",
                table: "ie_event_discovery_snapshot_items",
                columns: new[] { "tenant_id", "snapshot_id", "canonical_kind", "canonical_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_snapshots_tenant_id_expires_at_utc_id",
                table: "ie_event_discovery_snapshots",
                columns: new[] { "tenant_id", "expires_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_discovery_snapshots_tenant_id_criteria_hash_6ee94b63",
                table: "ie_event_discovery_snapshots",
                columns: new[] { "tenant_id", "criteria_hash", "identity_epoch", "disclosure_epoch", "expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_event_discovery_snapshot_items");

            migrationBuilder.DropTable(
                name: "ie_event_discovery_snapshots");

            migrationBuilder.DropTable(
                name: "ie_event_discovery_snapshot_reservations");

            migrationBuilder.DropIndex(
                name: "ix_events_discovery_source_sort_key",
                table: "ie_events");

            migrationBuilder.DropIndex(
                name: "ix_events_tenant_id_created_at_discovery_source_sort_key",
                table: "ie_events");

            migrationBuilder.DropIndex(
                name: "ix_events_tenant_id_total_views_discovery_source_sort_key",
                table: "ie_events");

            migrationBuilder.DropIndex(
                name: "ix_ie_events_tenant_id_discovery_title_sort_key_discove_13363d56",
                table: "ie_events");

            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_discovery_source_sort_key",
                table: "ie_atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_ie_atproto_event_projections_created_at_discovery_so_8483aff5",
                table: "ie_atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_ie_atproto_event_projections_discovery_title_sort_ke_9fa0b9db",
                table: "ie_atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_ie_atproto_event_projections_starts_at_discovery_sou_e9a41e4a",
                table: "ie_atproto_event_projections");

            migrationBuilder.DropColumn(
                name: "discovery_source_sort_key",
                table: "ie_events");

            migrationBuilder.DropColumn(
                name: "discovery_title_sort_key",
                table: "ie_events");

            migrationBuilder.DropColumn(
                name: "discovery_source_sort_key",
                table: "ie_atproto_event_projections");

            migrationBuilder.DropColumn(
                name: "discovery_title_sort_key",
                table: "ie_atproto_event_projections");

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_created_at_atproto_record_id",
                table: "ie_atproto_event_projections",
                columns: new[] { "created_at", "atproto_record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_name_atproto_record_id",
                table: "ie_atproto_event_projections",
                columns: new[] { "name", "atproto_record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_starts_at_atproto_record_id",
                table: "ie_atproto_event_projections",
                columns: new[] { "starts_at", "atproto_record_id" });
        }
    }
}
