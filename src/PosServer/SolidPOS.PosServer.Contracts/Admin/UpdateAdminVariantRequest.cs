using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminVariantRequest(
    Guid ProductId,
    string Sku,
    string Name,
    JsonElement Attributes,
    string Status,
    long? ExpectedVersion = null);
