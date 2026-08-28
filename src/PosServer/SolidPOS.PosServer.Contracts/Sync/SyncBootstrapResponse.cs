using System.Text.Json;
using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncBootstrapResponse(
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    DateTimeOffset ServerTime,
    string InitialCursor,
    TerminalRuntimeContextResponse Terminal,
    JsonElement AccessSnapshot,
    JsonElement CatalogSnapshot,
    JsonElement InventoryPolicy,
    JsonElement LowStockThresholds,
    JsonElement SyncSettings);
