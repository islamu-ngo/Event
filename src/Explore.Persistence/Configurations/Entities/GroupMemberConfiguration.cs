using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.CreatedAt)
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        builder.HasOne(e => e.GroupTenant)
            .WithMany(g => g.Members)
            .HasForeignKey(e => new { e.TenantId, e.GroupTenantId })
            .HasPrincipalKey(g => new { g.TenantId, g.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.GroupPosition)
            .WithMany()
            .HasForeignKey(e => e.GroupPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.GroupTenantId, e.UserId })
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.UserId });
    }
}
