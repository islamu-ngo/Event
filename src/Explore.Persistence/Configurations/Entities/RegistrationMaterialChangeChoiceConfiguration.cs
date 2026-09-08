using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationMaterialChangeChoiceConfiguration
    : IEntityTypeConfiguration<RegistrationMaterialChangeChoice>
{
    public void Configure(EntityTypeBuilder<RegistrationMaterialChangeChoice> builder)
    {
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Status).HasConversion<int>();
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.RefundCampaignId,
            value.PaymentAttemptId,
            value.PaidOrderAcceptanceSnapshotId
        }).IsUnique();
        builder.HasIndex(value => new { value.TenantId, value.RegistrationOrderId, value.Status });
        builder.HasOne<RefundCampaign>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.RefundCampaignId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttempt>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PaymentAttemptId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaidOrderAcceptanceSnapshot>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PaidOrderAcceptanceSnapshotId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
