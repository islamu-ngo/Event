using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Reads bounded Global account columns without moving tenant-filtered media or transactional repositories.</summary>
public sealed class UserProfileReader(
    IGlobalExploreDbContextFactory globalFactory,
    IStorageObjectRepository storageObjects) : IUserProfileReader
{
    public async Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        User? user;
        await using (ExploreDbContext context = await globalFactory.CreateDbContextAsync(cancellationToken))
        {
            user = await context.Users.IgnoreAutoIncludes().AsNoTracking()
                .Where(row => row.Id == userId)
                .Select(row => new User
                {
                    Id = row.Id,
                    EmailVerified = row.EmailVerified,
                    ConcurrencyStamp = row.ConcurrencyStamp,
                    Pii = row.Pii == null ? null! : new UserPii
                    {
                        UserId = row.Pii.UserId,
                        Email = row.Pii.Email,
                        FirstName = row.Pii.FirstName,
                        LastName = row.Pii.LastName
                    },
                    Actor = row.Actor == null ? null : new Actor
                    {
                        Id = row.Actor.Id,
                        ActorType = null!,
                        BackgroundColor = row.Actor.BackgroundColor,
                        BackgroundEffect = row.Actor.BackgroundEffect,
                        BannerColor = row.Actor.BannerColor,
                        Pii = row.Actor.Pii == null ? null! : new ActorPii
                        {
                            ActorId = row.Actor.Pii.ActorId,
                            DisplayName = row.Actor.Pii.DisplayName,
                            ProfilePictureStorageObjectId = row.Actor.Pii.ProfilePictureStorageObjectId,
                            ExternalProfilePictureUri = row.Actor.Pii.ExternalProfilePictureUri
                        }
                    }
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (user?.Actor?.Pii is { ProfilePictureStorageObjectId: { } imageId } pii)
            pii.ProfilePicture = await storageObjects.GetForGenericAccessAsync(imageId, cancellationToken);
        return user;
    }
}
