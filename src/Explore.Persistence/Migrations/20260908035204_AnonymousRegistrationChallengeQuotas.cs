using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnonymousRegistrationChallengeQuotas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anonymous_challenge_event_quotas",
                schema: "islamu_event",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    window_minute = table.Column<long>(type: "bigint", nullable: false),
                    issued = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anonymous_challenge_event_quotas", x => new { x.tenant_id, x.event_id });
                    table.CheckConstraint("ck_anon_challenge_event_count", "issued >= 0 AND issued <= 10000");
                    table.CheckConstraint("ck_anon_challenge_event_minute", "window_minute >= 0");
                    table.ForeignKey(
                        name: "fk_anonymous_challenge_event_quotas_events_tenant_id_event_id",
                        columns: x => new { x.tenant_id, x.event_id },
                        principalSchema: "islamu_event",
                        principalTable: "events",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "anonymous_challenge_tenant_quotas",
                schema: "islamu_event",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    window_minute = table.Column<long>(type: "bigint", nullable: false),
                    issued = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anonymous_challenge_tenant_quotas", x => x.tenant_id);
                    table.CheckConstraint("ck_anon_challenge_tenant_count", "issued >= 0 AND issued <= 10000");
                    table.CheckConstraint("ck_anon_challenge_tenant_minute", "window_minute >= 0");
                    table.ForeignKey(
                        name: "fk_anonymous_challenge_tenant_quotas_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anonymous_challenge_event_quotas",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "anonymous_challenge_tenant_quotas",
                schema: "islamu_event");
        }
    }
}
