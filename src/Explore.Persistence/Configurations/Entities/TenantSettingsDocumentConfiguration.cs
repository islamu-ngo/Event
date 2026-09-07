namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain.Settings.Documents;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class TenantSettingsDocumentConfiguration : IEntityTypeConfiguration<TenantSettingsDocument>
{
    public void Configure(EntityTypeBuilder<TenantSettingsDocument> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_tenant_settings_documents_schema_version_positive", "schema_version > 0");
            t.HasCheckConstraint("ck_tenant_settings_documents_document_key_not_blank", "length(trim(document_key)) > 0");
            t.HasCheckConstraint("ck_tenant_settings_documents_payload_object", "jsonb_typeof(payload_json) = 'object'");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.DocumentKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.SchemaVersion)
            .IsRequired();
        builder.Property(e => e.DefaultsVersion)
            .IsRequired()
            .HasMaxLength(64);
        builder.Property(e => e.PayloadJson)
            .IsRequired()
            .HasColumnType("jsonb");
        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();
        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(e => new { e.TenantId, e.DocumentKey })
            .IsUnique();
        builder.HasIndex(e => e.DocumentKey);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}
