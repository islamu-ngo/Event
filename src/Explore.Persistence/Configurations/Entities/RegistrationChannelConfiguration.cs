using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationChannelConfiguration : IEntityTypeConfiguration<RegistrationChannel>
{
    public void Configure(EntityTypeBuilder<RegistrationChannel> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_registration_channels_provider_shape",
            "(is_native = true AND registration_provider_binding_id IS NULL AND registration_provider_binding_key = '00000000-0000-0000-0000-000000000000') OR " +
            "(is_native = false AND registration_provider_binding_id IS NOT NULL AND registration_provider_binding_key = registration_provider_binding_id)"));
        builder.Property(channel => channel.Id).ValueGeneratedNever();
        var providerBindingKey = builder.Property<Guid>("RegistrationProviderBindingKey");
        providerBindingKey
            .HasColumnType("uuid")
            .HasComputedColumnSql(
                "COALESCE(registration_provider_binding_id, '00000000-0000-0000-0000-000000000000'::uuid)",
                stored: true)
            .ValueGeneratedOnAdd()
            .IsRequired();
        providerBindingKey.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        providerBindingKey.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(channel => channel.CreatedAt).IsRequired();
        builder.Property(channel => channel.IsDeleted).HasDefaultValue(false);
        builder.Property(channel => channel.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(channel => new
        {
            channel.TenantId,
            channel.EventId,
            channel.RegistrationWorkflowId,
            channel.RegistrationRequirementId,
            channel.Id
        });
        builder.HasAlternateKey(
            nameof(RegistrationChannel.TenantId),
            nameof(RegistrationChannel.EventId),
            nameof(RegistrationChannel.RegistrationWorkflowId),
            nameof(RegistrationChannel.RegistrationRequirementId),
            nameof(RegistrationChannel.Id),
            "RegistrationProviderBindingKey");
        builder.HasMany<RegistrationAttempt>().WithOne()
            .HasForeignKey(
                nameof(RegistrationAttempt.TenantId),
                nameof(RegistrationAttempt.EventId),
                nameof(RegistrationAttempt.RegistrationWorkflowId),
                nameof(RegistrationAttempt.RegistrationRequirementId),
                nameof(RegistrationAttempt.RegistrationChannelId),
                "RegistrationProviderBindingKey")
            .HasPrincipalKey(
                nameof(RegistrationChannel.TenantId),
                nameof(RegistrationChannel.EventId),
                nameof(RegistrationChannel.RegistrationWorkflowId),
                nameof(RegistrationChannel.RegistrationRequirementId),
                nameof(RegistrationChannel.Id),
                "RegistrationProviderBindingKey")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(channel => channel.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(channel => new { channel.TenantId, channel.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationRequirement>().WithMany(requirement => requirement.Channels)
            .HasForeignKey(channel => new
            {
                channel.TenantId,
                channel.EventId,
                channel.RegistrationWorkflowId,
                channel.RegistrationRequirementId
            })
            .HasPrincipalKey(requirement => new
            {
                requirement.TenantId,
                requirement.EventId,
                requirement.RegistrationWorkflowId,
                requirement.Id
            })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RegistrationProviderBinding>().WithMany()
            .HasForeignKey(channel => new { channel.TenantId, channel.RegistrationProviderBindingId })
            .HasPrincipalKey(binding => new { binding.TenantId, binding.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(channel => new { channel.RegistrationRequirementId, channel.Ordinal }).IsUnique();
        builder.HasIndex(channel => new { channel.TenantId, channel.EventId });
        builder.HasIndex(channel => channel.RegistrationProviderBindingId);
    }
}
