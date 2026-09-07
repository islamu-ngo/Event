using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class TenantInvitationConfiguration : IEntityTypeConfiguration<TenantInvitation>
{
    public void Configure(EntityTypeBuilder<TenantInvitation> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(e => e.Token)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(e => e.ExpiresAt)
            .IsRequired();

        builder.Property(e => e.IsAccepted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.AllowedDomain)
            .HasMaxLength(255);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // Unique token index for secure lookup
        builder.HasIndex(e => e.Token)
            .IsUnique();

        // Composite index for finding pending invitations per tenant+email
        builder.HasIndex(e => new { e.TenantId, e.Email });

        // Relationships
        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
