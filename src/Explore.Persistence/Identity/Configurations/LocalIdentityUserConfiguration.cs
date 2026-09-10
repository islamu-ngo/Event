using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Identity.Configurations;

public sealed class LocalIdentityUserConfiguration : IEntityTypeConfiguration<LocalIdentityUser>
{
    public void Configure(EntityTypeBuilder<LocalIdentityUser> builder)
    {
        builder.HasKey(user => user.Id);

        builder.HasIndex(user => user.NormalizedUserName)
            .HasDatabaseName("identity_user_name_index")
            .IsUnique();
        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("identity_email_index")
            .IsUnique();

        builder.Property(user => user.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(user => user.UserName).HasMaxLength(256);
        builder.Property(user => user.NormalizedUserName).HasMaxLength(256);
        builder.Property(user => user.Email).HasMaxLength(256);
        builder.Property(user => user.NormalizedEmail).HasMaxLength(256);
        builder.Property(user => user.PhoneNumber).HasMaxLength(64);
        builder.Property(user => user.FirstName).HasMaxLength(200).IsRequired();
        builder.Property(user => user.LastName).HasMaxLength(200).IsRequired();

        builder.HasMany<IdentityUserClaim<Guid>>()
            .WithOne()
            .HasForeignKey(claim => claim.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IdentityUserLogin<Guid>>()
            .WithOne()
            .HasForeignKey(login => login.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IdentityUserToken<Guid>>()
            .WithOne()
            .HasForeignKey(token => token.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IdentityUserRole<Guid>>()
            .WithOne()
            .HasForeignKey(userRole => userRole.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
