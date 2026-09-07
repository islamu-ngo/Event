using Explore.Application.Features.TenantUsers.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.TenantUsers.Validators;

public sealed class RemoveTenantMembershipCommandValidator : AbstractValidator<RemoveTenantMembershipCommand>
{
    public RemoveTenantMembershipCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
    }
}
