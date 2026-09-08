namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ActorPiiConfiguration : IEntityTypeConfiguration<ActorPii>
{
    public void Configure(EntityTypeBuilder<ActorPii> builder)
    {
        builder.HasKey(e => e.ActorId);

        builder.Property(e => e.DisplayName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.ProfilePictureUri)
            .HasMaxLength(500);
    }
}
