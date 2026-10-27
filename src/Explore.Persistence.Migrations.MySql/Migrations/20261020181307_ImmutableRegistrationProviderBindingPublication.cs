using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class ImmutableRegistrationProviderBindingPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ie_registration_attempts_ie_registration_provider_bi_8d371343",
                table: "ie_registration_attempts");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_ie_registration_provider_bindings_tenant_id_id_publi_1e608e3d",
                table: "ie_registration_provider_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                table: "ie_registration_provider_bindings");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash_key",
                table: "ie_registration_provider_bindings",
                type: "varchar(44)",
                maxLength: 44,
                nullable: false,
                defaultValue: "",
                collation: "ascii_bin",
                oldClrType: typeof(string),
                oldType: "varchar(44)",
                oldMaxLength: 44,
                oldDefaultValue: "")
                .Annotation("MySql:CharSet", "ascii")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash",
                table: "ie_registration_provider_bindings",
                type: "varchar(44)",
                maxLength: 44,
                nullable: true,
                collation: "ascii_bin",
                oldClrType: typeof(string),
                oldType: "varchar(44)",
                oldMaxLength: 44,
                oldNullable: true)
                .Annotation("MySql:CharSet", "ascii")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash_key",
                table: "ie_registration_attempts",
                type: "varchar(44)",
                maxLength: 44,
                nullable: false,
                collation: "ascii_bin",
                oldClrType: typeof(string),
                oldType: "varchar(44)",
                oldMaxLength: 44)
                .Annotation("MySql:CharSet", "ascii")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash",
                table: "ie_registration_attempts",
                type: "varchar(44)",
                maxLength: 44,
                nullable: true,
                collation: "ascii_bin",
                oldClrType: typeof(string),
                oldType: "varchar(44)",
                oldMaxLength: 44,
                oldNullable: true)
                .Annotation("MySql:CharSet", "ascii")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_registration_provider_binding_publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    registration_provider_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mapping_revision_hash_key = table.Column<string>(type: "varchar(44)", maxLength: 44, nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    published_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_registration_provider_binding_publications", x => x.id);
                    table.UniqueConstraint("ak_ie_registration_provider_binding_publications_tenant_62ecb541", x => new { x.tenant_id, x.registration_provider_binding_id, x.mapping_revision_hash_key, x.published_at });
                    table.UniqueConstraint("ak_ie_registration_provider_binding_publications_tenant_b7c798ce", x => new { x.tenant_id, x.registration_provider_binding_id, x.mapping_revision_hash_key });
                    table.ForeignKey(
                        name: "fk_ie_registration_provider_binding_publications_ie_ten_4fdf8eac",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_registration_provider_bindings_tenant_id_id_publi_b0aea94a",
                table: "ie_registration_provider_bindings",
                columns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key", "published_at" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                table: "ie_registration_provider_bindings",
                sql: "(state_id = 2 AND published_mapping_revision_hash IS NOT NULL AND published_at IS NOT NULL AND published_mapping_revision_hash_key = published_mapping_revision_hash) OR (state_id <> 2 AND published_mapping_revision_hash IS NULL AND published_at IS NULL AND published_mapping_revision_hash_key = '')");

            migrationBuilder.CreateIndex(
                name: "ix_ie_registration_provider_binding_publications_tenant_21cd1bf4",
                table: "ie_registration_provider_binding_publications",
                columns: new[] { "tenant_id", "registration_provider_binding_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_ie_registration_attempts_ie_registration_provider_bi_95d5eaef",
                table: "ie_registration_attempts",
                columns: new[] { "tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key" },
                principalTable: "ie_registration_provider_binding_publications",
                principalColumns: new[] { "tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ie_registration_provider_bindings_ie_registration_pr_97b81ac1",
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
                name: "fk_ie_registration_attempts_ie_registration_provider_bi_95d5eaef",
                table: "ie_registration_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_ie_registration_provider_bindings_ie_registration_pr_97b81ac1",
                table: "ie_registration_provider_bindings");

            migrationBuilder.DropTable(
                name: "ie_registration_provider_binding_publications");

            migrationBuilder.DropIndex(
                name: "ix_ie_registration_provider_bindings_tenant_id_id_publi_b0aea94a",
                table: "ie_registration_provider_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                table: "ie_registration_provider_bindings");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash_key",
                table: "ie_registration_provider_bindings",
                type: "varchar(44)",
                maxLength: 44,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(44)",
                oldMaxLength: 44,
                oldDefaultValue: "",
                oldCollation: "ascii_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "ascii");

            migrationBuilder.AlterColumn<string>(
                name: "published_mapping_revision_hash",
                table: "ie_registration_provider_bindings",
                type: "varchar(44)",
                maxLength: 44,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(44)",
                oldMaxLength: 44,
                oldNullable: true,
                oldCollation: "ascii_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "ascii");

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash_key",
                table: "ie_registration_attempts",
                type: "varchar(44)",
                maxLength: 44,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(44)",
                oldMaxLength: 44,
                oldCollation: "ascii_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "ascii");

            migrationBuilder.AlterColumn<string>(
                name: "provider_mapping_revision_hash",
                table: "ie_registration_attempts",
                type: "varchar(44)",
                maxLength: 44,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(44)",
                oldMaxLength: 44,
                oldNullable: true,
                oldCollation: "ascii_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "ascii");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_ie_registration_provider_bindings_tenant_id_id_publi_1e608e3d",
                table: "ie_registration_provider_bindings",
                columns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_registration_provider_bindings_publication",
                table: "ie_registration_provider_bindings",
                sql: "(state_id = 2 AND published_mapping_revision_hash IS NOT NULL AND published_at IS NOT NULL) OR (state_id <> 2)");

            migrationBuilder.AddForeignKey(
                name: "fk_ie_registration_attempts_ie_registration_provider_bi_8d371343",
                table: "ie_registration_attempts",
                columns: new[] { "tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key" },
                principalTable: "ie_registration_provider_bindings",
                principalColumns: new[] { "tenant_id", "id", "published_mapping_revision_hash_key" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
