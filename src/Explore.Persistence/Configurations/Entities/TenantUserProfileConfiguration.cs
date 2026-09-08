using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class TenantUserProfileConfiguration : IEntityTypeConfiguration<TenantUserProfile>
{
    public void Configure(EntityTypeBuilder<TenantUserProfile> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.DisplayNameOverride).HasMaxLength(256);
        builder.Property(e => e.ContactEmailOverride).HasMaxLength(320);
        builder.Property(e => e.Locale).HasMaxLength(35);
        builder.Property(e => e.TimeZone).HasMaxLength(128);
        builder.Property(e => e.PreferencesJson).HasColumnType("jsonb");
        builder.Property(e => e.ConsentJson).HasColumnType("jsonb");
        builder.Property(e => e.AdminNote).HasMaxLength(2000);
        builder.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("NOW()");

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.TenantUser)
            .WithOne(e => e.Profile)
            .HasForeignKey<TenantUserProfile>(e => e.TenantUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TenantUserId)
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.ContactEmailOverride })
            .HasFilter("contact_email_override IS NOT NULL");
    }
}
