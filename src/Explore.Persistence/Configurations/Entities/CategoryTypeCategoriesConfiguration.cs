using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class CategoryTypeCategoriesConfiguration : IEntityTypeConfiguration<CategoryTypeCategories>
{
    public void Configure(EntityTypeBuilder<CategoryTypeCategories> builder)
    {
        builder.HasIndex(e => new { e.TenantId, e.CategoryId, e.CategoryTypeId })
            .IsUnique();

        builder.HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.CategoryType)
            .WithMany()
            .HasForeignKey(e => e.CategoryTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
