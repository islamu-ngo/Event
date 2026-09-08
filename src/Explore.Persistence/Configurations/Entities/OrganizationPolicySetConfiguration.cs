using Explore.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class OrganizationPolicySetConfiguration : IEntityTypeConfiguration<OrganizationPolicySet>
{
    public void Configure(EntityTypeBuilder<OrganizationPolicySet> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(x => x.OrganizationId).IsUnique();

        builder.OwnsOne(x => x.Events, events =>
        {
            ConfigureSlot(events, e => e.AllowUserSubmittedEvents);
            ConfigureSlot(events, e => e.AllowOrganizationSubmittedEvents);
            ConfigureSlot(events, e => e.AllowGroupSubmittedEvents);
            ConfigureSlot(events, e => e.EventCardClickOpensDetailPage);
        });
    }

    private static void ConfigureSlot<TOwner, TPolicy, T>(
        OwnedNavigationBuilder<TOwner, TPolicy> policyBuilder,
        System.Linq.Expressions.Expression<Func<TPolicy, PolicySlot<T>?>> slotSelector)
        where TOwner : class
        where TPolicy : class
    {
        policyBuilder.OwnsOne(slotSelector);
    }
}
