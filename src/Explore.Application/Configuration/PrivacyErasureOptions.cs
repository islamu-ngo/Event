namespace Explore.Application.Configuration;

public sealed class PrivacyErasureOptions
{
    public const string SectionName = "PrivacyErasure";

    public int CurrentPolicyVersion { get; set; } = 1;
    public TimeSpan ReceiptLifetime { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan ProviderLeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan ProviderLocatorLifetime { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan ProviderPollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int ProviderBatchSize { get; set; } = 25;
    public int ProviderMaxAttempts { get; set; } = 8;
    public TimeSpan ProviderInitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ProviderMaxRetryDelay { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan MaximumBackupHorizon { get; set; } = TimeSpan.FromDays(365);
    public TimeSpan AuthorityRetentionSafetyMargin { get; set; } = TimeSpan.FromDays(30);
    public bool RetentionCleanupEnabled { get; set; } = true;
    public bool RetentionCleanupDryRun { get; set; } = true;
    public int RetentionCleanupBatchSize { get; set; } = 100;

    public TimeSpan AuthorityRetention => MaximumBackupHorizon + AuthorityRetentionSafetyMargin;

    public void Validate()
    {
        if (CurrentPolicyVersion <= 0)
        {
            throw new InvalidOperationException("PrivacyErasure:CurrentPolicyVersion must be positive.");
        }

        if (ReceiptLifetime <= TimeSpan.Zero || ReceiptLifetime > TimeSpan.FromDays(30))
        {
            throw new InvalidOperationException("PrivacyErasure:ReceiptLifetime must be greater than zero and no more than 30 days.");
        }

        if (ProviderLeaseDuration < TimeSpan.FromSeconds(10) || ProviderLeaseDuration > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException("PrivacyErasure:ProviderLeaseDuration must be between 10 seconds and 30 minutes.");
        }

        if (ProviderLocatorLifetime <= TimeSpan.Zero || ProviderLocatorLifetime > TimeSpan.FromDays(30))
        {
            throw new InvalidOperationException("PrivacyErasure:ProviderLocatorLifetime must be greater than zero and no more than 30 days.");
        }

        if (ProviderPollingInterval < TimeSpan.FromSeconds(1) || ProviderPollingInterval > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException("PrivacyErasure:ProviderPollingInterval must be between one second and five minutes.");
        }

        if (ProviderBatchSize is < 1 or > 500 || RetentionCleanupBatchSize is < 1 or > 1000)
        {
            throw new InvalidOperationException("Privacy-erasure batch sizes are outside their supported bounds.");
        }

        if (ProviderMaxAttempts is < 1 or > 25)
        {
            throw new InvalidOperationException("PrivacyErasure:ProviderMaxAttempts must be between 1 and 25.");
        }

        if (ProviderInitialRetryDelay <= TimeSpan.Zero
            || ProviderMaxRetryDelay < ProviderInitialRetryDelay
            || ProviderMaxRetryDelay > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException("Privacy-erasure provider retry delays are invalid.");
        }

        if (MaximumBackupHorizon <= TimeSpan.Zero
            || AuthorityRetentionSafetyMargin < TimeSpan.Zero
            || AuthorityRetention <= ReceiptLifetime)
        {
            throw new InvalidOperationException("Privacy-erasure authority retention must exceed the receipt lifetime.");
        }
    }
}
