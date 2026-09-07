namespace Event.Architecture.Tests;

using System.Text.RegularExpressions;

public sealed class DurableSideEffectBoundaryTests
{
    private static readonly Regex DirectSmtpSendPattern = new(@"\.SendAsync\s*\(|\.SendMailAsync\s*\(", RegexOptions.Compiled);
    private static readonly Regex BrokerMethodPattern = new(@"\.PublishAsync\s*\(|\.BulkPublishAsync\s*\(|\.SubscribeAsync\s*\(", RegexOptions.Compiled);
    private static readonly Regex DirectBrokerOperationPattern = new(
        @"BasicPublish\s*\(|BasicAck\s*\(|BasicReject\s*\(|BasicNack\s*\(|RabbitMQ\.Client|MQContract\.RabbitMQ",
        RegexOptions.Compiled);
    private static readonly Regex SchedulerOperationPattern = new(
        @"Quartz|ISchedulerFactory|IScheduler\b|JobDataMap|TriggerBuilder|JobBuilder|CronScheduleBuilder|SimpleScheduleBuilder|DisallowConcurrentExecution",
        RegexOptions.Compiled);
    private static readonly Regex SchedulerPayloadSensitivePattern = new(
        @"(?:(?<![A-Za-z0-9])|(?<=[a-z0-9]))(?i:EmailMessage|Recipient|RecipientEmail|ToAddress|Subject|Body|HtmlBody|TextBody|Smtp|ProviderMessageId|RawError|ExceptionMessage|AccessToken|Secret)(?![a-z0-9])|(?<!User)(?:(?<![A-Za-z0-9])|(?<=[a-z0-9]))(?i:Secrets)(?![a-z0-9])",
        RegexOptions.Compiled);

    [Test]
    public async Task ApplicationHandlersShouldNotSendEmailOrPublishBrokerMessagesDirectly()
    {
        string repoRoot = FindRepoRoot();
        string featuresRoot = Path.Combine(repoRoot, "src", "Explore.Application", "Features");

        var violations = new List<string>();

        foreach (string file in EnumerateHandlerSourceFiles(featuresRoot))
        {
            string content = await File.ReadAllTextAsync(file);
            if (ReferencesEmailTransport(content))
            {
                AddForbiddenMatches(violations, file, content, DirectSmtpSendPattern, "direct SMTP send");
            }

            AddForbiddenMatches(violations, file, content, DirectBrokerOperationPattern, "direct broker operation");

            if (ReferencesBrokerTransport(content))
            {
                AddForbiddenMatches(violations, file, content, BrokerMethodPattern, "direct broker operation");
            }

            if (ReferencesEmailTransport(content) || ReferencesBrokerTransport(content))
            {
                violations.Add($"{GetRelativePath(repoRoot, file)} references a side-effect transport contract; create durable intent instead.");
            }

            AddForbiddenMatches(violations, file, content, SchedulerOperationPattern, "direct scheduler operation");
        }

        await Assert.That(violations).IsEmpty()
            .Because("Application handlers must create durable intent rows and leave SMTP/RabbitMQ/scheduler side effects to approved background infrastructure.");
    }

    [Test]
    public async Task ApiControllersShouldNotSendEmailOrPublishBrokerMessagesDirectly()
    {
        string repoRoot = FindRepoRoot();
        string controllersRoot = Path.Combine(repoRoot, "src", "Explore.API", "Controllers");

        var violations = new List<string>();

        foreach (string file in EnumerateSourceFiles(controllersRoot, "*.cs"))
        {
            string content = await File.ReadAllTextAsync(file);
            if (ReferencesEmailTransport(content))
            {
                AddForbiddenMatches(violations, file, content, DirectSmtpSendPattern, "direct SMTP send");
                violations.Add($"{GetRelativePath(repoRoot, file)} references an email transport contract; create durable intent instead.");
            }

            AddForbiddenMatches(violations, file, content, DirectBrokerOperationPattern, "direct broker operation");

            if (ReferencesBrokerTransport(content))
            {
                AddForbiddenMatches(violations, file, content, BrokerMethodPattern, "direct broker operation");
            }

            AddForbiddenMatches(violations, file, content, SchedulerOperationPattern, "direct scheduler operation");
        }

        await Assert.That(violations).IsEmpty()
            .Because("API controllers may dispatch MediatR requests or run safe config checks, but must not perform SMTP sends, broker operations, or scheduler side effects directly.");
    }

