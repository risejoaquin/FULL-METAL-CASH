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
CREATE TABLE IF NOT EXISTS local_cash_shifts (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  opened_by_user_id TEXT NOT NULL,
  opened_at_utc TEXT NOT NULL,
  opening_amount_cents INTEGER NOT NULL,
  status TEXT NOT NULL,
  closed_by_user_id TEXT NULL,
  closed_at_utc TEXT NULL,
  counted_cash_cents INTEGER NULL,
  expected_cash_cents INTEGER NULL,
  difference_cents INTEGER NULL
);
CREATE TABLE IF NOT EXISTS local_cash_movements (
  id TEXT PRIMARY KEY,
  shift_id TEXT NOT NULL,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  movement_type TEXT NOT NULL,
  amount_cents INTEGER NOT NULL,
  occurred_at_utc TEXT NOT NULL,
  source_type TEXT NOT NULL,
  source_id TEXT NULL,
  note TEXT NULL
);
CREATE TABLE IF NOT EXISTS local_sale_payments (
  local_payment_id TEXT PRIMARY KEY,
  local_sale_id TEXT NOT NULL,
  shift_id TEXT NOT NULL,
  method_code TEXT NOT NULL,
  amount_cents INTEGER NOT NULL,
  tendered_cents INTEGER NOT NULL,
  change_cents INTEGER NOT NULL,
  created_at_utc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_local_catalog_products_sku ON local_catalog_products(sku);
CREATE INDEX IF NOT EXISTS idx_local_inventory_recipes_output ON local_inventory_recipes(output_product_id, output_variant_id, status);
CREATE INDEX IF NOT EXISTS idx_local_inventory_recipe_items_recipe ON local_inventory_recipe_items(recipe_id);
CREATE INDEX IF NOT EXISTS idx_local_inventory_movements_sale ON local_inventory_movements(local_sale_id);
CREATE INDEX IF NOT EXISTS idx_local_cash_shifts_open ON local_cash_shifts(status, opened_at_utc);
CREATE INDEX IF NOT EXISTS idx_local_cash_movements_shift ON local_cash_movements(shift_id, occurred_at_utc);
CREATE INDEX IF NOT EXISTS idx_local_sale_payments_shift ON local_sale_payments(shift_id);
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



    public Task OpenLocalCashShiftAsync(LocalCashShift shift, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM local_cash_shifts WHERE status = 'open';";
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            {
                throw new InvalidOperationException("A local cash shift is already open for this terminal.");
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO local_cash_shifts (id, tenant_id, store_id, terminal_id, opened_by_user_id, opened_at_utc, opening_amount_cents, status)
VALUES ($id, $tenantId, $storeId, $terminalId, $openedByUserId, $openedAtUtc, $openingAmountCents, $status);
""";
            command.Parameters.AddWithValue("$id", shift.Id.ToString());
            command.Parameters.AddWithValue("$tenantId", shift.TenantId.ToString());
            command.Parameters.AddWithValue("$storeId", shift.StoreId.ToString());
            command.Parameters.AddWithValue("$terminalId", shift.TerminalId.ToString());
            command.Parameters.AddWithValue("$openedByUserId", shift.OpenedByUserId.ToString());
            command.Parameters.AddWithValue("$openedAtUtc", shift.OpenedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$openingAmountCents", shift.OpeningAmountCents);
            command.Parameters.AddWithValue("$status", shift.Status);
            command.ExecuteNonQuery();
        }

        InsertCashMovement(connection, transaction, new LocalCashMovement(
            Guid.NewGuid(),
            shift.Id,
            shift.TenantId,
            shift.StoreId,
            shift.TerminalId,
            "opening",
            shift.OpeningAmountCents,
            shift.OpenedAtUtc,
            "local_cash_shift",
            shift.Id,
            "opening_amount"));

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<LocalCashShift?> GetOpenLocalCashShiftAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, tenant_id, store_id, terminal_id, opened_by_user_id, opened_at_utc, opening_amount_cents, status, closed_by_user_id, closed_at_utc, counted_cash_cents, expected_cash_cents, difference_cents
FROM local_cash_shifts
WHERE status = 'open'
ORDER BY opened_at_utc DESC
LIMIT 1;
""";
        using var reader = command.ExecuteReader();
        return Task.FromResult<LocalCashShift?>(reader.Read() ? ReadCashShift(reader) : null);
    }

    public Task<LocalCashShift?> GetLocalCashShiftAsync(Guid shiftId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, tenant_id, store_id, terminal_id, opened_by_user_id, opened_at_utc, opening_amount_cents, status, closed_by_user_id, closed_at_utc, counted_cash_cents, expected_cash_cents, difference_cents
FROM local_cash_shifts
WHERE id = $shiftId
LIMIT 1;
""";
        command.Parameters.AddWithValue("$shiftId", shiftId.ToString());
        using var reader = command.ExecuteReader();
        return Task.FromResult<LocalCashShift?>(reader.Read() ? ReadCashShift(reader) : null);
    }

    public Task AddLocalCashMovementAsync(LocalCashMovement movement, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        InsertCashMovement(connection, transaction, movement);
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task RecordLocalCashSaleAsync(Guid shiftId, OfflineSaleDraft sale, int tenderedCents, int changeCents, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var payment = sale.Payments.FirstOrDefault(payment => string.Equals(payment.MethodCode, "cash", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Offline sale does not contain a cash payment.");
        var localPaymentId = payment.LocalPaymentId ?? Guid.NewGuid();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO local_sale_payments (local_payment_id, local_sale_id, shift_id, method_code, amount_cents, tendered_cents, change_cents, created_at_utc)
VALUES ($localPaymentId, $localSaleId, $shiftId, $methodCode, $amountCents, $tenderedCents, $changeCents, $createdAtUtc);
""";
            command.Parameters.AddWithValue("$localPaymentId", localPaymentId.ToString());
            command.Parameters.AddWithValue("$localSaleId", sale.LocalSaleId.ToString());
            command.Parameters.AddWithValue("$shiftId", shiftId.ToString());
            command.Parameters.AddWithValue("$methodCode", payment.MethodCode);
            command.Parameters.AddWithValue("$amountCents", payment.AmountCents);
            command.Parameters.AddWithValue("$tenderedCents", tenderedCents);
            command.Parameters.AddWithValue("$changeCents", changeCents);
            command.Parameters.AddWithValue("$createdAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        InsertCashMovement(connection, transaction, new LocalCashMovement(
            Guid.NewGuid(),
            shiftId,
            sale.TenantId,
            sale.StoreId,
            sale.TerminalId,
            "sale_cash",
            sale.TotalCents,
            sale.OccurredAtUtc,
            "offline_sale",
            sale.LocalSaleId,
            $"tendered={tenderedCents};change={changeCents}"));

        transaction.Commit();
        return Task.CompletedTask;
    }

    public async Task CloseLocalCashShiftAsync(Guid shiftId, Guid closedByUserId, int countedCashCents, DateTimeOffset closedAtUtc, CancellationToken cancellationToken = default)
    {
        LocalCashShiftSummary summary = await GetLocalCashShiftSummaryAsync(shiftId, cancellationToken).ConfigureAwait(false);
        int differenceCents = countedCashCents - summary.ExpectedCashCents;
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE local_cash_shifts
SET status = 'closed',
    closed_by_user_id = $closedByUserId,
    closed_at_utc = $closedAtUtc,
    counted_cash_cents = $countedCashCents,
    expected_cash_cents = $expectedCashCents,
    difference_cents = $differenceCents
WHERE id = $shiftId AND status = 'open';
""";
        command.Parameters.AddWithValue("$shiftId", shiftId.ToString());
        command.Parameters.AddWithValue("$closedByUserId", closedByUserId.ToString());
        command.Parameters.AddWithValue("$closedAtUtc", closedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$countedCashCents", countedCashCents);
        command.Parameters.AddWithValue("$expectedCashCents", summary.ExpectedCashCents);
        command.Parameters.AddWithValue("$differenceCents", differenceCents);
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException($"Open local cash shift not found: {shiftId}.");
        }
    }

    public Task<LocalCashShiftSummary> GetLocalCashShiftSummaryAsync(Guid shiftId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        LocalCashShift shift;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
SELECT id, tenant_id, store_id, terminal_id, opened_by_user_id, opened_at_utc, opening_amount_cents, status, closed_by_user_id, closed_at_utc, counted_cash_cents, expected_cash_cents, difference_cents
FROM local_cash_shifts
WHERE id = $shiftId
LIMIT 1;
""";
            command.Parameters.AddWithValue("$shiftId", shiftId.ToString());
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException($"Local cash shift not found: {shiftId}.");
            shift = ReadCashShift(reader);
        }

        int cashSales = ScalarInt(connection, "SELECT COALESCE(SUM(amount_cents), 0) FROM local_cash_movements WHERE shift_id = $shiftId AND movement_type = 'sale_cash';", shiftId);
        int cashIn = ScalarInt(connection, "SELECT COALESCE(SUM(amount_cents), 0) FROM local_cash_movements WHERE shift_id = $shiftId AND movement_type = 'cash_in';", shiftId);
        int cashOut = ScalarInt(connection, "SELECT COALESCE(SUM(amount_cents), 0) FROM local_cash_movements WHERE shift_id = $shiftId AND movement_type = 'cash_out';", shiftId);
        int movementCount = ScalarInt(connection, "SELECT COUNT(*) FROM local_cash_movements WHERE shift_id = $shiftId;", shiftId);
        int paymentCount = ScalarInt(connection, "SELECT COUNT(*) FROM local_sale_payments WHERE shift_id = $shiftId;", shiftId);
        int expectedCash = shift.OpeningAmountCents + cashSales + cashIn - cashOut;
        int? countedCash = shift.CountedCashCents;
        int? difference = shift.DifferenceCents ?? (countedCash.HasValue ? countedCash.Value - expectedCash : null);

        return Task.FromResult(new LocalCashShiftSummary(shift.Id, shift.Status, shift.OpeningAmountCents, cashSales, cashIn, cashOut, expectedCash, countedCash, difference, paymentCount, movementCount));
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



    private static void InsertCashMovement(SqliteConnection connection, SqliteTransaction transaction, LocalCashMovement movement)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO local_cash_movements (id, shift_id, tenant_id, store_id, terminal_id, movement_type, amount_cents, occurred_at_utc, source_type, source_id, note)
VALUES ($id, $shiftId, $tenantId, $storeId, $terminalId, $movementType, $amountCents, $occurredAtUtc, $sourceType, $sourceId, $note);
""";
        command.Parameters.AddWithValue("$id", movement.Id.ToString());
        command.Parameters.AddWithValue("$shiftId", movement.ShiftId.ToString());
        command.Parameters.AddWithValue("$tenantId", movement.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", movement.StoreId.ToString());
        command.Parameters.AddWithValue("$terminalId", movement.TerminalId.ToString());
        command.Parameters.AddWithValue("$movementType", movement.MovementType);
        command.Parameters.AddWithValue("$amountCents", movement.AmountCents);
        command.Parameters.AddWithValue("$occurredAtUtc", movement.OccurredAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$sourceType", movement.SourceType);
        command.Parameters.AddWithValue("$sourceId", movement.SourceId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$note", movement.Note ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static int ScalarInt(SqliteConnection connection, string sql, Guid shiftId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$shiftId", shiftId.ToString());
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static LocalCashShift ReadCashShift(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Guid.Parse(reader.GetString(2)),
        Guid.Parse(reader.GetString(3)),
        Guid.Parse(reader.GetString(4)),
        DateTimeOffset.Parse(reader.GetString(5)),
        reader.GetInt32(6),
        reader.GetString(7),
        reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)),
        reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
        reader.IsDBNull(10) ? null : reader.GetInt32(10),
        reader.IsDBNull(11) ? null : reader.GetInt32(11),
        reader.IsDBNull(12) ? null : reader.GetInt32(12));

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
