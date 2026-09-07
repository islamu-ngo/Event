using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventTypeConfiguration : IEntityTypeConfiguration<EventType>
{
    public void Configure(EntityTypeBuilder<EventType> builder)
    {
        builder.Property(e => e.FullName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.MasterCode)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Global master-data codes (TenantId = NULL) must stay unique.
        builder.HasIndex(e => e.MasterCode)
            .IsUnique()
            .HasFilter("tenant_id IS NULL");

        // Tenant-specific custom codes must be unique per tenant.
        builder.HasIndex(e => new { e.TenantId, e.MasterCode })
            .IsUnique()
            .HasFilter("tenant_id IS NOT NULL");
    }
}
