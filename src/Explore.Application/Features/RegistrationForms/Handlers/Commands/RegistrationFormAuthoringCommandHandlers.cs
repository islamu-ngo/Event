using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Validators;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using FluentValidation.Results;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.RegistrationForms.Handlers.Commands;

public sealed class CreateRegistrationWorkflowCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<CreateRegistrationWorkflowCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateRegistrationWorkflowCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.CreateWorkflowAsync, cancellationToken);
}

public sealed class UpdateRegistrationWorkflowCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<UpdateRegistrationWorkflowCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateRegistrationWorkflowCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateWorkflowAsync, cancellationToken);
}

public sealed class CreateRegistrationRequirementCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<CreateRegistrationRequirementCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateRegistrationRequirementCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.CreateRequirementAsync, cancellationToken);
}

public sealed class UpdateRegistrationRequirementCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<UpdateRegistrationRequirementCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateRegistrationRequirementCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateRequirementAsync, cancellationToken);
}

public sealed class DeleteRegistrationRequirementCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<DeleteRegistrationRequirementCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteRegistrationRequirementCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.DeleteRequirementAsync, cancellationToken);
}

public sealed class CreateRegistrationFormCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<CreateRegistrationFormCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateRegistrationFormCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.CreateFormAsync, cancellationToken);
}

public sealed class CreateRegistrationFormVersionCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<CreateRegistrationFormVersionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateRegistrationFormVersionCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.CreateVersionAsync, cancellationToken);
}

public sealed class AddRegistrationFormSectionCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<AddRegistrationFormSectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AddRegistrationFormSectionCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.AddSectionAsync, cancellationToken);
}

public sealed class UpdateRegistrationFormSectionCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<UpdateRegistrationFormSectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateRegistrationFormSectionCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateSectionAsync, cancellationToken);
}

public sealed class ReorderRegistrationFormSectionsCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<ReorderRegistrationFormSectionsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(ReorderRegistrationFormSectionsCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.ReorderSectionsAsync, cancellationToken);
}

public sealed class DeleteRegistrationFormSectionCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<DeleteRegistrationFormSectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteRegistrationFormSectionCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.DeleteSectionAsync, cancellationToken);
}

public sealed class AddRegistrationFormFieldCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<AddRegistrationFormFieldCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AddRegistrationFormFieldCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.AddFieldAsync, cancellationToken);
}

public sealed class UpdateRegistrationFormFieldCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<UpdateRegistrationFormFieldCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateRegistrationFormFieldCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateFieldAsync, cancellationToken);
}

public sealed class ReorderRegistrationFormFieldsCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<ReorderRegistrationFormFieldsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(ReorderRegistrationFormFieldsCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.ReorderFieldsAsync, cancellationToken);
}

public sealed class DeleteRegistrationFormFieldCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<DeleteRegistrationFormFieldCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteRegistrationFormFieldCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.DeleteFieldAsync, cancellationToken);
}

public sealed class AddRegistrationFormFieldOptionCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<AddRegistrationFormFieldOptionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AddRegistrationFormFieldOptionCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.AddOptionAsync, cancellationToken);
}

public sealed class UpdateRegistrationFormFieldOptionCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<UpdateRegistrationFormFieldOptionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateRegistrationFormFieldOptionCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateOptionAsync, cancellationToken);
}

public sealed class RetireRegistrationFormFieldOptionCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<RetireRegistrationFormFieldOptionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(RetireRegistrationFormFieldOptionCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.RetireOptionAsync, cancellationToken);
}

public sealed class AddRegistrationFormRuleCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<AddRegistrationFormRuleCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AddRegistrationFormRuleCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.AddRuleAsync, cancellationToken);
}

public sealed class UpdateRegistrationFormRuleCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<UpdateRegistrationFormRuleCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateRegistrationFormRuleCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.UpdateRuleAsync, cancellationToken);
}

public sealed class DeleteRegistrationFormRuleCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<DeleteRegistrationFormRuleCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteRegistrationFormRuleCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.DeleteRuleAsync, cancellationToken);
}

public sealed class PublishRegistrationFormVersionCommandHandler(RegistrationFormAuthoringCommandService service)
    : ICommandHandler<PublishRegistrationFormVersionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(PublishRegistrationFormVersionCommand request, CancellationToken cancellationToken = default) =>
        await RegistrationFormCommandHandler.Run(request, service.PublishAsync, cancellationToken);
}

internal static class RegistrationFormCommandHandler
{
    public static async Task<BaseCommandResponse<Guid>> Run<TCommand>(
        TCommand request,
        Func<TCommand, CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken = default)
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
