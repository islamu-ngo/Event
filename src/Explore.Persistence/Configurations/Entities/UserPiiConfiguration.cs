namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class UserPiiConfiguration : IEntityTypeConfiguration<UserPii>
{
    public void Configure(EntityTypeBuilder<UserPii> builder)
    {
        builder.HasKey(e => e.UserId);

        builder.Property(e => e.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(e => e.FirstName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.LastName)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(e => e.Email);
    }
}
