using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;

static string GetOption(string[] args, string name, string? fallback = null)
{
    var index = Array.IndexOf(args, name);
    if (index >= 0 && index + 1 < args.Length) return args[index + 1];
    if (fallback is not null) return fallback;
    throw new InvalidOperationException($"Missing option {name}.");
}

if (args.Length == 0)
{
    Console.WriteLine("SolidPOS PosCore CLI commands: init, bind, sale-offline, outbox-status");
    return 0;
}

var command = args[0];
var dbPath = GetOption(args, "--db", Path.Combine(Environment.CurrentDirectory, "solidpos-poscore.local.sqlite"));
var database = new SQLiteLocalDatabase(dbPath);
var repository = new SQLiteLocalPosRepository(database);
await repository.InitializeAsync().ConfigureAwait(false);

switch (command)
{
    case "init":
        Console.WriteLine($"PosCore local SQLite initialized: {dbPath}");
        return 0;

    case "bind":
    {
        var binding = new TerminalBinding(
            Guid.Parse(GetOption(args, "--tenant-id")),
            Guid.Parse(GetOption(args, "--store-id")),
            Guid.Parse(GetOption(args, "--terminal-id")),
            GetOption(args, "--fingerprint"),
            GetOption(args, "--terminal-token"),
            DateTimeOffset.UtcNow,
            int.Parse(GetOption(args, "--schema-version", "4")));
        await repository.SaveTerminalBindingAsync(binding).ConfigureAwait(false);
        Console.WriteLine($"Terminal bound locally: {binding.TerminalId}");
        return 0;
    }

    case "sale-offline":
    {
        var binding = await repository.GetTerminalBindingAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before creating offline sales.");
        var productId = Guid.Parse(GetOption(args, "--product-id"));
        var sku = GetOption(args, "--sku", "QSR-AMERICANO");
        var name = GetOption(args, "--name", "Americano 12oz");
        var priceCents = int.Parse(GetOption(args, "--price-cents", "4500"));
        var quantity = int.Parse(GetOption(args, "--quantity", "1"));
        var sale = new OfflineSaleDraft(
            Guid.NewGuid(),
            binding.TenantId,
            binding.StoreId,
            binding.TerminalId,
            DateTimeOffset.UtcNow,
            new[] { new OfflineSaleLineDraft(productId, null, sku, name, quantity, priceCents) },
            new[] { new OfflineSalePaymentDraft("cash", quantity * priceCents) },
            GetOption(args, "--currency", "MXN"));

        var service = new OfflineSaleService(repository, new SystemClock());
        var outboxEvent = await service.CreateOfflineSaleAsync(sale).ConfigureAwait(false);
        Console.WriteLine($"Offline sale queued. localSaleId={sale.LocalSaleId}; outboxEventId={outboxEvent.Id}; totalCents={sale.TotalCents}");
        return 0;
    }

    case "outbox-status":
    {
        var pending = await repository.GetPendingOutboxEventsAsync(500).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            Console.WriteLine("No pending local outbox events.");
            return 0;
        }

        var batch = LocalOutboxBatchPlanner.CreateBatch(Guid.NewGuid(), pending);
        Console.WriteLine($"Pending outbox events: {pending.Count}; batchId={batch.BatchId}; firstSequence={batch.Events[0].SequenceNumber}");
        return 0;
    }

    default:
        throw new InvalidOperationException($"Unknown command: {command}");
}
