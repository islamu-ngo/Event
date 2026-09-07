using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventSessionStatusConfiguration : IEntityTypeConfiguration<EventSessionStatus>
{
    public void Configure(EntityTypeBuilder<EventSessionStatus> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.MasterCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);

    }
}
