using Explore.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class PolicyChangeOutboxConfiguration : IEntityTypeConfiguration<PolicyChangeOutbox>
{
    public void Configure(EntityTypeBuilder<PolicyChangeOutbox> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.LastError).HasMaxLength(2000);

        builder.HasIndex(x => new { x.Status, x.NextRetryAt });
    }
}
