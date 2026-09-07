using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Validators;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using FluentValidation.Results;
using MediatR;

namespace Explore.Application.Features.RegistrationForms.Handlers.Commands;

public sealed class CreateRegistrationWorkflowCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<CreateRegistrationWorkflowCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(CreateRegistrationWorkflowCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.CreateWorkflowAsync, cancellationToken);
}

public sealed class UpdateRegistrationWorkflowCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<UpdateRegistrationWorkflowCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpdateRegistrationWorkflowCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateWorkflowAsync, cancellationToken);
}

public sealed class CreateRegistrationRequirementCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<CreateRegistrationRequirementCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(CreateRegistrationRequirementCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.CreateRequirementAsync, cancellationToken);
}

public sealed class UpdateRegistrationRequirementCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<UpdateRegistrationRequirementCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpdateRegistrationRequirementCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateRequirementAsync, cancellationToken);
}

public sealed class DeleteRegistrationRequirementCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<DeleteRegistrationRequirementCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DeleteRegistrationRequirementCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.DeleteRequirementAsync, cancellationToken);
}

public sealed class CreateRegistrationFormCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<CreateRegistrationFormCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(CreateRegistrationFormCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.CreateFormAsync, cancellationToken);
}

public sealed class CreateRegistrationFormVersionCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<CreateRegistrationFormVersionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(CreateRegistrationFormVersionCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.CreateVersionAsync, cancellationToken);
}

public sealed class AddRegistrationFormSectionCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<AddRegistrationFormSectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(AddRegistrationFormSectionCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.AddSectionAsync, cancellationToken);
}

public sealed class UpdateRegistrationFormSectionCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<UpdateRegistrationFormSectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpdateRegistrationFormSectionCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateSectionAsync, cancellationToken);
}

public sealed class ReorderRegistrationFormSectionsCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<ReorderRegistrationFormSectionsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ReorderRegistrationFormSectionsCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.ReorderSectionsAsync, cancellationToken);
}

public sealed class DeleteRegistrationFormSectionCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<DeleteRegistrationFormSectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DeleteRegistrationFormSectionCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.DeleteSectionAsync, cancellationToken);
}

public sealed class AddRegistrationFormFieldCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<AddRegistrationFormFieldCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(AddRegistrationFormFieldCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.AddFieldAsync, cancellationToken);
}

public sealed class UpdateRegistrationFormFieldCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<UpdateRegistrationFormFieldCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpdateRegistrationFormFieldCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateFieldAsync, cancellationToken);
}

public sealed class ReorderRegistrationFormFieldsCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<ReorderRegistrationFormFieldsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ReorderRegistrationFormFieldsCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.ReorderFieldsAsync, cancellationToken);
}

public sealed class DeleteRegistrationFormFieldCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<DeleteRegistrationFormFieldCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DeleteRegistrationFormFieldCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.DeleteFieldAsync, cancellationToken);
}

public sealed class AddRegistrationFormFieldOptionCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<AddRegistrationFormFieldOptionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(AddRegistrationFormFieldOptionCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.AddOptionAsync, cancellationToken);
}

public sealed class UpdateRegistrationFormFieldOptionCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<UpdateRegistrationFormFieldOptionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpdateRegistrationFormFieldOptionCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateOptionAsync, cancellationToken);
}

public sealed class RetireRegistrationFormFieldOptionCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<RetireRegistrationFormFieldOptionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(RetireRegistrationFormFieldOptionCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.RetireOptionAsync, cancellationToken);
}

public sealed class AddRegistrationFormRuleCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<AddRegistrationFormRuleCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(AddRegistrationFormRuleCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.AddRuleAsync, cancellationToken);
}

public sealed class UpdateRegistrationFormRuleCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<UpdateRegistrationFormRuleCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpdateRegistrationFormRuleCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateRuleAsync, cancellationToken);
}

public sealed class DeleteRegistrationFormRuleCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<DeleteRegistrationFormRuleCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DeleteRegistrationFormRuleCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.DeleteRuleAsync, cancellationToken);
}

public sealed class PublishRegistrationFormVersionCommandHandler(RegistrationFormAuthoringCommandService service)
    : IRequestHandler<PublishRegistrationFormVersionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(PublishRegistrationFormVersionCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormCommandHandler.Run(request, service.PublishAsync, cancellationToken);
}

internal static class RegistrationFormCommandHandler
{
    public static async Task<BaseCommandResponse<Guid>> Run<TCommand>(
        TCommand request,
        Func<TCommand, CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken)
        where TCommand : IRegistrationFormAuthoringCommand
    {
        ValidationResult validation = await new RegistrationFormAuthoringCommandValidator<TCommand>()
            .ValidateAsync(request, cancellationToken);
        if (validation.IsValid)
        {
            return await operation(request, cancellationToken);
        }

        bool isReorder = request is ReorderRegistrationFormSectionsCommand or ReorderRegistrationFormFieldsCommand;
        return BaseCommandResponse.Failure<Guid>(
            isReorder ? "registration_form_reorder_invalid" : "registration_form_validation_failed",
            "Registration authoring request is invalid.",
            validation.Errors.Select(error => error.ErrorMessage),
            Guid.Empty);
    }
}
