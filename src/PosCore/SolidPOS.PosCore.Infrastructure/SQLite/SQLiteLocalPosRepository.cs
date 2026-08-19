using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Infrastructure.SQLite;

public sealed class SQLiteLocalPosRepository : ILocalPosRepository
{
    private readonly SQLiteLocalDatabase _database;

    public SQLiteLocalPosRepository(SQLiteLocalDatabase database)
    {
        _database = database;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        Execute(connection, """
CREATE TABLE IF NOT EXISTS terminal_binding (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  terminal_fingerprint TEXT NOT NULL,
  terminal_token TEXT NOT NULL,
  bound_at_utc TEXT NOT NULL,
  schema_version INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS offline_sales (
  local_sale_id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  occurred_at_utc TEXT NOT NULL,
  currency TEXT NOT NULL,
  subtotal_cents INTEGER NOT NULL,
  discount_cents INTEGER NOT NULL,
  total_cents INTEGER NOT NULL,
  paid_cents INTEGER NOT NULL,
  status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_outbox_events (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  event_type TEXT NOT NULL,
  schema_version INTEGER NOT NULL,
  sequence_number INTEGER NOT NULL,
  payload_json TEXT NOT NULL,
  status INTEGER NOT NULL,
  created_at_utc TEXT NOT NULL,
  synced_at_utc TEXT NULL,
  last_error TEXT NULL,
  attempts INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS local_sync_acknowledgements (
  id TEXT PRIMARY KEY,
  batch_id TEXT NOT NULL,
  outbox_event_id TEXT NOT NULL,
  remote_status TEXT NOT NULL,
  remote_response_json TEXT NOT NULL,
  acknowledged_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_catalog_products (
  product_id TEXT NOT NULL,
  variant_id TEXT NULL,
  sku TEXT NOT NULL,
  name TEXT NOT NULL,
  price_cents INTEGER NOT NULL,
  currency TEXT NOT NULL,
  status TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL,
  synced_at_utc TEXT NOT NULL,
  PRIMARY KEY(product_id, variant_id)
);
CREATE TABLE IF NOT EXISTS local_catalog_sync_state (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  product_count INTEGER NOT NULL,
  synced_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_inventory_recipes (
  recipe_id TEXT PRIMARY KEY,
  output_product_id TEXT NOT NULL,
  output_variant_id TEXT NULL,
  yield_quantity TEXT NOT NULL,
  yield_unit_id TEXT NOT NULL,
  waste_percent TEXT NOT NULL,
  status TEXT NOT NULL,
  synced_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_inventory_recipe_items (
  recipe_item_id TEXT PRIMARY KEY,
  recipe_id TEXT NOT NULL,
  ingredient_product_id TEXT NOT NULL,
  ingredient_variant_id TEXT NULL,
  quantity TEXT NOT NULL,
  unit_id TEXT NOT NULL,
  optional INTEGER NOT NULL,
  synced_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_inventory_cache_sync_state (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  recipe_count INTEGER NOT NULL,
  recipe_item_count INTEGER NOT NULL,
  synced_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_inventory_movements (
  id TEXT PRIMARY KEY,
  local_sale_id TEXT NOT NULL,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  product_id TEXT NOT NULL,
  variant_id TEXT NULL,
  movement_type TEXT NOT NULL,
  quantity_delta TEXT NOT NULL,
  unit_id TEXT NOT NULL,
  occurred_at_utc TEXT NOT NULL,
  source TEXT NOT NULL,
  created_at_utc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_local_catalog_products_sku ON local_catalog_products(sku);
CREATE INDEX IF NOT EXISTS idx_local_inventory_recipes_output ON local_inventory_recipes(output_product_id, output_variant_id, status);
CREATE INDEX IF NOT EXISTS idx_local_inventory_recipe_items_recipe ON local_inventory_recipe_items(recipe_id);
CREATE INDEX IF NOT EXISTS idx_local_inventory_movements_sale ON local_inventory_movements(local_sale_id);
CREATE INDEX IF NOT EXISTS idx_local_outbox_pending ON local_outbox_events(status, sequence_number);
CREATE INDEX IF NOT EXISTS idx_local_sync_ack_event ON local_sync_acknowledgements(outbox_event_id, acknowledged_at_utc);
""");
        return Task.CompletedTask;
    }

