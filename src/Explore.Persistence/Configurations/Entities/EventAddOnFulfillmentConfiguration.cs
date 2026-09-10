using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventAddOnFulfillmentConfiguration :
    IEntityTypeConfiguration<EventAddOnFulfillment>
{
    public void Configure(EntityTypeBuilder<EventAddOnFulfillment> builder)
    {
        builder.Property(fulfillment => fulfillment.Id).ValueGeneratedNever();
        builder.Property(fulfillment => fulfillment.FulfilledAt).IsRequired();
        builder.Property(fulfillment => fulfillment.CreatedAt).IsRequired();
        builder.Property(fulfillment => fulfillment.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(fulfillment => new { fulfillment.TenantId, fulfillment.Id });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(fulfillment => fulfillment.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrderAddOnLine>()
            .WithMany()
            .HasForeignKey(fulfillment => new
            {
                fulfillment.TenantId,
                fulfillment.EventId,
                fulfillment.RegistrationOrderId,
                fulfillment.RegistrationOrderAddOnLineId,
            })
            .HasPrincipalKey(line => new
            {
                line.TenantId,
                line.EventId,
                line.RegistrationOrderId,
                line.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(fulfillment => new { fulfillment.TenantId, fulfillment.OperationId })
            .IsUnique();
        builder.HasIndex(fulfillment => new
        {
            fulfillment.TenantId,
            fulfillment.RegistrationOrderAddOnLineId,
        })
            .IsUnique();
    }
}
