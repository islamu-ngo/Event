using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RefundCampaignConfiguration : IEntityTypeConfiguration<RefundCampaign>
{
    public void Configure(EntityTypeBuilder<RefundCampaign> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_refund_campaigns_kind", "kind BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_refund_campaigns_status", "status BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "ck_refund_campaigns_counts",
                "total_payment_count >= 0 AND generated_count >= 0 AND pending_count >= 0 AND succeeded_count >= 0 AND failed_count >= 0 AND unknown_count >= 0 AND operator_case_count >= 0 AND generated_count <= total_payment_count");
            table.HasCheckConstraint(
                "ck_refund_campaigns_cursor",
                "cursor >= 0");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Kind).HasConversion<int>();
        builder.Property(value => value.Status).HasConversion<int>();
        builder.Property(value => value.DecisionReason).IsRequired().HasMaxLength(500);
        builder.Property(value => value.DecisionAt).IsRequired();
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.EventId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.EventId, value.Kind, value.DecisionAt }).IsUnique();
        builder.HasIndex(value => new { value.Status, value.ProcessingLeaseExpiresAt, value.DecisionAt, value.Id });
    }
}
