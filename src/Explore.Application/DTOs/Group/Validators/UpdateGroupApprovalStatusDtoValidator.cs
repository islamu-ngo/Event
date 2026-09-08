using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.DTOs.Group.Validators;

public class UpdateGroupApprovalStatusDtoValidator : AbstractValidator<UpdateGroupApprovalStatusDto>
{
    private readonly IApprovalStatusRepository _approvalStatusRepository;

    public UpdateGroupApprovalStatusDtoValidator(IApprovalStatusRepository approvalStatusRepository)
    {
        _approvalStatusRepository = approvalStatusRepository;

        RuleFor(p => p.ApprovalStatusId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (id, _) => await _approvalStatusRepository.Exists(id))
            .WithMessage("{PropertyName} does not exist.");

        RuleFor(p => p.ApprovalNotes)
            .MaximumLength(2000)
            .When(p => !string.IsNullOrWhiteSpace(p.ApprovalNotes));
    }
}
