using System.Text.Json;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Contracts.Terminals;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sync;

public sealed class SyncPullContractTests
{
    [Fact]
    public void Sync_pull_contract_can_represent_bootstrap_snapshot()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        SyncPullResponse response = new(
            tenantId,
            storeId,
            terminalId,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow.ToString("O"),
            false,
            new TerminalRuntimeContextResponse(tenantId, storeId, terminalId),
            [
                new SyncPullChangeResponse(
                    Guid.NewGuid(),
                    "tenant.config",
                    tenantId,
                    "snapshot",
                    1,
                    DateTimeOffset.UtcNow,
                    JsonDocument.Parse("""{"uiLayout":"touch_grid"}""").RootElement.Clone(),
                    null,
                    null)
            ]);

        Assert.Null(response.PreviousCursor);
        Assert.Equal("tenant.config", response.Changes.Single().EntityType);
        Assert.False(response.HasMore);
    }
}