    [Test]
    public async Task DomainShouldNotReferenceSideEffectTransportsOrSchedulers()
    {
        string repoRoot = FindRepoRoot();
        string domainRoot = Path.Combine(repoRoot, "src", "Explore.Domain");

        var violations = new List<string>();

        foreach (string file in EnumerateSourceFiles(domainRoot, "*.cs"))
        {
            string content = await File.ReadAllTextAsync(file);
            AddForbiddenMatches(violations, file, content, DirectBrokerOperationPattern, "direct broker operation");
            AddForbiddenMatches(violations, file, content, SchedulerOperationPattern, "direct scheduler operation");

            if (ReferencesEmailTransport(content) || ReferencesBrokerTransport(content))
            {
                violations.Add($"{GetRelativePath(repoRoot, file)} references a side-effect transport contract; keep Domain persistence- and infrastructure-free.");
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("Domain entities may model durable intent and state, but must not know SMTP, RabbitMQ, or the scheduler.");
    }

    [Test]
    public async Task SchedulerEmailDispatchJobShouldNotCarryEmailPayloadOrSecrets()
    {
        string repoRoot = FindRepoRoot();
        string schedulerRoot = Path.Combine(repoRoot, "src", "Explore.API", "Scheduling");

        var violations = new List<string>();

        foreach (string file in EnumerateSourceFiles(schedulerRoot, "*.cs"))
        {
            string content = await File.ReadAllTextAsync(file);
            AddForbiddenMatchesIgnoringSyntacticallyIrrelevantLines(
                violations,
                file,
                content,
                SchedulerPayloadSensitivePattern,
                "scheduler payload or sensitive email field");
        }

        await Assert.That(violations).IsEmpty()
            .Because("Scheduler jobs must trigger pointer-only or payload-free work; email body, recipients, subjects, provider IDs, raw errors, and secrets stay out of scheduler state.");
    }

    [Test]
    public async Task SchedulerPayloadScannerShouldIgnoreUsingDirectivesButFlagSensitiveDeclarations()
    {
        const string content = """
            using Explore.Secrets.Database;

            public sealed class SchedulerState
            {
                public string Secret { get; init; } = string.Empty;
                public string SchedulerSecretPayload { get; init; } = string.Empty;
                var configuration = new ConfigurationBuilder().AddUserSecrets<SchedulerState>();
            }
            """;
        var violations = new List<string>();

        AddForbiddenMatchesIgnoringSyntacticallyIrrelevantLines(
            violations,
            Path.Combine(FindRepoRoot(), "tests", "Event.Architecture.Tests", "DurableSideEffectBoundaryTests.cs"),
            content,
            SchedulerPayloadSensitivePattern,
            "scheduler payload or sensitive email field");

        await Assert.That(violations.Count).IsEqualTo(2);
        await Assert.That(violations[0]).Contains(":5 contains scheduler payload or sensitive email field: 'Secret'");
        await Assert.That(violations[1]).Contains(":6 contains scheduler payload or sensitive email field: 'Secret'");
    }

    private static IEnumerable<string> EnumerateHandlerSourceFiles(string root)
    {
        return EnumerateSourceFiles(root, "*.cs")
            .Where(path => path.Replace(Path.DirectorySeparatorChar, '/')
                .Contains("/Handlers/", StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root, string searchPattern)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (string file in Directory.GetFiles(root, searchPattern, SearchOption.AllDirectories)
                     .Where(path => !IsGeneratedOrBuildOutput(path))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            yield return file;
        }
    }

    private static void AddForbiddenMatches(
        ICollection<string> violations,
        string file,
        string content,
        Regex pattern,
        string description)
    {
        foreach (Match match in pattern.Matches(content))
        {
            violations.Add($"{GetRelativePath(FindRepoRoot(), file)}:{GetLineNumber(content, match.Index)} contains {description}: '{match.Value.Trim()}'.");
        }
    }

    private static void AddForbiddenMatchesIgnoringSyntacticallyIrrelevantLines(
        ICollection<string> violations,
        string file,
        string content,
        Regex pattern,
        string description)
    {
        string maskedContent = MaskSyntacticallyIrrelevantLines(content);

        foreach (Match match in pattern.Matches(maskedContent))
        {
            string matchedValue = content.Substring(match.Index, match.Length);
            violations.Add($"{GetRelativePath(FindRepoRoot(), file)}:{GetLineNumber(content, match.Index)} contains {description}: '{matchedValue.Trim()}'.");
        }
    }

    private static string MaskSyntacticallyIrrelevantLines(string content)
    {
        string[] lines = content.Split('\n');
        bool inBlockComment = false;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string trimmed = line.TrimStart();
            bool masksLine = inBlockComment
                || string.IsNullOrWhiteSpace(line)
                || trimmed.StartsWith("using ", StringComparison.Ordinal)
                || trimmed.StartsWith("global using ", StringComparison.Ordinal)
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith('#')
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith('*');

            if (masksLine)
            {
                lines[index] = new string(' ', line.Length);
            }

            if (inBlockComment && trimmed.Contains("*/", StringComparison.Ordinal))
            {
                inBlockComment = false;
            }
            else if (!inBlockComment && trimmed.StartsWith("/*", StringComparison.Ordinal) && !trimmed.Contains("*/", StringComparison.Ordinal))
            {
                inBlockComment = true;
            }
        }

        return string.Join('\n', lines);
    }

    private static bool ReferencesEmailTransport(string content)
    {
        return content.Contains("IEmailService", StringComparison.Ordinal)
            || content.Contains("EmailMessage", StringComparison.Ordinal)
            || content.Contains("System.Net.Mail.SmtpClient", StringComparison.Ordinal)
            || content.Contains("SmtpClient", StringComparison.Ordinal)
            || content.Contains("MailKit", StringComparison.Ordinal)
            || content.Contains("MimeKit", StringComparison.Ordinal);
    }

    private static bool ReferencesBrokerTransport(string content)
    {
        return content.Contains("IMessagingProvider", StringComparison.Ordinal)
            || content.Contains("RabbitMqMessagingProvider", StringComparison.Ordinal)
            || content.Contains("RabbitMQ.Client", StringComparison.Ordinal)
            || content.Contains("MQContract.RabbitMQ", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }

    private static bool IsGeneratedOrBuildOutput(string path)
    {
        string normalized = path.Replace(Path.DirectorySeparatorChar, '/');

        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.EndsWith(".g.cs", StringComparison.Ordinal)
            || normalized.EndsWith(".Designer.cs", StringComparison.Ordinal);
    }

    private static string GetRelativePath(string repoRoot, string path)
    {
        return Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static int GetLineNumber(string content, int index)
    {
        return content[..index].Count(character => character == '\n') + 1;
    }
}
