using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventDiscoveryTraversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_created_at_atproto_record_id",
                schema: "islamu_event",
                table: "atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_name_atproto_record_id",
                schema: "islamu_event",
                table: "atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_starts_at_atproto_record_id",
                schema: "islamu_event",
                table: "atproto_event_projections");

            migrationBuilder.AddColumn<string>(
                name: "discovery_source_sort_key",
                schema: "islamu_event",
                table: "events",
                type: "character varying(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.AddColumn<string>(
                name: "discovery_title_sort_key",
                schema: "islamu_event",
                table: "events",
                type: "character varying(800)",
                unicode: false,
                maxLength: 800,
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.AddColumn<string>(
                name: "discovery_source_sort_key",
                schema: "islamu_event",
                table: "atproto_event_projections",
                type: "character varying(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.AddColumn<string>(
                name: "discovery_title_sort_key",
                schema: "islamu_event",
                table: "atproto_event_projections",
                type: "character varying(960)",
                unicode: false,
                maxLength: 960,
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.CreateTable(
                name: "event_discovery_snapshot_reservations",
                schema: "islamu_event",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_discovery_snapshot_reservations", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "event_discovery_snapshots",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criteria_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    identity_epoch = table.Column<long>(type: "bigint", nullable: false),
                    disclosure_epoch = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    truncated = table.Column<bool>(type: "boolean", nullable: false),
                    local_source_complete = table.Column<bool>(type: "boolean", nullable: false),
                    remote_source_complete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_discovery_snapshots", x => x.id);
                    table.UniqueConstraint("ak_event_discovery_snapshots_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_discovery_snapshot_count", "item_count BETWEEN 0 AND 1000");
                    table.CheckConstraint("ck_discovery_snapshot_epochs", "identity_epoch >= 0 AND disclosure_epoch >= 0");
                    table.CheckConstraint("ck_discovery_snapshot_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_discovery_snapshot_hash", "length(criteria_hash) = 64");
                    table.ForeignKey(
                        name: "fk_event_discovery_snapshots_event_discovery_snaps_0336c4e51b80",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "event_discovery_snapshot_reservations",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_discovery_snapshot_items",
                schema: "islamu_event",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<long>(type: "bigint", nullable: false),
                    source_kind = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_kind = table.Column<int>(type: "integer", nullable: false),
                    canonical_id = table.Column<Guid>(type: "uuid", nullable: false),
                    matching_session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_discovery_snapshot_items", x => new { x.tenant_id, x.snapshot_id, x.ordinal });
                    table.CheckConstraint("ck_discovery_snapshot_item_canonical", "canonical_kind IN (1, 2, 3)");
                    table.CheckConstraint("ck_discovery_snapshot_item_ordinal", "ordinal BETWEEN 0 AND 999");
                    table.CheckConstraint("ck_discovery_snapshot_item_source", "source_kind IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_event_discovery_snapshot_items_event_discovery__6f2a65a7ae25",
                        columns: x => new { x.tenant_id, x.snapshot_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_discovery_snapshots",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_discovery_source_sort_key",
                schema: "islamu_event",
                table: "events",
                column: "discovery_source_sort_key");

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_created_at_discovery_source_sort_key",
                schema: "islamu_event",
                table: "events",
                columns: new[] { "tenant_id", "created_at", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_discovery_title_sort_key_disco_c6c109b50434",
                schema: "islamu_event",
                table: "events",
                columns: new[] { "tenant_id", "discovery_title_sort_key", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_total_views_discovery_source_sort_key",
                schema: "islamu_event",
                table: "events",
                columns: new[] { "tenant_id", "total_views", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_created_at_discovery__b2b9501c7695",
                schema: "islamu_event",
                table: "atproto_event_projections",
                columns: new[] { "created_at", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_discovery_source_sort_key",
                schema: "islamu_event",
                table: "atproto_event_projections",
                column: "discovery_source_sort_key");

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_discovery_title_sort__b611fc2f3cfa",
                schema: "islamu_event",
                table: "atproto_event_projections",
                columns: new[] { "discovery_title_sort_key", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_starts_at_discovery_s_fe0f3754da9c",
                schema: "islamu_event",
                table: "atproto_event_projections",
                columns: new[] { "starts_at", "discovery_source_sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_snapshot_items_tenant_id_snapsh_963be6b21c2c",
                schema: "islamu_event",
                table: "event_discovery_snapshot_items",
                columns: new[] { "tenant_id", "snapshot_id", "canonical_kind", "canonical_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_snapshots_tenant_id_criteria_ha_3861dbef7753",
                schema: "islamu_event",
                table: "event_discovery_snapshots",
                columns: new[] { "tenant_id", "criteria_hash", "identity_epoch", "disclosure_epoch", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_event_discovery_snapshots_tenant_id_expires_at_utc_id",
                schema: "islamu_event",
                table: "event_discovery_snapshots",
                columns: new[] { "tenant_id", "expires_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_discovery_snapshot_items",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "event_discovery_snapshots",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "event_discovery_snapshot_reservations",
                schema: "islamu_event");

            migrationBuilder.DropIndex(
                name: "ix_events_discovery_source_sort_key",
                schema: "islamu_event",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_tenant_id_created_at_discovery_source_sort_key",
                schema: "islamu_event",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_tenant_id_discovery_title_sort_key_disco_c6c109b50434",
                schema: "islamu_event",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_tenant_id_total_views_discovery_source_sort_key",
                schema: "islamu_event",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_created_at_discovery__b2b9501c7695",
                schema: "islamu_event",
                table: "atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_discovery_source_sort_key",
                schema: "islamu_event",
                table: "atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_discovery_title_sort__b611fc2f3cfa",
                schema: "islamu_event",
                table: "atproto_event_projections");

            migrationBuilder.DropIndex(
                name: "ix_atproto_event_projections_starts_at_discovery_s_fe0f3754da9c",
                schema: "islamu_event",
                table: "atproto_event_projections");

            migrationBuilder.DropColumn(
                name: "discovery_source_sort_key",
                schema: "islamu_event",
                table: "events");

            migrationBuilder.DropColumn(
                name: "discovery_title_sort_key",
                schema: "islamu_event",
                table: "events");

            migrationBuilder.DropColumn(
                name: "discovery_source_sort_key",
                schema: "islamu_event",
                table: "atproto_event_projections");

            migrationBuilder.DropColumn(
                name: "discovery_title_sort_key",
                schema: "islamu_event",
                table: "atproto_event_projections");

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_created_at_atproto_record_id",
                schema: "islamu_event",
                table: "atproto_event_projections",
                columns: new[] { "created_at", "atproto_record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_name_atproto_record_id",
                schema: "islamu_event",
                table: "atproto_event_projections",
                columns: new[] { "name", "atproto_record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_atproto_event_projections_starts_at_atproto_record_id",
                schema: "islamu_event",
                table: "atproto_event_projections",
                columns: new[] { "starts_at", "atproto_record_id" });
        }
    }
}
