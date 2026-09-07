using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class WebhookProviderBindingVerificationStateLookupConfiguration
    : IEntityTypeConfiguration<WebhookProviderBindingVerificationStateLookup>
{
    public void Configure(EntityTypeBuilder<WebhookProviderBindingVerificationStateLookup> builder)
    {
        builder.Property(state => state.Id).ValueGeneratedNever();
        builder.Property(state => state.MasterCode).HasMaxLength(100).IsRequired();
        builder.Property(state => state.FullName).HasMaxLength(200).IsRequired();
        builder.Property(state => state.Description).HasMaxLength(500);
        builder.HasIndex(state => state.MasterCode)
            .IsUnique();
    }
}
