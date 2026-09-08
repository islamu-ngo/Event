using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationProviderSubscriptionStateConfiguration : IEntityTypeConfiguration<RegistrationProviderSubscriptionState>
{
    public void Configure(EntityTypeBuilder<RegistrationProviderSubscriptionState> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_registration_provider_subscription_states_generation", "processing_generation >= 0");
            table.HasCheckConstraint("ck_registration_provider_subscription_states_failure_counts", "renewal_failure_count >= 0 AND sweep_failure_count >= 0");
            table.HasCheckConstraint("ck_registration_provider_subscription_states_watch_expiry", "watch_expires_at > created_at");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ProviderEventType).IsRequired().HasMaxLength(RegistrationProviderSubscriptionState.MaxProviderEventTypeLength);
        builder.Property(value => value.WatchId).IsRequired().HasMaxLength(RegistrationProviderSubscriptionState.MaxWatchIdLength);
        builder.Property(value => value.ResponseCheckpoint).HasMaxLength(RegistrationProviderSubscriptionState.MaxResponseCheckpointLength);
        builder.Property(value => value.FailureCategory).HasMaxLength(RegistrationProviderSubscriptionState.MaxFailureCategoryLength);
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.IsDeleted).HasDefaultValue(false);
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Binding)
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.RegistrationProviderBindingId })
            .HasPrincipalKey(binding => new { binding.TenantId, binding.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.RegistrationProviderBindingId, value.ProviderEventType })
            .IsUnique();
        builder.HasIndex(value => new { value.WatchExpiresAt, value.LeaseExpiresAt });
        builder.HasIndex(value => new { value.PendingNotificationAt, value.NextSweepAttemptAt, value.LeaseExpiresAt });
    }
}
