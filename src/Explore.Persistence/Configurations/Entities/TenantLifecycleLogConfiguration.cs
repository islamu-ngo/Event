using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class TenantLifecycleLogConfiguration : IEntityTypeConfiguration<TenantLifecycleLog>
{
    public void Configure(EntityTypeBuilder<TenantLifecycleLog> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Reason)
            .HasMaxLength(1000);

        builder.Property(e => e.TransitionedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // Indexes for audit trail queries
        builder.HasIndex(e => new { e.TenantId, e.TransitionedAt });
        builder.HasIndex(e => e.TransitionedByUserId);

        // Relationships
        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.OldStatus)
            .WithMany()
            .HasForeignKey(e => e.OldStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.NewStatus)
            .WithMany()
            .HasForeignKey(e => e.NewStatusId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
