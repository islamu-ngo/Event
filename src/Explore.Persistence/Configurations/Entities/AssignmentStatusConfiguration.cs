using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AssignmentStatusConfiguration : IEntityTypeConfiguration<AssignmentStatus>
{
    public void Configure(EntityTypeBuilder<AssignmentStatus> builder)
    {
        builder.Property(status => status.Id).ValueGeneratedNever();
        builder.Property(status => status.MasterCode).IsRequired().HasMaxLength(50);
        builder.Property(status => status.FullName).IsRequired().HasMaxLength(100);
        builder.HasIndex(status => status.MasterCode).IsUnique();
    }
}
