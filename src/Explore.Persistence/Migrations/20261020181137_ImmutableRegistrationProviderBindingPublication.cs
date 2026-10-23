using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImmutableRegistrationProviderBindingPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_registration_attempts_registration_provider_bin_90454f848571",
                schema: "islamu_event",
                table: "registration_attempts");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_registration_provider_bindings_tenant_id_id_published_mappi",
                schema: "islamu_event",
                table: "registration_provider_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                schema: "islamu_event",
                table: "registration_provider_bindings");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash_key",
                schema: "islamu_event",
                table: "registration_provider_bindings",
                type: "character varying(44)",
                maxLength: 44,
                nullable: false,
                defaultValue: "",
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldDefaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash",
                schema: "islamu_event",
                table: "registration_provider_bindings",
                type: "character varying(44)",
                maxLength: 44,
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash",
                schema: "islamu_event",
                table: "registration_attempts",
                type: "character varying(44)",
                maxLength: 44,
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash_key",
                schema: "islamu_event",
                table: "registration_attempts",
                type: "character varying(44)",
                maxLength: 44,
                nullable: false,
                computedColumnSql: "COALESCE(provider_mapping_revision_hash, '')",
                stored: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldComputedColumnSql: "COALESCE(provider_mapping_revision_hash, '')",
                oldStored: true);

            migrationBuilder.CreateTable(
                name: "registration_provider_binding_publications",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_provider_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mapping_revision_hash_key = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false, collation: "C"),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registration_provider_binding_publications", x => x.id);
                    table.UniqueConstraint("ak_registration_provider_binding_publications_tenant_id_regist", x => new { x.tenant_id, x.registration_provider_binding_id, x.mapping_revision_hash_key });
                    table.UniqueConstraint("ak_registration_provider_binding_publications_tenant_id_regist1", x => new { x.tenant_id, x.registration_provider_binding_id, x.mapping_revision_hash_key, x.published_at });
                    table.ForeignKey(
                        name: "fk_registration_provider_binding_publications_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_registration_provider_bindings_tenant_id_id_pub_7d4311ff3928",
                schema: "islamu_event",
                table: "registration_provider_bindings",
                columns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key", "published_at" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                schema: "islamu_event",
                table: "registration_provider_bindings",
                sql: "(state_id = 2 AND published_mapping_revision_hash IS NOT NULL AND published_at IS NOT NULL AND published_mapping_revision_hash_key = published_mapping_revision_hash) OR (state_id <> 2 AND published_mapping_revision_hash IS NULL AND published_at IS NULL AND published_mapping_revision_hash_key = '')");

            migrationBuilder.CreateIndex(
                name: "ix_registration_provider_binding_publications_tena_d4f0b0a76cbb",
                schema: "islamu_event",
                table: "registration_provider_binding_publications",
                columns: new[] { "tenant_id", "registration_provider_binding_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_registration_attempts_registration_provider_bin_8dc716109008",
                schema: "islamu_event",
                table: "registration_attempts",
                columns: new[] { "tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key" },
                principalSchema: "islamu_event",
                principalTable: "registration_provider_binding_publications",
                principalColumns: new[] { "tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_registration_provider_bindings_registration_pro_6cd1eac97312",
                schema: "islamu_event",
                table: "registration_provider_bindings",
                columns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key", "published_at" },
                principalSchema: "islamu_event",
                principalTable: "registration_provider_binding_publications",
                principalColumns: new[] { "tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key", "published_at" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_registration_attempts_registration_provider_bin_8dc716109008",
                schema: "islamu_event",
                table: "registration_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_registration_provider_bindings_registration_pro_6cd1eac97312",
                schema: "islamu_event",
                table: "registration_provider_bindings");

            migrationBuilder.DropTable(
                name: "registration_provider_binding_publications",
                schema: "islamu_event");

            migrationBuilder.DropIndex(
                name: "ix_registration_provider_bindings_tenant_id_id_pub_7d4311ff3928",
                schema: "islamu_event",
                table: "registration_provider_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                schema: "islamu_event",
                table: "registration_provider_bindings");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash_key",
                schema: "islamu_event",
                table: "registration_provider_bindings",
                type: "character varying(44)",
                maxLength: 44,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldDefaultValue: "",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash",
                schema: "islamu_event",
                table: "registration_provider_bindings",
                type: "character varying(44)",
                maxLength: 44,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash",
                schema: "islamu_event",
                table: "registration_attempts",
                type: "character varying(44)",
                maxLength: 44,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash_key",
                schema: "islamu_event",
                table: "registration_attempts",
                type: "character varying(44)",
                maxLength: 44,
                nullable: false,
                computedColumnSql: "COALESCE(provider_mapping_revision_hash, '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldComputedColumnSql: "COALESCE(provider_mapping_revision_hash, '')",
                oldStored: true,
                oldCollation: "C");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_registration_provider_bindings_tenant_id_id_published_mappi",
                schema: "islamu_event",
                table: "registration_provider_bindings",
                columns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                schema: "islamu_event",
                table: "registration_provider_bindings",
                sql: "(state_id = 2 AND published_mapping_revision_hash IS NOT NULL AND published_at IS NOT NULL) OR (state_id <> 2)");

            migrationBuilder.AddForeignKey(
                name: "fk_registration_attempts_registration_provider_bin_90454f848571",
                schema: "islamu_event",
                table: "registration_attempts",
                columns: new[] { "tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key" },
                principalSchema: "islamu_event",
                principalTable: "registration_provider_bindings",
                principalColumns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
