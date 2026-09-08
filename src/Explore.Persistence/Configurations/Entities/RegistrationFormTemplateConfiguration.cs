using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationFormTemplateConfiguration : IEntityTypeConfiguration<RegistrationFormTemplate>
{
    public void Configure(EntityTypeBuilder<RegistrationFormTemplate> builder)
    {
        builder.Property(template => template.Id).ValueGeneratedNever();
        builder.Property(template => template.Name).IsRequired().HasMaxLength(200);
        builder.Property(template => template.Description).IsRequired().HasMaxLength(1000);
        builder.Property(template => template.Category).IsRequired().HasMaxLength(100);
        builder.Property(template => template.PackKey).HasMaxLength(100);
        builder.Property(template => template.CreatedAt).IsRequired();
        builder.Property(template => template.IsDeleted).HasDefaultValue(false);
        builder.Property(template => template.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(template => template.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(template => new { template.TenantId, template.Category, template.Name });
        builder.HasIndex(template => new { template.SourceRegistrationFormId, template.SourceRegistrationFormVersionId });
    }
}
