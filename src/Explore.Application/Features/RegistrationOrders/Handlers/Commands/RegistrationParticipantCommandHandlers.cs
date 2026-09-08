using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using FluentValidation.Results;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class AddRegistrationParticipantCommandHandler(RegistrationParticipantCommandService service)
    : IRequestHandler<AddRegistrationParticipantCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(AddRegistrationParticipantCommand request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await new AddRegistrationParticipantCommandValidator().ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? await service.AddAsync(request.ParticipantTypeId, request.RegistrationOrderId, request.GuardianParticipantId, request.Details, cancellationToken)
            : Invalid(request.RegistrationOrderId, validation);
    }

    internal static BaseCommandResponse<Guid> Invalid(Guid id, ValidationResult validation) => BaseCommandResponse.Validation(
        validation.Errors.Select(error => error.ErrorMessage), "Registration participant request is invalid.", id);
}

public sealed class UpdateRegistrationParticipantCommandHandler(RegistrationParticipantCommandService service)
    : IRequestHandler<UpdateRegistrationParticipantCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpdateRegistrationParticipantCommand request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await new UpdateRegistrationParticipantCommandValidator().ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? await service.UpdateAsync(request.RegistrationOrderId, request.ParticipantId, request.ParticipantTypeId, request.GuardianParticipantId, request.Details, cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(request.ParticipantId, validation);
    }
}

public sealed class AssignRegistrationTicketCommandHandler(RegistrationParticipantCommandService service)
    : IRequestHandler<AssignRegistrationTicketCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(AssignRegistrationTicketCommand request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await new AssignRegistrationTicketCommandValidator().ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? await service.AssignAsync(request.RegistrationOrderId,
                [new TicketParticipantAssignmentInputDto(request.RegistrationOrderLineId, request.Ordinal, request.ParticipantId)], cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(request.RegistrationOrderId, validation);
    }
}

public sealed class BulkAssignRegistrationTicketsCommandHandler(RegistrationParticipantCommandService service)
    : IRequestHandler<BulkAssignRegistrationTicketsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(BulkAssignRegistrationTicketsCommand request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await new BulkAssignRegistrationTicketsCommandValidator().ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? await service.AssignAsync(request.RegistrationOrderId, request.Assignments, cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(request.RegistrationOrderId, validation);
    }
}

public sealed class DeferRegistrationTicketCommandHandler(RegistrationParticipantCommandService service)
    : IRequestHandler<DeferRegistrationTicketCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DeferRegistrationTicketCommand request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await new DeferRegistrationTicketCommandValidator().ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? await service.DeferAsync(request.RegistrationOrderId,
                [new TicketDeferralInputDto(request.RegistrationOrderLineId, request.Ordinal)], request.AssignmentDeadline, cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(request.RegistrationOrderId, validation);
    }
}

public sealed class BulkDeferRegistrationTicketsCommandHandler(RegistrationParticipantCommandService service)
    : IRequestHandler<BulkDeferRegistrationTicketsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(BulkDeferRegistrationTicketsCommand request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await new BulkDeferRegistrationTicketsCommandValidator().ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? await service.DeferAsync(request.RegistrationOrderId, request.Assignments, request.AssignmentDeadline, cancellationToken)
            : AddRegistrationParticipantCommandHandler.Invalid(request.RegistrationOrderId, validation);
    }
}

public sealed class ImportCompanyRegistrationAssignmentsCsvCommandHandler(RegistrationParticipantCommandService service)
    : IRequestHandler<ImportCompanyRegistrationAssignmentsCsvCommand, BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto>>
{
    public async Task<BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto>> Handle(
        ImportCompanyRegistrationAssignmentsCsvCommand request,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await new ImportCompanyRegistrationAssignmentsCsvCommandValidator().ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? await service.ImportCompanyCsvAsync(request.EventId, request.RegistrationOrderId, request.CsvUtf8, request.LineageKey, cancellationToken)
            : BaseCommandResponse.Validation<CompanyRegistrationAssignmentCsvResultDto>(
                validation.Errors.Select(error => error.ErrorMessage),
                "Company assignment CSV request is invalid.");
    }
}
