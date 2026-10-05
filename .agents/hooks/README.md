ABOUTME: Hook system documentation for Claude Code integration.
ABOUTME: Describes 4 C# hooks (SkillTrigger, ContextTracker, FormatCode, BuildCheck) and their configuration.

# Hooks (.NET 10 / C#)

Native C# hooks for Claude Code that enable skill auto-activation, Clean Architecture tracking, and build validation.

## What Are Hooks?

Hooks are C# scripts that run at specific points in Claude's workflow:
- **UserPromptSubmit**: Checks your prompt to suggest specialized agents/skills.
- **PostToolUse**: Tracks which Clean Architecture layers are being modified.
- **Stop**: Runs validation tasks like `dotnet build` and `dotnet format` when Claude finishes.

All hooks are written in C# and executed via `dotnet` CLI — no Node.js or Bash required.

## Hook Inventory

### 1. SkillTrigger.cs (UserPromptSubmit)

Reads the user prompt and suggests relevant agents/skills based on keyword matching.

**Triggers**: auth (401/403/keycloak/cerbos), frontend (blazor/mudblazor/css/razor), architecture (refactor/clean arch/cqs/cqrs/mapperly), build errors (error cs/build fail), database (ef core/migration/postgres), testing (tunit/bunit/mock), outbox (dead letter/message dispatch), design system (design token/wrapper/appearance), footer (social links/footer template), accessibility (wcag/aria/a11y), secrets (infisical/vault/encryption).

### 2. ContextTracker.cs (PostToolUse)

Monitors Edit/Write tool calls, detects the Clean Architecture layer from file paths, and logs to `.claude/build-cache/`. Updates `context-state.json` with the most recently touched layer.

**Layer detection**: Domain, Application, Infrastructure, API, Frontend, Shared.

### 3. FormatCode.cs (Stop)

Runs `dotnet format` on modified files to enforce `.editorconfig` rules.

### 4. BuildCheck.cs (Stop)

Verifies compilation. Reads the ContextTracker cache for targeted builds, falls back to full solution build. Captures errors to `.claude/build-cache/last-errors.txt` for the `auto-error-resolver` agent.

## Configuration

Hooks are registered in `.claude/settings.json`:

```json
{
  "hooks": {
    "UserPromptSubmit": [
      {
        "type": "command",
        "command": "dotnet .agents/hooks/SkillTrigger.cs"
      }
    ],
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "dotnet .agents/hooks/SecurityCheck.cs"
          }
        ]
      }
    ],
    "PostToolUse": [
      {
        "matcher": "Edit|MultiEdit|Write",
        "hooks": [
          {
            "type": "command",
            "command": "dotnet .agents/hooks/ContextTracker.cs"
          }
        ]
      }
    ],
    "Stop": [
      {
        "type": "command",
        "command": "dotnet .agents/hooks/FormatCode.cs"
      },
      {
        "type": "command",
        "command": "dotnet .agents/hooks/BuildCheck.cs"
      }
    ]
  }
}
```

## Supported Hook Events

| Event | When It Fires | Use Case |
|-------|---------------|----------|
| `UserPromptSubmit` | Before processing user message | Skill/agent suggestions |
| `PreToolUse` | Before a tool executes | Security checks (Bash validation) |
| `PostToolUse` | After a tool executes | File change tracking |
| `Stop` | When Claude finishes a task | Build verification, formatting |
| `Notification` | On system notifications | (not currently used) |
| `SubagentStop` | When a subagent completes | (not currently used) |

## Troubleshooting

### "dotnet: command not found"
.NET SDK is not in PATH. Run `dotnet --version` to verify installation.

### Hooks failing on Windows
Path separator issues. The C# scripts handle path normalization automatically. Use relative paths like `dotnet .agents/hooks/...` in settings.json.

### Build is too slow
ContextTracker identifies modified layers so BuildCheck can run targeted builds. Verify ContextTracker is correctly detecting layers in `.claude/build-cache/`.

## Cache

- **Location**: `.claude/build-cache/`
- **Auto-cleanup**: BuildCheck deletes cache on successful build (exit code 0).
