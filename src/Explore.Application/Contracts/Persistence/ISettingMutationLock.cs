namespace Explore.Application.Contracts.Persistence;

public interface ISettingMutationLock
{
    Task<T> ExecuteAsync<T>(
        string settingKey,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteManyAsync<T>(
        IEnumerable<string> settingKeys,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteOrderedGroupsAsync<T>(
        IEnumerable<IEnumerable<string>> settingKeyGroups,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingKeyGroups);
        ArgumentNullException.ThrowIfNull(operation);
        string[][] groups = settingKeyGroups
            .Select(group =>
            {
                ArgumentNullException.ThrowIfNull(group);
                return group.ToArray();
            })
            .Where(group => group.Length > 0)
            .ToArray();
        if (groups.Length == 0)
        {
            throw new ArgumentException(
                "At least one setting-key group is required.",
                nameof(settingKeyGroups));
        }

        return ExecuteGroupAsync(groupIndex: 0, cancellationToken);

        Task<T> ExecuteGroupAsync(
            int groupIndex,
            CancellationToken token) =>
            groupIndex == groups.Length
                ? operation(token)
                : ExecuteManyAsync(
                    groups[groupIndex],
                    nextToken => ExecuteGroupAsync(groupIndex + 1, nextToken),
                    token);
    }
}
