namespace Explore.Application.Analytics;

public static class AnalyticsEvents
{
    public static class Properties
    {
        public const string TenantId = "tenant_id";
        public const string PageTitle = "page_title";
        public const string PageReferrer = "page_referrer";
        public const string NavigationSource = "navigation_source";
        public const string StepIndex = "step_index";
        public const string StepName = "step_name";
        public const string TotalSteps = "total_steps";
        public const string CompletedSteps = "completed_steps";
    }

    public static class PublicExperience
    {
        public static readonly AnalyticsEventDefinition PageViewed = new(
            EventName: "public.page_viewed",
            AllowedPropertyKeys: new HashSet<string>(StringComparer.Ordinal)
            {
                Properties.TenantId,
                Properties.PageTitle,
                Properties.PageReferrer,
                Properties.NavigationSource
            });
    }

    public static class TenantOnboarding
    {
        public static readonly AnalyticsEventDefinition StepCompleted = new(
            EventName: "onboarding.step_completed",
            AllowedPropertyKeys: new HashSet<string>(StringComparer.Ordinal)
            {
                Properties.TenantId,
                Properties.StepIndex,
                Properties.StepName,
                Properties.TotalSteps,
                Properties.CompletedSteps
            });
    }
}
