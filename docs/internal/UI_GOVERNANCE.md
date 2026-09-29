# UI Governance

## 1. Overview
This document defines the strict architectural rules governing the UI implementation for the ISLAMU Blazor frontend. As the platform adopts MudBlazor v9 and modernizes its component ecosystem, these guidelines ensure consistency, accessibility, and long-term maintainability.

## 2. Component Architecture Policy

### 2.1 Primitive vs. Semantic Components
The Blazor frontend distinguishes between *Primitive Proxy Wrappers* and *Semantic Composition Components*.

- **Primitive Proxy Wrappers (BANNED):** We do not wrap native MudBlazor components simply to set default values or forward generic properties (e.g., `AppButton`, `AppTextField`, `AppCard`).
  - **Reasoning:** Proxy wrappers create high maintenance overhead, break intellisense, hide upstream API changes, and introduce unnecessary layers of indirection.
  - **Resolution:** Use native MudBlazor components directly (e.g., `MudButton`, `MudTextField`). Set visual defaults via design tokens, `MudTheme`, and explicit component parameters.

- **Semantic Composition Components (ENCOURAGED):** We build and maintain wrappers that encapsulate business intent, standardize complex layouts, or combine multiple primitives (e.g., `AppDialogShell`, `AuditLogPanel`, `PermissionGate`).
  - **Reasoning:** These components standardize UX patterns, enforce structural accessibility boundaries, and encapsulate business logic.
  - **Rule:** Semantic components MUST preserve `AdditionalAttributes` (and pass them to their root element) to ensure testing hooks and ARIA attributes can be applied.

## 3. Standard form Architecture

All forms in the platform must adhere to the **Standard form Architecture Standard** to guarantee consistent UX, robust validation, and accessibility.

- **Foundation:** Forms must use `EditForm` with `EditContext` and `FluentValidation` rather than MudBlazor's legacy `MudForm`.
- **Validation Pipeline:**
  - Standardized infrastructure components must manage form submit state (`FormSubmitState`), guarding against concurrent submissions (`FormSubmissionGuard`).
  - Server errors (e.g., 401 Unauthorized, 403 Forbidden, 400 Validation Failures) must be uniformly mapped into the `EditContext` using standard adapters (e.g., `ServerValidationErrorStore`).
- **Accessibility & Focus Management:**
  - The submit button must be keyboard reachable.
  - On validation failure, focus must be returned to the first invalid field or to a centralized `AppValidationSummary`.
  - Asynchronous submit states must be announced to screen readers.
  - Closing a dialog must restore focus to the original triggering element.

### 3.1 FormSubmitState — Mandatory Submission Lifecycle

All components with asynchronous submission logic **must** use `FormSubmitState` (from `Explore.Blazor.Client.Components.Forms`) instead of manual `bool _isSaving` fields. This enforces a consistent, enterprise-grade submission lifecycle across the entire codebase.

**Infrastructure location:** `Explore.Blazor.Client/Components/Forms/`

| Component | Purpose |
|:---|:---|
| `FormSubmitState` | Tracks `IsSubmitting`, `IsSuccess`, `HasError`, `ErrorMessage` with a clean state machine (`Start()` → `Complete()` / `Fail(msg)` / `Reset()`). |
| `FormSubmissionGuard` | Wraps `EditForm` to prevent concurrent submissions and propagate cancellation tokens. |
| `ServerValidationErrorStore` | Maps server-side validation/auth errors (400/401/403) into `EditContext` for display. |
| `AppValidationSummary` | Accessible validation summary with screen-reader announcements and focus management. |

**Usage pattern:**

```csharp
@using Explore.Blazor.Client.Components.Forms

@code {
    private FormSubmitState _submitState = new();

    private async Task SaveAsync()
    {
        _submitState.Start();
        try
        {
            var response = await Service.SaveAsync(model);
            if (response?.Success == true)
            {
                Snackbar.Add("Saved.", Severity.Success);
                _submitState.Complete();
                return;
            }
            _submitState.Fail(response?.Message ?? "Save failed.");
        }
        catch (Exception)
        {
            _submitState.Fail("An unexpected error occurred.");
        }
    }
}
```

**Button binding:**

```razor
<MudButton Disabled="_submitState.IsSubmitting" OnClick="SaveAsync">
    @(_submitState.IsSubmitting ? "Saving..." : "Save")
</MudButton>
```

**Migration status:** As of 2026-05-11, all `private bool _isSaving` declarations have been eliminated project-wide. Zero instances remain. Every component with async submission uses `FormSubmitState`.

### 3.2 Banned Patterns

| Pattern | Replacement |
|:---|:---|
| `private bool _isSaving` | `private FormSubmitState _submitState = new()` |
| `_isSaving = true / false` | `_submitState.Start()` / `_submitState.Complete()` / `_submitState.Fail(msg)` |
| `Disabled="_isSaving"` | `Disabled="_submitState.IsSubmitting"` |
| `<MudForm>` | `<EditForm>` with `FluentValidation` |

## 4. MudBlazor v9 Default Strategy

MudBlazor v9 removed `MudGlobal` theming defaults (such as `ButtonVariant`, `InputDefaults`, etc.) to cleanly separate visual theming from component behavior.

- **Do Not Use `MudGlobal`:** Do not attempt to re-introduce or rely on `MudGlobal` visual defaults.
- **Provider Options:** Configure valid provider-level defaults exclusively within `AddMudServices()` in `Program.cs` (e.g., Snackbar configuration, dialog positioning defaults).
- **Visual Styling:**
  - Manage visual consistency using `MudTheme` (especially `LayoutProperties`), CSS custom properties (design tokens), and explicit component parameters.
  - Scoped component CSS should be used for highly specific layout adjustments instead of global overrides.

## 5. CSS Styling and Overrides

- **Global Overrides:** The use of `.mud-*` selectors in global stylesheets (`mudblazor-overrides.css`) is strictly limited. Only low-risk visual tokens should be mapped, and structural/layout overrides should be thoroughly documented and scoped.
- **CSS Layers:** All custom styling must respect the defined `@layer` architecture in `DESIGN_SYSTEM.md`. Scoped CSS isolation (`::deep`) is the preferred method for customizing MudBlazor components internally within semantic wrappers.

## 6. MudForm Deprecation Status

`MudForm` is **banned** from all new development. As of 2026-05-11:

- **Zero** `<MudForm>` tags exist in the Blazor client codebase.
- **Zero** `private bool _isSaving` declarations exist in the Blazor client codebase.
- All forms use `EditForm` + `FluentValidation` or have no form submission logic.
- All async submission flows use `FormSubmitState` from the standard form infrastructure.
