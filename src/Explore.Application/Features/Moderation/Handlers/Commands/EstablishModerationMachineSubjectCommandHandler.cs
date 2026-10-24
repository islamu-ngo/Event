using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class EstablishModerationMachineSubjectCommandHandler(IModerationMachineSubjectBindingRepository bindings)
    : ICommandHandler<EstablishModerationMachineSubjectCommand, ModerationMachineSubjectResponse>
{
    public async Task<ModerationMachineSubjectResponse> ExecuteAsync(
        EstablishModerationMachineSubjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new EstablishModerationMachineSubjectValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationMachineSubjectResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await bindings.EstablishAsync(new ModerationMachineSubjectInstallation(
            command.Id, command.HostBindingId, command.CredentialSource, command.KeyId,
            command.AuthorityTenantId, command.OwnerType, command.OwnerId, command.ServicePrincipalId), cancellationToken);
        return ModerationMachineSubjectResponse.FromOutcome(command.Id, result);
    }
}

public sealed class EstablishModerationMachineSubjectValidator : AbstractValidator<EstablishModerationMachineSubjectCommand>
{
    public EstablishModerationMachineSubjectValidator()
    {
        RuleFor(command => command.Id).Must(IsUuidV7);
        RuleFor(command => command.HostBindingId).Must(IsUuidV7);
        RuleFor(command => command.CredentialSource).IsInEnum();
        RuleFor(command => command.KeyId).NotEmpty().MaximumLength(64);
        RuleFor(command => command.AuthorityTenantId).Must(value => value is null || IsUuidV7(value.Value));
        RuleFor(command => command.OwnerType).IsInEnum();
        RuleFor(command => command.OwnerId).Must(IsUuidV7);
        RuleFor(command => command.ServicePrincipalId).Must(IsUuidV7);
    }

    private static bool IsUuidV7(Guid value) =>
        value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}
