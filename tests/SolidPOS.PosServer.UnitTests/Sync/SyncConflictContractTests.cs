using System.Text.Json;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Contracts.Terminals;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sync;

public sealed class SyncConflictContractTests
{
    [Fact]
    public void Sync_conflict_response_preserves_payloads_and_resolution_state()
    {
        JsonElement localPayload = JsonSerializer.SerializeToElement(new { local = true });
        JsonElement serverPayload = JsonSerializer.SerializeToElement(new { server = true });

        SyncConflictResponse response = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "sale",
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            2,
            localPayload,
            serverPayload,
            null,
            null,
            "pending",
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        Assert.Equal("pending", response.Status);
        Assert.Equal(JsonValueKind.Object, response.LocalPayload.ValueKind);
        Assert.Equal(JsonValueKind.Object, response.ServerPayload.ValueKind);
    }

    [Fact]
    public void Sync_bootstrap_response_contains_terminal_and_snapshots()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        SyncBootstrapResponse response = new(
            tenantId,
            storeId,
            terminalId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.ToString("O"),
            new TerminalRuntimeContextResponse(tenantId, storeId, terminalId),
            JsonSerializer.SerializeToElement(new { users = Array.Empty<object>() }),
            JsonSerializer.SerializeToElement(new { products = Array.Empty<object>() }),
            JsonSerializer.SerializeToElement(new { allowNegativeStock = true }),
            JsonSerializer.SerializeToElement(Array.Empty<object>()),
            JsonSerializer.SerializeToElement(new { maxEventsPerBatch = 500 }));

        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal(storeId, response.Terminal.StoreId);
        Assert.Equal(JsonValueKind.Object, response.CatalogSnapshot.ValueKind);
    }
}
