using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Features.Promotions.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.Features.Promotions.Handlers.Commands;

public sealed class CreatePromotionDraftCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    IPromotionManagementRepository promotions,
    IPromotionCodeDigestService digests,
    ITenantContext tenant,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : ICommandHandler<CreatePromotionDraftCommand, PromotionCodeIssuedCommandResponseDto>
{
    public async Task<PromotionCodeIssuedCommandResponseDto> ExecuteAsync(CreatePromotionDraftCommand command, CancellationToken cancellationToken)
    {
        await new CreatePromotionDraftCommandValidator().ValidateAndThrowAsync(command, cancellationToken);

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(command.EventId, token);
            if (!PromotionManagementHandlerSupport.IsPlatformManaged(eventTarget, tenant.TenantId))
            {
                return PromotionManagementHandlerSupport.IssuedNotFound();
            }

            EventTicketCatalogVersion? catalog = await PromotionManagementHandlerSupport.GetScopedCatalogAsync(catalogs, command.EventId, tenant.TenantId, command.TicketCatalogVersionId, token);
            if (catalog is null)
            {
                return PromotionManagementHandlerSupport.IssuedNotFound();
            }

            PromotionDefinition definition;
            try
            {
                PromotionScopeMetadata scope = PromotionScopeMetadata.Create(tenant.TenantId, command.EventId, catalog.Id, catalog.VersionNumber, catalog.CurrencyCode);
                definition = PromotionDefinition.CreateDraft(
                    scope,
                    command.DisplayLabel,
                    PromotionManagementHandlerSupport.CreateEligibility(command.EligibleTicketTypeIds),
                    PromotionManagementHandlerSupport.CreateDiscount(scope.CurrencyCode, command.DiscountKind, command.FixedDiscountMinor, command.BasisPointDiscount, command.MaximumDiscountMinor),
                    command.StartsAtUtc,
                    command.EndsAtUtc,
                    command.TotalRedemptionLimit,
                    command.PerVerifiedPurchaserLimit);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return PromotionManagementHandlerSupport.IssuedValidationFailed(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return PromotionManagementHandlerSupport.IssuedValidationFailed(exception.Message);
            }

            await digests.ComputeActiveAsync(tenant.TenantId, command.EventId, command.Code, token);
            await promotions.AddDefinitionAsync(definition, token);
            await promotions.SaveChangesAsync(token);

            return PromotionManagementHandlerSupport.IssuedSuccess(definition.Id, "Promotion draft created.", PromotionManagementMapper.Map(definition, null, eventTarget!), command.Code);
        }, cancellationToken);
    }
}

public sealed class RevisePromotionCommandHandler(
    IEventRepository events,
    IPromotionManagementRepository promotions,
    ITenantContext tenant,
    IUnitOfWork unitOfWork) : ICommandHandler<RevisePromotionCommand, PromotionManagementCommandResponseDto>
{
    public async Task<PromotionManagementCommandResponseDto> ExecuteAsync(RevisePromotionCommand command, CancellationToken cancellationToken)
    {
        await new RevisePromotionCommandValidator().ValidateAndThrowAsync(command, cancellationToken);

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(command.EventId, token);
            if (!PromotionManagementHandlerSupport.IsPlatformManaged(eventTarget, tenant.TenantId))
            {
                return PromotionManagementHandlerSupport.NotFound();
            }

            PromotionDefinition? current = await promotions.GetDefinitionForUpdateAsync(tenant.TenantId, command.EventId, command.PromotionDefinitionId, token);
            if (current is null)
            {
                return PromotionManagementHandlerSupport.NotFound();
            }

            PromotionDefinition revision;
            try
            {
                revision = current.CreateRevision(
                    command.DisplayLabel,
                    PromotionManagementHandlerSupport.CreateEligibility(command.EligibleTicketTypeIds),
                    PromotionManagementHandlerSupport.CreateDiscount(current.ScopeMetadata.CurrencyCode, command.DiscountKind, command.FixedDiscountMinor, command.BasisPointDiscount, command.MaximumDiscountMinor),
                    command.StartsAtUtc,
                    command.EndsAtUtc,
                    command.TotalRedemptionLimit,
                    command.PerVerifiedPurchaserLimit);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return PromotionManagementHandlerSupport.ValidationFailed(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return PromotionManagementHandlerSupport.ValidationFailed(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return PromotionManagementHandlerSupport.ValidationFailed(exception.Message);
            }

            await promotions.AddDefinitionAsync(revision, token);
            await promotions.SaveChangesAsync(token);

            return PromotionManagementHandlerSupport.Success(revision.Id, "Promotion revision created.", PromotionManagementMapper.Map(revision, null, eventTarget!));
        }, cancellationToken);
    }
}

