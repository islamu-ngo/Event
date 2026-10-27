using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdjudicatedCaseReadGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "moderation_adjudicated_case_read_grants",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer_role_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer_platform_grant = table.Column<bool>(type: "boolean", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_snapshot_hash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    original_effect_binding_hash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false, collation: "C"),
                    original_retain_until = table.Column<long>(type: "bigint", nullable: false),
                    issued_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    revoked_at = table.Column<long>(type: "bigint", nullable: true),
                    revocation_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_by_role_grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_by_platform_grant = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_adjudicated_case_read_grants", x => x.id);
                    table.CheckConstraint("ck_adjudicated_read_grant_case_revision", "case_revision > 0 AND case_revision < 9223372036854775807");
                    table.CheckConstraint("ck_adjudicated_read_grant_hashes", "length(original_snapshot_hash) = 64 AND length(original_effect_binding_hash) = 64");
                    table.CheckConstraint("ck_adjudicated_read_grant_revocation", "(revision = 1 AND revoked_at IS NULL AND revocation_operation_id IS NULL AND revoked_by_user_id IS NULL AND revoked_by_role_grant_id IS NULL AND revoked_by_platform_grant IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_at >= issued_at AND revoked_at < 3155378975999999999 AND revoked_at % 10 = 0 AND revocation_operation_id IS NOT NULL AND revocation_operation_id <> id AND revocation_operation_id <> '00000000-0000-0000-0000-000000000000' AND revoked_by_user_id IS NOT NULL AND revoked_by_user_id <> '00000000-0000-0000-0000-000000000000' AND revoked_by_role_grant_id IS NOT NULL AND revoked_by_role_grant_id <> '00000000-0000-0000-0000-000000000000' AND revoked_by_platform_grant IS NOT NULL)");
                    table.CheckConstraint("ck_adjudicated_read_grant_window", "issued_at > 0 AND expires_at > issued_at AND expires_at <= original_retain_until AND original_retain_until < 3155378975999999999 AND issued_at % 10 = 0 AND expires_at % 10 = 0");
                    table.ForeignKey(
                        name: "fk_moderation_adjudicated_case_read_grants_moderat_7d30eaa72223",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_adjudicated_case_read_grants_moderat_de802c26ce29",
                        column: x => x.original_effect_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_adjudicated_case_read_grants_moderat_e8e1e416e492",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_adjudicated_case_read_grants_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_adjudicated_case_read_grants_case_id",
                schema: "islamu_event",
                table: "moderation_adjudicated_case_read_grants",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_adjudicated_case_read_grants_host_binding_id",
                schema: "islamu_event",
                table: "moderation_adjudicated_case_read_grants",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_adjudicated_case_read_grants_original_effect_id",
                schema: "islamu_event",
                table: "moderation_adjudicated_case_read_grants",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_adjudicated_case_read_grants_tenant_id",
                schema: "islamu_event",
                table: "moderation_adjudicated_case_read_grants",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_adjudicated_case_read_grants_trust_d_273ddf4fa112",
                schema: "islamu_event",
                table: "moderation_adjudicated_case_read_grants",
                columns: new[] { "trust_domain_id", "tenant_id", "case_id", "recipient_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moderation_adjudicated_case_read_grants",
                schema: "islamu_event");
        }
    }
}
