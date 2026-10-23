using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private void ValidateRegistrationProviderPublications()
    {
        var bindings = ChangeTracker.Entries<RegistrationProviderBinding>().ToArray();
        foreach (var entry in ChangeTracker.Entries<RegistrationProviderBindingPublication>().ToArray())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Provider publication evidence is immutable.");
            if (entry.State != EntityState.Added) continue;

            var publication = entry.Entity;
            var owner = bindings.SingleOrDefault(binding => ReferenceEquals(binding.Entity.Publication, publication));
            bool transition = owner is not null && (owner.State == EntityState.Added
                || owner.State == EntityState.Modified
                && owner.OriginalValues.GetValue<RegistrationEvidenceHash?>(nameof(RegistrationProviderBinding.PublishedMappingRevisionHash)) is null
                && owner.OriginalValues.GetValue<DateTime?>(nameof(RegistrationProviderBinding.PublishedAt)) is null
                && owner.OriginalValues.GetValue<int>(nameof(RegistrationProviderBinding.StateId)) == (int)RegistrationProviderBindingStateEnum.Draft);
            if (!transition || owner!.Entity.IsDeleted
                || owner.Entity.StateId != (int)RegistrationProviderBindingStateEnum.Published
                || publication.Id == Guid.Empty || publication.TenantId == Guid.Empty
                || publication.TenantId != owner.Entity.TenantId
                || publication.RegistrationProviderBindingId != owner.Entity.Id
                || publication.MappingRevisionHashKey != owner.Entity.PublishedMappingRevisionHash?.Value
                || publication.MappingRevisionHashKey != owner.Entity.PublishedMappingRevisionHashKey
                || publication.PublishedAt != owner.Entity.PublishedAt
                || publication.PublishedAt.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException("New provider publication evidence requires its actual publishing binding transition.");
        }

        foreach (var entry in bindings.Where(binding => binding.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var binding = entry.Entity;
            if (entry.State != EntityState.Added
                && entry.OriginalValues.GetValue<int>(nameof(RegistrationProviderBinding.StateId)) == (int)RegistrationProviderBindingStateEnum.Published
                && (entry.State == EntityState.Deleted || binding.IsDeleted
                    || entry.Property(value => value.StateId).IsModified
                    || entry.Property(value => value.PublishedMappingRevisionHash).IsModified
                    || entry.Property(value => value.PublishedMappingRevisionHashKey).IsModified
                    || entry.Property(value => value.PublishedAt).IsModified))
                throw new InvalidOperationException("A persisted binding publication cannot be replaced or removed.");

            if (entry.State == EntityState.Deleted) continue;
            bool published = binding.StateId == (int)RegistrationProviderBindingStateEnum.Published;
            if (published && (binding.Publication is null
                    || binding.Publication.TenantId != binding.TenantId
                    || binding.Publication.RegistrationProviderBindingId != binding.Id
                    || binding.Publication.MappingRevisionHashKey != binding.PublishedMappingRevisionHash?.Value
                    || binding.Publication.MappingRevisionHashKey != binding.PublishedMappingRevisionHashKey
                    || binding.Publication.PublishedAt != binding.PublishedAt)
                || !published && (binding.Publication is not null || binding.PublishedAt is not null
                    || binding.PublishedMappingRevisionHash is not null || binding.PublishedMappingRevisionHashKey.Length != 0))
                throw new InvalidOperationException("Provider binding publication coordinates must be complete and exact.");
        }
    }
}
