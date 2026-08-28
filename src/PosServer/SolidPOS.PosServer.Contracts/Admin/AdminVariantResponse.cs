using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminVariantResponse(
    Guid Id,
    Guid TenantId,
    Guid ProductId,
    string Sku,
    string Name,
    JsonElement Attributes,
    string Status,
    long Version,
    DateTimeOffset UpdatedAt);
