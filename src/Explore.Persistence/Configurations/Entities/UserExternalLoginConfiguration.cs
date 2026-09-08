using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class UserExternalLoginConfiguration : IEntityTypeConfiguration<UserExternalLogin>
{
    public void Configure(EntityTypeBuilder<UserExternalLogin> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.ProviderKey).HasMaxLength(2_048).IsRequired();
        builder.Property(e => e.ProviderDisplayName).HasMaxLength(500);

        builder.HasIndex(e => new { e.AuthenticationProviderId, e.ProviderKey })
            .IsUnique();

        builder.HasOne(e => e.AuthenticationProvider)
            .WithMany()
            .HasForeignKey(e => e.AuthenticationProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}
