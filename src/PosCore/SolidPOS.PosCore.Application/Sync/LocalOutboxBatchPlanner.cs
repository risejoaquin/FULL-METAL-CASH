using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Sync;

public static class LocalOutboxBatchPlanner
{
    public static LocalOutboxBatch CreateBatch(Guid batchId, IReadOnlyList<LocalOutboxEvent> events)
    {
        if (events.Count == 0) throw new InvalidOperationException("Cannot create an empty sync batch.");

        var first = events[0];
        if (events.Any(item => item.TenantId != first.TenantId || item.StoreId != first.StoreId || item.TerminalId != first.TerminalId))
        {
            throw new InvalidOperationException("A sync batch cannot mix tenant, store, or terminal scopes.");
        }

        var ordered = events.OrderBy(item => item.SequenceNumber).ToArray();
        return new LocalOutboxBatch(batchId, first.TenantId, first.StoreId, first.TerminalId, ordered);
    }
}
