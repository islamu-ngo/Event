using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class ExternalApiKeyQuotaConfiguration : IEntityTypeConfiguration<ExternalApiKeyQuota>
{
    public void Configure(EntityTypeBuilder<ExternalApiKeyQuota> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.HasIndex(e => new { e.ExternalApiKeyId, e.PeriodStart }).IsUnique();

        builder.HasOne(e => e.ExternalApiKey)
            .WithMany()
            .HasForeignKey(e => e.ExternalApiKeyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
