# Microsoft Forms Conformance Evidence

Verified: 2026-08-11

## Evidence sources

- Microsoft Learn:
  - <https://learn.microsoft.com/en-us/connectors/microsoftforms/>
  - <https://learn.microsoft.com/en-us/power-automate/forms/overview>
  - <https://learn.microsoft.com/en-us/power-automate/forms/popular-scenarios>
  - <https://learn.microsoft.com/en-us/connectors/custom-connectors/connection-parameters>
  - <https://learn.microsoft.com/en-us/power-automate/drafts-versioning>
  - <https://learn.microsoft.com/en-us/power-automate/export-flow-solution>
- Microsoft Support:
  - <https://support.microsoft.com/en-us/forms/send-a-form-and-collect-responses>
  - <https://support.microsoft.com/en-us/forms/create-an-automated-workflow-for-microsoft-forms>
  - <https://support.microsoft.com/en-us/forms/check-and-share-your-form-results>
- Context7 library `/microsoftdocs/power-platform`, queried for solution export, connection references, and flow versioning.
- Tavily MCP was requested and invoked, but no Tavily MCP server was registered in this session. No Tavily-derived claim is included.

## Pinned tuple

| Field | Value |
|---|---|
| Provider code | `MICROSOFT_FORMS` |
| Deployment kind | `MICROSOFT_365` |
| API version | `POWER_AUTOMATE_V1` |
| Adapter policy | `ISLAMU_EVENT_MICROSOFT_FORMS_V1` |
| Evidence revision | `2026-08-11` |

Unknown deployment codes, API versions, policy versions, or evidence revisions are unsupported and fail closed.

## Proven contracts

- The Microsoft Forms connector works only with organizational accounts.
- The documented connector exposes `When a new response is submitted`, `Get response details`, and `Get form details`.
- The response trigger supplies a response ID; `Get response details` requires the form ID and response ID.
- Group forms may require manually entering the form ID in Power Automate.
- Microsoft documents link and iframe sharing through the Forms collection surface.
- API-key authentication is supported for Power Platform custom connectors and can place the key in a request header.
- Solution-aware cloud flows export only the latest published version; drafts and version history are not included in exports.
- Duplicate flow actions are possible, so the ISLAMU Event callback contract must be idempotent.
- Excel exports are reporting and reconciliation inputs. Workbook edits do not change the form response and Excel is not registration transaction authority.

## Proven capability profile

The connector can support `REDIRECT`, `EMBED`, `MANUAL`, and `CALLBACK_VERIFICATION` only for the exact tuple above and only after binding-level setup gates pass.

`SCHEMA_READ`, `FORM_PROVISION`, `SUBMISSION_WRITE`, `SUBMISSION_READ`, `SUBSCRIPTION_MANAGEMENT`, and provider polling are not claimed. The organizer configures the Form and Power Automate flow in Microsoft 365, then maps documented response fields in ISLAMU.

Automatic completion is not a Microsoft Forms-native guarantee. The callback is organizer-controlled delegated automation, and ISLAMU policy remains authoritative for finalization or reconciliation review.

## Unsupported claims

- Microsoft Forms does not have a documented first-party webhook endpoint used by this integration; Power Automate or Logic Apps delivers the callback.
- No documented Microsoft Graph response API is used.
- Arbitrary Forms URL query parameters are not treated as preserved response metadata.
- No hidden-field or automatic per-attempt correlation capability is claimed without a documented Forms contract.
- The organizer must create one required short-answer correlation question. ISLAMU prefills its configured field with `attemptId|attemptToken`; Power Automate splits that visible correlation-only value into the callback envelope.
- Delivery is not claimed to be exactly once.
- Personal Microsoft accounts are not claimed to support the Forms Power Automate connector; they remain link/embed/manual-reconciliation only.
