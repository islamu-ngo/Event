using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ConfigurationManifestTenantResultConfiguration
    : IEntityTypeConfiguration<ConfigurationManifestTenantResult>
{
    public void Configure(EntityTypeBuilder<ConfigurationManifestTenantResult> builder)
    {
        builder.Property(result => result.Id).ValueGeneratedNever();
        builder.Property(result => result.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(result => result.ReasonCode)
            .HasMaxLength(ConfigurationManifestTenantResult.MaxReasonCodeLength)
            .IsRequired();
        builder.Property<string>("_changedSettingKeyNames")
            .HasColumnName("changed_setting_key_names")
            .HasMaxLength(ConfigurationManifestTenantResult.MaxChangedKeyNamesLength)
            .IsRequired();
        builder.Property<string>("_changedDocumentKeyNames")
            .HasColumnName("changed_document_key_names")
            .HasMaxLength(ConfigurationManifestTenantResult.MaxChangedKeyNamesLength)
            .IsRequired();
        builder.Ignore(result => result.ChangedSettingKeyNames);
        builder.Ignore(result => result.ChangedDocumentKeyNames);
        builder.Ignore(result => result.ChangedKeyNames);

        builder.HasOne(result => result.Operation)
            .WithMany()
            .HasForeignKey(result => result.OperationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(result => result.Tenant)
            .WithMany()
            .HasForeignKey(result => result.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(result => new { result.TenantId, result.OperationId })
            .HasDatabaseName("ux_configuration_manifest_results_tenant_operation")
            .IsUnique();
        builder.HasIndex(result => new { result.TenantId, result.Status, result.CompletedAt })
            .HasDatabaseName("ix_configuration_manifest_results_tenant_status_completed")
            .IsDescending(false, false, true);
    }
}
