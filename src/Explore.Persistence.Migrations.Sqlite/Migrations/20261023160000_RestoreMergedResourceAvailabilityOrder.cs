using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RestoreMergedResourceAvailabilityOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_event_resources_relative_availability_order",
                table: "ie_event_resources",
                sql: "availability_start_anchor_id IS NULL OR availability_end_anchor_id IS NULL OR availability_start_anchor_id <> availability_end_anchor_id OR availability_end_offset_ticks > availability_start_offset_ticks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_event_resources_relative_availability_order",
                table: "ie_event_resources");
        }
    }
}
