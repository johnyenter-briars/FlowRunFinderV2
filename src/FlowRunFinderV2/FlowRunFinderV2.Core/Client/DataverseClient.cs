using System.Net.Http.Headers;
using System.Globalization;
using System.Text.Json;
using FlowRunFinderV2.Core.Model;

namespace FlowRunFinderV2.Core.Client;

public sealed class DataverseClient : IDisposable
{
    private readonly Uri _baseApiUri;
    private readonly HttpClient _httpClient;

    public DataverseClient(Uri environmentUrl, string accessToken)
    {
        _baseApiUri = new Uri($"{environmentUrl.GetLeftPart(UriPartial.Authority).TrimEnd('/')}/api/data/v9.2/");
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
    }

    public async Task<IReadOnlyList<CloudFlow>> GetCloudFlowsAsync(CancellationToken cancellationToken)
    {
        const string query = "workflows?$select=workflowid,name,uniquename,modifiedon&$filter=category eq 5&$orderby=name asc";
        using var document = await GetJsonAsync(query, cancellationToken).ConfigureAwait(false);
        var rows = document.RootElement.GetProperty("value");
        var flows = new List<CloudFlow>();

        foreach (var row in rows.EnumerateArray())
        {
            var id = row.GetGuidOrDefault("workflowid");
            var name = row.GetStringOrDefault("name");

            if (id == Guid.Empty || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            flows.Add(new CloudFlow
            {
                WorkflowId = id,
                Name = name,
                UniqueName = row.GetStringOrDefault("uniquename"),
                ModifiedOn = row.GetDateTimeOffsetOrDefault("modifiedon")
            });
        }

        return flows;
    }

    public async Task<IReadOnlyList<FlowRun>> GetLatestFlowRunsAsync(
        Guid flowId,
        int top,
        CancellationToken cancellationToken)
    {
        if (top < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "Top must be at least 1.");
        }

        var query = "flowruns" +
                    "?$select=flowrunid,name,status,starttime,endtime,_workflow_value,workflowid,clienttrackingid,partitionid" +
                    $"&$filter=_workflow_value eq {flowId:D}" +
                    "&$orderby=starttime desc" +
                    $"&$top={top}";

        return await GetFlowRunsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FlowRun>> SearchFlowRunsAsync(
        Guid flowId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        int maxRunsToQuery,
        CancellationToken cancellationToken)
    {
        if (maxRunsToQuery < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRunsToQuery), "Max runs to query must be at least 1.");
        }

        var start = FormatDataverseDateTime(startUtc);
        var end = FormatDataverseDateTime(endUtc);
        var query = "flowruns" +
                    "?$select=flowrunid,name,status,starttime,endtime,_workflow_value,workflowid,clienttrackingid,partitionid" +
                    $"&$filter=_workflow_value eq {flowId:D} and starttime ge {start} and starttime le {end}" +
                    "&$orderby=starttime desc" +
                    $"&$top={maxRunsToQuery}";

        return await GetFlowRunsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<FlowRun>> GetFlowRunsAsync(string relativeQuery, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(relativeQuery, cancellationToken).ConfigureAwait(false);
        var rows = document.RootElement.GetProperty("value");
        var runs = new List<FlowRun>();

        foreach (var row in rows.EnumerateArray())
        {
            var runName = row.GetStringOrDefault("name") ??
                          row.GetStringOrDefault("clienttrackingid") ??
                          row.GetStringOrDefault("partitionid");

            runs.Add(new FlowRun
            {
                RunId = row.GetStringOrDefault("flowrunid") ?? runName,
                Name = runName,
                Status = row.GetStringOrDefault("status"),
                StartedOn = row.GetDateTimeOffsetOrDefault("starttime"),
                EndedOn = row.GetDateTimeOffsetOrDefault("endtime")
            });
        }

        return runs;
    }

    private static string FormatDataverseDateTime(DateTimeOffset value)
    {
        return value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeQuery, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(new Uri(_baseApiUri, relativeQuery), cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Dataverse request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        return JsonDocument.Parse(body);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

internal static class JsonElementExtensions
{
    public static string? GetStringOrDefault(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    public static Guid GetGuidOrDefault(this JsonElement element, string propertyName)
    {
        var value = element.GetStringOrDefault(propertyName);
        return Guid.TryParse(value, out var guid) ? guid : Guid.Empty;
    }

    public static DateTimeOffset? GetDateTimeOffsetOrDefault(this JsonElement element, string propertyName)
    {
        var value = element.GetStringOrDefault(propertyName);
        return DateTimeOffset.TryParse(value, out var dateTime) ? dateTime : null;
    }
}
