using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using FluentValidation.Results;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class AddRegistrationParticipantCommandHandler(RegistrationParticipantCommandService service)
    : ICommandHandler<AddRegistrationParticipantCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AddRegistrationParticipantCommand command, CancellationToken cancellationToken = default)
    {
        ValidationResult validation = await new AddRegistrationParticipantCommandValidator().ValidateAsync(command, cancellationToken);
        return validation.IsValid
            ? await service.AddAsync(command.ParticipantTypeId, command.RegistrationOrderId, command.GuardianParticipantId, command.Details, cancellationToken)
            : Invalid(command.RegistrationOrderId, validation);
    }

    internal static BaseCommandResponse<Guid> Invalid(Guid id, ValidationResult validation) => BaseCommandResponse.Validation(
        validation.Errors.Select(error => error.ErrorMessage), "Registration participant request is invalid.", id);
}

public sealed class UpdateRegistrationParticipantCommandHandler(RegistrationParticipantCommandService service)
    : ICommandHandler<UpdateRegistrationParticipantCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateRegistrationParticipantCommand command, CancellationToken cancellationToken = default)
    {
        ValidationResult validation = await new UpdateRegistrationParticipantCommandValidator().ValidateAsync(command, cancellationToken);
        return validation.IsValid
            ? await service.UpdateAsync(command.RegistrationOrderId, command.ParticipantId, command.ParticipantTypeId, command.GuardianParticipantId, command.Details, cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(command.ParticipantId, validation);
    }
}

public sealed class AssignRegistrationTicketCommandHandler(RegistrationParticipantCommandService service)
    : ICommandHandler<AssignRegistrationTicketCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AssignRegistrationTicketCommand command, CancellationToken cancellationToken = default)
    {
        ValidationResult validation = await new AssignRegistrationTicketCommandValidator().ValidateAsync(command, cancellationToken);
        return validation.IsValid
            ? await service.AssignAsync(command.RegistrationOrderId,
                [new TicketParticipantAssignmentInputDto(command.RegistrationOrderLineId, command.Ordinal, command.ParticipantId)], cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(command.RegistrationOrderId, validation);
    }
}

public sealed class BulkAssignRegistrationTicketsCommandHandler(RegistrationParticipantCommandService service)
    : ICommandHandler<BulkAssignRegistrationTicketsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(BulkAssignRegistrationTicketsCommand command, CancellationToken cancellationToken = default)
    {
        ValidationResult validation = await new BulkAssignRegistrationTicketsCommandValidator().ValidateAsync(command, cancellationToken);
        return validation.IsValid
            ? await service.AssignAsync(command.RegistrationOrderId, command.Assignments, cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(command.RegistrationOrderId, validation);
    }
}

public sealed class DeferRegistrationTicketCommandHandler(RegistrationParticipantCommandService service)
    : ICommandHandler<DeferRegistrationTicketCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeferRegistrationTicketCommand command, CancellationToken cancellationToken = default)
    {
        ValidationResult validation = await new DeferRegistrationTicketCommandValidator().ValidateAsync(command, cancellationToken);
        return validation.IsValid
            ? await service.DeferAsync(command.RegistrationOrderId,
                [new TicketDeferralInputDto(command.RegistrationOrderLineId, command.Ordinal)], command.AssignmentDeadline, cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(command.RegistrationOrderId, validation);
    }
}

public sealed class BulkDeferRegistrationTicketsCommandHandler(RegistrationParticipantCommandService service)
    : ICommandHandler<BulkDeferRegistrationTicketsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(BulkDeferRegistrationTicketsCommand command, CancellationToken cancellationToken = default)
    {
        ValidationResult validation = await new BulkDeferRegistrationTicketsCommandValidator().ValidateAsync(command, cancellationToken);
        return validation.IsValid
            ? await service.DeferAsync(command.RegistrationOrderId, command.Assignments, command.AssignmentDeadline, cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(command.RegistrationOrderId, validation);
    }
}

public sealed class ImportCompanyRegistrationAssignmentsCsvCommandHandler(RegistrationParticipantCommandService service)
    : ICommandHandler<ImportCompanyRegistrationAssignmentsCsvCommand, BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto>>
{
    public async Task<BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto>> ExecuteAsync(
        ImportCompanyRegistrationAssignmentsCsvCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidationResult validation = await new ImportCompanyRegistrationAssignmentsCsvCommandValidator().ValidateAsync(command, cancellationToken);
        return validation.IsValid
            ? await service.ImportCompanyCsvAsync(command.EventId, command.RegistrationOrderId, command.CsvUtf8, command.LineageKey, cancellationToken)
            : BaseCommandResponse.Validation<CompanyRegistrationAssignmentCsvResultDto>(
                validation.Errors.Select(error => error.ErrorMessage),
                "Company assignment CSV request is invalid.");
    }
}
