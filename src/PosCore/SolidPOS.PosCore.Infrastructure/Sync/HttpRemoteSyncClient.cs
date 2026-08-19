using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SolidPOS.PosCore.Application.Sync;

namespace SolidPOS.PosCore.Infrastructure.Sync;

public sealed class HttpRemoteSyncClient : IRemoteSyncClient
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
        var failedCount = GetInt(root, "failedCount");

        var acknowledged = failedCount == 0
            ? request.Events.Select(item => item.EventId).ToArray()
            : request.Events.Take(acceptedCount + duplicateCount).Select(item => item.EventId).ToArray();

        return new RemoteSyncPushResult(
            request.BatchId,
            request.Events.Count,
            acceptedCount,
            duplicateCount,
            failedCount,
            raw,
            acknowledged);
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

    private static int GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property)) return 0;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) ? value : 0;
    }
}
