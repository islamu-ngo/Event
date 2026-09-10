using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class TicketingRecoveryCheckpointConfiguration :
    IEntityTypeConfiguration<TicketingRecoveryCheckpoint>
{
    public void Configure(
        EntityTypeBuilder<TicketingRecoveryCheckpoint> builder)
    {
        builder.ToTable("ticketing_recovery_checkpoints");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ManifestDigest)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(value => value.ReleaseRevision)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(value => value.SchemaRevision)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(value => value.FailureCode)
            .HasMaxLength(64);
        builder.Property(value => value.ConcurrencyStamp)
            .IsConcurrencyToken();
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.RecoveryOperationId,
        })
            .IsUnique();
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.Status,
        });
    }
}

public sealed class TicketingRecoveryReissueIntentConfiguration :
    IEntityTypeConfiguration<TicketingRecoveryReissueIntent>
{
    public void Configure(
        EntityTypeBuilder<TicketingRecoveryReissueIntent> builder)
    {
        builder.ToTable("ticketing_recovery_reissue_intents");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ConcurrencyStamp)
            .IsConcurrencyToken();
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.RecoveryOperationId,
            value.AdmissionTicketId,
        })
            .IsUnique();
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.Status,
        });
    }
}
