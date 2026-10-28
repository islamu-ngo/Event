using Explore.Domain;
using Explore.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Learning;

public sealed class TenantLearningSpaceConfiguration : IEntityTypeConfiguration<TenantLearningSpace>
{
    public void Configure(EntityTypeBuilder<TenantLearningSpace> builder)
    {
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.Property(value => value.PackKey).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.HasIndex(value => value.TenantId).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
