using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Registration;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class RegistrationAnswerFileLinkPolicy : ILinkPolicy<RegistrationAnswerFileDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationAnswerFileDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetRegistrationAnswerFile,
            new { id = dto.Id },
            HttpMethods.Get,
            RequiresAuth: true);

        yield return new LinkDefinition(
            "storage-object",
            RouteNames.GetStorageObjectById,
            new { id = dto.StorageObjectId },
            HttpMethods.Get,
            RequiresAuth: true);

        if (dto.ReleasedAt is null)
        {
            yield return new LinkDefinition(
                LinkRelations.Release,
                RouteNames.ReleaseRegistrationAnswerFile,
                new { id = dto.Id },
                HttpMethods.Post,
                "Release quarantined file",
                RequiresAuth: true);
        }
    }
}

/// <summary>
/// Answer-file download affordances live on the detail policy, which carries the answer scope needed to
/// authorize them. The collection shape adds none.
/// </summary>
public sealed class RegistrationAnswerFileCollectionLinkPolicy
    : ICollectionLinkPolicy<RegistrationAnswerFileDto>;
