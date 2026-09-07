using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class NotificationPreferenceResolver : INotificationPreferenceResolver
{
    private readonly ExploreDbContext _dbContext;

    public NotificationPreferenceResolver(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<NotificationPreferenceDecision> ResolveAsync(
        NotificationPreferenceResolveRequest request,
        CancellationToken cancellationToken = default)
    {
        var decisions = await ResolveBatchAsync([request], cancellationToken);
        return decisions[0];
    }

    public async Task<IReadOnlyList<NotificationPreferenceDecision>> ResolveBatchAsync(
        IReadOnlyCollection<NotificationPreferenceResolveRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var requestPairs = requests
            .Select(request => new
            {
                Original = request,
                Normalized = request with
                {
                    CategoryCode = NormalizeCode(request.CategoryCode),
                    ChannelCode = NormalizeCode(request.ChannelCode)
                }
            })
            .ToArray();

        var categoryCodes = requestPairs.Select(pair => pair.Normalized.CategoryCode).Distinct().ToArray();
        var channelCodes = requestPairs.Select(pair => pair.Normalized.ChannelCode).Distinct().ToArray();

        var categories = await _dbContext.NotificationPreferenceCategories
            .AsNoTracking()
            .Where(category => categoryCodes.Contains(category.MasterCode))
            .ToDictionaryAsync(category => category.MasterCode, cancellationToken);

        var channels = await _dbContext.NotificationPreferenceChannels
            .AsNoTracking()
            .Where(channel => channelCodes.Contains(channel.MasterCode))
            .ToDictionaryAsync(channel => channel.MasterCode, cancellationToken);

        var decisions = new Dictionary<int, NotificationPreferenceDecision>();

        foreach (var group in requestPairs.Select((pair, index) => new { pair.Original, pair.Normalized, Index = index }).GroupBy(pair => new ResolverContext(
            pair.Normalized.TenantId,
            pair.Normalized.UserId,
            pair.Normalized.OrganizationId,
            pair.Normalized.GroupId)))
        {
            var context = group.Key;
            var contextCategoryIds = group
                .Select(pair => categories.TryGetValue(pair.Normalized.CategoryCode, out var category) ? category.Id : 0)
                .Where(id => id != 0)
                .Distinct()
                .ToArray();
            var contextChannelIds = group
                .Select(pair => channels.TryGetValue(pair.Normalized.ChannelCode, out var channel) ? channel.Id : 0)
                .Where(id => id != 0)
                .Distinct()
                .ToArray();

            var preferences = await LoadPreferencesAsync(context, contextCategoryIds, contextChannelIds, cancellationToken);
            var profiles = await LoadProfilesAsync(context, cancellationToken);
            var hierarchy = BuildHierarchy(context);
            var muteDecision = ResolveMute(profiles, hierarchy);

            foreach (var pair in group)
            {
                decisions[pair.Index] = ResolveRequest(
                    pair.Normalized,
                    categories,
                    channels,
                    preferences,
                    hierarchy,
                    muteDecision);
            }
        }

        return Enumerable.Range(0, requestPairs.Length)
            .Select(index => decisions[index])
            .ToArray();
    }

    private async Task<List<NotificationChannelPreference>> LoadPreferencesAsync(
        ResolverContext context,
        IReadOnlyCollection<int> categoryIds,
        IReadOnlyCollection<int> channelIds,
        CancellationToken cancellationToken)
    {
        if (categoryIds.Count == 0 || channelIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.NotificationChannelPreferences
            .AsNoTracking()
            .Where(preference => preference.TenantId == context.TenantId)
            .Where(preference => categoryIds.Contains(preference.CategoryId))
            .Where(preference => channelIds.Contains(preference.ChannelId))
            .Where(preference =>
                preference.ScopeId == (int)ConfigurationScopeEnum.System
                || preference.ScopeId == (int)ConfigurationScopeEnum.Instance
                || preference.ScopeId == (int)ConfigurationScopeEnum.Tenant
                || (preference.ScopeId == (int)ConfigurationScopeEnum.Organization && preference.OrganizationId == context.OrganizationId)
                || (preference.ScopeId == (int)ConfigurationScopeEnum.Group && preference.GroupId == context.GroupId)
                || (preference.ScopeId == (int)ConfigurationScopeEnum.User && preference.UserId == context.UserId))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<NotificationPreferenceProfile>> LoadProfilesAsync(
        ResolverContext context,
        CancellationToken cancellationToken)
    {
        return await _dbContext.NotificationPreferenceProfiles
            .AsNoTracking()
            .Where(profile => profile.TenantId == context.TenantId)
            .Where(profile =>
                profile.ScopeId == (int)ConfigurationScopeEnum.System
                || profile.ScopeId == (int)ConfigurationScopeEnum.Instance
                || profile.ScopeId == (int)ConfigurationScopeEnum.Tenant
                || (profile.ScopeId == (int)ConfigurationScopeEnum.Organization && profile.OrganizationId == context.OrganizationId)
                || (profile.ScopeId == (int)ConfigurationScopeEnum.Group && profile.GroupId == context.GroupId)
                || (profile.ScopeId == (int)ConfigurationScopeEnum.User && profile.UserId == context.UserId))
            .ToListAsync(cancellationToken);
    }

    private static NotificationPreferenceDecision ResolveRequest(
        NotificationPreferenceResolveRequest request,
        IReadOnlyDictionary<string, NotificationPreferenceCategory> categories,
        IReadOnlyDictionary<string, NotificationPreferenceChannel> channels,
        IReadOnlyCollection<NotificationChannelPreference> preferences,
        IReadOnlyCollection<ScopeTarget> hierarchy,
        MuteDecision muteDecision)
    {
        if (!categories.TryGetValue(request.CategoryCode, out var category))
        {
            throw new InvalidOperationException($"Unknown notification preference category '{request.CategoryCode}'.");
        }

        if (!channels.TryGetValue(request.ChannelCode, out var channel))
        {
            throw new InvalidOperationException($"Unknown notification preference channel '{request.ChannelCode}'.");
        }

        var enabled = DefaultEnabled(category, channel);
        var sourceScope = "Default";
        var locked = false;
        string? lockReason = null;

        foreach (var scope in hierarchy)
        {
            var row = preferences.FirstOrDefault(preference => Matches(preference, scope)
                && preference.CategoryId == category.Id
                && preference.ChannelId == channel.Id);

            if (row is null)
            {
                continue;
            }

            enabled = row.IsEnabled;
            sourceScope = ScopeName(scope.ScopeId);

            if (!row.IsLocked)
            {
                continue;
            }

            locked = true;
            lockReason = $"{sourceScope} preference lock";
            break;
        }

        if (category.IsRequired)
        {
            return new NotificationPreferenceDecision(
                category.MasterCode,
                channel.MasterCode,
                true,
                true,
                true,
                false,
                "RequiredCategory",
                "Required notification category");
        }

        if (muteDecision.IsMuted)
        {
            enabled = false;
        }

        return new NotificationPreferenceDecision(
            category.MasterCode,
            channel.MasterCode,
            enabled,
            false,
            locked,
            muteDecision.IsMuted,
            sourceScope,
            lockReason);
    }

    private static MuteDecision ResolveMute(
        IReadOnlyCollection<NotificationPreferenceProfile> profiles,
        IReadOnlyCollection<ScopeTarget> hierarchy)
    {
        var muted = false;

        foreach (var scope in hierarchy)
        {
            var row = profiles.FirstOrDefault(profile => Matches(profile, scope));
            if (row is null)
            {
                continue;
            }

            muted = row.IsMuted;
            if (row.IsLocked)
            {
                break;
            }
        }

        return new MuteDecision(muted);
    }

    private static IReadOnlyList<ScopeTarget> BuildHierarchy(ResolverContext context)
    {
        var scopes = new List<ScopeTarget>
        {
            new((int)ConfigurationScopeEnum.System, null),
            new((int)ConfigurationScopeEnum.Instance, null),
            new((int)ConfigurationScopeEnum.Tenant, null)
        };

        if (context.OrganizationId is Guid organizationId)
        {
            scopes.Add(new ScopeTarget((int)ConfigurationScopeEnum.Organization, organizationId));
        }

        if (context.GroupId is Guid groupId)
        {
            scopes.Add(new ScopeTarget((int)ConfigurationScopeEnum.Group, groupId));
        }

        scopes.Add(new ScopeTarget((int)ConfigurationScopeEnum.User, context.UserId));
        return scopes;
    }

    private static bool Matches(NotificationChannelPreference preference, ScopeTarget scope)
    {
        return preference.ScopeId == scope.ScopeId
            && TargetId(preference) == scope.TargetId;
    }

    private static bool Matches(NotificationPreferenceProfile profile, ScopeTarget scope)
    {
        return profile.ScopeId == scope.ScopeId
            && TargetId(profile) == scope.TargetId;
    }

    private static Guid? TargetId(NotificationChannelPreference preference)
    {
        return preference.ScopeId switch
        {
            (int)ConfigurationScopeEnum.Organization => preference.OrganizationId,
            (int)ConfigurationScopeEnum.Group => preference.GroupId,
            (int)ConfigurationScopeEnum.User => preference.UserId,
            _ => null
        };
    }

    private static Guid? TargetId(NotificationPreferenceProfile profile)
    {
        return profile.ScopeId switch
        {
            (int)ConfigurationScopeEnum.Organization => profile.OrganizationId,
            (int)ConfigurationScopeEnum.Group => profile.GroupId,
            (int)ConfigurationScopeEnum.User => profile.UserId,
            _ => null
        };
    }

    private static bool DefaultEnabled(NotificationPreferenceCategory category, NotificationPreferenceChannel channel)
    {
        return channel.Id switch
        {
            (int)NotificationPreferenceChannelEnum.Email => category.DefaultEmailEnabled,
            (int)NotificationPreferenceChannelEnum.InApp => category.DefaultInAppEnabled,
            (int)NotificationPreferenceChannelEnum.Push => category.DefaultPushEnabled,
            _ => false
        };
    }

    private static string ScopeName(int scopeId)
    {
        return Enum.IsDefined(typeof(ConfigurationScopeEnum), scopeId)
            ? ((ConfigurationScopeEnum)scopeId).ToString()
            : "Unknown";
    }

    private static string NormalizeCode(string code) => code.Trim().ToLowerInvariant();

    private sealed record ResolverContext(Guid TenantId, Guid UserId, Guid? OrganizationId, Guid? GroupId);

    private sealed record ScopeTarget(int ScopeId, Guid? TargetId);

    private sealed record MuteDecision(bool IsMuted);
}
