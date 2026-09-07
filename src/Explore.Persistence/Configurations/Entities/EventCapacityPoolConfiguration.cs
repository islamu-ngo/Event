using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventCapacityPoolConfiguration : IEntityTypeConfiguration<EventCapacityPool>
{
    public void Configure(EntityTypeBuilder<EventCapacityPool> builder)
    {
        builder.Property(pool => pool.Id).ValueGeneratedNever();
        builder.Property(pool => pool.Name).IsRequired().HasMaxLength(200);
        builder.Property(pool => pool.CreatedAt).IsRequired();
        builder.Property(pool => pool.IsDeleted).HasDefaultValue(false);
        builder.Property(pool => pool.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(pool => new { pool.TenantId, pool.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(pool => pool.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany(@event => @event.CapacityPools).HasForeignKey(pool => new { pool.TenantId, pool.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(pool => pool.CapacityHoldPolicy).WithMany().HasForeignKey(pool => pool.CapacityHoldPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(pool => pool.CapacityOversellPolicy).WithMany().HasForeignKey(pool => pool.CapacityOversellPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(pool => new { pool.TenantId, pool.EventId, pool.Name }).IsUnique().HasFilter("is_deleted = false");
    }
}
