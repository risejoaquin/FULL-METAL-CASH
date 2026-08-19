using System.Net.Http.Headers;
using System.Text.Json;
using SolidPOS.PosCore.Application.Catalog;

namespace SolidPOS.PosCore.Infrastructure.Sync;

public sealed class HttpRemoteCatalogClient : IRemoteCatalogClient, IRemoteInventoryCatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public HttpRemoteCatalogClient(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<IReadOnlyList<RemoteCatalogProductSnapshot>> GetCatalogProductsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/v1/tenant/catalog");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Remote catalog sync failed with HTTP {(int)response.StatusCode}: {responseJson}");
        }

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var productsById = new Dictionary<Guid, JsonElement>();
        if (root.TryGetProperty("products", out var productsElement) && productsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var product in productsElement.EnumerateArray())
            {
                if (!product.TryGetProperty("id", out var idElement)) continue;
                if (!Guid.TryParse(idElement.GetString(), out var productId)) continue;
                productsById[productId] = product.Clone();
            }
        }

        var pricesByProduct = new Dictionary<Guid, JsonElement>();
        if (root.TryGetProperty("prices", out var pricesElement) && pricesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var price in pricesElement.EnumerateArray())
            {
                if (!price.TryGetProperty("productId", out var productIdElement)) continue;
                if (!Guid.TryParse(productIdElement.GetString(), out var productId)) continue;
                if (price.TryGetProperty("variantId", out var variantIdElement) && variantIdElement.ValueKind != JsonValueKind.Null)
                {
                    continue;
                }
                pricesByProduct[productId] = price.Clone();
            }
        }

        var snapshots = new List<RemoteCatalogProductSnapshot>();
        foreach (var (productId, product) in productsById)
        {
            var status = GetString(product, "status", "active");
            var isSellable = GetBool(product, "isSellable", true);
            if (!isSellable || !status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!pricesByProduct.TryGetValue(productId, out var price))
            {
                continue;
            }

            var priceCents = GetInt(price, "priceCents", 0);
            if (priceCents <= 0)
            {
                continue;
            }

            snapshots.Add(new RemoteCatalogProductSnapshot(
                productId,
                null,
                GetString(product, "sku", string.Empty),
                GetString(product, "name", string.Empty),
                priceCents,
                GetString(price, "currency", "MXN"),
                status,
                GetDateTimeOffset(product, "updatedAt", DateTimeOffset.UtcNow)));
        }

        return snapshots
            .OrderBy(product => product.Sku, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }


    public async Task<RemoteInventoryCacheSnapshot> GetInventoryCacheAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/v1/tenant/catalog");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Remote inventory catalog sync failed with HTTP {(int)response.StatusCode}: {responseJson}");
        }

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var recipes = new List<RemoteCatalogRecipeSnapshot>();
        if (root.TryGetProperty("recipes", out var recipesElement) && recipesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var recipe in recipesElement.EnumerateArray())
            {
                Guid recipeId = GetGuid(recipe, "id", Guid.Empty);
                Guid outputProductId = GetGuid(recipe, "outputProductId", Guid.Empty);
                Guid yieldUnitId = GetGuid(recipe, "yieldUnitId", Guid.Empty);
                if (recipeId == Guid.Empty || outputProductId == Guid.Empty || yieldUnitId == Guid.Empty) continue;

                recipes.Add(new RemoteCatalogRecipeSnapshot(
                    recipeId,
                    outputProductId,
                    GetNullableGuid(recipe, "outputVariantId"),
                    GetDecimal(recipe, "yieldQuantity", 1m),
                    yieldUnitId,
                    GetDecimal(recipe, "wastePercent", 0m),
                    GetString(recipe, "status", "active")));
            }
        }

        var recipeItems = new List<RemoteCatalogRecipeItemSnapshot>();
        if (root.TryGetProperty("recipeItems", out var recipeItemsElement) && recipeItemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in recipeItemsElement.EnumerateArray())
            {
                Guid recipeItemId = GetGuid(item, "id", Guid.Empty);
                Guid recipeId = GetGuid(item, "recipeId", Guid.Empty);
                Guid ingredientProductId = GetGuid(item, "ingredientProductId", Guid.Empty);
                Guid unitId = GetGuid(item, "unitId", Guid.Empty);
                if (recipeItemId == Guid.Empty || recipeId == Guid.Empty || ingredientProductId == Guid.Empty || unitId == Guid.Empty) continue;

                recipeItems.Add(new RemoteCatalogRecipeItemSnapshot(
                    recipeItemId,
                    recipeId,
                    ingredientProductId,
                    GetNullableGuid(item, "ingredientVariantId"),
                    GetDecimal(item, "quantity", 0m),
                    unitId,
                    GetBool(item, "optional", false)));
            }
        }

        return new RemoteInventoryCacheSnapshot(recipes, recipeItems);
    }

    private static string GetString(JsonElement element, string name, string fallback)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
        return value.GetString() ?? fallback;
    }

    private static bool GetBool(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static int GetInt(JsonElement element, string name, int fallback)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        return value.TryGetInt32(out var intValue) ? intValue : fallback;
    }


    private static Guid GetGuid(JsonElement element, string name, Guid fallback)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
        return Guid.TryParse(value.GetString(), out var parsed) ? parsed : fallback;
    }

    private static Guid? GetNullableGuid(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return Guid.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static decimal GetDecimal(JsonElement element, string name, decimal fallback)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static DateTimeOffset GetDateTimeOffset(JsonElement element, string name, DateTimeOffset fallback)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : fallback;
    }
}
