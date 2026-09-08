namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ConfigurationImportOperationConfiguration :
    IEntityTypeConfiguration<ConfigurationImportOperation>
{
    public void Configure(EntityTypeBuilder<ConfigurationImportOperation> builder)
    {
        builder.ToTable(
            "configuration_import_operations",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_configuration_import_operations_target",
                    "((target_authority_key = 'instance' AND target_tenant_id IS NULL) OR "
                    + "(target_authority_key <> 'instance' AND target_tenant_id IS NOT NULL))");
                table.HasCheckConstraint(
                    "ck_configuration_import_operations_kind",
                    "kind BETWEEN 1 AND 2");
                table.HasCheckConstraint(
                    "ck_configuration_import_operations_status",
                    "status BETWEEN 1 AND 4");
            });
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.SessionId).IsRequired();
        builder.Property(operation => operation.Kind).IsRequired();
        builder.Property(operation => operation.Status).IsRequired();
        builder.Property(operation => operation.TargetAuthorityKey)
            .HasMaxLength(ConfigurationImportOperation.MaximumAuthorityKeyLength)
            .IsRequired();
        builder.Property(operation => operation.TargetTenantId);
        builder.Property(operation => operation.ActorUserId).IsRequired();
        builder.Property(operation => operation.SourceOperationId);
        FixedDigest(builder.Property(operation => operation.ArtifactDigest));
        FixedDigest(builder.Property(operation => operation.TargetRevisionDigest));
        FixedDigest(builder.Property(operation => operation.SelectedSectionsDigest));
        FixedDigest(builder.Property(operation => operation.MappingDigest));
        FixedDigest(builder.Property(operation => operation.ApprovalDigest));
        builder.Property(operation => operation.ApplyMode).IsRequired();
        builder.Property(operation => operation.SnapshotArtifactHandleId);
        builder.Property(operation => operation.SnapshotDigest)
            .HasMaxLength(64)
            .IsFixedLength();
        Utc(builder.Property(operation => operation.SnapshotExpiresAt));
        builder.Property(operation => operation.EffectOutboxId);
        builder.Property(operation => operation.FidelityVerified).IsRequired();
        FixedDigest(builder.Property(operation => operation.FidelityDigest));
        builder.Property(operation => operation.FailureCode)
            .HasMaxLength(ConfigurationImportOperation.MaximumFailureCodeLength);
        builder.Property(operation => operation.FailureReason)
            .HasMaxLength(ConfigurationImportOperation.MaximumFailureReasonLength);
        Utc(builder.Property(operation => operation.StartedAt), required: true);
        Utc(builder.Property(operation => operation.CompletedAt));
        builder.Property<string>("_selectedSectionKeys")
            .HasColumnName("selected_section_keys")
            .HasMaxLength(ConfigurationImportOperation.MaximumSectionKeysLength)
            .IsRequired();
        builder.Property<string>("_omittedSectionKeys")
            .HasColumnName("omitted_section_keys")
            .HasMaxLength(ConfigurationImportOperation.MaximumOmittedSectionKeysLength)
            .IsRequired();
        builder.Ignore(operation => operation.SelectedSectionKeys);
        builder.Ignore(operation => operation.OmittedSectionKeys);
        builder.HasIndex(operation => new
            {
                operation.TargetAuthorityKey,
                operation.StartedAt
            });
        builder.HasIndex(operation => operation.SessionId).IsUnique();
        builder.HasIndex(operation => operation.SourceOperationId);
        builder.HasIndex(operation => operation.SnapshotArtifactHandleId).IsUnique();
        builder.HasOne<ConfigurationImportOperation>()
            .WithMany()
            .HasForeignKey(operation => operation.SourceOperationId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void FixedDigest(
        PropertyBuilder<string> property,
        bool required = true)
    {
        property.HasMaxLength(64).IsFixedLength();
        if (required)
            property.IsRequired();
    }

    private static void Utc(
        PropertyBuilder<DateTime> property,
        bool required)
    {
        property.HasConversion(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        if (required)
            property.IsRequired();
    }

    private static void Utc(PropertyBuilder<DateTime?> property)
    {
        property.HasConversion(
            value => value,
            value => value.HasValue
                ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                : null);
    }
}
