using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationAmendmentConfiguration : IEntityTypeConfiguration<RegistrationAmendment>
{
    public void Configure(EntityTypeBuilder<RegistrationAmendment> builder)
    {
        builder.Property(amendment => amendment.Id).ValueGeneratedNever();
        builder.Property(amendment => amendment.Reason).HasMaxLength(500).IsRequired();
        builder.Property(amendment => amendment.ChangeKind).HasMaxLength(64).IsRequired();
        builder.Property(amendment => amendment.Source).HasMaxLength(64).IsRequired();
        builder.Property(amendment => amendment.LineageKey).HasMaxLength(128).IsRequired();
        builder.Property(amendment => amendment.OccurredAt).IsRequired();
        builder.Property(amendment => amendment.CreatedAt).IsRequired();
        builder.HasAlternateKey(amendment => new { amendment.TenantId, amendment.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(amendment => amendment.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(amendment => amendment.Event).WithMany()
            .HasForeignKey(amendment => new { amendment.TenantId, amendment.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(amendment => amendment.RegistrationOrder).WithMany()
            .HasForeignKey(amendment => new { amendment.TenantId, amendment.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(amendment => new { amendment.TenantId, amendment.RegistrationOrderId, amendment.Source, amendment.LineageKey });
        builder.HasIndex(amendment => new { amendment.TenantId, amendment.RegistrationOrderLineId, amendment.Ordinal });
        builder.HasIndex(amendment => new
        {
            amendment.TenantId,
            amendment.EventId,
            amendment.RegistrationOrderId,
            amendment.Source,
            amendment.LineageKey,
            amendment.RegistrationOrderLineId,
            amendment.Ordinal
        })
            .IsUnique();
    }
}
