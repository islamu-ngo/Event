namespace Explore.Application.DTOs.TenantSettings.Validators;

using Explore.Domain.ValueObjects;
using FluentValidation;

public sealed class TenantDirectoryOperatorIdentityInputDtoValidator :
    AbstractValidator<TenantDirectoryOperatorIdentityInputDto>
{
    public TenantDirectoryOperatorIdentityInputDtoValidator(
        TenantDirectoryOperatorIdentityCapability capability)
    {
        RuleFor(identity => identity)
            .Must(identity =>
                TenantDirectoryOperatorIdentity
                    .Evaluate(identity.ToPayload(), capability)
                    .IsReady)
            .WithMessage($"Directory operator identity is incomplete or invalid for {capability}.");
    }
}
