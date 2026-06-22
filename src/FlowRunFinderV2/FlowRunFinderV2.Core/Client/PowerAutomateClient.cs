using System.Net.Http.Headers;
using System.Text.Json;
using FlowRunFinderV2.Core.Model;
using FlowRunFinderV2.Core.Logging;

namespace FlowRunFinderV2.Core.Client;

public sealed class PowerAutomateClient : IDisposable
{
    private const string ApiVersion = "2016-11-01";
    private readonly HttpClient _httpClient = new();
    private readonly HttpClient _sasHttpClient = new();
    private readonly AppLogger? _logger;

    public PowerAutomateClient(string accessToken, AppLogger? logger = null)
    {
        _logger = logger;
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

    public async Task<PowerAutomateRunPage> GetRunsPageAsync(
        string environmentId,
        Guid flowId,
        CancellationToken cancellationToken)
    {
        var baseUrl = BuildPowerPlatformFlowBaseUrl(environmentId, flowId);
        var url = $"{baseUrl}/runs?api-version=1";
        return await GetRunsPageAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PowerAutomateRunPage> GetRunsPageAsync(string pageUrl, CancellationToken cancellationToken)
    {
        _logger?.Debug($"Power Platform run page request. Url={pageUrl}.");
        using var document = await GetJsonAsync(pageUrl, cancellationToken).ConfigureAwait(false);
        var runs = document.RootElement.TryGetProperty("value", out var runArray)
            ? runArray.EnumerateArray().Select(run => run.Clone()).ToList()
            : [];

        return new PowerAutomateRunPage(runs, GetNextLink(document.RootElement));
    }

    public async Task LoadTriggerOutputsAsync(
        string environmentId,
        Guid flowId,
        JsonElement run,
        FlowRun flowRun,
        CancellationToken cancellationToken)
    {
        var baseUrl = BuildPowerPlatformFlowBaseUrl(environmentId, flowId);
        await LoadTriggerOutputsFromRunContentAsync(environmentId, baseUrl, run, flowRun, cancellationToken)
            .ConfigureAwait(false);
    }

    public FlowRun CreateFlowRun(string environmentId, Guid flowId, JsonElement run)
    {
        return CreateFlowRunFromList(run, environmentId, BuildPowerPlatformFlowBaseUrl(environmentId, flowId));
    }

    private static FlowRun CreateFlowRunFromList(JsonElement run, string environmentId, string baseUrl)
    {
        var properties = run.TryGetProperty("properties", out var props)
            ? props
            : default;
        var runName = run.GetStringOrDefault("name");

        return new FlowRun
        {
            RunId = run.GetStringOrDefault("id") ?? runName,
            Name = runName,
            RunUrl = BuildRunUrl(environmentId, baseUrl, runName),
            Status = properties.GetStringOrDefault("status") ?? run.GetStringOrDefault("status"),
            StartedOn = properties.GetDateTimeOffsetOrDefault("startTime") ??
                        properties.GetDateTimeOffsetOrDefault("starttime"),
            EndedOn = properties.GetDateTimeOffsetOrDefault("endTime") ??
                      properties.GetDateTimeOffsetOrDefault("endtime")
        };
    }

    private static string? BuildRunUrl(string environmentId, string flowBaseUrl, string? runName)
    {
        if (string.IsNullOrWhiteSpace(runName))
        {
            return null;
        }

        var flowId = flowBaseUrl.Split('/').LastOrDefault();
        return string.IsNullOrWhiteSpace(flowId)
            ? null
            : $"https://make.powerautomate.com/environments/{environmentId}/flows/{flowId}/runs/{runName}";
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

    private static string? GetNextLink(JsonElement root)
    {
        return root.GetStringOrDefault("nextLink") ??
               root.GetStringOrDefault("@odata.nextLink") ??
               root.GetStringOrDefault("nextPageLink");
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
        var sourceRun = runFromList;
        JsonDocument? detailDocument = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(flowRun.Name))
            {
                var runName = Uri.EscapeDataString(flowRun.Name);
                var detailUrl = $"{flowBaseUrl}/runs/{runName}?api-version=1";
                _logger?.Trace($"Loading run detail. RunName={flowRun.Name}; Url={detailUrl}.");
                detailDocument = await GetJsonAsync(detailUrl, cancellationToken).ConfigureAwait(false);
                sourceRun = detailDocument.RootElement;
            }
        }
        catch (HttpRequestException)
        {
            _logger?.Debug($"Run detail request failed; using list payload. RunName={flowRun.Name}.");
            detailDocument?.Dispose();
            detailDocument = null;
            sourceRun = runFromList;
        }

        try
        {
            var triggerContent = await GetTriggerContentAsync(sourceRun, cancellationToken).ConfigureAwait(false);
            foreach (var triggerInput in ExpandTriggerOutputsContent(triggerContent))
            {
                flowRun.TriggerInputs[triggerInput.Key] = triggerInput.Value;
            }

            if (flowRun.TriggerInputs.Count > 0)
            {
                _logger?.Trace($"Trigger content loaded. RunName={flowRun.Name}; TriggerKeys={flowRun.TriggerInputs.Count}.");
                return;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger?.Debug($"Trigger content request failed. RunName={flowRun.Name}; Error={ex.Message}");
            // Keep the run visible even if trigger content has expired or is unavailable.
        }
        catch (JsonException ex)
        {
            _logger?.Debug($"Trigger content parsing failed. RunName={flowRun.Name}; Error={ex.Message}");
            // Keep the run visible even if this trigger content shape is unexpected.
        }
        finally
        {
            detailDocument?.Dispose();
        }
    }

    private async Task<JsonElement> GetTriggerContentAsync(JsonElement run, CancellationToken cancellationToken)
    {
        if (!run.TryGetProperty("properties", out var properties) ||
            !properties.TryGetProperty("trigger", out var trigger))
        {
            throw new JsonException("Run payload does not contain properties.trigger.");
        }

        var link = GetTriggerContentLink(trigger, "outputsLink") ??
                   GetTriggerContentLink(trigger, "inputsLink");

        if (!string.IsNullOrWhiteSpace(link))
        {
            _logger?.Trace($"Loading trigger content from signed/direct link. Signed={HasSignedPowerPlatformContentQuery(link)}.");
            var client = HasSignedPowerPlatformContentQuery(link) ? _sasHttpClient : _httpClient;
            using var response = await client.GetAsync(link, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Power Automate content request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
            }

            using var contentDocument = JsonDocument.Parse(body);
            return contentDocument.RootElement.Clone();
        }

        if (trigger.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Object)
        {
            return outputs.Clone();
        }

        if (trigger.TryGetProperty("inputs", out var inputs) && inputs.ValueKind == JsonValueKind.Object)
        {
            return inputs.Clone();
        }

        throw new JsonException("Run trigger does not contain outputsLink, inputsLink, outputs, or inputs.");
    }

    private static string? GetTriggerContentLink(JsonElement trigger, string linkPropertyName)
    {
        if (trigger.TryGetProperty(linkPropertyName, out var link) &&
            link.ValueKind == JsonValueKind.Object &&
            link.TryGetProperty("uri", out var uri))
        {
            return uri.GetString();
        }

        return null;
    }

    private static bool HasSignedPowerPlatformContentQuery(string url)
    {
        return url.Contains("sig=", StringComparison.OrdinalIgnoreCase) &&
               url.Contains("sv=", StringComparison.OrdinalIgnoreCase) &&
               url.Contains("sp=", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ExpandTriggerOutputsContent(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("body", out var body) &&
            body.ValueKind == JsonValueKind.Object)
        {
            var outputBody = SelectTriggerOutputBody(body);
            FlattenTo(outputBody, result);
            return result;
        }

        var fallbackBody = SelectTriggerOutputBody(root);
        FlattenTo(fallbackBody, result);
        return result;
    }

    private static JsonElement SelectTriggerOutputBody(JsonElement body)
    {
        if (TryGetObjectProperty(body, "BusinessEntity", out var businessEntity))
        {
            return businessEntity;
        }

        if (TryGetObjectProperty(body, "body", out var nestedBody))
        {
            return SelectTriggerOutputBody(nestedBody);
        }

        if (TryGetObjectProperty(body, "outputs", out var outputs) &&
            TryGetObjectProperty(outputs, "body", out var outputsBody))
        {
            return SelectTriggerOutputBody(outputsBody);
        }

        return body;
    }

    private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Object)
            {
                value = property.Value;
                return true;
            }
        }

        return false;
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

            if (string.IsNullOrWhiteSpace(prefix) && IsTopLevelWrapperProperty(property.Name))
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

    private static bool IsTopLevelWrapperProperty(string propertyName)
    {
        return propertyName.Equals("host", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("headers", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("parameters", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("subscriptionRequest", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _sasHttpClient.Dispose();
    }
}

public sealed record PowerAutomateRunPage(
    IReadOnlyList<JsonElement> Runs,
    string? NextLink);
