namespace Explore.Application.Contracts.Services;

using Explore.Application.Analytics;
using Explore.Application.Settings.Groups;

public interface IAnalyticsRuntimeProfileResolver
{
    AnalyticsRuntimeProfile Resolve(AnalyticsSettingGroup settings);
}
