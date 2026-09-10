namespace Explore.Persistence.Configurations.Entities;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ConfigurationImportSessionConfiguration :
    IEntityTypeConfiguration<ConfigurationImportSession>
{
    public void Configure(EntityTypeBuilder<ConfigurationImportSession> builder)
    {
        builder.ToTable(
            "configuration_import_sessions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_configuration_import_sessions_target",
                    "((target_scope = 1 AND target_tenant_id IS NULL) OR "
                    + "(target_scope = 2 AND target_tenant_id IS NOT NULL))");
                table.HasCheckConstraint(
                    "ck_configuration_import_sessions_state",
                    "state BETWEEN 1 AND 5");
                table.HasCheckConstraint(
                    "ck_configuration_import_sessions_artifact_length",
                    $"artifact_byte_length BETWEEN 1 AND {ConfigurationImportSessionLimits.MaximumArtifactBytes}");
            });
        builder.HasKey(session => session.SessionId);
        builder.Ignore(session => session.Target);
        builder.Ignore(session => session.Artifact);
        builder.Ignore(session => session.PreviewBinding);
        builder.Property(session => session.TargetScope).IsRequired();
        builder.Property(session => session.TargetTenantId);
        builder.Property(session => session.TargetAuthorityKey)
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(session => session.ArtifactHandleId).IsRequired();
        builder.Property(session => session.ArtifactDigest)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(session => session.ArtifactByteLength).IsRequired();
        builder.Property(session => session.ArtifactExpiresAt)
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
        builder.Property(session => session.AccessTokenDigest)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(session => session.State).IsRequired();
        builder.Property(session => session.CreatedAt)
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
        builder.Property(session => session.UpdatedAt)
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
        builder.Property(session => session.ExpiresAt)
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
        builder.Property(session => session.CancelledAt)
            .HasConversion(
                value => value,
                value => value.HasValue
                    ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                    : null);
        builder.Property(session => session.ConsumedAt)
            .HasConversion(
                value => value,
                value => value.HasValue
                    ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                    : null);
        builder.Property(session => session.PreviewExpiresAt)
            .HasConversion(
                value => value,
                value => value.HasValue
                    ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                    : null);
        builder.Property(session => session.PreviewArtifactDigest)
            .HasMaxLength(64)
            .IsFixedLength();
        builder.Property(session => session.PreviewTargetRevisionDigest)
            .HasMaxLength(64)
            .IsFixedLength();
        builder.Property(session => session.PreviewSelectedSectionsDigest)
            .HasMaxLength(64)
            .IsFixedLength();
        builder.Property(session => session.PreviewMappingDigest)
            .HasMaxLength(64)
            .IsFixedLength();
        builder.Property(session => session.PreviewRequiredApprovalDigest)
            .HasMaxLength(64)
            .IsFixedLength();
        builder.Property(session => session.Revision)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(session => new
        {
            session.TargetAuthorityKey,
            session.State,
            session.ExpiresAt
        })
            .HasDatabaseName(
                "ix_configuration_import_sessions_target_state_expiry");
        builder.HasIndex(session => session.ArtifactHandleId)
            .IsUnique()
            .HasDatabaseName(
                "ux_configuration_import_sessions_artifact_handle");
    }
}
