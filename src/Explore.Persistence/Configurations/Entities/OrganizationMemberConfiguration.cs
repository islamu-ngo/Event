using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");

        builder.HasOne(m => m.OrganizationTenant)
            .WithMany(o => o.Members)
            .HasForeignKey(m => new { m.TenantId, m.OrganizationTenantId })
            .HasPrincipalKey(o => new { o.TenantId, o.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Role)
            .WithMany()
            .HasForeignKey(m => m.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.OrganizationPosition)
            .WithMany()
            .HasForeignKey(m => m.OrganizationPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== Performance Indexes =====

        // Unique constraint: one membership per user per org
        builder.HasIndex(m => new { m.OrganizationTenantId, m.UserId })
            .IsUnique();

        // Find all orgs for a user (my organizations)
        builder.HasIndex(m => m.UserId);

        // NOTE: Business entity seed data moved to DatabaseSeeder for conditional (Development-only) seeding.
        // See Explore.Persistence/Seed/DatabaseSeeder.cs and SeedData.cs
    }
}
