using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Groups.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Groups.Handlers.Commands;

public class DeleteGroupCommandHandler : IRequestHandler<DeleteGroupCommand, BaseCommandResponse<Guid>>
{
    private readonly IGroupRepository _groupRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly HybridCache _cache;

    public DeleteGroupCommandHandler(
        IGroupRepository groupRepository,
        IGroupMemberRepository groupMemberRepository,
        IUserContext userContext,
        ITenantContext tenantContext,
        HybridCache cache)
    {
        _groupRepository = groupRepository;
        _groupMemberRepository = groupMemberRepository;
        _userContext = userContext;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(DeleteGroupCommand request, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.GetRequiredUserId();

        var hasPermission = await _groupMemberRepository.HasPermissionInGroup(
            request.Id, currentUserId, PermissionCodes.GroupDelete);
        if (!hasPermission)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["You do not have permission to delete this group."],
                "You do not have permission to delete this group.");
        }

        var group = await _groupRepository.GetById(request.Id);
        if (group == null)
        {
            throw new NotFoundException(nameof(Group), request.Id);
        }

        await _groupRepository.Delete(group);

        await _cache.RemoveAsync($"group:detail:{group.Id}", cancellationToken);

        return BaseCommandResponse.Success(group.Id, "Group deleted successfully.");
    }
}
