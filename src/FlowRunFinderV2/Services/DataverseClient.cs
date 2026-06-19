using System.Net.Http.Headers;
using System.Text.Json;
using FlowRunFinderV2.Models;

namespace FlowRunFinderV2.Services;

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

    public async Task<IReadOnlyList<FlowRun>> GetLatestRunsAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var entitySetName = await ResolveEntitySetNameAsync("flowrun", "flowruns", cancellationToken).ConfigureAwait(false);
        var filter = Uri.EscapeDataString($"workflowid eq {workflowId}");
        var select = Uri.EscapeDataString("flowrunid,name,status,starttime,endtime,duration");
        var query = $"{entitySetName}?$select={select}&$filter={filter}&$orderby=starttime desc&$top=50";

        try
        {
            return await ReadRunsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (entitySetName != "flowruns")
        {
            var fallbackQuery = $"flowruns?$select={select}&$filter={filter}&$orderby=starttime desc&$top=50";
            return await ReadRunsAsync(fallbackQuery, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<FlowRun>> ReadRunsAsync(string query, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(query, cancellationToken).ConfigureAwait(false);
        var rows = document.RootElement.GetProperty("value");
        var runs = new List<FlowRun>();

        foreach (var row in rows.EnumerateArray())
        {
            runs.Add(new FlowRun
            {
                RunId = row.GetStringOrDefault("flowrunid"),
                Name = row.GetStringOrDefault("name"),
                Status = row.GetStringOrDefault("status"),
                StartedOn = row.GetDateTimeOffsetOrDefault("starttime"),
                EndedOn = row.GetDateTimeOffsetOrDefault("endtime"),
                Duration = row.GetStringOrDefault("duration")
            });
        }

        return runs;
    }

    private async Task<string> ResolveEntitySetNameAsync(string logicalName, string fallback, CancellationToken cancellationToken)
    {
        var query = $"EntityDefinitions(LogicalName='{logicalName}')?$select=EntitySetName";

        try
        {
            using var document = await GetJsonAsync(query, cancellationToken).ConfigureAwait(false);
            return document.RootElement.GetStringOrDefault("EntitySetName") ?? fallback;
        }
        catch (HttpRequestException)
        {
            return fallback;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeQuery, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(new Uri(_baseApiUri, relativeQuery), cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
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
