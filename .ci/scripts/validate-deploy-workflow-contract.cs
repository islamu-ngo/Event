#:property RestorePackagesWithLockFile=false

var failures = new List<string>();

ValidateActionContract(".ci/actions/deploy-coolify/action.yml", failures);
ValidateDeployWorkflow(
    ".github/workflows/deploy-coolify.yml",
    environmentName: "production",
    immutablePrefix: "sha-",
    requireProductionSmoke: true,
    failures);
ValidateDeployWorkflow(
    ".github/workflows/deploy-coolify-develop.yml",
    environmentName: "staging",
    immutablePrefix: "dev-",
    requireProductionSmoke: false,
    failures);

if (failures.Count > 0)
{
    Console.WriteLine("Deploy workflow contract validation failed:");
    foreach (var failure in failures)
    {
        Console.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("Deploy workflow callers preserve the shared Coolify action evidence contract.");
return 0;

static void ValidateActionContract(string path, List<string> failures)
{
    if (!File.Exists(path))
    {
        failures.Add($"{path}: local deploy action is missing.");
        return;
    }

    var text = File.ReadAllText(path);
    foreach (var input in new[]
    {
        "environment-name",
        "component",
        "coolify-webhook",
        "coolify-token",
        "smoke-base-url",
        "registry",
        "registry-user",
        "image-name",
        "immutable-tag-prefix",
        "expected-image-digest",
        "promotion-evidence-path",
        "deployment-freeze",
        "override-reason",
        "require-smoke-check"
    })
    {
        RequireContains(text, $"{input}:", path, $"missing `{input}` input", failures);
        RequireContains(text, $"inputs.{input}", path, $"missing `{input}` environment binding", failures);
    }

    RequireContains(text, "Expected image digest", path, "deployment summaries must retain expected digest", failures);
    RequireContains(text, "Promotion evidence", path, "deployment summaries must retain promotion evidence path", failures);
    RequireContains(text, "Deployment freeze", path, "deployment summaries must retain freeze state", failures);
    RequireContains(text, "Smoke check required", path, "deployment summaries must retain smoke requirement", failures);
}

static void ValidateDeployWorkflow(string path, string environmentName, string immutablePrefix, bool requireProductionSmoke, List<string> failures)
{
    if (!File.Exists(path))
    {
        failures.Add($"{path}: deploy workflow is missing.");
        return;
    }

    var text = File.ReadAllText(path);
    RequireContains(text, "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1", path, "must download retained container build evidence with pinned download-artifact", failures);
    RequireContains(text, "pattern: container-build-*", path, "must download all container-build evidence artifacts", failures);
    RequireContains(text, $"resolve-deploy-image-evidence.cs -- artifacts/container-build islamu-event-api {immutablePrefix}", path, "must resolve API immutable tag/digest evidence", failures);
    RequireContains(text, $"resolve-deploy-image-evidence.cs -- artifacts/container-build islamu-event-ui {immutablePrefix}", path, "must resolve UI immutable tag/digest evidence", failures);
    RequireContains(text, "uses: ./.ci/actions/deploy-coolify", path, "must call the shared local Coolify deploy action", failures);
    RequireCount(text, "uses: ./.ci/actions/deploy-coolify", 2, path, "must call deploy action for API and UI", failures);
    RequireContains(text, $"environment-name: {environmentName}", path, "must pass environment name to deploy action", failures);
    RequireContains(text, "expected-image-digest: ${{ steps.api-evidence.outputs.expected-image-digest }}", path, "API deploy must receive resolved expected digest", failures);
    RequireContains(text, "expected-image-digest: ${{ steps.ui-evidence.outputs.expected-image-digest }}", path, "UI deploy must receive resolved expected digest", failures);
    RequireContains(text, "promotion-evidence-path: ${{ steps.api-evidence.outputs.promotion-evidence-path }}", path, "API deploy must retain promotion evidence path", failures);
    RequireContains(text, "promotion-evidence-path: ${{ steps.ui-evidence.outputs.promotion-evidence-path }}", path, "UI deploy must retain promotion evidence path", failures);
    RequireContains(text, "deployment-freeze: ${{ vars.DEPLOYMENT_FREEZE }}", path, "deploy action must receive deployment freeze state", failures);
    RequireContains(text, "override-reason: ${{ inputs.override_reason }}", path, "deploy action must receive manual override reason", failures);
    RequireContains(text, $"immutable-tag-prefix: {immutablePrefix}", path, "deploy action must receive immutable tag prefix", failures);

    if (requireProductionSmoke)
    {
        RequireCount(text, "require-smoke-check: \"true\"", 2, path, "production API and UI deploys must require smoke checks", failures);
    }
}

static void RequireContains(string text, string expected, string path, string message, List<string> failures)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        failures.Add($"{path}: {message} (`{expected}`).");
    }
}

static void RequireCount(string text, string expected, int count, string path, string message, List<string> failures)
{
    var actual = 0;
    var index = 0;
    while ((index = text.IndexOf(expected, index, StringComparison.Ordinal)) >= 0)
    {
        actual++;
        index += expected.Length;
    }

    if (actual != count)
    {
        failures.Add($"{path}: {message}; expected {count}, found {actual} (`{expected}`).");
    }
}