public sealed class PublishPromotionCommandHandler(
    IEventRepository events,
    IPromotionManagementRepository promotions,
    IPromotionCodeDigestService digests,
    ITenantContext tenant,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : ICommandHandler<PublishPromotionCommand, PromotionManagementCommandResponseDto>
{
    public async Task<PromotionManagementCommandResponseDto> ExecuteAsync(PublishPromotionCommand command, CancellationToken cancellationToken)
    {
        await new PublishPromotionCommandValidator().ValidateAndThrowAsync(command, cancellationToken);
        DateTime publishedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(command.EventId, token);
            if (!PromotionManagementHandlerSupport.IsPlatformManaged(eventTarget, tenant.TenantId))
            {
                return PromotionManagementHandlerSupport.NotFound();
            }

            PromotionDefinition? definition = await promotions.GetDefinitionForUpdateAsync(tenant.TenantId, command.EventId, command.PromotionDefinitionId, token);
            if (definition is null)
            {
                return PromotionManagementHandlerSupport.NotFound();
            }

            try
            {
                definition.Publish(publishedAtUtc);
                PromotionCodeDigest digest = await digests.ComputeActiveAsync(tenant.TenantId, command.EventId, command.Code, token);
                PromotionCode code = PromotionCode.Create(definition, PromotionManagementHandlerSupport.MaskSuffix(digests.NormalizeCode(command.Code)), definition.ScopeMetadata);
                await promotions.AddPublishedCodeAsync(code, digest, token);
                await promotions.SaveChangesAsync(token);
                return PromotionManagementHandlerSupport.Success(definition.Id, "Promotion published.", PromotionManagementMapper.Map(definition, code, eventTarget!));
            }
            catch (ArgumentException exception)
            {
                return PromotionManagementHandlerSupport.ValidationFailed(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return PromotionManagementHandlerSupport.ValidationFailed(exception.Message);
            }
        }, cancellationToken);
    }
}

public sealed class RevokePromotionCommandHandler(
    IEventRepository events,
    IPromotionManagementRepository promotions,
    ITenantContext tenant,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : ICommandHandler<RevokePromotionCommand, PromotionManagementCommandResponseDto>
{
    public async Task<PromotionManagementCommandResponseDto> ExecuteAsync(RevokePromotionCommand command, CancellationToken cancellationToken)
    {
        await new RevokePromotionCommandValidator().ValidateAndThrowAsync(command, cancellationToken);
        DateTime decisionAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(command.EventId, token);
            if (!PromotionManagementHandlerSupport.IsPlatformManaged(eventTarget, tenant.TenantId))
            {
                return PromotionManagementHandlerSupport.NotFound();
            }

            PromotionDefinition? definition = await promotions.GetDefinitionForUpdateAsync(tenant.TenantId, command.EventId, command.PromotionDefinitionId, token);
            if (definition is null)
            {
                return PromotionManagementHandlerSupport.NotFound();
            }

            try
            {
                definition.Revoke(decisionAtUtc, decisionAtUtc);
                await promotions.SaveChangesAsync(token);
                return PromotionManagementHandlerSupport.Success(definition.Id, "Promotion revoked.", PromotionManagementMapper.Map(definition, null, eventTarget!));
            }
            catch (ArgumentException exception)
            {
                return PromotionManagementHandlerSupport.ValidationFailed(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return PromotionManagementHandlerSupport.ValidationFailed(exception.Message);
            }
        }, cancellationToken);
    }
}

public sealed class RotatePromotionCodeCommandHandler(
    IEventRepository events,
    IPromotionManagementRepository promotions,
    IPromotionCodeDigestService digests,
    ITenantContext tenant,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : ICommandHandler<RotatePromotionCodeCommand, PromotionCodeIssuedCommandResponseDto>
{
    public async Task<PromotionCodeIssuedCommandResponseDto> ExecuteAsync(RotatePromotionCodeCommand command, CancellationToken cancellationToken)
    {
        await new RotatePromotionCodeCommandValidator().ValidateAndThrowAsync(command, cancellationToken);
        DateTime rotatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(command.EventId, token);
            if (!PromotionManagementHandlerSupport.IsPlatformManaged(eventTarget, tenant.TenantId))
            {
                return PromotionManagementHandlerSupport.IssuedNotFound();
            }

            PromotionDefinition? definition = await promotions.GetDefinitionForUpdateAsync(tenant.TenantId, command.EventId, command.PromotionDefinitionId, token);
            if (definition is null)
            {
                return PromotionManagementHandlerSupport.IssuedNotFound();
            }

            try
            {
                PromotionCodeDigest digest = await digests.ComputeActiveAsync(tenant.TenantId, command.EventId, command.Code, token);
                PromotionCode code = PromotionCode.Create(definition, PromotionManagementHandlerSupport.MaskSuffix(digests.NormalizeCode(command.Code)), definition.ScopeMetadata);
                await promotions.ReplaceActiveCodeAsync(definition, code, digest, rotatedAtUtc, token);
                await promotions.SaveChangesAsync(token);
                return PromotionManagementHandlerSupport.IssuedSuccess(definition.Id, "Promotion code rotated.", PromotionManagementMapper.Map(definition, code, eventTarget!), command.Code);
            }
            catch (ArgumentException exception)
            {
                return PromotionManagementHandlerSupport.IssuedValidationFailed(exception.Message);
            }
        }, cancellationToken);
    }
}

