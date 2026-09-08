using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PaymentDisputeConfiguration : IEntityTypeConfiguration<PaymentDispute>
{
    public void Configure(EntityTypeBuilder<PaymentDispute> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_payment_disputes_stage", "stage BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_payment_disputes_status", "status BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_payment_disputes_amount", "amount_minor > 0");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ProviderDisputeId).IsRequired().HasMaxLength(200);
        builder.Property(value => value.Stage).HasConversion<int>();
        builder.Property(value => value.Status).HasConversion<int>();
        builder.Property(value => value.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.LastObservedAt).IsRequired();
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttempt>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PaymentAttemptId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.ProviderDisputeId }).IsUnique();
        builder.HasIndex(value => new { value.TenantId, value.PaymentAttemptId, value.Status });
    }
}
