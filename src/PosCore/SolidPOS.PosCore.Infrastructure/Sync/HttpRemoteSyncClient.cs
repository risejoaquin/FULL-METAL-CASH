using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SolidPOS.PosCore.Application.Sync;

namespace SolidPOS.PosCore.Infrastructure.Sync;

public sealed class HttpRemoteSyncClient : IRemoteSyncClient, IRemoteSyncPullClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public HttpRemoteSyncClient(HttpClient httpClient, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Base URL is required.", nameof(baseUrl));
        _httpClient = httpClient;
        _baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public async Task<RemoteSyncPushResult> PushAsync(RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "api/v1/sync/push"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", terminalAccessToken);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(ToWireRequest(request)), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Remote sync push failed with HTTP {(int)response.StatusCode}: {raw}");
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = document.RootElement;
        var acceptedCount = GetInt(root, "acceptedCount");
        var duplicateCount = GetInt(root, "duplicateCount");
        var failedCount = GetInt(root, "failedCount") + GetInt(root, "rejectedCount");

        Guid[] acknowledged = ExtractAcknowledgedEventIds(root);
        if (acknowledged.Length == 0 && failedCount == 0)
        {
            acknowledged = request.Events.Select(item => item.EventId).ToArray();
        }
        else if (acknowledged.Length == 0)
        {
            acknowledged = request.Events.Take(acceptedCount + duplicateCount).Select(item => item.EventId).ToArray();
        }

        return new RemoteSyncPushResult(
            request.BatchId,
            request.Events.Count,
            acceptedCount,
            duplicateCount,
            failedCount,
            raw,
            acknowledged);
    }


    public async Task<RemoteSyncPullResponse> PullAsync(string? cursor, int limit, string terminalAccessToken, CancellationToken cancellationToken = default)
    {
        string query = string.IsNullOrWhiteSpace(cursor)
            ? $"api/v1/sync/pull?cursor=&limit={limit}"
            : $"api/v1/sync/pull?cursor={Uri.EscapeDataString(cursor)}&limit={limit}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, query));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", terminalAccessToken);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Remote sync pull failed with HTTP {(int)response.StatusCode}: {raw}");
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = document.RootElement;
        var changes = new List<RemoteSyncPullChange>();
        if (root.TryGetProperty("changes", out JsonElement changesElement) && changesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement change in changesElement.EnumerateArray())
            {
                changes.Add(new RemoteSyncPullChange(
                    Guid.Parse(change.GetProperty("id").GetString()!),
                    change.GetProperty("entityType").GetString()!,
                    Guid.Parse(change.GetProperty("entityId").GetString()!),
                    change.GetProperty("operation").GetString()!,
                    change.GetProperty("entityVersion").GetInt64(),
                    change.GetProperty("changedAt").GetDateTimeOffset(),
                    change.GetProperty("payload").Clone(),
                    TryGetGuid(change, "storeId"),
                    TryGetGuid(change, "sourceTerminalId")));
            }
        }

        return new RemoteSyncPullResponse(
            Guid.Parse(root.GetProperty("tenantId").GetString()!),
            Guid.Parse(root.GetProperty("storeId").GetString()!),
            Guid.Parse(root.GetProperty("terminalId").GetString()!),
            root.GetProperty("serverTime").GetDateTimeOffset(),
            root.TryGetProperty("previousCursor", out JsonElement previousCursor) && previousCursor.ValueKind == JsonValueKind.String ? previousCursor.GetString() : null,
            root.GetProperty("nextCursor").GetString()!,
            root.TryGetProperty("hasMore", out JsonElement hasMore) && hasMore.ValueKind == JsonValueKind.True,
            changes,
            raw);
    }

    private static object ToWireRequest(RemoteSyncPushRequest request) => new
    {
        batchId = request.BatchId,
        events = request.Events.Select(item => new
        {
            eventId = item.EventId,
            eventType = item.EventType,
            entityType = item.EntityType,
            entityId = item.EntityId,
            localOccurredAt = item.LocalOccurredAt,
            schemaVersion = item.SchemaVersion,
            payload = item.Payload
        })
    };

    private static Guid[] ExtractAcknowledgedEventIds(JsonElement root)
    {
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<Guid> acknowledged = [];
        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("status", out var statusProperty) || statusProperty.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var status = statusProperty.GetString();
            if (status is not ("accepted" or "duplicate"))
            {
                continue;
            }

            if (result.TryGetProperty("eventId", out var eventIdProperty) && eventIdProperty.ValueKind == JsonValueKind.String && Guid.TryParse(eventIdProperty.GetString(), out var eventId))
            {
                acknowledged.Add(eventId);
            }
        }

        return acknowledged.ToArray();
    }

    private static Guid? TryGetGuid(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String) return null;
        return Guid.TryParse(property.GetString(), out Guid value) ? value : null;
    }

    private static int GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property)) return 0;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) ? value : 0;
    }
}
