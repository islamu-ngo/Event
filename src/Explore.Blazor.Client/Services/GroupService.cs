using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Services;

public interface IGroupService
{
    Task<ICollection<GroupListDto>> GetMyGroupsAsync();
    Task<bool> CreateGroupAsync(string fullName, string? description = null);
    Task<HalResourceOfGroupDto?> GetGroupDetailsAsync(Guid groupId);
    Task<BaseCommandResponseOfGuid?> UpdateGroupAsync(Guid groupId, Guid expectedConcurrencyStamp, UpdateGroupDto group);
    Task<IReadOnlyList<GroupMemberDto>> GetGroupMembersAsync(Guid groupId);
    Task<GroupMembersResult> GetGroupMembersWithAffordancesAsync(Guid groupId);
    Task<BaseCommandResponseOfGuid?> AddGroupMemberAsync(AddGroupMemberDto member);
    Task<BaseCommandResponseOfGuid?> UpdateGroupMemberRoleAsync(UpdateGroupMemberRoleDto updateDto);
    Task<BaseCommandResponseOfGuid?> DeleteGroupMemberAsync(Guid memberId);
}

public sealed record GroupMembersResult
{
    public GroupMembersResult(IEnumerable<GroupMemberDto> Members, bool CanCreate)
    {
        this.Members = Array.AsReadOnly(Members.ToArray());
        this.CanCreate = CanCreate;
    }

    public IReadOnlyList<GroupMemberDto> Members { get; }
    public bool CanCreate { get; }
    public static GroupMembersResult Empty { get; } = new([], false);
}

public class GroupService : IGroupService
{
    private readonly IGroupClient _apiClient;
    private readonly IGroupMemberClient _groupMemberClient;
    private readonly ILogger<GroupService> _logger;

    public GroupService(IGroupClient apiClient, IGroupMemberClient groupMemberClient, ILogger<GroupService> logger)
    {
        _apiClient = apiClient;
        _groupMemberClient = groupMemberClient;
        _logger = logger;
    }

    public async Task<bool> CreateGroupAsync(string fullName, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            _logger.LogWarning("[GroupService.CreateGroupAsync] Group name is required.");
            return false;
        }

        try
        {
            var response = await _apiClient.CreateGroupAsync(new CreateGroupDto
            {
                FullName = fullName.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
            });

            return response.Success == true;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[GroupService.CreateGroupAsync] API error creating group. StatusCode: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GroupService.CreateGroupAsync] Unexpected error creating group");
            return false;
        }
    }

    public async Task<ICollection<GroupListDto>> GetMyGroupsAsync()
    {
        try
        {
            var response = await _apiClient.GetMyGroupsAsync(1, 100);
            return response._embedded?.Items?
                .Select(MapGroupPublisher)
                .Where(group => group is not null)
                .Select(group => group!)
                .ToList() ?? new List<GroupListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GroupService.GetMyGroupsAsync] Unexpected error fetching groups");
            return new List<GroupListDto>();
        }
    }

    public async Task<HalResourceOfGroupDto?> GetGroupDetailsAsync(Guid groupId)
    {
        try
        {
            return await _apiClient.GetGroupByIdAsync(groupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GroupService.GetGroupDetailsAsync] Unexpected error fetching group. GroupId: {GroupId}", groupId);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateGroupAsync(Guid groupId, Guid expectedConcurrencyStamp, UpdateGroupDto group)
    {
        try
        {
            if (groupId == Guid.Empty || expectedConcurrencyStamp == Guid.Empty)
            {
                return new BaseCommandResponseOfGuid
                {
                    Success = false,
                    Message = "Group ID and concurrency stamp are required."
                };
            }

            return await _apiClient.UpdateGroupAsync(groupId, group, $"\"{expectedConcurrencyStamp:D}\"");
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[GroupService.UpdateGroupAsync] API error updating group. GroupId: {GroupId}, StatusCode: {StatusCode}", groupId, ex.StatusCode);
            throw;
        }
    }

    public async Task<IReadOnlyList<GroupMemberDto>> GetGroupMembersAsync(Guid groupId)
    {
        var result = await GetGroupMembersWithAffordancesAsync(groupId);
        return result.Members;
    }

    public async Task<GroupMembersResult> GetGroupMembersWithAffordancesAsync(Guid groupId)
    {
        try
        {
            var response = await _groupMemberClient.GetGroupMembersAsync(groupId);
            if (response is null)
            {
                return GroupMembersResult.Empty;
            }

            return new GroupMembersResult(
                response.GetItems().ToList(),
                response.HasLink("create"));
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[GroupService.GetGroupMembersAsync] API error fetching group members. GroupId: {GroupId}, StatusCode: {StatusCode}", groupId, ex.StatusCode);
            return GroupMembersResult.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GroupService.GetGroupMembersAsync] Unexpected error fetching group members. GroupId: {GroupId}", groupId);
            return GroupMembersResult.Empty;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> AddGroupMemberAsync(AddGroupMemberDto member)
    {
        try
        {
            return await _groupMemberClient.CreateGroupMemberAsync(member);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[GroupService.AddGroupMemberAsync] API error adding group member. StatusCode: {StatusCode}", ex.StatusCode);
            throw;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateGroupMemberRoleAsync(UpdateGroupMemberRoleDto updateDto)
    {
        try
        {
            return await _groupMemberClient.UpdateGroupMemberAsync(updateDto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[GroupService.UpdateGroupMemberRoleAsync] API error updating group member role. StatusCode: {StatusCode}", ex.StatusCode);
            throw;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> DeleteGroupMemberAsync(Guid memberId)
    {
        try
        {
            return await _groupMemberClient.DeleteGroupMemberAsync(memberId);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[GroupService.DeleteGroupMemberAsync] API error deleting group member. MemberId: {MemberId}, StatusCode: {StatusCode}", memberId, ex.StatusCode);
            throw;
        }
    }

    private static GroupListDto? MapGroupPublisher(HalResourceOfGroupListDto group)
    {
        if (group.Id is null)
        {
            return null;
        }

        return new GroupListDto
        {
            Id = group.Id.Value,
            FullName = group.FullName ?? string.Empty,
            CurrentUserRoleId = group.CurrentUserRoleId,
            ApprovalStatusFullName = group.ApprovalStatusFullName ?? string.Empty
        };
    }
}