    public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO terminal_binding (id, tenant_id, store_id, terminal_id, terminal_fingerprint, terminal_token, bound_at_utc, schema_version)
VALUES (1, $tenantId, $storeId, $terminalId, $fingerprint, $token, $boundAtUtc, $schemaVersion)
ON CONFLICT(id) DO UPDATE SET
  tenant_id = excluded.tenant_id,
  store_id = excluded.store_id,
  terminal_id = excluded.terminal_id,
  terminal_fingerprint = excluded.terminal_fingerprint,
  terminal_token = excluded.terminal_token,
  bound_at_utc = excluded.bound_at_utc,
  schema_version = excluded.schema_version;
""";
        command.Parameters.AddWithValue("$tenantId", binding.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", binding.StoreId.ToString());
        command.Parameters.AddWithValue("$terminalId", binding.TerminalId.ToString());
        command.Parameters.AddWithValue("$fingerprint", binding.TerminalFingerprint);
        command.Parameters.AddWithValue("$token", binding.TerminalToken);
        command.Parameters.AddWithValue("$boundAtUtc", binding.BoundAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$schemaVersion", binding.SchemaVersion);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tenant_id, store_id, terminal_id, terminal_fingerprint, terminal_token, bound_at_utc, schema_version FROM terminal_binding WHERE id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return Task.FromResult<TerminalBinding?>(null);

        var binding = new TerminalBinding(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            reader.GetInt32(6));
        return Task.FromResult<TerminalBinding?>(binding);
    }

    public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var saleCommand = connection.CreateCommand())
        {
            saleCommand.Transaction = transaction;
            saleCommand.CommandText = """
INSERT INTO offline_sales (local_sale_id, tenant_id, store_id, terminal_id, occurred_at_utc, currency, subtotal_cents, discount_cents, total_cents, paid_cents, status)
VALUES ($saleId, $tenantId, $storeId, $terminalId, $occurredAtUtc, $currency, $subtotalCents, $discountCents, $totalCents, $paidCents, 'pending_sync');
""";
            saleCommand.Parameters.AddWithValue("$saleId", sale.LocalSaleId.ToString());
            saleCommand.Parameters.AddWithValue("$tenantId", sale.TenantId.ToString());
            saleCommand.Parameters.AddWithValue("$storeId", sale.StoreId.ToString());
            saleCommand.Parameters.AddWithValue("$terminalId", sale.TerminalId.ToString());
            saleCommand.Parameters.AddWithValue("$occurredAtUtc", sale.OccurredAtUtc.ToString("O"));
            saleCommand.Parameters.AddWithValue("$currency", sale.Currency);
            saleCommand.Parameters.AddWithValue("$subtotalCents", sale.SubtotalCents);
            saleCommand.Parameters.AddWithValue("$discountCents", sale.DiscountCents);
            saleCommand.Parameters.AddWithValue("$totalCents", sale.TotalCents);
            saleCommand.Parameters.AddWithValue("$paidCents", sale.PaidCents);
            saleCommand.ExecuteNonQuery();
        }

        InsertOutboxEvent(connection, transaction, outboxEvent);
        transaction.Commit();
        return Task.CompletedTask;
    }


    public Task SaveOfflineSaleWithInventoryAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, IReadOnlyCollection<LocalInventoryMovement> movements, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var saleCommand = connection.CreateCommand())
        {
            saleCommand.Transaction = transaction;
            saleCommand.CommandText = """
INSERT INTO offline_sales (local_sale_id, tenant_id, store_id, terminal_id, occurred_at_utc, currency, subtotal_cents, discount_cents, total_cents, paid_cents, status)
VALUES ($saleId, $tenantId, $storeId, $terminalId, $occurredAtUtc, $currency, $subtotalCents, $discountCents, $totalCents, $paidCents, 'pending_sync');
""";
            saleCommand.Parameters.AddWithValue("$saleId", sale.LocalSaleId.ToString());
            saleCommand.Parameters.AddWithValue("$tenantId", sale.TenantId.ToString());
            saleCommand.Parameters.AddWithValue("$storeId", sale.StoreId.ToString());
            saleCommand.Parameters.AddWithValue("$terminalId", sale.TerminalId.ToString());
            saleCommand.Parameters.AddWithValue("$occurredAtUtc", sale.OccurredAtUtc.ToString("O"));
            saleCommand.Parameters.AddWithValue("$currency", sale.Currency);
            saleCommand.Parameters.AddWithValue("$subtotalCents", sale.SubtotalCents);
            saleCommand.Parameters.AddWithValue("$discountCents", sale.DiscountCents);
            saleCommand.Parameters.AddWithValue("$totalCents", sale.TotalCents);
            saleCommand.Parameters.AddWithValue("$paidCents", sale.PaidCents);
            saleCommand.ExecuteNonQuery();
        }

        foreach (LocalInventoryMovement movement in movements)
        {
            InsertInventoryMovement(connection, transaction, movement);
        }

        InsertOutboxEvent(connection, transaction, outboxEvent);
        transaction.Commit();
        return Task.CompletedTask;
    }


    public Task SaveCatalogProductsAsync(IReadOnlyCollection<LocalCatalogProduct> products, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM local_catalog_products;";
            delete.ExecuteNonQuery();
        }

        foreach (var product in products)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO local_catalog_products (product_id, variant_id, sku, name, price_cents, currency, status, updated_at_utc, synced_at_utc)
VALUES ($productId, $variantId, $sku, $name, $priceCents, $currency, $status, $updatedAtUtc, $syncedAtUtc);
""";
            command.Parameters.AddWithValue("$productId", product.ProductId.ToString());
            command.Parameters.AddWithValue("$variantId", product.VariantId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$sku", product.Sku);
            command.Parameters.AddWithValue("$name", product.Name);
            command.Parameters.AddWithValue("$priceCents", product.PriceCents);
            command.Parameters.AddWithValue("$currency", product.Currency);
            command.Parameters.AddWithValue("$status", product.Status);
            command.Parameters.AddWithValue("$updatedAtUtc", product.UpdatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$syncedAtUtc", product.SyncedAtUtc.ToString("O"));
            command.ExecuteNonQuery();
        }

        using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = """
INSERT INTO local_catalog_sync_state (id, product_count, synced_at_utc)
VALUES (1, $productCount, $syncedAtUtc)
ON CONFLICT(id) DO UPDATE SET
  product_count = excluded.product_count,
  synced_at_utc = excluded.synced_at_utc;
""";
            state.Parameters.AddWithValue("$productCount", products.Count);
            state.Parameters.AddWithValue("$syncedAtUtc", syncedAtUtc.ToString("O"));
            state.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<LocalCatalogProduct?> GetCatalogProductBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT product_id, variant_id, sku, name, price_cents, currency, status, updated_at_utc, synced_at_utc
FROM local_catalog_products
WHERE sku = $sku
LIMIT 1;
""";
        command.Parameters.AddWithValue("$sku", sku);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return Task.FromResult<LocalCatalogProduct?>(null);
        var product = new LocalCatalogProduct(
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            DateTimeOffset.Parse(reader.GetString(7)),
            DateTimeOffset.Parse(reader.GetString(8)));
        return Task.FromResult<LocalCatalogProduct?>(product);
    }

    public Task<int> CountCatalogProductsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM local_catalog_products;";
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }


    public Task SaveInventoryRecipeCacheAsync(IReadOnlyCollection<LocalInventoryRecipe> recipes, IReadOnlyCollection<LocalInventoryRecipeItem> recipeItems, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteItems = connection.CreateCommand())
        {
            deleteItems.Transaction = transaction;
            deleteItems.CommandText = "DELETE FROM local_inventory_recipe_items;";
            deleteItems.ExecuteNonQuery();
        }
        using (var deleteRecipes = connection.CreateCommand())
        {
            deleteRecipes.Transaction = transaction;
            deleteRecipes.CommandText = "DELETE FROM local_inventory_recipes;";
            deleteRecipes.ExecuteNonQuery();
        }

        foreach (LocalInventoryRecipe recipe in recipes)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO local_inventory_recipes (recipe_id, output_product_id, output_variant_id, yield_quantity, yield_unit_id, waste_percent, status, synced_at_utc)
VALUES ($recipeId, $outputProductId, $outputVariantId, $yieldQuantity, $yieldUnitId, $wastePercent, $status, $syncedAtUtc);
""";
            command.Parameters.AddWithValue("$recipeId", recipe.RecipeId.ToString());
            command.Parameters.AddWithValue("$outputProductId", recipe.OutputProductId.ToString());
            command.Parameters.AddWithValue("$outputVariantId", recipe.OutputVariantId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$yieldQuantity", recipe.YieldQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$yieldUnitId", recipe.YieldUnitId.ToString());
            command.Parameters.AddWithValue("$wastePercent", recipe.WastePercent.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$status", recipe.Status);
            command.Parameters.AddWithValue("$syncedAtUtc", recipe.SyncedAtUtc.ToString("O"));
            command.ExecuteNonQuery();
        }

        foreach (LocalInventoryRecipeItem item in recipeItems)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO local_inventory_recipe_items (recipe_item_id, recipe_id, ingredient_product_id, ingredient_variant_id, quantity, unit_id, optional, synced_at_utc)
VALUES ($recipeItemId, $recipeId, $ingredientProductId, $ingredientVariantId, $quantity, $unitId, $optional, $syncedAtUtc);
""";
            command.Parameters.AddWithValue("$recipeItemId", item.RecipeItemId.ToString());
            command.Parameters.AddWithValue("$recipeId", item.RecipeId.ToString());
            command.Parameters.AddWithValue("$ingredientProductId", item.IngredientProductId.ToString());
            command.Parameters.AddWithValue("$ingredientVariantId", item.IngredientVariantId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$quantity", item.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$unitId", item.UnitId.ToString());
            command.Parameters.AddWithValue("$optional", item.Optional ? 1 : 0);
            command.Parameters.AddWithValue("$syncedAtUtc", item.SyncedAtUtc.ToString("O"));
            command.ExecuteNonQuery();
        }

        using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = """
INSERT INTO local_inventory_cache_sync_state (id, recipe_count, recipe_item_count, synced_at_utc)
VALUES (1, $recipeCount, $recipeItemCount, $syncedAtUtc)
ON CONFLICT(id) DO UPDATE SET
  recipe_count = excluded.recipe_count,
  recipe_item_count = excluded.recipe_item_count,
  synced_at_utc = excluded.synced_at_utc;
""";
            state.Parameters.AddWithValue("$recipeCount", recipes.Count);
            state.Parameters.AddWithValue("$recipeItemCount", recipeItems.Count);
            state.Parameters.AddWithValue("$syncedAtUtc", syncedAtUtc.ToString("O"));
            state.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<LocalInventoryRecipe?> GetRecipeForOutputAsync(Guid productId, Guid? variantId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT recipe_id, output_product_id, output_variant_id, yield_quantity, yield_unit_id, waste_percent, status, synced_at_utc
FROM local_inventory_recipes
WHERE output_product_id = $productId
  AND ((output_variant_id IS NULL AND $variantId IS NULL) OR output_variant_id = $variantId)
  AND status = 'active'
LIMIT 1;
""";
        command.Parameters.AddWithValue("$productId", productId.ToString());
        command.Parameters.AddWithValue("$variantId", variantId?.ToString() ?? (object)DBNull.Value);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return Task.FromResult<LocalInventoryRecipe?>(null);
        return Task.FromResult<LocalInventoryRecipe?>(new LocalInventoryRecipe(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
            decimal.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture),
            Guid.Parse(reader.GetString(4)),
            decimal.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
            reader.GetString(6),
            DateTimeOffset.Parse(reader.GetString(7))));
    }

    public Task<IReadOnlyList<LocalInventoryRecipeItem>> GetRecipeItemsAsync(Guid recipeId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT recipe_item_id, recipe_id, ingredient_product_id, ingredient_variant_id, quantity, unit_id, optional, synced_at_utc
FROM local_inventory_recipe_items
WHERE recipe_id = $recipeId
ORDER BY recipe_item_id;
""";
        command.Parameters.AddWithValue("$recipeId", recipeId.ToString());
        using var reader = command.ExecuteReader();
        var items = new List<LocalInventoryRecipeItem>();
        while (reader.Read())
        {
            items.Add(new LocalInventoryRecipeItem(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                decimal.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture),
                Guid.Parse(reader.GetString(5)),
                reader.GetInt32(6) == 1,
                DateTimeOffset.Parse(reader.GetString(7))));
        }

        return Task.FromResult<IReadOnlyList<LocalInventoryRecipeItem>>(items);
    }

    public Task<IReadOnlyList<LocalInventoryMovement>> GetInventoryMovementsByLocalSaleIdAsync(Guid localSaleId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, local_sale_id, tenant_id, store_id, terminal_id, product_id, variant_id, movement_type, quantity_delta, unit_id, occurred_at_utc, source, created_at_utc
FROM local_inventory_movements
WHERE local_sale_id = $localSaleId
ORDER BY product_id, movement_type;
""";
        command.Parameters.AddWithValue("$localSaleId", localSaleId.ToString());
        using var reader = command.ExecuteReader();
        var movements = new List<LocalInventoryMovement>();
        while (reader.Read())
        {
            movements.Add(ReadInventoryMovement(reader));
        }

        return Task.FromResult<IReadOnlyList<LocalInventoryMovement>>(movements);
    }

    public Task<int> CountInventoryRecipesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM local_inventory_recipes;";
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task<int> CountInventoryRecipeItemsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM local_inventory_recipe_items;";
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        InsertOutboxEvent(connection, transaction, outboxEvent);
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, tenant_id, store_id, terminal_id, event_type, schema_version, sequence_number, payload_json, status, created_at_utc, synced_at_utc, last_error, attempts
FROM local_outbox_events
WHERE status = $status
ORDER BY sequence_number
LIMIT $limit;
""";
        command.Parameters.AddWithValue("$status", (int)LocalOutboxStatus.Pending);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var events = new List<LocalOutboxEvent>();
        while (reader.Read())
        {
            events.Add(ReadOutboxEvent(reader));
        }

        return Task.FromResult<IReadOnlyList<LocalOutboxEvent>>(events);
    }

    public Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, tenant_id, store_id, terminal_id, event_type, schema_version, sequence_number, payload_json, status, created_at_utc, synced_at_utc, last_error, attempts
FROM local_outbox_events
WHERE status = $status
ORDER BY sequence_number DESC
LIMIT 1;
""";
        command.Parameters.AddWithValue("$status", (int)status);
        using var reader = command.ExecuteReader();
        return Task.FromResult(reader.Read() ? ReadOutboxEvent(reader) : null);
    }

    public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        foreach (var eventId in eventIds)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE local_outbox_events SET status = $status, synced_at_utc = $syncedAtUtc WHERE id = $id;";
            command.Parameters.AddWithValue("$status", (int)LocalOutboxStatus.Synced);
            command.Parameters.AddWithValue("$syncedAtUtc", syncedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$id", eventId.ToString());
            command.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE local_outbox_events SET status = $status, last_error = $error, attempts = attempts + 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$status", (int)LocalOutboxStatus.Failed);
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$id", eventId.ToString());
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE local_outbox_events
SET status = $pendingStatus, synced_at_utc = NULL, last_error = $reason
WHERE id = $id;
""";
        command.Parameters.AddWithValue("$pendingStatus", (int)LocalOutboxStatus.Pending);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$id", eventId.ToString());
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE local_outbox_events
SET status = $pendingStatus, last_error = $reason
WHERE status = $failedStatus AND attempts < $maxAttempts;
SELECT changes();
""";
        command.Parameters.AddWithValue("$pendingStatus", (int)LocalOutboxStatus.Pending);
        command.Parameters.AddWithValue("$failedStatus", (int)LocalOutboxStatus.Failed);
        command.Parameters.AddWithValue("$maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("$reason", reason);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var acknowledgement in acknowledgements)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO local_sync_acknowledgements (id, batch_id, outbox_event_id, remote_status, remote_response_json, acknowledged_at_utc)
VALUES ($id, $batchId, $outboxEventId, $remoteStatus, $remoteResponseJson, $acknowledgedAtUtc);";
            command.Parameters.AddWithValue("$id", acknowledgement.Id.ToString());
            command.Parameters.AddWithValue("$batchId", acknowledgement.BatchId.ToString());
            command.Parameters.AddWithValue("$outboxEventId", acknowledgement.OutboxEventId.ToString());
            command.Parameters.AddWithValue("$remoteStatus", acknowledgement.RemoteStatus);
            command.Parameters.AddWithValue("$remoteResponseJson", acknowledgement.RemoteResponseJson);
            command.Parameters.AddWithValue("$acknowledgedAtUtc", acknowledgement.AcknowledgedAtUtc.ToString("O"));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM local_outbox_events WHERE status = $status;";
        command.Parameters.AddWithValue("$status", (int)status);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }


    private static void InsertInventoryMovement(SqliteConnection connection, SqliteTransaction transaction, LocalInventoryMovement movement)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO local_inventory_movements (id, local_sale_id, tenant_id, store_id, terminal_id, product_id, variant_id, movement_type, quantity_delta, unit_id, occurred_at_utc, source, created_at_utc)
VALUES ($id, $localSaleId, $tenantId, $storeId, $terminalId, $productId, $variantId, $movementType, $quantityDelta, $unitId, $occurredAtUtc, $source, $createdAtUtc);
""";
        command.Parameters.AddWithValue("$id", movement.Id.ToString());
        command.Parameters.AddWithValue("$localSaleId", movement.LocalSaleId.ToString());
        command.Parameters.AddWithValue("$tenantId", movement.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", movement.StoreId.ToString());
        command.Parameters.AddWithValue("$terminalId", movement.TerminalId.ToString());
        command.Parameters.AddWithValue("$productId", movement.ProductId.ToString());
        command.Parameters.AddWithValue("$variantId", movement.VariantId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$movementType", movement.MovementType);
        command.Parameters.AddWithValue("$quantityDelta", movement.QuantityDelta.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$unitId", movement.UnitId.ToString());
        command.Parameters.AddWithValue("$occurredAtUtc", movement.OccurredAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$source", movement.Source);
        command.Parameters.AddWithValue("$createdAtUtc", movement.CreatedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertOutboxEvent(SqliteConnection connection, SqliteTransaction transaction, LocalOutboxEvent outboxEvent)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO local_outbox_events (id, tenant_id, store_id, terminal_id, event_type, schema_version, sequence_number, payload_json, status, created_at_utc, synced_at_utc, last_error, attempts)
VALUES ($id, $tenantId, $storeId, $terminalId, $eventType, $schemaVersion, $sequenceNumber, $payloadJson, $status, $createdAtUtc, $syncedAtUtc, $lastError, $attempts);
""";
        command.Parameters.AddWithValue("$id", outboxEvent.Id.ToString());
        command.Parameters.AddWithValue("$tenantId", outboxEvent.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", outboxEvent.StoreId.ToString());
        command.Parameters.AddWithValue("$terminalId", outboxEvent.TerminalId.ToString());
        command.Parameters.AddWithValue("$eventType", outboxEvent.EventType);
        command.Parameters.AddWithValue("$schemaVersion", outboxEvent.SchemaVersion);
        command.Parameters.AddWithValue("$sequenceNumber", outboxEvent.SequenceNumber);
        command.Parameters.AddWithValue("$payloadJson", outboxEvent.PayloadJson);
        command.Parameters.AddWithValue("$status", (int)outboxEvent.Status);
        command.Parameters.AddWithValue("$createdAtUtc", outboxEvent.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$syncedAtUtc", outboxEvent.SyncedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastError", outboxEvent.LastError ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$attempts", outboxEvent.Attempts);
        command.ExecuteNonQuery();
    }


    private static LocalInventoryMovement ReadInventoryMovement(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Guid.Parse(reader.GetString(2)),
        Guid.Parse(reader.GetString(3)),
        Guid.Parse(reader.GetString(4)),
        Guid.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
        reader.GetString(7),
        decimal.Parse(reader.GetString(8), System.Globalization.CultureInfo.InvariantCulture),
        Guid.Parse(reader.GetString(9)),
        DateTimeOffset.Parse(reader.GetString(10)),
        reader.GetString(11),
        DateTimeOffset.Parse(reader.GetString(12)));

    private static LocalOutboxEvent ReadOutboxEvent(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Guid.Parse(reader.GetString(2)),
        Guid.Parse(reader.GetString(3)),
        reader.GetString(4),
        reader.GetInt32(5),
        reader.GetInt64(6),
        reader.GetString(7),
        (LocalOutboxStatus)reader.GetInt32(8),
        DateTimeOffset.Parse(reader.GetString(9)),
        reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.GetInt32(12));

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
