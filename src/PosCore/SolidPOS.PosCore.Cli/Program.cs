using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Catalog;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using SolidPOS.PosCore.Infrastructure.Sync;

static string GetOption(string[] args, string name, string? fallback = null)
{
    var index = Array.IndexOf(args, name);
    if (index >= 0 && index + 1 < args.Length) return args[index + 1];
    if (fallback is not null) return fallback;
    throw new InvalidOperationException($"Missing option {name}.");
}

if (args.Length == 0)
{
    Console.WriteLine("SolidPOS PosCore CLI commands: init, bind, sync-catalog, catalog-status, sale-offline, sale-offline-from-cache, queue-health-check, outbox-status, sync-push, retry-failed, requeue-latest-synced, fail-first-pending");
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


    case "sync-catalog":
    {
        var binding = await repository.GetTerminalBindingAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before local catalog sync.");
        var baseUrl = GetOption(args, "--base-url");
        var accessToken = GetOption(args, "--access-token", binding.TerminalToken);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var remoteCatalogClient = new HttpRemoteCatalogClient(httpClient, baseUrl);
        var service = new CatalogCacheService(repository, remoteCatalogClient, new SystemClock());
        var result = await service.RefreshAsync(accessToken).ConfigureAwait(false);
        Console.WriteLine($"Local catalog cache refreshed. products={result.CachedProductCount}; syncedAt={result.SyncedAtUtc:O}");
        return 0;
    }

    case "catalog-status":
    {
        var count = await repository.CountCatalogProductsAsync().ConfigureAwait(false);
        Console.WriteLine($"Local catalog products: {count}");
        var sku = GetOption(args, "--sku", string.Empty);
        if (!string.IsNullOrWhiteSpace(sku))
        {
            var product = await repository.GetCatalogProductBySkuAsync(sku).ConfigureAwait(false);
            if (product is null)
            {
                Console.WriteLine($"SKU not found in local catalog: {sku}");
                return 2;
            }

            Console.WriteLine($"Catalog product cached. sku={product.Sku}; productId={product.ProductId}; name={product.Name}; priceCents={product.PriceCents}; currency={product.Currency}; syncedAt={product.SyncedAtUtc:O}");
        }

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
        var cashierUserId = Guid.Parse(GetOption(args, "--cashier-user-id", binding.TerminalId.ToString()));
        var localSaleId = Guid.Parse(GetOption(args, "--local-sale-id", Guid.NewGuid().ToString()));
        var localPaymentId = Guid.Parse(GetOption(args, "--local-payment-id", Guid.NewGuid().ToString()));
        var sale = new OfflineSaleDraft(
            localSaleId,
            binding.TenantId,
            binding.StoreId,
            binding.TerminalId,
            DateTimeOffset.UtcNow,
            new[] { new OfflineSaleLineDraft(productId, null, sku, name, quantity, priceCents) },
            new[] { new OfflineSalePaymentDraft("cash", quantity * priceCents, localPaymentId) },
            GetOption(args, "--currency", "MXN"),
            cashierUserId);

        var service = new OfflineSaleService(repository, new SystemClock());
        var outboxEvent = await service.CreateOfflineSaleAsync(sale).ConfigureAwait(false);
        Console.WriteLine($"Offline sale queued. localSaleId={sale.LocalSaleId}; outboxEventId={outboxEvent.Id}; totalCents={sale.TotalCents}");
        return 0;
    }


    case "sale-offline-from-cache":
    {
        var binding = await repository.GetTerminalBindingAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before creating offline sales.");
        var sku = GetOption(args, "--sku");
        var product = await repository.GetCatalogProductBySkuAsync(sku).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"SKU {sku} is not available in the local catalog cache. Run sync-catalog before selling offline.");
        if (!product.IsSellable)
        {
            throw new InvalidOperationException($"SKU {sku} is not sellable locally. status={product.Status}; priceCents={product.PriceCents}.");
        }

        var quantity = int.Parse(GetOption(args, "--quantity", "1"));
        var cashierUserId = Guid.Parse(GetOption(args, "--cashier-user-id", binding.TerminalId.ToString()));
        var localSaleId = Guid.Parse(GetOption(args, "--local-sale-id", Guid.NewGuid().ToString()));
        var localPaymentId = Guid.Parse(GetOption(args, "--local-payment-id", Guid.NewGuid().ToString()));
        var sale = new OfflineSaleDraft(
            localSaleId,
            binding.TenantId,
            binding.StoreId,
            binding.TerminalId,
            DateTimeOffset.UtcNow,
            new[] { new OfflineSaleLineDraft(product.ProductId, product.VariantId, product.Sku, product.Name, quantity, product.PriceCents) },
            new[] { new OfflineSalePaymentDraft("cash", quantity * product.PriceCents, localPaymentId) },
            product.Currency,
            cashierUserId);

        var service = new OfflineSaleService(repository, new SystemClock());
        var outboxEvent = await service.CreateOfflineSaleAsync(sale).ConfigureAwait(false);
        Console.WriteLine($"Offline sale queued from cache. sku={product.Sku}; name={product.Name}; localSaleId={sale.LocalSaleId}; outboxEventId={outboxEvent.Id}; totalCents={sale.TotalCents}; unitPriceCents={product.PriceCents}");
        return 0;
    }

    case "queue-health-check":
    {
        var source = GetOption(args, "--source", "poscore-cli");
        var service = new LocalHealthCheckOutboxService(repository, new SystemClock());
        var outboxEvent = await service.QueueAsync(source).ConfigureAwait(false);
        Console.WriteLine($"Health check queued. outboxEventId={outboxEvent.Id}; eventType={outboxEvent.EventType}; sequence={outboxEvent.SequenceNumber}");
        return 0;
    }

    case "outbox-status":
    {
        var pending = await repository.GetPendingOutboxEventsAsync(500).ConfigureAwait(false);
        var syncedCount = await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Synced).ConfigureAwait(false);
        var failedCount = await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Failed).ConfigureAwait(false);
        var deadLetterCount = await repository.CountOutboxByStatusAsync(LocalOutboxStatus.DeadLetter).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            Console.WriteLine($"No pending local outbox events. synced={syncedCount}; failed={failedCount}; deadLetter={deadLetterCount}");
            return 0;
        }

        var batch = LocalOutboxBatchPlanner.CreateBatch(Guid.NewGuid(), pending);
        Console.WriteLine($"Pending outbox events: {pending.Count}; synced={syncedCount}; failed={failedCount}; deadLetter={deadLetterCount}; batchId={batch.BatchId}; firstSequence={batch.Events[0].SequenceNumber}");
        return 0;
    }

    case "sync-push":
    {
        var binding = await repository.GetTerminalBindingAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before remote sync push.");
        var baseUrl = GetOption(args, "--base-url");
        var batchId = Guid.Parse(GetOption(args, "--batch-id", Guid.NewGuid().ToString()));
        var limit = int.Parse(GetOption(args, "--limit", "500"));
        var accessToken = GetOption(args, "--terminal-access-token", binding.TerminalToken);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var remoteClient = new HttpRemoteSyncClient(httpClient, baseUrl);
        var service = new RemoteSyncPushService(repository, remoteClient, new SystemClock());
        var result = await service.PushPendingAsync(limit, batchId, accessToken).ConfigureAwait(false);
        if (result is null)
        {
            Console.WriteLine("No pending local outbox events to sync.");
            return 0;
        }

        Console.WriteLine($"Remote sync push completed. batchId={result.BatchId}; attempted={result.AttemptedCount}; accepted={result.AcceptedCount}; duplicate={result.DuplicateCount}; failed={result.FailedCount}; acknowledged={result.AcknowledgedEventIds.Count}");
        return 0;
    }

    case "retry-failed":
    {
        var maxAttempts = int.Parse(GetOption(args, "--max-attempts", "5"));
        var count = await repository.RetryFailedOutboxEventsAsync(maxAttempts, "manual_retry_requested").ConfigureAwait(false);
        Console.WriteLine($"Failed outbox events moved back to pending: {count}");
        return 0;
    }

    case "requeue-latest-synced":
    {
        var latest = await repository.GetLatestOutboxEventByStatusAsync(LocalOutboxStatus.Synced).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No synced outbox event exists to requeue as duplicate validation.");
        await repository.ResetOutboxEventToPendingAsync(latest.Id, "duplicate_validation_requeue").ConfigureAwait(false);
        Console.WriteLine($"Synced outbox event requeued to pending for duplicate validation: {latest.Id}");
        return 0;
    }

    case "fail-first-pending":
    {
        var pending = await repository.GetPendingOutboxEventsAsync(1).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            Console.WriteLine("No pending outbox event exists to mark failed.");
            return 0;
        }

        await repository.MarkOutboxFailedAsync(pending[0].Id, "local_validation_forced_failure").ConfigureAwait(false);
        Console.WriteLine($"Pending outbox event marked failed for retry validation: {pending[0].Id}");
        return 0;
    }

    default:
        throw new InvalidOperationException($"Unknown command: {command}");
}
