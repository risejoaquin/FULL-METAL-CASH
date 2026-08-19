using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Storage;

public interface ILocalPosRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default);
    Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default);
    Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default);
    Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default);
    Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default);
    Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default);
    Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default);
}
