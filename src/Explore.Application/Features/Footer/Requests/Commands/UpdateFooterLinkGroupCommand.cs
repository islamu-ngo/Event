using Explore.Application.Authorization;
using Explore.Application.Responses;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Footer.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record UpdateFooterLinkGroupCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid GroupId { get; init; }
    public required PatchFooterLinkGroupDto Update { get; init; }
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);

}

public sealed record PatchFooterLinkGroupDto
{
    public PatchFooterLinkGroupTitleDto? Title { get; init; }
    public PatchFooterLinkGroupIsActiveDto? IsActive { get; init; }
}

public sealed record PatchFooterLinkGroupTitleDto
{
    public required string Value { get; init; }
}

public sealed record PatchFooterLinkGroupIsActiveDto
{
    public bool? Value { get; init; }
}

public sealed class PatchFooterLinkGroupDtoValidator : AbstractValidator<PatchFooterLinkGroupDto>
{
    public PatchFooterLinkGroupDtoValidator()
    {
        RuleFor(dto => dto.Title!.Value)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(100).WithMessage("Title must not exceed 100 characters.")
            .When(dto => dto.Title is not null);

        RuleFor(dto => dto.IsActive!.Value)
            .NotNull().WithMessage("IsActive group must include Value.")
            .When(dto => dto.IsActive is not null);

        RuleFor(dto => dto)
            .Must(dto => dto.Title is not null || dto.IsActive is not null)
            .WithMessage("At least one footer link group update must be provided.");
    }
}
