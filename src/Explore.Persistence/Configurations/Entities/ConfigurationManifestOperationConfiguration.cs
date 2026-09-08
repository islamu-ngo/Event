using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ConfigurationManifestOperationConfiguration
    : IEntityTypeConfiguration<ConfigurationManifestOperation>
{
    public void Configure(EntityTypeBuilder<ConfigurationManifestOperation> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_configuration_manifest_operations_counts",
                "requested_tenant_count >= 0 AND created_tenant_count >= 0 " +
                "AND skipped_existing_tenant_count >= 0 AND failed_tenant_count >= 0");
            table.HasCheckConstraint(
                "ck_configuration_manifest_operations_timestamps",
                "completed_at >= started_at");
            table.HasCheckConstraint(
                "ck_configuration_manifest_operations_outcome",
                "(status = 'Validated' AND mode = 'ValidateOnly' AND created_tenant_count = 0 " +
                "AND skipped_existing_tenant_count = 0 AND failed_tenant_count = 0 " +
                "AND reason_code IS NULL AND reason IS NULL) OR " +
                "(status = 'Applied' AND mode = 'Bootstrap' " +
                "AND created_tenant_count + skipped_existing_tenant_count = requested_tenant_count " +
                "AND failed_tenant_count = 0 AND reason_code IS NULL AND reason IS NULL " +
                "AND instance_section_digest IS NOT NULL AND bootstrap_generation > 0) OR " +
                "(status = 'Failed' AND created_tenant_count = 0 AND skipped_existing_tenant_count = 0 " +
                "AND reason_code IS NOT NULL AND reason IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_configuration_manifest_operations_bootstrap_state",
                "(instance_section_digest IS NULL AND bootstrap_generation IS NULL) " +
                "OR (instance_section_digest IS NOT NULL AND bootstrap_generation > 0)");
        });

        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.ApiVersion)
            .HasMaxLength(ConfigurationManifestOperation.MaxApiVersionLength)
            .IsRequired();
        builder.Property(operation => operation.Kind)
            .HasMaxLength(ConfigurationManifestOperation.MaxKindLength)
            .IsRequired();
        builder.Property(operation => operation.ManifestName)
            .HasMaxLength(ConfigurationManifestOperation.MaxManifestNameLength)
            .IsRequired();
        builder.Property(operation => operation.Digest)
            .HasMaxLength(ConfigurationManifestOperation.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.InstanceSectionDigest)
            .HasMaxLength(ConfigurationManifestOperation.DigestLength)
            .IsFixedLength();
        builder.Property(operation => operation.BootstrapGeneration);
        builder.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.ReasonCode)
            .HasMaxLength(ConfigurationManifestOperation.MaxReasonCodeLength);
        builder.Property(operation => operation.Reason)
            .HasMaxLength(ConfigurationManifestOperation.MaxReasonLength);
        builder.Property<string>("_instanceChangedSettingKeyNames")
            .HasColumnName("instance_changed_setting_key_names")
            .HasMaxLength(
                ConfigurationManifestOperation.MaxChangedKeyNamesLength)
            .IsRequired();
        builder.Property<string>("_instanceChangedDocumentKeyNames")
            .HasColumnName("instance_changed_document_key_names")
            .HasMaxLength(
                ConfigurationManifestOperation.MaxChangedKeyNamesLength)
            .IsRequired();

        builder.HasIndex(operation => new { operation.Digest, operation.Mode, operation.CompletedAt })
            .HasDatabaseName("ix_configuration_manifest_operations_digest_mode_completed")
            .IsDescending(false, false, true);
        builder.HasIndex(operation => new { operation.Status, operation.CompletedAt })
            .HasDatabaseName("ix_configuration_manifest_operations_status_completed")
            .IsDescending(false, true);
        builder.HasIndex(operation => new
            {
                operation.Status,
                operation.BootstrapGeneration,
                operation.CompletedAt
            })
            .HasDatabaseName(
                "ix_configuration_manifest_operations_bootstrap_generation_completed")
            .IsDescending(false, true, true);
    }
}
