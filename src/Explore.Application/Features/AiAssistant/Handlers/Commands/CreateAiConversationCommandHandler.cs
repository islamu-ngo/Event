using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai.Validators;
using Explore.Application.Features.AiAssistant.Actors;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Ai;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class CreateAiConversationCommandHandler
    : IRequestHandler<CreateAiConversationCommand, BaseCommandResponse<Guid>>
{
    private readonly IAiConversationRepository _conversationRepository;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiAssistantActorContextService _actorContextService;

    public CreateAiConversationCommandHandler(
        IAiConversationRepository conversationRepository,
        IHierarchicalSettingsResolver settingsResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAiAssistantActorContextService actorContextService)
    {
        _conversationRepository = conversationRepository;
        _settingsResolver = settingsResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _actorContextService = actorContextService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        CreateAiConversationCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new CreateAiConversationRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Conversation, cancellationToken);

        if (!validationResult.IsValid)
        {
            return Failure(
                "AI conversation creation failed.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId is not { } userId)
        {
            return Failure("AI conversations require an authenticated user.", ["User is not authenticated."], "unauthenticated");
        }

        var tenantId = _tenantContext.TenantId;
        var settings = await _settingsResolver.ResolveGroupAsync<AiAssistantSettingGroup>(
            new SettingContext(TenantId: tenantId), cancellationToken);
        var disabledReason = AiAssistantAvailability.ResolveDisabledReason(settings);

        if (disabledReason is not null)
        {
            return Failure(
                "AI assistant is not available for this tenant.",
                [$"AI assistant is unavailable: {disabledReason}."],
                disabledReason);
        }

        var actorResolution = await _actorContextService.ResolveAuthorizedActorAsync(
            tenantId,
            userId,
            request.Conversation.ActorId,
            cancellationToken);
        if (!actorResolution.Succeeded)
        {
            return Failure(
                "AI conversation acting actor is not authorized.",
                [actorResolution.FailureMessage ?? "AI acting actor is not authorized."],
                actorResolution.FailureCode);
        }

        var utcNow = DateTime.UtcNow;
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            ActorId = actorResolution.ActorId,
            Title = string.IsNullOrWhiteSpace(request.Conversation.Title) ? null : request.Conversation.Title.Trim(),
            Provider = AiAssistantAvailability.NormalizeProvider(settings.Provider),
            ModelId = AiAssistantAvailability.ResolveModelId(settings),
            CreatedAt = utcNow,
            CreatedBy = userId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        var created = await _conversationRepository.Create(conversation);

        return BaseCommandResponse.Success(created.Id, "AI conversation created.");
    }

    private static BaseCommandResponse<Guid> Failure(
        string message,
        IEnumerable<string> errors,
        string? failureCode = null) => failureCode is null
            ? BaseCommandResponse.Validation<Guid>(errors, message)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, errors);
}
