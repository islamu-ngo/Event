using Explore.Application.Features.Promotions.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Promotions.Validators;

public sealed class CreatePromotionDraftCommandValidator : AbstractValidator<CreatePromotionDraftCommand>
{
    public CreatePromotionDraftCommandValidator()
    {
        Include(new PromotionDefinitionShapeValidator<CreatePromotionDraftCommand>());
        RuleFor(command => command.TicketCatalogVersionId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(128);
    }
}

public sealed class RevisePromotionCommandValidator : AbstractValidator<RevisePromotionCommand>
{
    public RevisePromotionCommandValidator()
    {
        Include(new PromotionDefinitionShapeValidator<RevisePromotionCommand>());
        RuleFor(command => command.PromotionDefinitionId).NotEmpty();
    }
}

public sealed class PublishPromotionCommandValidator : AbstractValidator<PublishPromotionCommand>
{
    public PublishPromotionCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.PromotionDefinitionId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(128);
    }
}

public sealed class RevokePromotionCommandValidator : AbstractValidator<RevokePromotionCommand>
{
    public RevokePromotionCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.PromotionDefinitionId).NotEmpty();
    }
}

public sealed class RotatePromotionCodeCommandValidator : AbstractValidator<RotatePromotionCodeCommand>
{
    public RotatePromotionCodeCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.PromotionDefinitionId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(128);
    }
}

internal sealed class PromotionDefinitionShapeValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : PromotionManagementCommandBase
{
    public PromotionDefinitionShapeValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => GetDisplayLabel(command)).NotEmpty().MaximumLength(160);
        RuleFor(command => GetDiscountKind(command)).Must(kind => kind is "fixed" or "basis_points");
        RuleFor(command => GetStartsAtUtc(command)).Must(BeUtc).WithMessage("StartsAtUtc must be UTC.");
        RuleFor(command => GetEndsAtUtc(command)).Must(BeUtc).WithMessage("EndsAtUtc must be UTC.");
        RuleFor(command => command).Must(command => GetEndsAtUtc(command) > GetStartsAtUtc(command)).WithMessage("EndsAtUtc must be after StartsAtUtc.");
        RuleFor(command => GetTotalLimit(command)).GreaterThan(0).When(command => GetTotalLimit(command).HasValue);
        RuleFor(command => GetPurchaserLimit(command)).GreaterThan(0).When(command => GetPurchaserLimit(command).HasValue);
        RuleFor(command => GetFixedDiscount(command)).GreaterThan(0).When(command => GetDiscountKind(command) == "fixed");
        RuleFor(command => GetBasisPointDiscount(command)).InclusiveBetween(1, 10_000).When(command => GetDiscountKind(command) == "basis_points");
        RuleFor(command => GetMaximumDiscount(command)).GreaterThan(0).When(command => GetMaximumDiscount(command).HasValue);
    }

    private static bool BeUtc(DateTime value) => value != default && value.Kind == DateTimeKind.Utc;

    private static string GetDisplayLabel(TCommand command) => command switch
    {
        CreatePromotionDraftCommand create => create.DisplayLabel,
        RevisePromotionCommand revise => revise.DisplayLabel,
        _ => string.Empty
    };

    private static string GetDiscountKind(TCommand command) => command switch
    {
        CreatePromotionDraftCommand create => create.DiscountKind,
        RevisePromotionCommand revise => revise.DiscountKind,
        _ => string.Empty
    };

    private static DateTime GetStartsAtUtc(TCommand command) => command switch
    {
        CreatePromotionDraftCommand create => create.StartsAtUtc,
        RevisePromotionCommand revise => revise.StartsAtUtc,
        _ => default
    };

    private static DateTime GetEndsAtUtc(TCommand command) => command switch
    {
        CreatePromotionDraftCommand create => create.EndsAtUtc,
        RevisePromotionCommand revise => revise.EndsAtUtc,
        _ => default
    };

    private static int? GetTotalLimit(TCommand command) => command switch
    {
        CreatePromotionDraftCommand create => create.TotalRedemptionLimit,
        RevisePromotionCommand revise => revise.TotalRedemptionLimit,
        _ => null
    };

    private static int? GetPurchaserLimit(TCommand command) => command switch
    {
        CreatePromotionDraftCommand create => create.PerVerifiedPurchaserLimit,
        RevisePromotionCommand revise => revise.PerVerifiedPurchaserLimit,
        _ => null
    };

    private static long? GetFixedDiscount(TCommand command) => command switch
    {
        CreatePromotionDraftCommand create => create.FixedDiscountMinor,
        RevisePromotionCommand revise => revise.FixedDiscountMinor,
        _ => null
    };

    private static int? GetBasisPointDiscount(TCommand command) => command switch
    {
        CreatePromotionDraftCommand create => create.BasisPointDiscount,
        RevisePromotionCommand revise => revise.BasisPointDiscount,
        _ => null
    };

    private static long? GetMaximumDiscount(TCommand command) => command switch
    {
        CreatePromotionDraftCommand create => create.MaximumDiscountMinor,
        RevisePromotionCommand revise => revise.MaximumDiscountMinor,
        _ => null
    };
}
