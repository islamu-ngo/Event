using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Components.Forms;

namespace Explore.Blazor.Client.Components.Forms;

public class ServerValidationErrorStore
{
    private ValidationMessageStore? _messageStore;
    private EditContext? _editContext;

    public void Init(EditContext editContext)
    {
        _editContext = editContext;
        _messageStore = new ValidationMessageStore(_editContext);

        // Ensure errors are cleared on standard validation request
        _editContext.OnValidationRequested += (s, e) => ClearErrors();
        _editContext.OnFieldChanged += (s, e) =>
        {
            if (_messageStore != null)
            {
                _messageStore.Clear(e.FieldIdentifier);
                _editContext.NotifyValidationStateChanged();
            }
        };
    }

    public void ClearErrors()
    {
        if (_editContext == null || _messageStore == null)
            return;

        _messageStore.Clear();
        _editContext.NotifyValidationStateChanged();
    }

    public void DisplayErrors(IDictionary<string, ICollection<string>> errors)
    {
        if (_editContext == null || _messageStore == null || errors == null)
            return;

        _messageStore.Clear();
        foreach (var error in errors)
        {
            var fieldIdentifier = new FieldIdentifier(_editContext.Model, ResolveFieldName(error.Key));
            foreach (var message in error.Value)
            {
                _messageStore.Add(fieldIdentifier, message);
            }
        }
        _editContext.NotifyValidationStateChanged();
    }

    public bool HandleApiError(Exception ex)
    {
        if (ex is ApiException<ProblemDetails> problemEx)
        {
            if (problemEx.Result?.AdditionalProperties != null && problemEx.Result.AdditionalProperties.TryGetValue("errors", out var errorsObj))
            {
                if (errorsObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                {
                    var dict = jsonElement.Deserialize<Dictionary<string, string[]>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (dict != null && dict.Count > 0)
                    {
                        var genericDict = dict.ToDictionary(k => k.Key, v => (ICollection<string>)v.Value);
                        DisplayErrors(genericDict);
                        return true;
                    }
                }
            }
        }
        else if (ex is ApiException<ValidationProblemDetails> validationEx)
        {
            if (validationEx.Result?.Errors is { Count: > 0 } errors)
            {
                DisplayErrors(errors);
                return true;
            }
        }
        else if (ex is ApiException<BaseCommandResponseOfGuid> commandEx)
        {
            if (commandEx.Result?.Errors != null && commandEx.Result.Errors.Count > 0)
            {
                var dict = new Dictionary<string, ICollection<string>>
                {
                    { string.Empty, commandEx.Result.Errors }
                };
                DisplayErrors(dict);
                return true;
            }
        }

        return false;
    }

    private string ResolveFieldName(string fieldName)
    {
        if (_editContext == null || string.IsNullOrWhiteSpace(fieldName))
        {
            return string.Empty;
        }

        var normalizedFieldName = fieldName.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fieldName;
        var property = _editContext.Model
            .GetType()
            .GetProperties()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, normalizedFieldName, StringComparison.OrdinalIgnoreCase));

        return property?.Name ?? normalizedFieldName;
    }
}
