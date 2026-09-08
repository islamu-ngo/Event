using Explore.Domain.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities.Federation;

public sealed class AtprotoOutboundRecordOwnershipConfiguration
    : IEntityTypeConfiguration<AtprotoOutboundRecordOwnership>
{
    public void Configure(EntityTypeBuilder<AtprotoOutboundRecordOwnership> builder)
    {
        builder.HasKey(value => value.AtprotoRecordId);
        builder.Property(value => value.SourceEntityType).HasMaxLength(100).IsRequired();
        builder.HasOne(value => value.AtprotoRecord)
            .WithOne()
            .HasForeignKey<AtprotoOutboundRecordOwnership>(value => value.AtprotoRecordId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(value => value.Tenant)
            .WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.User)
            .WithMany()
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.TenantUser)
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.UserId })
            .HasPrincipalKey(value => new { value.TenantId, value.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.SourceEntityType,
            value.SourceEntityId
        })
            .IsUnique();
        builder.HasIndex(value => new { value.TenantId, value.UserId });
    }
}
