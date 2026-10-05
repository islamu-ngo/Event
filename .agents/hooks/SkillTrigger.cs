using System;
using System.IO;
using System.Collections.Generic;

// Skill/Agent trigger script
// Runs on UserPromptSubmit — suggests agents/skills based on prompt keywords.

// 1. ANTI-BLOCKING SAFETY
if (!Console.IsInputRedirected)
{
    Environment.Exit(0);
}

try
{
    // 2. Read prompt
    string prompt = "";
    using (var reader = new StreamReader(Console.OpenStandardInput()))
    {
        if (reader.Peek() == -1) Environment.Exit(0);
        prompt = reader.ReadToEnd()?.ToLower() ?? "";
    }

    if (string.IsNullOrWhiteSpace(prompt)) Environment.Exit(0);

    // 3. Trigger rules
    var suggestions = new List<string>();

    // Authentication/Authorization
    if (prompt.Contains("401") || prompt.Contains("403") || prompt.Contains("keycloak") || prompt.Contains("token") || prompt.Contains("cerbos"))
        suggestions.Add("  Use 'auth-route-debugger' agent for security issues.");

    // Frontend/UI
    if (prompt.Contains("blazor") || prompt.Contains("mudblazor") || prompt.Contains("css") || prompt.Contains("razor") || prompt.Contains("component"))
        suggestions.Add("  Use 'frontend-error-fixer' agent for UI issues.");

    // Architecture/Refactoring
    if (prompt.Contains("refactor") || prompt.Contains("clean arch") || prompt.Contains("cqs") || prompt.Contains("cqrs") || prompt.Contains("mapperly"))
        suggestions.Add("  Use 'code-refactor-master' agent or consult Clean Architecture skills.");

    // Build Errors
    if (prompt.Contains("error cs") || prompt.Contains("build fail") || prompt.Contains("compilation"))
        suggestions.Add("  Use 'auto-error-resolver' agent to fix compilation errors.");

    // Database/EF Core
    if (prompt.Contains("database") || prompt.Contains("ef core") || prompt.Contains("migration") || prompt.Contains("postgres"))
        suggestions.Add("  Consult 'dotnet-efcore-guidelines' skill for database patterns.");

    // Testing
    if (prompt.Contains("test") || prompt.Contains("tunit") || prompt.Contains("mock") || prompt.Contains("bunit"))
        suggestions.Add("  Use 'codebase-verifier' agent for build/test verification.");

    // Outbox/Messaging
    if (prompt.Contains("outbox") || prompt.Contains("dead letter") || prompt.Contains("message dispatch") || prompt.Contains("event delivery"))
        suggestions.Add("  Consult 'outbox-pattern' skill for transactional outbox patterns.");

    // Design System/Styling
    if (prompt.Contains("design system") || prompt.Contains("design token") || prompt.Contains("wrapper component") || prompt.Contains("appbutton") || prompt.Contains("appearance"))
        suggestions.Add("  Consult 'design-system' skill for CSS layers, tokens, and wrapper components.");

    // Footer Management
    if (prompt.Contains("footer") || prompt.Contains("social links") || prompt.Contains("footer template"))
        suggestions.Add("  Consult 'footer-management' skill for footer customization patterns.");

    // Accessibility
    if (prompt.Contains("accessibility") || prompt.Contains("wcag") || prompt.Contains("aria") || prompt.Contains("screen reader") || prompt.Contains("a11y"))
        suggestions.Add("  Consult 'blazor-ui-conventions' skill (accessibility section) and docs/ACCESSIBILITY.md.");

    // Secrets Management
    if (prompt.Contains("secret") || prompt.Contains("infisical") || prompt.Contains("vault") || prompt.Contains("encryption") || prompt.Contains("key rotation"))
        suggestions.Add("  Consult docs/SECRETS.md for secret provider patterns.");

    // 4. Output
    if (suggestions.Count > 0)
    {
        Console.WriteLine("\n  Suggested Skills/Agents:");
        foreach (var s in suggestions) Console.WriteLine(s);
        Console.WriteLine("");
    }
}
catch
{
    // Never fail the user prompt because of a hook
}

Environment.Exit(0);
