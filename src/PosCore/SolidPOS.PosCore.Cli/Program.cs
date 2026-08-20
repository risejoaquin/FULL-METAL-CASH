using System.Text.Json;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Cash;
using SolidPOS.PosCore.Application.Hardware;
using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Application.Catalog;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using SolidPOS.PosCore.Infrastructure.Sync;
using SolidPOS.PosCore.Infrastructure.Hardware;

static string GetOption(string[] args, string name, string? fallback = null)
{
    var index = Array.IndexOf(args, name);
    if (index >= 0 && index + 1 < args.Length) return args[index + 1];
    if (fallback is not null) return fallback;
    throw new InvalidOperationException($"Missing option {name}.");
}


static Guid ReadGuid(JsonElement root, string name)
{
    if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String || !Guid.TryParse(property.GetString(), out Guid value))
    {
        throw new InvalidOperationException($"JSON property '{name}' must be a GUID string.");
    }

    return value;
}

static Guid? ReadNullableGuid(JsonElement root, string name)
{
    if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind == JsonValueKind.Null) return null;
    if (property.ValueKind != JsonValueKind.String) return null;
    return Guid.TryParse(property.GetString(), out Guid value) ? value : null;
}

static string ReadString(JsonElement root, string name, string fallback)
{
    return root.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
        ? property.GetString() ?? fallback
        : fallback;
}

static string? ReadNullableString(JsonElement root, string name)
{
    return root.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : null;
}

static int ReadInt(JsonElement root, string name)
{
    if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int value))
    {
        throw new InvalidOperationException($"JSON property '{name}' must be an integer.");
    }

    return value;
}

static DateTimeOffset? ReadNullableDateTimeOffset(JsonElement root, string name)
{
    if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String) return null;
    return DateTimeOffset.TryParse(property.GetString(), out DateTimeOffset value) ? value : null;
}

