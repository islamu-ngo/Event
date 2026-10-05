ABOUTME: Hook configuration and customization guide for the ISLAMU Event project.
ABOUTME: Covers hook registration format, agent integration mapping, and customization patterns.

# Hooks Configuration (.NET 10)

## Hook Registration Format

Hooks are registered in `.claude/settings.json` using the nested object format:

```json
{
  "hooks": {
    "EventName": [
      {
        "type": "command",
        "command": "dotnet .agents/hooks/ScriptName.cs"
      }
    ]
  }
}
```

For events with matchers (PreToolUse, PostToolUse):

```json
{
  "hooks": {
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
    ]
  }
}
```

## Agent Integration Mapping

| Hook Trigger | Targeted Agent | Purpose |
|---|---|---|
| Build failure (Stop) | `auto-error-resolver` | Parses CS errors and applies C# fixes |
| Auth keywords (UserPromptSubmit) | `auth-route-debugger` | Debugs Keycloak JWT and Cerbos policies |
| Frontend keywords (UserPromptSubmit) | `frontend-error-fixer` | Fixes Blazor lifecycle and MudBlazor syntax |
| Architecture keywords (UserPromptSubmit) | `code-refactor-master` | Validates CQS pattern and native operation usage |
| Test keywords (UserPromptSubmit) | `codebase-verifier` | Runs build/test verification sequence |
| Outbox keywords (UserPromptSubmit) | (skill suggestion) | Points to `outbox-pattern` skill |
| Design system keywords (UserPromptSubmit) | (skill suggestion) | Points to `design-system` skill |
| Footer keywords (UserPromptSubmit) | (skill suggestion) | Points to `footer-management` skill |

## Customization

### Adding New Triggers to SkillTrigger.cs

Add a new keyword block in the trigger rules section:

```csharp
// New Feature Area
if (prompt.Contains("keyword1") || prompt.Contains("keyword2"))
    suggestions.Add("  Consult 'skill-name' skill for feature guidance.");
```

### Adjusting Architecture Detection

If projects are renamed, update `ContextTracker.cs` layer detection:

```csharp
// Map path patterns to architecture layers
static string DetectCleanArchLayer(string filePath) =>
    filePath switch
    {
        _ when filePath.Contains("Event.Domain") => "Domain",
        _ when filePath.Contains("Event.Application") => "Application",
        _ when filePath.Contains("Event.Persistence") => "Infrastructure",
        _ when filePath.Contains("Event.API") => "API",
        _ when filePath.Contains("Explore.Blazor") => "Frontend",
        _ => "Shared"
    };
```

### Excluding Files from Build Checks

Modify `BuildCheck.cs` to add build flags:

```csharp
var processInfo = new ProcessStartInfo("dotnet", "build -p:NoWarn=CS1591");
```

## Cache Management

- **Location**: `.claude/build-cache/`
- **Contents**: Modified layer tracking, last build errors, session logs.
- **Auto-cleanup**: BuildCheck deletes cache on successful build (exit code 0).
- **Manual cleanup**: Delete the `build-cache` directory to reset tracking state.
