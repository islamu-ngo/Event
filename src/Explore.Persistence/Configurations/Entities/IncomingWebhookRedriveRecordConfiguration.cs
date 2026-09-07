using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class IncomingWebhookRedriveRecordConfiguration : IEntityTypeConfiguration<IncomingWebhookRedriveRecord>
{
    public void Configure(EntityTypeBuilder<IncomingWebhookRedriveRecord> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_incoming_webhook_redrive_records_generation_order",
                "target_processing_generation > source_processing_generation AND source_processing_generation >= 1");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.ActorId).HasMaxLength(IncomingWebhookRedriveRecord.MaxActorIdLength).IsRequired();
        builder.Property(e => e.Reason).HasMaxLength(IncomingWebhookRedriveRecord.MaxReasonLength).IsRequired();
        builder.Property(e => e.ResultId).IsRequired();
        builder.Ignore(e => e.Result);

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ResultLookup)
            .WithMany()
            .HasForeignKey(e => e.ResultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new
        {
            e.TenantId,
            e.IncomingWebhookMessageId,
            e.TargetProcessingGeneration
        }).IsUnique();
    }
}
