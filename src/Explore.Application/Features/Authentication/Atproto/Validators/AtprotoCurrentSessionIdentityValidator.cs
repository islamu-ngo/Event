using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Domain.ValueObjects;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Atproto.Validators;

public sealed class AtprotoCurrentSessionIdentityValidator : AbstractValidator<AtprotoCurrentSessionIdentity>
{
    public AtprotoCurrentSessionIdentityValidator()
    {
        RuleFor(identity => identity.TenantId).NotEmpty();
        RuleFor(identity => identity.UserId).NotEmpty();
        RuleFor(identity => identity.Did)
            .NotEqual(default(AtprotoDid));
    }
}
