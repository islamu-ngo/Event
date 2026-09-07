using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Validators;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using FluentValidation.Results;
using MediatR;

namespace Explore.Application.Features.RegistrationForms.Handlers.Commands;

public sealed class CreateRegistrationFormTemplateCommandHandler(RegistrationFormTemplateCommandService service)
    : IRequestHandler<CreateRegistrationFormTemplateCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(CreateRegistrationFormTemplateCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormTemplateCommandRunner.Run(request, service.CreateAsync, cancellationToken);
}

public sealed class InstantiateRegistrationFormTemplateCommandHandler(RegistrationFormTemplateCommandService service)
    : IRequestHandler<InstantiateRegistrationFormTemplateCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(InstantiateRegistrationFormTemplateCommand request, CancellationToken cancellationToken) =>
        await RegistrationFormTemplateCommandRunner.Run(request, service.InstantiateAsync, cancellationToken);
}

file static class RegistrationFormTemplateCommandRunner
{
    public static async Task<BaseCommandResponse<Guid>> Run<TCommand>(
        TCommand request,
        Func<TCommand, CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await new RegistrationFormTemplateCommandValidator<TCommand>()
            .ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? await operation(request, cancellationToken)
            : BaseCommandResponse.Failure<Guid>(
                "registration_form_template_validation_failed",
                "Registration form template request is invalid.",
                validation.Errors.Select(error => error.ErrorMessage),
                Guid.Empty);
    }
}