if (args.Length == 0)
{
    Console.WriteLine("SolidPOS PosCore CLI commands: init, bind, sync-catalog, sync-inventory-cache, catalog-status, inventory-status, sale-offline, sale-offline-from-cache, sale-offline-from-cache-with-inventory, queue-health-check, outbox-status, sync-push, retry-failed, requeue-latest-synced, fail-first-pending, inventory-reconcile, open-local-shift, cash-in, cash-out, cash-status, close-local-shift, sale-offline-from-cache-cash, sync-pull, pull-status, save-remote-sale, save-remote-receipt, readmodel-status, sync-local-user, login-local, require-permission-local, whoami-local, logout-local, auth-status, queue-receipt-print, process-print-jobs, open-cash-drawer-hardware, scan-barcode, authorize-payment-terminal, hardware-status");
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



    case "sync-local-user":
    {
        var tenantId = Guid.Parse(GetOption(args, "--tenant-id"));
        var storeId = Guid.Parse(GetOption(args, "--store-id"));
        var userId = Guid.Parse(GetOption(args, "--user-id"));
        var email = GetOption(args, "--email");
        var displayName = GetOption(args, "--display-name", email);
        var password = GetOption(args, "--password");
        var roleCode = GetOption(args, "--role", "cashier");
        var maxOfflineHours = int.Parse(GetOption(args, "--max-offline-hours", "72"));
        var lastSyncHoursAgo = int.Parse(GetOption(args, "--last-sync-hours-ago", "0"));
        var permissions = GetOption(args, "--permissions", "sales.create,sync.push,sync.pull,cash.shift.open,cash.shift.close,receipts.issue")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var syncedAt = DateTimeOffset.UtcNow.AddHours(-lastSyncHoursAgo);
        var user = new LocalUser(userId, tenantId, storeId, email, displayName, LocalPasswordHasher.Hash(password), roleCode, true, syncedAt, maxOfflineHours);
        await repository.SaveLocalUserAsync(user, permissions, CancellationToken.None).ConfigureAwait(false);
        await repository.LogLocalAuditEventAsync(new LocalAuditEvent(Guid.NewGuid(), tenantId, storeId, userId, null, "local.auth.user_synced", $"Local auth user cache refreshed for {email}.", DateTimeOffset.UtcNow)).ConfigureAwait(false);
        Console.WriteLine($"Local auth user cached. userId={user.UserId}; email={user.Email}; role={user.RoleCode}; permissions={permissions.Length}; maxOfflineHours={user.MaxOfflineHours}; lastSyncedAt={user.LastSyncedAtUtc:O}");
        return 0;
    }

    case "login-local":
    {
        var email = GetOption(args, "--email");
        var password = GetOption(args, "--password");
        var service = new LocalAuthService(repository, new SystemClock());
        var session = await service.LoginAsync(email, password, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Local login succeeded. sessionId={session.SessionId}; userId={session.UserId}; email={session.Email}; role={session.RoleCode}; expiresAt={session.ExpiresAtUtc:O}");
        return 0;
    }

    case "require-permission-local":
    {
        var sessionIdText = GetOption(args, "--session-id", string.Empty);
        var session = string.IsNullOrWhiteSpace(sessionIdText)
            ? await repository.GetLatestActiveLocalSessionAsync().ConfigureAwait(false)
            : await repository.GetLocalSessionAsync(Guid.Parse(sessionIdText)).ConfigureAwait(false);
        if (session is null) throw new InvalidOperationException("An active local session is required.");
        var permission = GetOption(args, "--permission");
        var service = new LocalAuthService(repository, new SystemClock());
        await service.RequirePermissionAsync(session.SessionId, permission, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Local permission granted. sessionId={session.SessionId}; userId={session.UserId}; permission={permission}");
        return 0;
    }

    case "whoami-local":
    {
        var sessionIdText = GetOption(args, "--session-id", string.Empty);
        var session = string.IsNullOrWhiteSpace(sessionIdText)
            ? await repository.GetLatestActiveLocalSessionAsync().ConfigureAwait(false)
            : await repository.GetLocalSessionAsync(Guid.Parse(sessionIdText)).ConfigureAwait(false);
        if (session is null)
        {
            Console.WriteLine("No active local session found.");
            return 2;
        }
        Console.WriteLine($"Local session. sessionId={session.SessionId}; userId={session.UserId}; email={session.Email}; displayName={session.DisplayName}; role={session.RoleCode}; status={session.Status}; expiresAt={session.ExpiresAtUtc:O}");
        return 0;
    }

    case "logout-local":
    {
        var sessionIdText = GetOption(args, "--session-id", string.Empty);
        var session = string.IsNullOrWhiteSpace(sessionIdText)
            ? await repository.GetLatestActiveLocalSessionAsync().ConfigureAwait(false)
            : await repository.GetLocalSessionAsync(Guid.Parse(sessionIdText)).ConfigureAwait(false);
        if (session is null) throw new InvalidOperationException("An active local session is required for logout.");
        var service = new LocalAuthService(repository, new SystemClock());
        await service.LogoutAsync(session.SessionId, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Local logout completed. sessionId={session.SessionId}; userId={session.UserId}");
        return 0;
    }

    case "auth-status":
    {
        var summary = await repository.GetLocalAuthSummaryAsync().ConfigureAwait(false);
        var lastSyncedAt = summary.LastSyncedAtUtc?.ToString("O") ?? string.Empty;
        Console.WriteLine($"Local auth cache. users={summary.UserCount}; permissions={summary.PermissionCount}; activeSessions={summary.ActiveSessionCount}; auditEvents={summary.AuditEventCount}; lastSyncedAt={lastSyncedAt}");
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


    case "sync-inventory-cache":
    {
        var binding = await repository.GetTerminalBindingAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before local inventory cache sync.");
        var baseUrl = GetOption(args, "--base-url");
        var accessToken = GetOption(args, "--access-token", binding.TerminalToken);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var remoteCatalogClient = new HttpRemoteCatalogClient(httpClient, baseUrl);
        var service = new InventoryCacheService(repository, remoteCatalogClient, new SystemClock());
        var result = await service.RefreshAsync(accessToken).ConfigureAwait(false);
        Console.WriteLine($"Local inventory cache refreshed. recipes={result.RecipeCount}; recipeItems={result.RecipeItemCount}; syncedAt={result.SyncedAtUtc:O}");
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


    case "inventory-status":
    {
        var recipeCount = await repository.CountInventoryRecipesAsync().ConfigureAwait(false);
        var recipeItemCount = await repository.CountInventoryRecipeItemsAsync().ConfigureAwait(false);
        Console.WriteLine($"Local inventory cache: recipes={recipeCount}; recipeItems={recipeItemCount}");
        var localSaleIdOption = GetOption(args, "--local-sale-id", string.Empty);
        if (!string.IsNullOrWhiteSpace(localSaleIdOption))
        {
            var movements = await repository.GetInventoryMovementsByLocalSaleIdAsync(Guid.Parse(localSaleIdOption)).ConfigureAwait(false);
            var totalQuantityDelta = movements.Sum(x => x.QuantityDelta);
            Console.WriteLine($"Local inventory movements: localSaleId={localSaleIdOption}; count={movements.Count}; totalQuantityDelta={totalQuantityDelta.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
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


    case "sale-offline-from-cache-with-inventory":
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

        var inventoryService = new LocalInventoryConsumptionService(repository, new SystemClock());
        var movements = await inventoryService.BuildMovementsAsync(sale).ConfigureAwait(false);
        if (movements.Count == 0)
        {
            throw new InvalidOperationException($"SKU {sku} has no local recipe inventory movements. Run sync-inventory-cache and verify recipe cache before validating iteration 09.");
        }

        var saleService = new OfflineSaleService(repository, new SystemClock());
        var outboxEvent = await saleService.CreateOfflineSaleWithInventoryAsync(sale, movements, CancellationToken.None).ConfigureAwait(false);
        var totalQuantityDelta = movements.Sum(x => x.QuantityDelta);
        Console.WriteLine($"Offline sale queued from cache with inventory. sku={product.Sku}; name={product.Name}; localSaleId={sale.LocalSaleId}; outboxEventId={outboxEvent.Id}; totalCents={sale.TotalCents}; unitPriceCents={product.PriceCents}; localInventoryMovements={movements.Count}; localInventoryTotalDelta={totalQuantityDelta.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return 0;
    }


    case "open-local-shift":
    {
        var binding = await repository.GetTerminalBindingAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before opening a local cash shift.");
        var shiftId = Guid.Parse(GetOption(args, "--shift-id", Guid.NewGuid().ToString()));
        var openedByUserId = Guid.Parse(GetOption(args, "--opened-by-user-id"));
        var openingAmountCents = int.Parse(GetOption(args, "--opening-amount-cents", "0"));
        if (openingAmountCents < 0) throw new InvalidOperationException("Opening amount cannot be negative.");
        var shift = new LocalCashShift(shiftId, binding.TenantId, binding.StoreId, binding.TerminalId, openedByUserId, DateTimeOffset.UtcNow, openingAmountCents, "open");
        await repository.OpenLocalCashShiftAsync(shift).ConfigureAwait(false);
        Console.WriteLine($"Local cash shift opened. shiftId={shift.Id}; openingAmountCents={shift.OpeningAmountCents}; status={shift.Status}");
        return 0;
    }

    case "cash-in":
    case "cash-out":
    {
        var binding = await repository.GetTerminalBindingAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before local cash movements.");
        var shiftIdText = GetOption(args, "--shift-id", string.Empty);
        var shift = string.IsNullOrWhiteSpace(shiftIdText)
            ? await repository.GetOpenLocalCashShiftAsync().ConfigureAwait(false)
            : await repository.GetLocalCashShiftAsync(Guid.Parse(shiftIdText)).ConfigureAwait(false);
        if (shift is null || shift.Status != "open") throw new InvalidOperationException("An open local cash shift is required for cash movements.");
        var amountCents = int.Parse(GetOption(args, "--amount-cents"));
        if (amountCents <= 0) throw new InvalidOperationException("Cash movement amount must be greater than zero.");
        var movementType = command == "cash-in" ? "cash_in" : "cash_out";
        var note = GetOption(args, "--note", command);
        await repository.AddLocalCashMovementAsync(new LocalCashMovement(Guid.NewGuid(), shift.Id, binding.TenantId, binding.StoreId, binding.TerminalId, movementType, amountCents, DateTimeOffset.UtcNow, "manual", null, note)).ConfigureAwait(false);
        Console.WriteLine($"Local cash movement recorded. shiftId={shift.Id}; movementType={movementType}; amountCents={amountCents}");
        return 0;
    }

    case "cash-status":
    {
        var shiftIdText = GetOption(args, "--shift-id", string.Empty);
        LocalCashShift? shift = string.IsNullOrWhiteSpace(shiftIdText)
            ? await repository.GetOpenLocalCashShiftAsync().ConfigureAwait(false)
            : await repository.GetLocalCashShiftAsync(Guid.Parse(shiftIdText)).ConfigureAwait(false);
        if (shift is null)
        {
            Console.WriteLine("No local cash shift found.");
            return 2;
        }

        var summary = await repository.GetLocalCashShiftSummaryAsync(shift.Id).ConfigureAwait(false);
        Console.WriteLine($"Local cash shift summary. shiftId={summary.ShiftId}; status={summary.Status}; openingAmountCents={summary.OpeningAmountCents}; cashSalesCents={summary.CashSalesCents}; cashInCents={summary.CashInCents}; cashOutCents={summary.CashOutCents}; expectedCashCents={summary.ExpectedCashCents}; countedCashCents={summary.CountedCashCents}; differenceCents={summary.DifferenceCents}; paymentCount={summary.PaymentCount}; movementCount={summary.MovementCount}");
        return 0;
    }

    case "close-local-shift":
    {
        var shiftIdText = GetOption(args, "--shift-id", string.Empty);
        var shift = string.IsNullOrWhiteSpace(shiftIdText)
            ? await repository.GetOpenLocalCashShiftAsync().ConfigureAwait(false)
            : await repository.GetLocalCashShiftAsync(Guid.Parse(shiftIdText)).ConfigureAwait(false);
        if (shift is null || shift.Status != "open") throw new InvalidOperationException("An open local cash shift is required before closing.");
        var closedByUserId = Guid.Parse(GetOption(args, "--closed-by-user-id"));
        var countedCashCents = int.Parse(GetOption(args, "--counted-cash-cents"));
        await repository.CloseLocalCashShiftAsync(shift.Id, closedByUserId, countedCashCents, DateTimeOffset.UtcNow).ConfigureAwait(false);
        var summary = await repository.GetLocalCashShiftSummaryAsync(shift.Id).ConfigureAwait(false);
        Console.WriteLine($"Local cash shift closed. shiftId={summary.ShiftId}; status={summary.Status}; expectedCashCents={summary.ExpectedCashCents}; countedCashCents={summary.CountedCashCents}; differenceCents={summary.DifferenceCents}; cashSalesCents={summary.CashSalesCents}");
        return 0;
    }

    case "sale-offline-from-cache-cash":
    {
        var binding = await repository.GetTerminalBindingAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before creating offline cash sales.");
        var shift = await repository.GetOpenLocalCashShiftAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Open local cash shift is required before creating offline cash sales.");
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
        var totalCents = quantity * product.PriceCents;
        var tenderedCents = int.Parse(GetOption(args, "--tendered-cents", totalCents.ToString()));
        var changeCents = LocalCashCalculator.CalculateChangeCents(totalCents, tenderedCents);
        var sale = new OfflineSaleDraft(
            localSaleId,
            binding.TenantId,
            binding.StoreId,
            binding.TerminalId,
            DateTimeOffset.UtcNow,
            new[] { new OfflineSaleLineDraft(product.ProductId, product.VariantId, product.Sku, product.Name, quantity, product.PriceCents) },
            new[] { new OfflineSalePaymentDraft("cash", totalCents, localPaymentId, $"tendered={tenderedCents};change={changeCents};localCashShiftId={shift.Id}") },
            product.Currency,
            cashierUserId);

        var saleService = new OfflineSaleService(repository, new SystemClock());
        var outboxEvent = await saleService.CreateOfflineSaleAsync(sale).ConfigureAwait(false);
        await repository.RecordLocalCashSaleAsync(shift.Id, sale, tenderedCents, changeCents).ConfigureAwait(false);
        Console.WriteLine($"Offline cash sale queued from cache. sku={product.Sku}; name={product.Name}; localSaleId={sale.LocalSaleId}; outboxEventId={outboxEvent.Id}; localCashShiftId={shift.Id}; totalCents={sale.TotalCents}; tenderedCents={tenderedCents}; changeCents={changeCents}; unitPriceCents={product.PriceCents}");
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


    case "sync-pull":
    {
        var binding = await repository.GetTerminalBindingAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before remote sync pull.");
        var baseUrl = GetOption(args, "--base-url");
        var limit = int.Parse(GetOption(args, "--limit", "100"));
        var accessToken = GetOption(args, "--terminal-access-token", binding.TerminalToken);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var remoteClient = new HttpRemoteSyncClient(httpClient, baseUrl);
        var service = new LocalSyncPullService(repository, remoteClient, new SystemClock());
        var result = await service.PullAndApplyAsync(limit, accessToken).ConfigureAwait(false);
        Console.WriteLine($"Remote sync pull applied. previousCursor={result.PreviousCursor}; nextCursor={result.NextCursor}; received={result.ReceivedChangeCount}; applied={result.AppliedChangeCount}; skippedDuplicates={result.SkippedDuplicateCount}; hasMore={result.HasMore}");
        return 0;
    }

    case "pull-status":
    {
        LocalSyncPullState state = await repository.GetSyncPullStateAsync().ConfigureAwait(false);
        int applied = await repository.CountAppliedSyncChangesAsync().ConfigureAwait(false);
        Console.WriteLine($"Local pull state. cursor={state.Cursor}; lastPulledAt={state.LastPulledAtUtc:O}; lastChangeCount={state.LastChangeCount}; appliedChanges={applied}; totalApplied={state.TotalAppliedChangeCount}");
        return 0;
    }

    case "save-remote-sale":
    {
        var jsonFile = GetOption(args, "--json-file");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(jsonFile).ConfigureAwait(false));
        JsonElement root = document.RootElement;
        Guid remoteSaleId = ReadGuid(root, "id");
        Guid? localSaleId = ReadNullableGuid(root, "localSaleId");
        var sale = new LocalRemoteSaleReadModel(
            remoteSaleId,
            localSaleId,
            ReadGuid(root, "tenantId"),
            ReadGuid(root, "storeId"),
            ReadGuid(root, "terminalId"),
            ReadString(root, "status", "completed"),
            ReadInt(root, "totalCents"),
            ReadNullableDateTimeOffset(root, "occurredAt"),
            root.GetRawText(),
            DateTimeOffset.UtcNow);
        await repository.SaveRemoteSaleReadModelAsync(sale).ConfigureAwait(false);
        Console.WriteLine($"Remote sale read model saved locally. remoteSaleId={sale.RemoteSaleId}; localSaleId={sale.LocalSaleId}; totalCents={sale.TotalCents}; status={sale.Status}");
        return 0;
    }

    case "save-remote-receipt":
    {
        var jsonFile = GetOption(args, "--json-file");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(jsonFile).ConfigureAwait(false));
        JsonElement root = document.RootElement;
        var receipt = new LocalRemoteReceiptReadModel(
            ReadGuid(root, "id"),
            ReadGuid(root, "saleId"),
            ReadString(root, "receiptNumber", string.Empty),
            ReadNullableString(root, "publicToken"),
            root.GetRawText(),
            DateTimeOffset.UtcNow);
        await repository.SaveRemoteReceiptReadModelAsync(receipt).ConfigureAwait(false);
        Console.WriteLine($"Remote receipt read model saved locally. receiptId={receipt.ReceiptId}; saleId={receipt.SaleId}; receiptNumber={receipt.ReceiptNumber}");
        return 0;
    }

    case "readmodel-status":
    {
        int sales = await repository.CountRemoteSalesAsync().ConfigureAwait(false);
        int receipts = await repository.CountRemoteReceiptsAsync().ConfigureAwait(false);
        int changes = await repository.CountAppliedSyncChangesAsync().ConfigureAwait(false);
        var localSaleIdText = GetOption(args, "--local-sale-id", string.Empty);
        if (!string.IsNullOrWhiteSpace(localSaleIdText))
        {
            var sale = await repository.GetRemoteSaleByLocalSaleIdAsync(Guid.Parse(localSaleIdText)).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"No local remote-sale read model exists for localSaleId={localSaleIdText}.");
            var receipt = await repository.GetRemoteReceiptBySaleIdAsync(sale.RemoteSaleId).ConfigureAwait(false);
            Console.WriteLine($"Local read models. remoteSales={sales}; remoteReceipts={receipts}; appliedChanges={changes}; localSaleId={sale.LocalSaleId}; remoteSaleId={sale.RemoteSaleId}; saleTotalCents={sale.TotalCents}; saleStatus={sale.Status}; receiptNumber={receipt?.ReceiptNumber}");
            return 0;
        }

        Console.WriteLine($"Local read models. remoteSales={sales}; remoteReceipts={receipts}; appliedChanges={changes}");
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


    case "inventory-reconcile":
    {
        var localSaleId = Guid.Parse(GetOption(args, "--local-sale-id"));
        var expectedRemoteMovementCount = int.Parse(GetOption(args, "--remote-movement-count"));
        var movements = await repository.GetInventoryMovementsByLocalSaleIdAsync(localSaleId).ConfigureAwait(false);
        if (movements.Count == 0)
        {
            throw new InvalidOperationException($"No local inventory movements found for localSaleId={localSaleId}.");
        }
        if (movements.Count != expectedRemoteMovementCount)
        {
            throw new InvalidOperationException($"Inventory movement count mismatch for localSaleId={localSaleId}. local={movements.Count}; remote={expectedRemoteMovementCount}.");
        }

        var localTotal = movements.Sum(x => x.QuantityDelta);
        Console.WriteLine($"Inventory reconciliation matched. localSaleId={localSaleId}; movementCount={movements.Count}; localTotalQuantityDelta={localTotal.ToString(System.Globalization.CultureInfo.InvariantCulture)}; remoteMovementCount={expectedRemoteMovementCount}");
        return 0;
    }


    case "queue-receipt-print":
    {
        var saleId = Guid.Parse(GetOption(args, "--sale-id"));
        var receiptId = Guid.Parse(GetOption(args, "--receipt-id"));
        var receiptNumber = GetOption(args, "--receipt-number");
        var content = GetOption(args, "--content", $"SolidPOS receipt {receiptNumber}");
        var service = new HardwareRuntimeService(repository, new FakeReceiptPrinter(), new FakeCashDrawerDevice(), new FakeBarcodeScannerDevice(), new FakePaymentTerminalDevice(), new SystemClock());
        var job = await service.QueueReceiptAsync(saleId, receiptId, receiptNumber, content, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Receipt print job queued. jobId={job.Id}; saleId={job.SaleId}; receiptId={job.ReceiptId}; receiptNumber={job.ReceiptNumber}; status={job.Status}");
        return 0;
    }

    case "process-print-jobs":
    {
        var service = new HardwareRuntimeService(repository, new FakeReceiptPrinter(), new FakeCashDrawerDevice(), new FakeBarcodeScannerDevice(), new FakePaymentTerminalDevice(), new SystemClock());
        var processed = await service.ProcessNextReceiptPrintJobAsync(CancellationToken.None).ConfigureAwait(false);
        if (processed is null)
        {
            Console.WriteLine("No pending receipt print jobs.");
            return 0;
        }

        Console.WriteLine($"Receipt print job processed. jobId={processed.Id}; receiptNumber={processed.ReceiptNumber}; status={processed.Status}");
        return 0;
    }

    case "open-cash-drawer-hardware":
    {
        var reason = GetOption(args, "--reason", "manual_validation");
        var service = new HardwareRuntimeService(repository, new FakeReceiptPrinter(), new FakeCashDrawerDevice(), new FakeBarcodeScannerDevice(), new FakePaymentTerminalDevice(), new SystemClock());
        var result = await service.OpenCashDrawerAsync(reason, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Cash drawer command executed. accepted={result.Accepted}; commandId={result.CommandId}; message={result.Message}");
        return 0;
    }

    case "scan-barcode":
    {
        var barcode = GetOption(args, "--barcode", "QSR-AMERICANO");
        var service = new HardwareRuntimeService(repository, new FakeReceiptPrinter(), new FakeCashDrawerDevice(), new FakeBarcodeScannerDevice(barcode), new FakePaymentTerminalDevice(), new SystemClock());
        var result = await service.ScanBarcodeAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Barcode scanned. barcode={result.Barcode}; scannedAt={result.ScannedAtUtc:O}");
        return 0;
    }

    case "authorize-payment-terminal":
    {
        var amountCents = int.Parse(GetOption(args, "--amount-cents"));
        var currency = GetOption(args, "--currency", "MXN");
        var service = new HardwareRuntimeService(repository, new FakeReceiptPrinter(), new FakeCashDrawerDevice(), new FakeBarcodeScannerDevice(), new FakePaymentTerminalDevice(), new SystemClock());
        var result = await service.AuthorizePaymentAsync(amountCents, currency, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Payment terminal authorization completed. approved={result.Approved}; authorizationCode={result.AuthorizationCode}; amountCents={result.AmountCents}; message={result.Message}");
        return 0;
    }

    case "hardware-status":
    {
        var summary = await repository.GetHardwareSummaryAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Local hardware status. pendingPrintJobs={summary.PendingPrintJobs}; printedPrintJobs={summary.PrintedPrintJobs}; failedPrintJobs={summary.FailedPrintJobs}; hardwareEvents={summary.HardwareEventCount}; latestDevice={summary.LatestEvent?.DeviceType}; latestEvent={summary.LatestEvent?.EventType}");
        return 0;
    }

    default:
        throw new InvalidOperationException($"Unknown command: {command}");
}
