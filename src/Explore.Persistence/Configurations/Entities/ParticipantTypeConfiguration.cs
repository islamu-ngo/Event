using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ParticipantTypeConfiguration : IEntityTypeConfiguration<ParticipantType>
{
    public void Configure(EntityTypeBuilder<ParticipantType> builder)
    {
        builder.Property(type => type.Id).ValueGeneratedNever();
        builder.Property(type => type.MasterCode).IsRequired().HasMaxLength(50);
        builder.Property(type => type.FullName).IsRequired().HasMaxLength(100);
        builder.HasIndex(type => type.MasterCode).IsUnique();
    }
}
