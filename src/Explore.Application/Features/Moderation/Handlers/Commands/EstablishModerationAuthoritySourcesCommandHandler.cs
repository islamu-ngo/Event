using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class EstablishModerationAuthoritySourcesCommandHandler(IModerationAuthoritySourceRepository sources)
    : ICommandHandler<EstablishModerationAuthoritySourcesCommand, ModerationAuthoritySourcesResponse>
{
    public async Task<ModerationAuthoritySourcesResponse> ExecuteAsync(
        EstablishModerationAuthoritySourcesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new EstablishModerationAuthoritySourcesValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationAuthoritySourcesResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var result = await sources.EstablishAsync(new ModerationAuthoritySourceInstallation(
            command.HostBindingId, command.TrustDomainId, command.SolutionId, command.InstanceId,
            command.MaintenanceServicePrincipalId, command.TenantId, command.EpochId), cancellationToken);
        return ModerationAuthoritySourcesResponse.FromOutcome(command.HostBindingId, result);
    }
}

public sealed class EstablishModerationAuthoritySourcesValidator : AbstractValidator<EstablishModerationAuthoritySourcesCommand>
{
    public EstablishModerationAuthoritySourcesValidator()
    {
        RuleFor(command => command.HostBindingId).Must(IsUuidV7);
        RuleFor(command => command.TrustDomainId).Must(IsUuidV7);
        RuleFor(command => command.SolutionId).Must(IsUuidV7);
        RuleFor(command => command.InstanceId).Must(IsUuidV7);
        RuleFor(command => command.MaintenanceServicePrincipalId).Must(IsUuidV7);
        RuleFor(command => command.TenantId).Must(IsUuidV7);
        RuleFor(command => command.EpochId).Must(IsUuidV7);
    }

    private static bool IsUuidV7(Guid value) =>
        value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}
