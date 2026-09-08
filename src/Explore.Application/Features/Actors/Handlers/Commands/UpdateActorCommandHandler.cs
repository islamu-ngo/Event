using System.Linq;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Actor;
using Explore.Application.DTOs.Actor.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Actors.Handlers.Commands;

public class UpdateActorCommandHandler : IRequestHandler<UpdateActorCommand, BaseCommandResponse<Guid>>
{
    private readonly IActorRepository _actorRepository;
    private readonly IActorTypeRepository _actorTypeRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IAuthorizationProvider _authorizationProvider;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly HybridCache _cache;

    public UpdateActorCommandHandler(
        IActorRepository actorRepository,
        IActorTypeRepository actorTypeRepository,
        IStorageObjectRepository storageObjectRepository,
        IAuthorizationProvider authorizationProvider,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        HybridCache cache)
    {
        _actorRepository = actorRepository;
        _actorTypeRepository = actorTypeRepository;
        _storageObjectRepository = storageObjectRepository;
        _authorizationProvider = authorizationProvider;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateActorCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateActorDtoValidator(
            _actorTypeRepository,
            _storageObjectRepository);
        var validationResult = await validator.ValidateAsync(request.UpdateActorDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Actor update failed.");
        }

        var transactionResponse = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var actor = await _actorRepository.GetById(request.ActorId);
            if (actor == null)
            {
                return BaseCommandResponse.Validation<Guid>(["Actor not found."], "Actor not found.");
            }

            await EnsureCanUpdatePresentGroupsAsync(actor, request.UpdateActorDto, token);

            if (actor.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
            {
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "The actor was modified by another request. Reload and retry.",
                    nameof(Actor),
                    actor.Id.ToString());
            }

            if (!await ApplyProfileImageAsync(actor, request.UpdateActorDto.ProfileImage))
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["Profile image was not found in the current tenant."],
                    "Profile image was not found in the current tenant.");
            }

            ApplyAppearance(actor, request.UpdateActorDto.Appearance);
            ApplyProfile(actor, request.UpdateActorDto.Profile);

            await _actorRepository.Update(actor);

            return BaseCommandResponse.Success(actor.Id, "Actor updated successfully.");
        }, cancellationToken);

        if (transactionResponse.IsSuccess)
        {
            await _cache.RemoveAsync($"actor:detail:{transactionResponse.Id}", cancellationToken);
        }

        return transactionResponse;
    }

    private async Task EnsureCanUpdatePresentGroupsAsync(Actor actor, UpdateActorDto dto, CancellationToken cancellationToken)
    {
        var checks = BuildPresentGroupAuthorizationRequests(actor, dto);
        if (checks.Count == 0)
        {
            return;
        }

        var decisions = (await _authorizationProvider.AuthorizeBatchAsync(checks, cancellationToken))
            .Select(decision => decision.IsAllowed)
            .ToArray();
        if (decisions.Any(isAllowed => !isAllowed))
        {
            throw new AuthorizationException(ResourceKinds.Actor, AuthorizationActions.Update);
        }
    }

    private IReadOnlyList<AuthorizationRequest> BuildPresentGroupAuthorizationRequests(Actor actor, UpdateActorDto dto)
    {
        var checks = new List<AuthorizationRequest>();
        AddGroupCheck(checks, actor, dto.Profile, "profile");
        AddGroupCheck(checks, actor, dto.ProfileImage, "profileImage");
        AddGroupCheck(checks, actor, dto.Appearance, "appearance");
        return checks;
    }

    private void AddGroupCheck<TGroup>(List<AuthorizationRequest> checks, Actor actor, TGroup? group, string groupName)
        where TGroup : class
    {
        if (group is null)
        {
            return;
        }

        // Each editable group is authorized separately, but the policy question is identical: may this
        // caller update this actor. The group name is a payload partition, not a policy fact.
        checks.Add(new AuthorizationRequest(
            ResourceKinds.Actor,
            actor.Id.ToString(),
            AuthorizationActions.Update,
            new AuthorizationScope(_tenantContext.TenantId.ToString()),
            new ActorAuthorizationFacts(_tenantContext.TenantId, actor.Id, actor.UserId)));
    }

    private static void ApplyProfile(Actor actor, UpdateActorProfileDto? dto)
    {
        if (dto is null)
        {
            return;
        }

        if (dto.ActorTypeId.HasValue)
        {
            actor.ActorTypeId = dto.ActorTypeId.Value;
        }

        if (dto.DisplayName is not null)
        {
            actor.DisplayName = dto.DisplayName;
        }

        ApplyOptional(dto.Description, value => actor.Description = value);
    }

    private async Task<bool> ApplyProfileImageAsync(Actor actor, UpdateActorProfileImageDto? dto)
    {
        if (dto?.ProfilePictureId.HasValue != true)
        {
            return true;
        }

        if (dto.ProfilePictureId.Value is not { } profilePictureId)
        {
            actor.ProfilePictureUri = null;
            return true;
        }

        var storageObject = await _storageObjectRepository.GetById(profilePictureId);
        if (storageObject is null
            || !SafeRasterContentPolicy.IsEligibleImageReference(
                storageObject,
                _tenantContext.TenantId))
        {
            return false;
        }

        storageObject.ActorId = actor.Id;
        actor.ProfilePictureUri = storageObject.Uri;
        return true;
    }

    private static void ApplyAppearance(Actor actor, UpdateActorAppearanceDto? dto)
    {
        if (dto is null)
        {
            return;
        }

        ApplyOptional(dto.BackgroundColor, value => actor.BackgroundColor = value);
        ApplyOptional(dto.BackgroundEffect, value => actor.BackgroundEffect = value);
        ApplyOptional(dto.BannerColor, value => actor.BannerColor = value);
    }

    private static void ApplyOptional<T>(OptionalUpdate<T> update, Action<T?> apply)
    {
        if (update.HasValue)
        {
            apply(update.Value);
        }
    }
}
