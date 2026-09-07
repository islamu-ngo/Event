namespace Explore.Persistence.Configurations.Entities;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ConfigurationImportStoredArtifactConfiguration :
    IEntityTypeConfiguration<ConfigurationImportStoredArtifact>
{
    private const int ProtectionOverheadBytes = 16 * 1024;

    public void Configure(
        EntityTypeBuilder<ConfigurationImportStoredArtifact> builder)
    {
        builder.ToTable(
            "configuration_import_artifacts",
            table => table.HasCheckConstraint(
                "ck_configuration_import_artifacts_byte_length",
                $"byte_length BETWEEN 1 AND {ConfigurationImportSessionLimits.MaximumArtifactBytes}"));
        builder.HasKey(artifact => artifact.Id);
        builder.Property(artifact => artifact.ProtectedPayload)
            .HasMaxLength(
                ConfigurationImportSessionLimits.MaximumArtifactBytes
                + ProtectionOverheadBytes)
            .IsRequired();
        builder.Property(artifact => artifact.Sha256Digest)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(artifact => artifact.ByteLength).IsRequired();
        builder.Property(artifact => artifact.CreatedAt)
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
        builder.Property(artifact => artifact.ExpiresAt)
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
        builder.HasIndex(artifact => artifact.ExpiresAt)
            .HasDatabaseName("ix_configuration_import_artifacts_expires_at");
    }
}
