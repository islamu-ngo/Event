using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Registration;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.RegistrationAnswerFiles.Queries;

public sealed record GetRegistrationAnswerFileQuery(Guid TenantId, Guid Id)
    : IRequest<RegistrationAnswerFileDto?>;

public sealed class GetRegistrationAnswerFileQueryHandler(IRegistrationAnswerFileRepository repository)
    : IRequestHandler<GetRegistrationAnswerFileQuery, RegistrationAnswerFileDto?>
{
    public async Task<RegistrationAnswerFileDto?> Handle(
        GetRegistrationAnswerFileQuery request,
        CancellationToken cancellationToken)
    {
        RegistrationAnswerFile? file = await repository.GetAsync(request.TenantId, request.Id, cancellationToken);
        if (file is null)
        {
            return null;
        }

        RegistrationAnswerFileRelease? release = file.IsReleased
            ? await repository.GetReleaseAsync(request.TenantId, request.Id, cancellationToken)
            : null;
        return Map(file, release);
    }

    private static RegistrationAnswerFileDto Map(
        RegistrationAnswerFile file,
        RegistrationAnswerFileRelease? release)
        => new(
            file.Id,
            file.RegistrationSubmissionId,
            file.RegistrationFormFieldId,
            file.StorageObjectId,
            file.SafeDisplayName,
            file.ContentType,
            file.Extension,
            file.Size,
            file.QuarantineState,
            file.ScanStatus,
            file.QuarantinedAt,
            file.ReleasedBy,
            file.ReleasedAt,
            release?.Reason);
}
