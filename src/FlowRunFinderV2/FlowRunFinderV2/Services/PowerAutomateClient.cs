using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;
using FlowRunFinderV2.Models;

namespace FlowRunFinderV2.Services;

public sealed class PowerAutomateClient : IDisposable
{
    private const string ApiVersion = "2016-11-01";
    private static readonly Regex WorkflowRunPathRegex = new(
        @"/workflows/(?<workflowId>[^/]+)/runs/(?<runId>[^/?#]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient = new();
    private readonly HttpClient _sasHttpClient = new();

    public PowerAutomateClient(string accessToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _sasHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string?> DetectEnvironmentIdAsync(Uri environmentUrl, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments?api-version={ApiVersion}",
            cancellationToken).ConfigureAwait(false);

        var targetUrl = environmentUrl.GetLeftPart(UriPartial.Authority).TrimEnd('/').ToLowerInvariant();
        if (!document.RootElement.TryGetProperty("value", out var environments))
        {
            return null;
        }

        foreach (var environment in environments.EnumerateArray())
        {
            var name = environment.GetStringOrDefault("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (environment.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty("linkedEnvironmentMetadata", out var metadata))
            {
                var instanceUrl = metadata.GetStringOrDefault("instanceUrl")?.TrimEnd('/').ToLowerInvariant();
                if (instanceUrl == targetUrl)
                {
                    return name;
                }
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<FlowRun>> GetLatestRunsFromPowerPlatformApiAsync(
        string environmentId,
        Guid flowId,
        int top,
        CancellationToken cancellationToken)
    {
        var baseUrl = BuildPowerPlatformFlowBaseUrl(environmentId, flowId);
        var url = $"{baseUrl}/runs?api-version=1";
        using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var result = new List<FlowRun>();

        if (!document.RootElement.TryGetProperty("value", out var runs))
        {
            return result;
        }

        foreach (var run in runs.EnumerateArray().Take(top))
        {
            var properties = run.TryGetProperty("properties", out var props)
                ? props
                : default;

            var flowRun = new FlowRun
            {
                RunId = run.GetStringOrDefault("id") ?? run.GetStringOrDefault("name"),
                Name = run.GetStringOrDefault("name"),
                Status = properties.GetStringOrDefault("status") ?? run.GetStringOrDefault("status"),
                StartedOn = properties.GetDateTimeOffsetOrDefault("startTime") ??
                            properties.GetDateTimeOffsetOrDefault("starttime"),
                EndedOn = properties.GetDateTimeOffsetOrDefault("endTime") ??
                          properties.GetDateTimeOffsetOrDefault("endtime"),
                Duration = properties.GetStringOrDefault("duration")
            };

            await LoadTriggerOutputsFromRunContentAsync(environmentId, baseUrl, run, flowRun, cancellationToken)
                .ConfigureAwait(false);
            result.Add(flowRun);
        }

        return result;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Power Automate request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        return JsonDocument.Parse(body);
    }

    private static string BuildPowerPlatformFlowBaseUrl(string environmentId, Guid flowId)
    {
        var hostEnvironmentId = environmentId.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        var host = hostEnvironmentId.Length == 32
            ? $"{hostEnvironmentId[..30]}.{hostEnvironmentId[30..]}"
            : hostEnvironmentId;

        return $"https://{host}.environment.api.powerplatform.com" +
               $"/powerautomate/flows/{flowId:D}";
    }

    private async Task LoadTriggerOutputsFromRunContentAsync(
        string environmentId,
        string flowBaseUrl,
        JsonElement runFromList,
        FlowRun flowRun,
        CancellationToken cancellationToken)
    {
        var workflowId = TryFindWorkflowId(runFromList);
        var runId = TryFindRunId(runFromList) ?? flowRun.Name;

        if (string.IsNullOrWhiteSpace(workflowId) || string.IsNullOrWhiteSpace(runId))
        {
            if (!string.IsNullOrWhiteSpace(flowRun.Name))
            {
                var runName = Uri.EscapeDataString(flowRun.Name);
                var detailUrl = $"{flowBaseUrl}/runs/{runName}?api-version=1";

                try
                {
                    using var detailDocument = await GetJsonAsync(detailUrl, cancellationToken).ConfigureAwait(false);
                    workflowId ??= TryFindWorkflowId(detailDocument.RootElement);
                    runId ??= TryFindRunId(detailDocument.RootElement) ?? flowRun.Name;
                }
                catch (HttpRequestException)
                {
                    // Fall back to anything already present on the list payload below.
                }
            }
        }

        if (string.IsNullOrWhiteSpace(workflowId) || string.IsNullOrWhiteSpace(runId))
        {
            await LoadTriggerInputsFromRun(runFromList, flowRun, cancellationToken).ConfigureAwait(false);
            return;
        }

        var contentUrl = BuildTriggerOutputsContentUrl(environmentId, workflowId, runId);
        try
        {
            using var contentDocument = await GetJsonAsync(contentUrl, cancellationToken).ConfigureAwait(false);
            foreach (var triggerInput in ExpandTriggerOutputsContent(contentDocument.RootElement))
            {
                flowRun.TriggerInputs[triggerInput.Key] = triggerInput.Value;
            }
        }
        catch (HttpRequestException)
        {
            await LoadTriggerInputsFromRun(runFromList, flowRun, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildTriggerOutputsContentUrl(string environmentId, string workflowId, string runId)
    {
        var hostEnvironmentId = environmentId.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        var host = hostEnvironmentId.Length == 32
            ? $"{hostEnvironmentId[..30]}.{hostEnvironmentId[30..]}"
            : hostEnvironmentId;

        return $"https://{host}.environment.api.powerplatformusercontent.com" +
               $"/powerautomate/automations/direct/workflows/{Uri.EscapeDataString(workflowId)}" +
               $"/runs/{Uri.EscapeDataString(runId)}/contents/TriggerOutputs";
    }

    private async Task LoadTriggerInputsFromRun(JsonElement run, FlowRun flowRun, CancellationToken cancellationToken)
    {
        if (!run.TryGetProperty("properties", out var properties) ||
            !properties.TryGetProperty("trigger", out var trigger))
        {
            return;
        }

        string? triggerJson = null;

        if (trigger.TryGetProperty("inputs", out var inputs) &&
            inputs.ValueKind == JsonValueKind.Object &&
            inputs.EnumerateObject().Any())
        {
            triggerJson = inputs.GetRawText();
        }
        else if (trigger.TryGetProperty("outputs", out var outputs) &&
                 outputs.ValueKind == JsonValueKind.Object &&
                 outputs.EnumerateObject().Any())
        {
            triggerJson = outputs.GetRawText();
        }
        else if (trigger.TryGetProperty("inputsLink", out var inputsLink) &&
                 inputsLink.TryGetProperty("uri", out var inputsUri))
        {
            var sasUri = inputsUri.GetString();
            if (!string.IsNullOrWhiteSpace(sasUri))
            {
                triggerJson = await GetStringFromSasUriAsync(sasUri, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (trigger.TryGetProperty("outputsLink", out var outputsLink) &&
                 outputsLink.TryGetProperty("uri", out var outputsUri))
        {
            var sasUri = outputsUri.GetString();
            if (!string.IsNullOrWhiteSpace(sasUri))
            {
                triggerJson = await GetStringFromSasUriAsync(sasUri, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var triggerInput in ExpandTriggerJson(triggerJson))
        {
            flowRun.TriggerInputs[triggerInput.Key] = triggerInput.Value;
        }
    }

    private async Task<string?> GetStringFromSasUriAsync(string sasUri, CancellationToken cancellationToken)
    {
        using var response = await _sasHttpClient.GetAsync(sasUri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, string> ExpandTriggerJson(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var source = document.RootElement;

            if (source.ValueKind == JsonValueKind.Object &&
                source.TryGetProperty("parameters", out var parameters) &&
                parameters.ValueKind == JsonValueKind.Object)
            {
                FlattenTo(parameters, result);
                return result;
            }

            if (source.ValueKind == JsonValueKind.Object &&
                source.TryGetProperty("body", out var body) &&
                body.ValueKind == JsonValueKind.Object)
            {
                source = body;

                if (body.TryGetProperty("BusinessEntity", out var businessEntity) &&
                    businessEntity.ValueKind == JsonValueKind.Object)
                {
                    source = businessEntity;

                    if (body.TryGetProperty("FormattedValues", out var formattedValues) &&
                        formattedValues.ValueKind == JsonValueKind.Object)
                    {
                        FlattenTo(formattedValues, result);
                    }
                }
            }

            FlattenTo(source, result);
        }
        catch (JsonException)
        {
            return result;
        }

        return result;
    }

    private static Dictionary<string, string> ExpandTriggerOutputsContent(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("body", out var body) &&
            body.ValueKind == JsonValueKind.Object)
        {
            FlattenTo(body, result);
        }

        return result;
    }

    private static void FlattenTo(JsonElement source, Dictionary<string, string> destination, string? prefix = null)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in source.EnumerateObject())
        {
            if (property.Name.StartsWith("@", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(prefix) &&
                property.Name.Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = string.IsNullOrWhiteSpace(prefix)
                ? property.Name
                : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                FlattenTo(property.Value, destination, key);
                continue;
            }

            destination[key] = property.Value.ValueKind switch
            {
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Array => $"[{property.Value.GetArrayLength()} items]",
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.Value.ToString()
            };
        }
    }

    private static string? TryFindWorkflowId(JsonElement element)
    {
        var match = TryFindWorkflowRunPathMatch(element);
        return match?.Groups["workflowId"].Value;
    }

    private static string? TryFindRunId(JsonElement element)
    {
        var match = TryFindWorkflowRunPathMatch(element);
        if (match is not null)
        {
            return Uri.UnescapeDataString(match.Groups["runId"].Value);
        }

        return element.GetStringOrDefault("name");
    }

    private static Match? TryFindWorkflowRunPathMatch(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                var match = WorkflowRunPathRegex.Match(value);
                if (match.Success)
                {
                    return match;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var match = TryFindWorkflowRunPathMatch(property.Value);
                if (match is not null)
                {
                    return match;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var match = TryFindWorkflowRunPathMatch(item);
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return null;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _sasHttpClient.Dispose();
    }
}
