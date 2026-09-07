using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class WebhookDeliveryAttemptConfiguration : IEntityTypeConfiguration<WebhookDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryAttempt> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.OutcomeId).IsRequired();
        builder.Ignore(e => e.Outcome);
        builder.Property(e => e.ProcessingLeaseToken);
        builder.Property(e => e.ProcessingFence).IsRequired().IsConcurrencyToken();
        builder.Property(e => e.ProcessingLeaseExpiresAt);
        builder.Property(e => e.FailureCategory).HasMaxLength(100);

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Message)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.MessageId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Endpoint)
            .WithMany()
            .HasForeignKey(e => e.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OutcomeLookup)
            .WithMany()
            .HasForeignKey(e => e.OutcomeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.MessageId, e.EndpointId, e.AttemptNumber })
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.OutcomeId, e.ScheduledAt, e.CreatedAt });

        builder.HasIndex(e => new { e.TenantId, e.EndpointId, e.OutcomeId, e.ScheduledAt });

        builder.HasIndex(e => new { e.TenantId, e.OutcomeId, e.ProcessingLeaseExpiresAt, e.EndpointId });
    }
}
