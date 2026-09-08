using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventRoleAssignmentConfiguration : IEntityTypeConfiguration<EventRoleAssignment>
{
    public void Configure(EntityTypeBuilder<EventRoleAssignment> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.StartsAtUtc).IsRequired();
        builder.Property(e => e.Version).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.UserId, e.RoleId })
            .IsUnique()
            .HasFilter("status IN (1, 2)");

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.EventId, e.Status });

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.UserId, e.Status });

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.RoleId, e.Status });

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_event_role_assignments_validity_window",
            "expires_at_utc IS NULL OR expires_at_utc > starts_at_utc"));
    }
}
