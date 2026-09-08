using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class TenantUserConfiguration : IEntityTypeConfiguration<TenantUser>
{
    public void Configure(EntityTypeBuilder<TenantUser> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.StatusId).IsRequired().HasDefaultValue((int)TenantUserStatusEnum.Active);
        builder.Property(e => e.ModerationNote).HasMaxLength(2000);
        builder.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("NOW()");

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasAlternateKey(e => new { e.TenantId, e.UserId });

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasIndex(e => new { e.TenantId, e.ActorId })
            .IsUnique()
            .HasFilter("actor_id IS NOT NULL");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_tenant_users_status",
            $"status_id IN ({(int)TenantUserStatusEnum.Active}, {(int)TenantUserStatusEnum.Suspended}, {(int)TenantUserStatusEnum.Banned}, {(int)TenantUserStatusEnum.Removed})"));
    }
}
