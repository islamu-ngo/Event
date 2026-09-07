using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using FluentValidation;

namespace Explore.Application.Features.RegistrationForms.Validators;

public sealed class RegistrationFormTemplateCommandValidator<TCommand> : AbstractValidator<TCommand>
{
    public RegistrationFormTemplateCommandValidator()
    {
        RuleFor(value => value).Must(Valid).WithMessage("Registration form template request is incomplete.");
    }

    private static bool Valid(TCommand value) => value switch
    {
        CreateRegistrationFormTemplateCommand command =>
            Present(command.Input.Name, command.Input.Description, command.Input.Category) &&
            NonEmpty(command.Input.SourceEventId, command.Input.SourceRegistrationFormId,
                command.Input.SourceRegistrationFormVersionId),
        InstantiateRegistrationFormTemplateCommand command =>
            NonEmpty(command.TemplateId, command.Input.EventId, command.Input.WorkflowId,
                command.Input.ExpectedWorkflowConcurrencyStamp) &&
            Present(command.Input.Namespace, command.Input.Key, command.Input.Name),
        _ => false
    };

    private static bool NonEmpty(params Guid[] values) => values.All(value => value != Guid.Empty);
    private static bool Present(params string?[] values) => values.All(value => !string.IsNullOrWhiteSpace(value));
}

public sealed class RegistrationFormTemplateQueryValidator<TQuery> : AbstractValidator<TQuery>
{
    public RegistrationFormTemplateQueryValidator()
    {
        RuleFor(value => value).Must(Valid).WithMessage("Registration form template query identifiers are required.");
    }

    private static bool Valid(TQuery value) => value switch
    {
        ListRegistrationFormTemplatesQuery => true,
        GetRegistrationFormTemplateQuery query => query.TemplateId != Guid.Empty,
        _ => false
    };
}
