<!-- ABOUTME: Versioned operator template for Microsoft Forms completion callbacks through Power Automate. -->
<!-- ABOUTME: Defines the exact envelope, activation gates, and CSV reconciliation contract without claiming a Forms API. -->

# Microsoft Forms Power Automate Template

> **Audience:** Operators | Integrators
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-08-11
> **Source Anchors:** `MicrosoftFormsRegistrationProviderDescriptor.cs`, `RegistrationProviderManagedPublishPreflightService.cs`, `RegistrationProviderManagementHandlers.cs`, `MicrosoftFormsRegistrationProviderDescriptorTests.cs`

This template connects an organizational Microsoft Form to ISLAMU Event through a solution-aware Power Automate flow. The pinned connector contract is `POWER_AUTOMATE_V1`.

## Supported Contract

| Field | Required value |
|---|---|
| Provider code | `MICROSOFT_FORMS` |
| Deployment | `MICROSOFT_365` |
| API/connector contract | `POWER_AUTOMATE_V1` |
| Adapter policy | `ISLAMU_EVENT_MICROSOFT_FORMS_V1` |
| Evidence revision | `2026-08-11` |
| Callback key header | `X-ISLAMU-Event-Callback-Key` |

The automated connector is supported only for Microsoft 365 organizational accounts. Personal-account forms remain link/embed/manual-reconciliation integrations.

## Configure The Binding

In Studio, open the event's Integrations section and use only the HAL-exposed connection, binding, mapping, and publish actions.

1. Create a Microsoft Forms connection with the pinned tuple above and `https://forms.office.com` as its approved origin.
2. Create or edit a binding and set:
   - **Provider form ID** to the Microsoft Form ID.
   - **Connector contract version** to `POWER_AUTOMATE_V1`.
   - **Callback secret binding ID** to the tenant secret binding containing the callback key.
   - completion mode to callback and trust level to completion-only.
3. Add a required field mapping from `system.registration_attempt_token` to a required short-answer question in the Form.
4. Map every required authoritative field to its Microsoft Forms response-detail field.

The launch URL prefills the correlation question with `attemptId|attemptToken`. The value is correlation-only, can be visible to the respondent, and is not identity proof. If it is removed or changed, processing parks the response for reconciliation.

## Build The Flow

Create a solution-aware cloud flow in the Microsoft 365 tenant:

1. Trigger: **Microsoft Forms - When a new response is submitted**.
2. Action: **Microsoft Forms - Get response details** using the same Form ID and trigger response ID.
3. Split the required correlation answer on the first `|`:
   - first segment: `attemptId`
   - second segment: `attemptToken`
4. HTTP `POST` to:
   `https://{islamu-event-host}/api/integrations/registration/MICROSOFT_FORMS/{bindingId}/callback`
5. Add header `X-ISLAMU-Event-Callback-Key` with the binding-scoped callback key.
6. Send `Content-Type: application/json` and this contract:

```json
{
  "providerCode": "MICROSOFT_FORMS",
  "bindingId": "{binding-guid}",
  "formId": "{microsoft-form-id}",
  "responseId": "{trigger-response-id}",
  "attemptId": "{first-correlation-segment}",
  "attemptToken": "{second-correlation-segment}",
  "timestamp": "{utcNow ISO-8601}",
  "mappedValues": {
    "registration.email": "{response-detail-value}"
  },
  "contractVersion": "POWER_AUTOMATE_V1",
  "idempotencyKey": "{microsoft-form-id}:{trigger-response-id}"
}
```

Use explicit `mappedValues` entries that match the saved Studio mappings. Do not send the complete response-details object.

## Activate

1. Save and publish the flow.
2. Submit one test response through the ISLAMU Event launch link.
3. Confirm the callback appears in provider health.
4. Publish the binding.

Publication fails closed until the form ID, connector contract, callback secret, required mappings, correlation mapping, and one successfully processed verified callback are present. Manual imports and pending callback effects do not satisfy this gate. A callback timestamp more than five minutes from server time is rejected.

## Reconcile From CSV

Export responses to Excel, save the relevant sheet as UTF-8 CSV, upload it through the normal storage-object flow, then use the Studio manual-import action with that storage object ID.

The CSV must be at most 1 MiB with one header row and at most 500 response rows. Required columns are:

```text
responseId,attemptId,attemptToken,timestamp
```

Additional columns become mapped values. Imports deduplicate against callback intake by `bindingId + responseId`; duplicate response IDs are skipped. The complete file is validated before one transaction persists its messages and effects, so a bad later row cannot leave earlier rows queued. Duplicate headers, malformed quoting, missing identity columns, and invalid rows fail closed.

Excel is reconciliation input only. Editing the workbook does not change Microsoft Forms and does not make Excel registration transaction authority.

## Solution Export Boundary

The repository does not fabricate an importable Power Platform solution package. Connection references, environment variables, publisher identity, and the final published flow must come from a real Microsoft 365/Dataverse environment. After validating this template, export that tenant's published solution and version it through the tenant's deployment process; Microsoft exports only the latest published flow version, not drafts or version history.

## Unsupported Claims

- No Microsoft Graph Forms response API is used.
- No first-party Microsoft Forms webhook endpoint is claimed.
- Delivery is not exactly once; ISLAMU Event enforces idempotency.
- ISLAMU Event does not provision Forms or read their schemas.
- Query parameters are used only for the organizer-configured correlation question, not as hidden or trusted identity fields.

## Related

- [Integrations](../INTEGRATIONS.md)
- [Security Model](../SECURITY-MODEL.md)
- [Microsoft Forms connector](https://learn.microsoft.com/en-us/connectors/microsoftforms/)
- [Power Automate Forms overview](https://learn.microsoft.com/en-us/power-automate/forms/overview)
