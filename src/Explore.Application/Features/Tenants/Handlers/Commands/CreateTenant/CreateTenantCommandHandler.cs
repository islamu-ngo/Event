using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Tenant.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Settings.Documents;

namespace Explore.Application.Features.Tenants.Handlers.Commands.CreateTenant;

/// <summary>
/// Validates transport intent and maps the dedicated human creator's entity result.
/// Creation authority and every graph effect remain on the original creator port.
/// </summary>
public sealed class CreateTenantCommandHandler(
    IHumanTenantCreationPort creation,
    ITypedSettingsDocumentResolver documents)
    : ICommandHandler<CreateTenantCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateTenantCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validator = new CreateTenantDtoValidator();
        var validation = await validator.ValidateAsync(request.TenantDto, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage),
                "Invalid tenant data.");

        HumanTenantCreationResult result;
        try
        {
            result = await creation.CreateAsync(request, cancellationToken);
        }
        catch (TenantDirectoryOperatorIdentityReadinessException exception)
        {
            return BaseCommandResponse.Failure<Guid>(exception.FailureCode, exception.Message, exception.ReasonCodes);
        }
        if (result.Creation is not { } created)
            return result.FailureCode == "tenant_slug_conflict"
                ? BaseCommandResponse.Validation<Guid>([result.Error!], result.Error!)
                : BaseCommandResponse.Failure<Guid>(result.FailureCode!, result.Error, [result.Error!]);

        documents.InvalidateTenantDocumentCache(created.Tenant.Id, SettingsDocumentKeys.Tenant.Branding);
        documents.InvalidateTenantDocumentCache(created.Tenant.Id, SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity);
        return BaseCommandResponse.Success(created.Tenant.Id,
            result.AdministratorAssigned
                ? "Tenant created successfully. You have been assigned as tenant administrator."
                : "Tenant created successfully.");
    }
}
