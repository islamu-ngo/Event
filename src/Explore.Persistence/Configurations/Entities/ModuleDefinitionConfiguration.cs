using Explore.Domain.Modules;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class ModuleDefinitionConfiguration : IEntityTypeConfiguration<ModuleDefinition>
{
    public void Configure(EntityTypeBuilder<ModuleDefinition> builder)
    {
        builder.Property(m => m.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(m => m.ModuleKey).HasMaxLength(50).IsRequired();
        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(500);
        builder.Property(m => m.WizardSchemaUrl).HasMaxLength(2048);
        builder.Property(m => m.IconName).HasMaxLength(50);
        builder.Property(m => m.Category).HasMaxLength(50);

        builder.HasIndex(m => m.ModuleKey).IsUnique();
        builder.HasIndex(m => m.DisplayOrder);

        // Seed default modules
    }
}
