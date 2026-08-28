using System.Text.Json;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Sync;

public sealed class LocalHealthCheckOutboxService
{
    private readonly ILocalPosRepository _repository;
    private readonly IClock _clock;

    public LocalHealthCheckOutboxService(ILocalPosRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<LocalOutboxEvent> QueueAsync(string source, CancellationToken cancellationToken = default)
    {
        TerminalBinding binding = await _repository.GetTerminalBindingAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before queueing sync health checks.");

        var payload = new
        {
            source,
            terminalId = binding.TerminalId,
            storeId = binding.StoreId,
            tenantId = binding.TenantId,
            queuedAtUtc = _clock.UtcNow
        };

        var outboxEvent = new LocalOutboxEvent(
            Guid.NewGuid(),
            binding.TenantId,
            binding.StoreId,
            binding.TerminalId,
            "pos.health_check",
            binding.SchemaVersion,
            _clock.UtcNow.ToUnixTimeMilliseconds(),
            JsonSerializer.Serialize(payload),
            LocalOutboxStatus.Pending,
            _clock.UtcNow);

        await _repository.SaveOutboxEventAsync(outboxEvent, cancellationToken).ConfigureAwait(false);
        return outboxEvent;
    }
}
