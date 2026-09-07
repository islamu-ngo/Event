using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EmailDispatchReceiptConfiguration : IEntityTypeConfiguration<EmailDispatchReceipt>
{
    public void Configure(EntityTypeBuilder<EmailDispatchReceipt> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.PublishEventId).IsRequired();
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.ConsumerId).HasMaxLength(200);
        builder.Property(e => e.FailureCode).HasMaxLength(100);
        builder.Property(e => e.FailureMessage).HasMaxLength(1000);
        builder.Property(e => e.ProviderMessageId).HasMaxLength(500);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EmailDispatchOutbox)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EmailDispatchOutboxId, e.PublishEventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id, e.PublishEventId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantId, e.PublishEventId })
            .IsUnique();

        builder.HasIndex(e => new { e.EmailDispatchOutboxId, e.Status });

        builder.HasIndex(e => new { e.TenantId, e.EmailDispatchOutboxId })
            .IsUnique();
    }
}