internal static class PromotionManagementHandlerSupport
{
    public const string NotFoundFailureCode = "promotion_management_not_found";
    public const string ValidationFailureCode = "promotion_management_validation_failed";

    public static bool IsPlatformManaged(Event? eventTarget, Guid tenantId) =>
        eventTarget?.TenantId == tenantId
        && eventTarget.ParticipationConfiguration?.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.PlatformManaged;

    public static async Task<EventTicketCatalogVersion?> GetScopedCatalogAsync(
        IEventTicketCatalogRepository catalogs,
        Guid eventId,
        Guid tenantId,
        Guid ticketCatalogVersionId,
        CancellationToken cancellationToken)
    {
        EventTicketCatalogVersion? catalog = await catalogs.GetManagementCatalogAsync(eventId, tenantId, cancellationToken)
            ?? await catalogs.GetPublishedCatalogAsync(eventId, tenantId, cancellationToken);

        return catalog?.Id == ticketCatalogVersionId ? catalog : null;
    }

    public static PromotionEligibility CreateEligibility(IReadOnlyCollection<Guid> ticketTypeIds) =>
        ticketTypeIds.Count == 0 ? PromotionEligibility.AllTickets() : PromotionEligibility.ForTicketTypes(ticketTypeIds);

    public static PromotionDiscountRule CreateDiscount(
        string currencyCode,
        string discountKind,
        long? fixedDiscountMinor,
        int? basisPointDiscount,
        long? maximumDiscountMinor) =>
        discountKind switch
        {
            "fixed" => PromotionDiscountRule.FixedMinor(currencyCode, fixedDiscountMinor ?? 0, maximumDiscountMinor),
            "basis_points" => PromotionDiscountRule.BasisPoints(currencyCode, basisPointDiscount ?? 0, maximumDiscountMinor),
            _ => throw new ArgumentException("Promotion discount kind is not supported.", nameof(discountKind))
        };

    public static string MaskSuffix(string normalizedCode) =>
        normalizedCode.Length <= 8 ? normalizedCode : normalizedCode[^8..];

    public static PromotionManagementCommandResponseDto NotFound() =>
        PromotionManagementCommandResponseDto.Failure(NotFoundFailure());

    public static PromotionCodeIssuedCommandResponseDto IssuedNotFound() =>
        PromotionCodeIssuedCommandResponseDto.Failure(NotFoundFailure());

    public static PromotionManagementCommandResponseDto ValidationFailed(string message) =>
        PromotionManagementCommandResponseDto.Failure(ValidationFailure(message));

    public static PromotionCodeIssuedCommandResponseDto IssuedValidationFailed(string message) =>
        PromotionCodeIssuedCommandResponseDto.Failure(ValidationFailure(message));

    private static BaseCommandResponse<Guid> NotFoundFailure() => BaseCommandResponse.Failure<Guid>(
        NotFoundFailureCode,
        "Promotion management resource was not found.",
        ["Promotion management resource was not found."]);

    private static BaseCommandResponse<Guid> ValidationFailure(string message) => BaseCommandResponse.Failure<Guid>(
        ValidationFailureCode,
        "Promotion management request is invalid.",
        [message]);

    public static PromotionManagementCommandResponseDto Success(Guid id, string message, PromotionManagementDto promotion) =>
        PromotionManagementCommandResponseDto.Success(id, message, promotion);

    public static PromotionCodeIssuedCommandResponseDto IssuedSuccess(Guid id, string message, PromotionManagementDto promotion, string issuedCode) =>
        PromotionCodeIssuedCommandResponseDto.Success(id, message, promotion, issuedCode);
}
