using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ImmutableRegistrationProviderBindingPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_registration_attempts_registration_provider_bindings_tenant_id_registration_provider_binding_id_provider_mapping_90454f848571",
                table: "ie_registration_attempts");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_registration_provider_bindings_tenant_id_id_published_mapping_revision_hash_key",
                table: "ie_registration_provider_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                table: "ie_registration_provider_bindings");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash_key",
                table: "ie_registration_provider_bindings",
                type: "TEXT",
                maxLength: 44,
                nullable: false,
                defaultValue: "",
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 44,
                oldDefaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash",
                table: "ie_registration_provider_bindings",
                type: "TEXT",
                maxLength: 44,
                nullable: true,
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 44,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash",
                table: "ie_registration_attempts",
                type: "TEXT",
                maxLength: 44,
                nullable: true,
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 44,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash_key",
                table: "ie_registration_attempts",
                type: "TEXT",
                maxLength: 44,
                nullable: false,
                computedColumnSql: "COALESCE(provider_mapping_revision_hash, '')",
                stored: true,
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 44,
                oldComputedColumnSql: "COALESCE(provider_mapping_revision_hash, '')",
                oldStored: true);

            migrationBuilder.CreateTable(
                name: "ie_registration_provider_binding_publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    registration_provider_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mapping_revision_hash_key = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false, collation: "BINARY"),
                    published_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_registration_provider_binding_publications", x => x.id);
                    table.UniqueConstraint("ak_registration_provider_binding_publications_tenant_id_registration_provider_binding_id_mapping_revision_hash_key", x => new { x.tenant_id, x.registration_provider_binding_id, x.mapping_revision_hash_key });
                    table.UniqueConstraint("ak_registration_provider_binding_publications_tenant_id_registration_provider_binding_id_mapping_revision_hash_key_published_at", x => new { x.tenant_id, x.registration_provider_binding_id, x.mapping_revision_hash_key, x.published_at });
                    table.ForeignKey(
                        name: "fk_registration_provider_binding_publications_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_registration_provider_bindings_tenant_id_id_published_mapping_revision_hash_key_published_at",
                table: "ie_registration_provider_bindings",
                columns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key", "published_at" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                table: "ie_registration_provider_bindings",
                sql: "(state_id = 2 AND published_mapping_revision_hash IS NOT NULL AND published_at IS NOT NULL AND published_mapping_revision_hash_key = published_mapping_revision_hash) OR (state_id <> 2 AND published_mapping_revision_hash IS NULL AND published_at IS NULL AND published_mapping_revision_hash_key = '')");

            migrationBuilder.CreateIndex(
                name: "ix_registration_provider_binding_publications_tenant_id_registration_provider_binding_id",
                table: "ie_registration_provider_binding_publications",
                columns: new[] { "tenant_id", "registration_provider_binding_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_registration_attempts_registration_provider_binding_publications_tenant_id_registration_provider_binding_id_prov_8dc716109008",
                table: "ie_registration_attempts",
                columns: new[] { "tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key" },
                principalTable: "ie_registration_provider_binding_publications",
                principalColumns: new[] { "tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_registration_provider_bindings_registration_provider_binding_publications_tenant_id_id_published_mapping_revisio_6cd1eac97312",
                table: "ie_registration_provider_bindings",
                columns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key", "published_at" },
                principalTable: "ie_registration_provider_binding_publications",
                principalColumns: new[] { "tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key", "published_at" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_registration_attempts_registration_provider_binding_publications_tenant_id_registration_provider_binding_id_prov_8dc716109008",
                table: "ie_registration_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_registration_provider_bindings_registration_provider_binding_publications_tenant_id_id_published_mapping_revisio_6cd1eac97312",
                table: "ie_registration_provider_bindings");

            migrationBuilder.DropTable(
                name: "ie_registration_provider_binding_publications");

            migrationBuilder.DropIndex(
                name: "ix_registration_provider_bindings_tenant_id_id_published_mapping_revision_hash_key_published_at",
                table: "ie_registration_provider_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                table: "ie_registration_provider_bindings");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash_key",
                table: "ie_registration_provider_bindings",
                type: "TEXT",
                maxLength: 44,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 44,
                oldDefaultValue: "",
                oldCollation: "BINARY");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash",
                table: "ie_registration_provider_bindings",
                type: "TEXT",
                maxLength: 44,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 44,
                oldNullable: true,
                oldCollation: "BINARY");

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash",
                table: "ie_registration_attempts",
                type: "TEXT",
                maxLength: 44,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 44,
                oldNullable: true,
                oldCollation: "BINARY");

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash_key",
                table: "ie_registration_attempts",
                type: "TEXT",
                maxLength: 44,
                nullable: false,
                computedColumnSql: "COALESCE(provider_mapping_revision_hash, '')",
                stored: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 44,
                oldComputedColumnSql: "COALESCE(provider_mapping_revision_hash, '')",
                oldStored: true,
                oldCollation: "BINARY");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_registration_provider_bindings_tenant_id_id_published_mapping_revision_hash_key",
                table: "ie_registration_provider_bindings",
                columns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                table: "ie_registration_provider_bindings",
                sql: "(state_id = 2 AND published_mapping_revision_hash IS NOT NULL AND published_at IS NOT NULL) OR (state_id <> 2)");

            migrationBuilder.AddForeignKey(
                name: "fk_registration_attempts_registration_provider_bindings_tenant_id_registration_provider_binding_id_provider_mapping_90454f848571",
                table: "ie_registration_attempts",
                columns: new[] { "tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key" },
                principalTable: "ie_registration_provider_bindings",
                principalColumns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
