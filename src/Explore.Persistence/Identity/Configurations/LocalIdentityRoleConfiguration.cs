using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Identity.Configurations;

public sealed class LocalIdentityRoleConfiguration : IEntityTypeConfiguration<LocalIdentityRole>
{
    public void Configure(EntityTypeBuilder<LocalIdentityRole> builder)
    {
        builder.HasKey(role => role.Id);

        builder.HasIndex(role => role.NormalizedName)
            .HasDatabaseName("identity_role_name_index")
            .IsUnique();
        builder.Property(role => role.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(role => role.Name).HasMaxLength(256);
        builder.Property(role => role.NormalizedName).HasMaxLength(256);

        builder.HasMany<IdentityUserRole<Guid>>()
            .WithOne()
            .HasForeignKey(userRole => userRole.RoleId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IdentityRoleClaim<Guid>>()
            .WithOne()
            .HasForeignKey(claim => claim.RoleId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
