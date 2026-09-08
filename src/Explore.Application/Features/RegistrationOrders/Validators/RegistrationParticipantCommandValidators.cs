using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.RegistrationOrders.Validators;

public sealed class AddRegistrationParticipantCommandValidator : AbstractValidator<AddRegistrationParticipantCommand>
{
    public AddRegistrationParticipantCommandValidator()
    {
        RuleFor(x => x.RegistrationOrderId).NotEmpty();
        RuleFor(x => x.ParticipantTypeId).GreaterThan(0);
        RuleFor(x => x.Details).NotNull();
    }
}

public sealed class UpdateRegistrationParticipantCommandValidator : AbstractValidator<UpdateRegistrationParticipantCommand>
{
    public UpdateRegistrationParticipantCommandValidator()
    {
        RuleFor(x => x.RegistrationOrderId).NotEmpty();
        RuleFor(x => x.ParticipantId).NotEmpty();
        RuleFor(x => x.ParticipantTypeId).GreaterThan(0);
        RuleFor(x => x.Details).NotNull();
    }
}

public sealed class AssignRegistrationTicketCommandValidator : AbstractValidator<AssignRegistrationTicketCommand>
{
    public AssignRegistrationTicketCommandValidator()
    {
        RuleFor(x => x.RegistrationOrderId).NotEmpty();
        RuleFor(x => x.RegistrationOrderLineId).NotEmpty();
        RuleFor(x => x.Ordinal).GreaterThan(0);
        RuleFor(x => x.ParticipantId).NotEmpty();
    }
}

public sealed class BulkAssignRegistrationTicketsCommandValidator : AbstractValidator<BulkAssignRegistrationTicketsCommand>
{
    public BulkAssignRegistrationTicketsCommandValidator()
    {
        RuleFor(x => x.RegistrationOrderId).NotEmpty();
        RuleFor(x => x.Assignments).NotNull().NotEmpty();
        RuleForEach(x => x.Assignments).SetValidator(new TicketParticipantAssignmentInputDtoValidator());
    }
}

public sealed class DeferRegistrationTicketCommandValidator : AbstractValidator<DeferRegistrationTicketCommand>
{
    public DeferRegistrationTicketCommandValidator()
    {
        RuleFor(x => x.RegistrationOrderId).NotEmpty();
        RuleFor(x => x.RegistrationOrderLineId).NotEmpty();
        RuleFor(x => x.Ordinal).GreaterThan(0);
        RuleFor(x => x.AssignmentDeadline).Must(value => value.Kind == DateTimeKind.Utc);
    }
}

public sealed class BulkDeferRegistrationTicketsCommandValidator : AbstractValidator<BulkDeferRegistrationTicketsCommand>
{
    public BulkDeferRegistrationTicketsCommandValidator()
    {
        RuleFor(x => x.RegistrationOrderId).NotEmpty();
        RuleFor(x => x.Assignments).NotNull().NotEmpty();
        RuleForEach(x => x.Assignments).SetValidator(new TicketDeferralInputDtoValidator());
        RuleFor(x => x.AssignmentDeadline).Must(value => value.Kind == DateTimeKind.Utc);
    }
}

public sealed class ImportCompanyRegistrationAssignmentsCsvCommandValidator : AbstractValidator<ImportCompanyRegistrationAssignmentsCsvCommand>
{
    public const int MaxCsvUtf8Bytes = 256 * 1024;

    public ImportCompanyRegistrationAssignmentsCsvCommandValidator()
    {
        RuleFor(x => x.RegistrationOrderId).NotEmpty();
        RuleFor(x => x.LineageKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.CsvUtf8).NotEmpty().Must(value => System.Text.Encoding.UTF8.GetByteCount(value) <= MaxCsvUtf8Bytes)
            .WithMessage($"Company assignment CSV must be at most {MaxCsvUtf8Bytes} UTF-8 bytes.");
    }
}

internal sealed class TicketParticipantAssignmentInputDtoValidator : AbstractValidator<DTOs.RegistrationOrders.TicketParticipantAssignmentInputDto>
{
    public TicketParticipantAssignmentInputDtoValidator()
    {
        RuleFor(x => x.RegistrationOrderLineId).NotEmpty();
        RuleFor(x => x.Ordinal).GreaterThan(0);
        RuleFor(x => x.ParticipantId).NotEmpty();
    }
}

internal sealed class TicketDeferralInputDtoValidator : AbstractValidator<DTOs.RegistrationOrders.TicketDeferralInputDto>
{
    public TicketDeferralInputDtoValidator()
    {
        RuleFor(x => x.RegistrationOrderLineId).NotEmpty();
        RuleFor(x => x.Ordinal).GreaterThan(0);
    }
}
