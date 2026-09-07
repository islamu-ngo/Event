using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationInventoryHoldConfiguration : IEntityTypeConfiguration<RegistrationInventoryHold>
{
    public void Configure(EntityTypeBuilder<RegistrationInventoryHold> builder)
    {
        builder.Property(hold => hold.Id).ValueGeneratedNever();
        builder.Property(hold => hold.CreatedAt).IsRequired();
        builder.Property(hold => hold.IsDeleted).HasDefaultValue(false);
        builder.Property(hold => hold.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(hold => new { hold.TenantId, hold.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(hold => hold.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany().HasForeignKey(hold => new { hold.TenantId, hold.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventCapacityPool>().WithMany().HasForeignKey(hold => new { hold.TenantId, hold.CapacityPoolId })
            .HasPrincipalKey(pool => new { pool.TenantId, pool.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventTicketType>().WithMany().HasForeignKey(hold => new { hold.TenantId, hold.TicketTypeId })
            .HasPrincipalKey(ticketType => new { ticketType.TenantId, ticketType.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(hold => hold.RegistrationInventoryHoldStatus).WithMany().HasForeignKey(hold => hold.RegistrationInventoryHoldStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(hold => new { hold.TenantId, hold.CapacityPoolId, hold.RegistrationInventoryHoldStatusId });
        builder.HasIndex(hold => new { hold.TenantId, hold.RegistrationInventoryHoldStatusId, hold.ExpiresAt });
    }
}
