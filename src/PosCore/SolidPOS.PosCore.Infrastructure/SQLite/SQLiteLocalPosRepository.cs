using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Infrastructure.SQLite;

public sealed class SQLiteLocalPosRepository : ILocalPosRepository, ILocalResilienceRepository, ILeasedLocalSyncRepository, IControlledLocalSyncRecoveryRepository, IAuthoritativePermissionCacheStore
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
CREATE TABLE IF NOT EXISTS local_sync_claims (
  outbox_event_id TEXT PRIMARY KEY,
  claim_id TEXT NOT NULL,
  claimed_at_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_local_sync_claims_expiry
  ON local_sync_claims(claimed_at_utc);
CREATE INDEX IF NOT EXISTS idx_local_sync_claims_batch
  ON local_sync_claims(claim_id);
CREATE TABLE IF NOT EXISTS local_sync_claim_history (
  claim_id TEXT PRIMARY KEY,
  first_claimed_at_utc TEXT NOT NULL
);
-- One-time additive initialization imports any claims created by older AC-DEV builds.
INSERT OR IGNORE INTO local_sync_claim_history (claim_id, first_claimed_at_utc)
SELECT claim_id, MIN(claimed_at_utc) FROM local_sync_claims GROUP BY claim_id;
CREATE TABLE IF NOT EXISTS local_sync_recovery_runs (
  recovery_id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  requested_at_utc TEXT NOT NULL,
  timeout_seconds INTEGER NOT NULL,
  max_events INTEGER NOT NULL,
  actor TEXT NOT NULL,
  reason TEXT NOT NULL,
  approval_reference TEXT NULL,
  dry_run INTEGER NOT NULL CHECK (dry_run IN (0, 1)),
  eligible_count INTEGER NOT NULL,
  recovered_count INTEGER NOT NULL,
  has_more INTEGER NOT NULL CHECK (has_more IN (0, 1))
);
CREATE TABLE IF NOT EXISTS local_sync_recovery_approvals (
  recovery_id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  approver_user_id TEXT NOT NULL,
  approver_session_id TEXT NOT NULL,
  approval_reference TEXT NOT NULL,
  processing_timeout_seconds INTEGER NOT NULL,
  max_events INTEGER NOT NULL,
  reason TEXT NOT NULL,
  approved_at_utc TEXT NOT NULL,
  expires_at_utc TEXT NOT NULL,
  consumed_at_utc TEXT NULL
);
CREATE TABLE IF NOT EXISTS local_sync_recovery_executors (
  recovery_id TEXT PRIMARY KEY,
  executor_user_id TEXT NOT NULL,
  executor_session_id TEXT NOT NULL,
  operated_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_sync_recovery_candidates (
  recovery_id TEXT NOT NULL,
  outbox_event_id TEXT NOT NULL,
  original_claim_id TEXT NOT NULL,
  PRIMARY KEY (recovery_id, outbox_event_id),
  FOREIGN KEY (recovery_id) REFERENCES local_sync_recovery_runs(recovery_id)
);
CREATE INDEX IF NOT EXISTS idx_local_sync_recovery_runs_time ON local_sync_recovery_runs(requested_at_utc);
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
CREATE TABLE IF NOT EXISTS local_sync_pull_state (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  cursor TEXT NULL,
  last_pulled_at_utc TEXT NULL,
  last_change_count INTEGER NOT NULL DEFAULT 0,
  total_applied_change_count INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS local_applied_changes (
  change_id TEXT PRIMARY KEY,
  entity_type TEXT NOT NULL,
  entity_id TEXT NOT NULL,
  operation TEXT NOT NULL,
  entity_version INTEGER NOT NULL,
  changed_at_utc TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  applied_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_remote_sales (
  remote_sale_id TEXT PRIMARY KEY,
  local_sale_id TEXT NULL,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  status TEXT NOT NULL,
  total_cents INTEGER NOT NULL,
  occurred_at_utc TEXT NULL,
  raw_json TEXT NOT NULL,
  applied_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_remote_receipts (
  receipt_id TEXT PRIMARY KEY,
  sale_id TEXT NOT NULL,
  receipt_number TEXT NOT NULL,
  public_token TEXT NULL,
  raw_json TEXT NOT NULL,
  applied_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_users (
  user_id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  password_hash TEXT NOT NULL,
  role_code TEXT NOT NULL,
  is_active INTEGER NOT NULL,
  last_synced_at_utc TEXT NOT NULL,
  max_offline_hours INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS local_authority_snapshot_state (
  user_id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  authority_version INTEGER NOT NULL CHECK (authority_version > 0),
  issued_at_utc TEXT NOT NULL,
  applied_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_user_permissions (
  user_id TEXT NOT NULL,
  permission_code TEXT NOT NULL,
  synced_at_utc TEXT NOT NULL,
  PRIMARY KEY(user_id, permission_code)
);
CREATE TABLE IF NOT EXISTS local_sessions (
  session_id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  email TEXT NOT NULL,
  display_name TEXT NOT NULL,
  role_code TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  expires_at_utc TEXT NOT NULL,
  status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_audit_events (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  user_id TEXT NULL,
  session_id TEXT NULL,
  event_type TEXT NOT NULL,
  message TEXT NOT NULL,
  occurred_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_print_jobs (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  sale_id TEXT NOT NULL,
  receipt_id TEXT NOT NULL,
  receipt_number TEXT NOT NULL,
  content TEXT NOT NULL,
  status TEXT NOT NULL,
  queued_at_utc TEXT NOT NULL,
  printed_at_utc TEXT NULL,
  last_error TEXT NULL,
  attempts INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS local_hardware_events (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL,
  store_id TEXT NOT NULL,
  terminal_id TEXT NOT NULL,
  device_type TEXT NOT NULL,
  event_type TEXT NOT NULL,
  message TEXT NOT NULL,
  occurred_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS local_recovery_journal (
  id TEXT PRIMARY KEY,
  operation TEXT NOT NULL,
  status TEXT NOT NULL,
  message TEXT NOT NULL,
  started_at_utc TEXT NOT NULL,
  completed_at_utc TEXT NULL
);
CREATE INDEX IF NOT EXISTS idx_local_recovery_journal_time ON local_recovery_journal(started_at_utc);
CREATE INDEX IF NOT EXISTS idx_local_sessions_active ON local_sessions(status, expires_at_utc);
CREATE INDEX IF NOT EXISTS idx_local_audit_events_time ON local_audit_events(occurred_at_utc);
CREATE INDEX IF NOT EXISTS idx_local_print_jobs_status ON local_print_jobs(status, queued_at_utc);
CREATE INDEX IF NOT EXISTS idx_local_hardware_events_time ON local_hardware_events(occurred_at_utc);
CREATE UNIQUE INDEX IF NOT EXISTS ux_local_catalog_products_sku ON local_catalog_products(sku);
CREATE INDEX IF NOT EXISTS idx_local_inventory_recipes_output ON local_inventory_recipes(output_product_id, output_variant_id, status);
CREATE INDEX IF NOT EXISTS idx_local_inventory_recipe_items_recipe ON local_inventory_recipe_items(recipe_id);
CREATE INDEX IF NOT EXISTS idx_local_inventory_movements_sale ON local_inventory_movements(local_sale_id);
CREATE INDEX IF NOT EXISTS idx_local_cash_shifts_open ON local_cash_shifts(status, opened_at_utc);
CREATE INDEX IF NOT EXISTS idx_local_cash_movements_shift ON local_cash_movements(shift_id, occurred_at_utc);
CREATE INDEX IF NOT EXISTS idx_local_sale_payments_shift ON local_sale_payments(shift_id);
CREATE INDEX IF NOT EXISTS idx_local_applied_changes_entity ON local_applied_changes(entity_type, entity_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_local_remote_sales_local_sale ON local_remote_sales(local_sale_id) WHERE local_sale_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_local_remote_receipts_sale ON local_remote_receipts(sale_id);
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


    /// <summary>
    /// SQLite uses an IMMEDIATE write transaction, so concurrent repositories
    /// cannot claim the same pending row. Only one terminal scope enters a batch.
    /// The claim owner is recorded durably alongside the InFlight status.
    /// </summary>
    public Task<IReadOnlyList<LocalOutboxEvent>> ClaimPendingOutboxEventsAsync(
        int limit, Guid claimId, DateTimeOffset claimedAtUtc, CancellationToken cancellationToken = default)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (claimId == Guid.Empty) throw new ArgumentException("Claim ID is required.", nameof(claimId));
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT COUNT(*) FROM local_sync_claim_history WHERE claim_id = $claimId;";
            existing.Parameters.AddWithValue("$claimId", claimId.ToString());
            if (Convert.ToInt32(existing.ExecuteScalar()) != 0)
            {
                throw new InvalidOperationException("Sync claim ID was already used; a new batch ID is required.");
            }
        }

        string? tenantId;
        string? storeId;
        string? terminalId;
        using (var first = connection.CreateCommand())
        {
            first.Transaction = transaction;
            first.CommandText = """
SELECT e.tenant_id, e.store_id, e.terminal_id
FROM local_outbox_events e
WHERE e.status = $pending
  AND NOT EXISTS (SELECT 1 FROM local_sync_claims c WHERE c.outbox_event_id = e.id)
ORDER BY e.sequence_number, e.id LIMIT 1;
""";
            first.Parameters.AddWithValue("$pending", (int)LocalOutboxStatus.Pending);
            using var reader = first.ExecuteReader();
            if (!reader.Read())
            {
                transaction.Commit();
                return Task.FromResult<IReadOnlyList<LocalOutboxEvent>>([]);
            }

            tenantId = reader.GetString(0);
            storeId = reader.GetString(1);
            terminalId = reader.GetString(2);
        }

        var selected = new List<LocalOutboxEvent>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
SELECT e.id, e.tenant_id, e.store_id, e.terminal_id, e.event_type,
       e.schema_version, e.sequence_number, e.payload_json, e.status,
       e.created_at_utc, e.synced_at_utc, e.last_error, e.attempts
FROM local_outbox_events e
WHERE e.status = $pending
  AND e.tenant_id = $tenantId AND e.store_id = $storeId AND e.terminal_id = $terminalId
  AND NOT EXISTS (SELECT 1 FROM local_sync_claims c WHERE c.outbox_event_id = e.id)
ORDER BY e.sequence_number, e.id LIMIT $limit;
""";
            read.Parameters.AddWithValue("$pending", (int)LocalOutboxStatus.Pending);
            read.Parameters.AddWithValue("$tenantId", tenantId);
            read.Parameters.AddWithValue("$storeId", storeId);
            read.Parameters.AddWithValue("$terminalId", terminalId);
            read.Parameters.AddWithValue("$limit", limit);
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                selected.Add(ReadOutboxEvent(reader));
            }
        }

        // Preserve all issued claim IDs. If an expired claim were reused, a
        // delayed worker could otherwise masquerade as its new owner.
        if (selected.Count > 0)
        {
            using var history = connection.CreateCommand();
            history.Transaction = transaction;
            history.CommandText = """
INSERT INTO local_sync_claim_history (claim_id, first_claimed_at_utc)
VALUES ($claimId, $claimedAtUtc);
""";
            history.Parameters.AddWithValue("$claimId", claimId.ToString());
            history.Parameters.AddWithValue("$claimedAtUtc", claimedAtUtc.ToUniversalTime().ToString("O"));
            history.ExecuteNonQuery();
        }

        foreach (var item in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText =
                    "UPDATE local_outbox_events SET status = $inFlight WHERE id = $id AND status = $pending;";
                update.Parameters.AddWithValue("$inFlight", (int)LocalOutboxStatus.InFlight);
                update.Parameters.AddWithValue("$pending", (int)LocalOutboxStatus.Pending);
                update.Parameters.AddWithValue("$id", item.Id.ToString());
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Pending event was changed during claim.");
            }
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
INSERT INTO local_sync_claims (outbox_event_id, claim_id, claimed_at_utc)
VALUES ($eventId, $claimId, $claimedAtUtc);
""";
            insert.Parameters.AddWithValue("$eventId", item.Id.ToString());
            insert.Parameters.AddWithValue("$claimId", claimId.ToString());
            insert.Parameters.AddWithValue("$claimedAtUtc", claimedAtUtc.ToUniversalTime().ToString("O"));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.FromResult<IReadOnlyList<LocalOutboxEvent>>(
            selected.Select(item => item with { Status = LocalOutboxStatus.InFlight }).ToArray());
    }

    public Task CommitClaimedSyncAcknowledgementsAsync(
        Guid claimId, IReadOnlyCollection<LocalSyncAcknowledgement> acknowledgements,
        DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acknowledgements);
        if (claimId == Guid.Empty) throw new ArgumentException("Claim ID is required.", nameof(claimId));
        if (acknowledgements.Count == 0) return Task.CompletedTask;
        if (acknowledgements.Select(item => item.OutboxEventId).Distinct().Count() != acknowledgements.Count)
            throw new InvalidOperationException("Repeated event acknowledgement in claimed batch.");

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var ack in acknowledgements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ack.BatchId != claimId || ack.Id == Guid.Empty || ack.OutboxEventId == Guid.Empty)
                throw new InvalidOperationException("Claim acknowledgement identifiers do not match.");

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
UPDATE local_outbox_events
SET status = $synced, synced_at_utc = $syncedAt, last_error = NULL
WHERE id = $eventId AND status = $inFlight
  AND EXISTS (SELECT 1 FROM local_sync_claims c
              WHERE c.outbox_event_id = $eventId AND c.claim_id = $claimId);
""";
                update.Parameters.AddWithValue("$synced", (int)LocalOutboxStatus.Synced);
                update.Parameters.AddWithValue("$inFlight", (int)LocalOutboxStatus.InFlight);
                update.Parameters.AddWithValue("$syncedAt", syncedAtUtc.ToString("O"));
                update.Parameters.AddWithValue("$eventId", ack.OutboxEventId.ToString());
                update.Parameters.AddWithValue("$claimId", claimId.ToString());
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Sync claim is missing or no longer owned by this batch.");
            }
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
INSERT INTO local_sync_acknowledgements
    (id, batch_id, outbox_event_id, remote_status, remote_response_json, acknowledged_at_utc)
VALUES ($id, $batchId, $eventId, $status, $response, $acknowledgedAt);
""";
                insert.Parameters.AddWithValue("$id", ack.Id.ToString());
                insert.Parameters.AddWithValue("$batchId", ack.BatchId.ToString());
                insert.Parameters.AddWithValue("$eventId", ack.OutboxEventId.ToString());
                insert.Parameters.AddWithValue("$status", ack.RemoteStatus);
                insert.Parameters.AddWithValue("$response", ack.RemoteResponseJson);
                insert.Parameters.AddWithValue("$acknowledgedAt", ack.AcknowledgedAtUtc.ToString("O"));
                insert.ExecuteNonQuery();
            }
            using var release = connection.CreateCommand();
            release.Transaction = transaction;
            release.CommandText = """
DELETE FROM local_sync_claims WHERE outbox_event_id = $eventId AND claim_id = $claimId;
""";
            release.Parameters.AddWithValue("$eventId", ack.OutboxEventId.ToString());
            release.Parameters.AddWithValue("$claimId", claimId.ToString());
            if (release.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Sync claim disappeared before acknowledgement was committed.");
        }
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task MarkClaimedOutboxFailureAsync(
        Guid claimId, Guid eventId, LocalOutboxStatus status, string reason,
        CancellationToken cancellationToken = default)
    {
        if (status is not (LocalOutboxStatus.Failed or LocalOutboxStatus.RetryPending or LocalOutboxStatus.DeadLetter))
            throw new ArgumentOutOfRangeException(nameof(status));
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
UPDATE local_outbox_events
SET status = $status, attempts = attempts + 1, last_error = $reason
WHERE id = $eventId AND status = $inFlight
  AND EXISTS (SELECT 1 FROM local_sync_claims c
              WHERE c.outbox_event_id = $eventId AND c.claim_id = $claimId);
""";
            update.Parameters.AddWithValue("$status", (int)status);
            update.Parameters.AddWithValue("$reason", reason);
            update.Parameters.AddWithValue("$eventId", eventId.ToString());
            update.Parameters.AddWithValue("$inFlight", (int)LocalOutboxStatus.InFlight);
            update.Parameters.AddWithValue("$claimId", claimId.ToString());
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Cannot mark failure: sync claim is no longer owned.");
        }
        using (var release = connection.CreateCommand())
        {
            release.Transaction = transaction;
            release.CommandText = "DELETE FROM local_sync_claims WHERE outbox_event_id = $eventId AND claim_id = $claimId;";
            release.Parameters.AddWithValue("$eventId", eventId.ToString());
            release.Parameters.AddWithValue("$claimId", claimId.ToString());
            if (release.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Sync failure transition lost its claim.");
        }
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task ReleaseClaimAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        if (claimId == Guid.Empty) throw new ArgumentException("Claim ID is required.", nameof(claimId));
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
UPDATE local_outbox_events SET status = $pending
WHERE status = $inFlight
  AND id IN (SELECT outbox_event_id FROM local_sync_claims WHERE claim_id = $claimId);
""";
            update.Parameters.AddWithValue("$pending", (int)LocalOutboxStatus.Pending);
            update.Parameters.AddWithValue("$inFlight", (int)LocalOutboxStatus.InFlight);
            update.Parameters.AddWithValue("$claimId", claimId.ToString());
            update.ExecuteNonQuery();
        }
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM local_sync_claims WHERE claim_id = $claimId;";
            delete.Parameters.AddWithValue("$claimId", claimId.ToString());
            delete.ExecuteNonQuery();
        }
        transaction.Commit();
        return Task.CompletedTask;
    }

    internal Task<int> RecoverStaleClaimsAsync(
        DateTimeOffset nowUtc, TimeSpan processingTimeout, CancellationToken cancellationToken = default)
    {
        if (processingTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(processingTimeout));
        cancellationToken.ThrowIfCancellationRequested();
        var cutoff = (nowUtc - processingTimeout).ToUniversalTime().ToString("O");
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        int recovered;
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
UPDATE local_outbox_events SET status = $pending
WHERE status = $inFlight
  AND id IN (SELECT outbox_event_id FROM local_sync_claims WHERE claimed_at_utc <= $cutoff);
""";
            update.Parameters.AddWithValue("$pending", (int)LocalOutboxStatus.Pending);
            update.Parameters.AddWithValue("$inFlight", (int)LocalOutboxStatus.InFlight);
            update.Parameters.AddWithValue("$cutoff", cutoff);
            recovered = update.ExecuteNonQuery();
        }
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM local_sync_claims WHERE claimed_at_utc <= $cutoff;";
            delete.Parameters.AddWithValue("$cutoff", cutoff);
            delete.ExecuteNonQuery();
        }
        transaction.Commit();
        return Task.FromResult(recovered);
    }


    /// <summary>
    /// Operator-controlled recovery. Both the event transitions and the durable
    /// audit journal are committed in one SQLite write transaction. Dry-runs also
    /// produce an audit record but never mutate claims or event statuses.
    /// Scope is always exactly one tenant, store and terminal.
    /// </summary>
    /// <summary>
    /// Write a single-use, time-bound and exactly scoped recovery approval only
    /// for a live approver session with the dedicated Approve permission.
    /// The executing user must be someone else (enforced at execution).
    /// </summary>
    public Task RegisterRecoveryApprovalAsync(
        LocalSyncRecoveryApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RecoveryId == Guid.Empty || request.TenantId == Guid.Empty ||
            request.StoreId == Guid.Empty || request.TerminalId == Guid.Empty ||
            request.ApproverSessionId == Guid.Empty)
            throw new ArgumentException("Approval requires identifiers, scope and approver session.", nameof(request));
        if (request.ProcessingTimeout < TimeSpan.FromMinutes(5) ||
            request.ProcessingTimeout > TimeSpan.FromHours(24) ||
            request.MaxEvents is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request), "Invalid recovery bounds.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 256 ||
            string.IsNullOrWhiteSpace(request.ApprovalReference) || request.ApprovalReference.Length > 128)
            throw new ArgumentException("Valid reason and approval reference are required.", nameof(request));

        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var approverId = RequireAuthorizedRecoverySession(
            connection, transaction, request.ApproverSessionId, request.TenantId,
            request.StoreId, LocalSyncRecoveryPermissions.Approve, now);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO local_sync_recovery_approvals
    (recovery_id, tenant_id, store_id, terminal_id, approver_user_id,
     approver_session_id, approval_reference, processing_timeout_seconds,
     max_events, reason, approved_at_utc, expires_at_utc, consumed_at_utc)
VALUES ($id, $tenant, $store, $terminal, $user, $session, $reference,
        $timeout, $max, $reason, $now, $expires, NULL);
""";
        command.Parameters.AddWithValue("$id", request.RecoveryId.ToString());
        command.Parameters.AddWithValue("$tenant", request.TenantId.ToString());
        command.Parameters.AddWithValue("$store", request.StoreId.ToString());
        command.Parameters.AddWithValue("$terminal", request.TerminalId.ToString());
        command.Parameters.AddWithValue("$user", approverId.ToString());
        command.Parameters.AddWithValue("$session", request.ApproverSessionId.ToString());
        command.Parameters.AddWithValue("$reference", request.ApprovalReference);
        command.Parameters.AddWithValue("$timeout", Convert.ToInt64(request.ProcessingTimeout.TotalSeconds));
        command.Parameters.AddWithValue("$max", request.MaxEvents);
        command.Parameters.AddWithValue("$reason", request.Reason);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$expires", now.AddMinutes(15).ToString("O"));
        command.ExecuteNonQuery();
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<LocalSyncRecoveryResult> RecoverStaleClaimsWithAuditAsync(
        LocalSyncRecoveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RecoveryId == Guid.Empty || request.TenantId == Guid.Empty ||
            request.StoreId == Guid.Empty || request.TerminalId == Guid.Empty)
            throw new ArgumentException("Recovery ID and complete terminal scope are required.", nameof(request));
        if (request.ProcessingTimeout < TimeSpan.FromMinutes(5) ||
            request.ProcessingTimeout > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(request), "Recovery timeout must be between 5 minutes and 24 hours.");
        if (request.MaxEvents is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request), "Recovery batch must contain 1 to 100 events.");
        if (string.IsNullOrWhiteSpace(request.Actor) || request.Actor.Length > 128 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 256)
            throw new ArgumentException("Recovery actor (max 128) and reason (max 256) are required.", nameof(request));
        if (!request.DryRun && (string.IsNullOrWhiteSpace(request.ApprovalReference) ||
            request.ApprovalReference.Length > 128))
            throw new InvalidOperationException("Explicit recovery requires an approval reference.");
        if (request.ApprovalReference?.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(request), "Approval reference must be at most 128 characters.");

        cancellationToken.ThrowIfCancellationRequested();
        // Never let a caller fast-forward the recovery clock to classify
        // recently claimed events as stale. The system clock is authoritative.
        var securityNow = DateTimeOffset.UtcNow;
        if (Math.Abs((request.NowUtc.ToUniversalTime() - securityNow).TotalSeconds) > 120)
            throw new ArgumentOutOfRangeException(nameof(request),
                "Recovery request clock differs from the trusted system UTC clock by more than two minutes.");
        var utcNow = securityNow;
        var cutoff = (utcNow - request.ProcessingTimeout).ToString("O");
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        // Authorization is verified inside the same write transaction as the
        // state mutation, so closed sessions/revoked permissions and consumed
        // approvals cannot pass via a stale preflight check.
        Guid executorId = RequireAuthorizedRecoverySession(
            connection, transaction, request.ExecutorSessionId, request.TenantId,
            request.StoreId, LocalSyncRecoveryPermissions.Execute, securityNow);
        if (!request.DryRun)
        {
            ConsumeRecoveryApproval(
                connection, transaction, request, executorId, securityNow);
        }

        int eligibleCount;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = """
SELECT COUNT(*)
FROM local_sync_claims c
JOIN local_outbox_events e ON e.id = c.outbox_event_id
WHERE e.status = $inFlight AND c.claimed_at_utc <= $cutoff
  AND e.tenant_id = $tenant AND e.store_id = $store AND e.terminal_id = $terminal;
""";
            count.Parameters.AddWithValue("$inFlight", (int)LocalOutboxStatus.InFlight);
            count.Parameters.AddWithValue("$cutoff", cutoff);
            count.Parameters.AddWithValue("$tenant", request.TenantId.ToString());
            count.Parameters.AddWithValue("$store", request.StoreId.ToString());
            count.Parameters.AddWithValue("$terminal", request.TerminalId.ToString());
            eligibleCount = Convert.ToInt32(count.ExecuteScalar());
        }

        var candidates = new List<(Guid EventId, Guid ClaimId)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
SELECT e.id, c.claim_id
FROM local_sync_claims c
JOIN local_outbox_events e ON e.id = c.outbox_event_id
WHERE e.status = $inFlight AND c.claimed_at_utc <= $cutoff
  AND e.tenant_id = $tenant AND e.store_id = $store AND e.terminal_id = $terminal
ORDER BY c.claimed_at_utc, e.id
LIMIT $limit;
""";
            select.Parameters.AddWithValue("$inFlight", (int)LocalOutboxStatus.InFlight);
            select.Parameters.AddWithValue("$cutoff", cutoff);
            select.Parameters.AddWithValue("$tenant", request.TenantId.ToString());
            select.Parameters.AddWithValue("$store", request.StoreId.ToString());
            select.Parameters.AddWithValue("$terminal", request.TerminalId.ToString());
            select.Parameters.AddWithValue("$limit", request.MaxEvents);
            using var reader = select.ExecuteReader();
            while (reader.Read())
                candidates.Add((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1))));
        }

        int recovered = 0;
        if (!request.DryRun)
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var update = connection.CreateCommand())
                {
                    update.Transaction = transaction;
                    update.CommandText = """
UPDATE local_outbox_events SET status = $pending
WHERE id = $eventId AND status = $inFlight
  AND tenant_id = $tenant AND store_id = $store AND terminal_id = $terminal
  AND EXISTS (
      SELECT 1 FROM local_sync_claims c
      WHERE c.outbox_event_id = $eventId AND c.claim_id = $claimId
        AND c.claimed_at_utc <= $cutoff);
""";
                    update.Parameters.AddWithValue("$pending", (int)LocalOutboxStatus.Pending);
                    update.Parameters.AddWithValue("$inFlight", (int)LocalOutboxStatus.InFlight);
                    update.Parameters.AddWithValue("$eventId", candidate.EventId.ToString());
                    update.Parameters.AddWithValue("$claimId", candidate.ClaimId.ToString());
                    update.Parameters.AddWithValue("$tenant", request.TenantId.ToString());
                    update.Parameters.AddWithValue("$store", request.StoreId.ToString());
                    update.Parameters.AddWithValue("$terminal", request.TerminalId.ToString());
                    update.Parameters.AddWithValue("$cutoff", cutoff);
                    if (update.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Stale claim changed during controlled recovery.");
                }
                using (var remove = connection.CreateCommand())
                {
                    remove.Transaction = transaction;
                    remove.CommandText =
                        "DELETE FROM local_sync_claims WHERE outbox_event_id = $eventId AND claim_id = $claimId;";
                    remove.Parameters.AddWithValue("$eventId", candidate.EventId.ToString());
                    remove.Parameters.AddWithValue("$claimId", candidate.ClaimId.ToString());
                    if (remove.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Stale claim ownership changed during recovery.");
                }
                recovered++;
            }
        }

        using (var run = connection.CreateCommand())
        {
            run.Transaction = transaction;
            run.CommandText = """
INSERT INTO local_sync_recovery_runs
    (recovery_id, tenant_id, store_id, terminal_id, requested_at_utc,
     timeout_seconds, max_events, actor, reason, approval_reference,
     dry_run, eligible_count, recovered_count, has_more)
VALUES ($id, $tenant, $store, $terminal, $now,
        $timeout, $max, $actor, $reason, $approval,
        $dry, $eligible, $recovered, $more);
""";
            run.Parameters.AddWithValue("$id", request.RecoveryId.ToString());
            run.Parameters.AddWithValue("$tenant", request.TenantId.ToString());
            run.Parameters.AddWithValue("$store", request.StoreId.ToString());
            run.Parameters.AddWithValue("$terminal", request.TerminalId.ToString());
            run.Parameters.AddWithValue("$now", utcNow.ToString("O"));
            run.Parameters.AddWithValue("$timeout", Convert.ToInt64(request.ProcessingTimeout.TotalSeconds));
            run.Parameters.AddWithValue("$max", request.MaxEvents);
            run.Parameters.AddWithValue("$actor", request.Actor);
            run.Parameters.AddWithValue("$reason", request.Reason);
            run.Parameters.AddWithValue("$approval", request.ApprovalReference is null ? DBNull.Value : request.ApprovalReference);
            run.Parameters.AddWithValue("$dry", request.DryRun ? 1 : 0);
            run.Parameters.AddWithValue("$eligible", eligibleCount);
            run.Parameters.AddWithValue("$recovered", recovered);
            run.Parameters.AddWithValue("$more", eligibleCount > candidates.Count ? 1 : 0);
            run.ExecuteNonQuery();
        }

        using (var identity = connection.CreateCommand())
        {
            identity.Transaction = transaction;
            identity.CommandText = """
INSERT INTO local_sync_recovery_executors
    (recovery_id, executor_user_id, executor_session_id, operated_at_utc)
VALUES ($id, $user, $session, $now);
""";
            identity.Parameters.AddWithValue("$id", request.RecoveryId.ToString());
            identity.Parameters.AddWithValue("$user", executorId.ToString());
            identity.Parameters.AddWithValue("$session", request.ExecutorSessionId.ToString());
            identity.Parameters.AddWithValue("$now", securityNow.ToString("O"));
            identity.ExecuteNonQuery();
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var detail = connection.CreateCommand();
            detail.Transaction = transaction;
            detail.CommandText = """
INSERT INTO local_sync_recovery_candidates (recovery_id, outbox_event_id, original_claim_id)
VALUES ($recoveryId, $eventId, $claimId);
""";
            detail.Parameters.AddWithValue("$recoveryId", request.RecoveryId.ToString());
            detail.Parameters.AddWithValue("$eventId", candidate.EventId.ToString());
            detail.Parameters.AddWithValue("$claimId", candidate.ClaimId.ToString());
            detail.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.FromResult(new LocalSyncRecoveryResult(
            request.RecoveryId, request.DryRun, eligibleCount, recovered,
            eligibleCount > candidates.Count, candidates.Select(x => x.EventId).ToArray()));
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

    public Task MarkOutboxRetryPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE local_outbox_events SET status = $status, last_error = $reason, attempts = attempts + 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$status", (int)LocalOutboxStatus.RetryPending);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$id", eventId.ToString());
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task MarkOutboxDeadLetterAsync(Guid eventId, string reason, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE local_outbox_events SET status = $status, last_error = $reason, attempts = attempts + 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$status", (int)LocalOutboxStatus.DeadLetter);
        command.Parameters.AddWithValue("$reason", reason);
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

    public Task<int> RecoverRetryPendingOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE local_outbox_events
SET status = $pendingStatus, last_error = $reason
WHERE status = $retryPendingStatus AND attempts < $maxAttempts;
SELECT changes();
""";
        command.Parameters.AddWithValue("$pendingStatus", (int)LocalOutboxStatus.Pending);
        command.Parameters.AddWithValue("$retryPendingStatus", (int)LocalOutboxStatus.RetryPending);
        command.Parameters.AddWithValue("$maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("$reason", reason);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }


    /// <summary>
    /// A remote success must not become Synced without its local acknowledgement.
    /// Both records are committed in the same SQLite transaction. Any insert or
    /// update failure rolls back all events in this batch, leaving them pending
    /// for a safe, server-idempotent replay.
    /// </summary>
    public Task CommitSyncAcknowledgementsAsync(
        IReadOnlyCollection<LocalSyncAcknowledgement> acknowledgements,
        DateTimeOffset syncedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acknowledgements);
        if (acknowledgements.Count == 0) return Task.CompletedTask;
        if (acknowledgements.Select(item => item.OutboxEventId).Distinct().Count() != acknowledgements.Count)
        {
            throw new InvalidOperationException("A sync commit cannot acknowledge an outbox event twice.");
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (LocalSyncAcknowledgement acknowledgement in acknowledgements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (acknowledgement.OutboxEventId == Guid.Empty || acknowledgement.Id == Guid.Empty)
            {
                throw new InvalidOperationException("A sync acknowledgement requires nonempty identifiers.");
            }

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
UPDATE local_outbox_events
SET status = $synced, synced_at_utc = $syncedAt, last_error = NULL
WHERE id = $eventId AND status = $pending;
""";
                update.Parameters.AddWithValue("$synced", (int)LocalOutboxStatus.Synced);
                update.Parameters.AddWithValue("$syncedAt", syncedAtUtc.ToString("O"));
                update.Parameters.AddWithValue("$eventId", acknowledgement.OutboxEventId.ToString());
                update.Parameters.AddWithValue("$pending", (int)LocalOutboxStatus.Pending);
                if (update.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException(
                        "Cannot commit sync acknowledgement: outbox event is missing or no longer pending.");
                }
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
INSERT INTO local_sync_acknowledgements
    (id, batch_id, outbox_event_id, remote_status, remote_response_json, acknowledged_at_utc)
VALUES ($id, $batchId, $eventId, $status, $response, $acknowledgedAt);
""";
            insert.Parameters.AddWithValue("$id", acknowledgement.Id.ToString());
            insert.Parameters.AddWithValue("$batchId", acknowledgement.BatchId.ToString());
            insert.Parameters.AddWithValue("$eventId", acknowledgement.OutboxEventId.ToString());
            insert.Parameters.AddWithValue("$status", acknowledgement.RemoteStatus);
            insert.Parameters.AddWithValue("$response", acknowledgement.RemoteResponseJson);
            insert.Parameters.AddWithValue("$acknowledgedAt", acknowledgement.AcknowledgedAtUtc.ToString("O"));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
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

    public Task<LocalSyncQueueHealthSummary> GetLocalSyncQueueHealthAsync(DateTimeOffset nowUtc, TimeSpan processingTimeout, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        int pending = CountOutboxStatus(connection, LocalOutboxStatus.Pending);
        int processing = CountOutboxStatus(connection, LocalOutboxStatus.InFlight);
        int retryPending = CountOutboxStatus(connection, LocalOutboxStatus.RetryPending);
        int deadLetter = CountOutboxStatus(connection, LocalOutboxStatus.DeadLetter);
        DateTimeOffset? oldestPending = ReadOldestOutboxCreatedAt(connection, LocalOutboxStatus.Pending);
        DateTimeOffset? oldestRetryPending = ReadOldestOutboxCreatedAt(connection, LocalOutboxStatus.RetryPending);
        DateTimeOffset? oldestProcessing = ReadOldestClaimedProcessingAt(connection);
        bool hasStuckProcessing = oldestProcessing.HasValue && nowUtc - oldestProcessing.Value > processingTimeout;
        bool requiresRecovery = retryPending > 0 || deadLetter > 0 || hasStuckProcessing;

        return Task.FromResult(new LocalSyncQueueHealthSummary(
            pending,
            processing,
            retryPending,
            deadLetter,
            oldestPending,
            oldestRetryPending,
            hasStuckProcessing,
            requiresRecovery));
    }

    public Task<LocalSyncPullState> GetSyncPullStateAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cursor, last_pulled_at_utc, last_change_count, total_applied_change_count FROM local_sync_pull_state WHERE id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return Task.FromResult(new LocalSyncPullState(null, null, 0, 0));
        }

        return Task.FromResult(new LocalSyncPullState(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1)),
            reader.GetInt32(2),
            reader.GetInt32(3)));
    }

    public Task<int> ApplySyncPullChangesAsync(IReadOnlyCollection<LocalAppliedSyncChange> changes, string nextCursor, DateTimeOffset pulledAtUtc, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        int appliedCount = 0;
        foreach (LocalAppliedSyncChange change in changes)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
INSERT OR IGNORE INTO local_applied_changes (change_id, entity_type, entity_id, operation, entity_version, changed_at_utc, payload_json, applied_at_utc)
VALUES ($changeId, $entityType, $entityId, $operation, $entityVersion, $changedAtUtc, $payloadJson, $appliedAtUtc);
""";
            command.Parameters.AddWithValue("$changeId", change.ChangeId.ToString());
            command.Parameters.AddWithValue("$entityType", change.EntityType);
            command.Parameters.AddWithValue("$entityId", change.EntityId.ToString());
            command.Parameters.AddWithValue("$operation", change.Operation);
            command.Parameters.AddWithValue("$entityVersion", change.EntityVersion);
            command.Parameters.AddWithValue("$changedAtUtc", change.ChangedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$payloadJson", change.PayloadJson);
            command.Parameters.AddWithValue("$appliedAtUtc", change.AppliedAtUtc.ToString("O"));
            appliedCount += command.ExecuteNonQuery();
        }

        using (var stateCommand = connection.CreateCommand())
        {
            stateCommand.Transaction = transaction;
            stateCommand.CommandText = """
INSERT INTO local_sync_pull_state (id, cursor, last_pulled_at_utc, last_change_count, total_applied_change_count)
VALUES (1, $cursor, $lastPulledAtUtc, $lastChangeCount, $appliedCount)
ON CONFLICT(id) DO UPDATE SET
  cursor = excluded.cursor,
  last_pulled_at_utc = excluded.last_pulled_at_utc,
  last_change_count = excluded.last_change_count,
  total_applied_change_count = local_sync_pull_state.total_applied_change_count + $appliedCount;
""";
            stateCommand.Parameters.AddWithValue("$cursor", nextCursor);
            stateCommand.Parameters.AddWithValue("$lastPulledAtUtc", pulledAtUtc.ToString("O"));
            stateCommand.Parameters.AddWithValue("$lastChangeCount", changes.Count);
            stateCommand.Parameters.AddWithValue("$appliedCount", appliedCount);
            stateCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.FromResult(appliedCount);
    }

    public Task<int> CountAppliedSyncChangesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM local_applied_changes;";
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task SaveRemoteSaleReadModelAsync(LocalRemoteSaleReadModel sale, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO local_remote_sales (remote_sale_id, local_sale_id, tenant_id, store_id, terminal_id, status, total_cents, occurred_at_utc, raw_json, applied_at_utc)
VALUES ($remoteSaleId, $localSaleId, $tenantId, $storeId, $terminalId, $status, $totalCents, $occurredAtUtc, $rawJson, $appliedAtUtc)
ON CONFLICT(remote_sale_id) DO UPDATE SET
  local_sale_id = excluded.local_sale_id,
  status = excluded.status,
  total_cents = excluded.total_cents,
  occurred_at_utc = excluded.occurred_at_utc,
  raw_json = excluded.raw_json,
  applied_at_utc = excluded.applied_at_utc;
""";
        command.Parameters.AddWithValue("$remoteSaleId", sale.RemoteSaleId.ToString());
        command.Parameters.AddWithValue("$localSaleId", sale.LocalSaleId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$tenantId", sale.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", sale.StoreId.ToString());
        command.Parameters.AddWithValue("$terminalId", sale.TerminalId.ToString());
        command.Parameters.AddWithValue("$status", sale.Status);
        command.Parameters.AddWithValue("$totalCents", sale.TotalCents);
        command.Parameters.AddWithValue("$occurredAtUtc", sale.OccurredAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$rawJson", sale.RawJson);
        command.Parameters.AddWithValue("$appliedAtUtc", sale.AppliedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task SaveRemoteReceiptReadModelAsync(LocalRemoteReceiptReadModel receipt, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO local_remote_receipts (receipt_id, sale_id, receipt_number, public_token, raw_json, applied_at_utc)
VALUES ($receiptId, $saleId, $receiptNumber, $publicToken, $rawJson, $appliedAtUtc)
ON CONFLICT(receipt_id) DO UPDATE SET
  receipt_number = excluded.receipt_number,
  public_token = excluded.public_token,
  raw_json = excluded.raw_json,
  applied_at_utc = excluded.applied_at_utc;
""";
        command.Parameters.AddWithValue("$receiptId", receipt.ReceiptId.ToString());
        command.Parameters.AddWithValue("$saleId", receipt.SaleId.ToString());
        command.Parameters.AddWithValue("$receiptNumber", receipt.ReceiptNumber);
        command.Parameters.AddWithValue("$publicToken", receipt.PublicToken ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$rawJson", receipt.RawJson);
        command.Parameters.AddWithValue("$appliedAtUtc", receipt.AppliedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<int> CountRemoteSalesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM local_remote_sales;";
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task<int> CountRemoteReceiptsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM local_remote_receipts;";
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task<LocalRemoteSaleReadModel?> GetRemoteSaleByLocalSaleIdAsync(Guid localSaleId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT remote_sale_id, local_sale_id, tenant_id, store_id, terminal_id, status, total_cents, occurred_at_utc, raw_json, applied_at_utc FROM local_remote_sales WHERE local_sale_id = $localSaleId LIMIT 1;";
        command.Parameters.AddWithValue("$localSaleId", localSaleId.ToString());
        using var reader = command.ExecuteReader();
        return Task.FromResult<LocalRemoteSaleReadModel?>(reader.Read() ? ReadRemoteSale(reader) : null);
    }

    public Task<LocalRemoteReceiptReadModel?> GetRemoteReceiptBySaleIdAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT receipt_id, sale_id, receipt_number, public_token, raw_json, applied_at_utc FROM local_remote_receipts WHERE sale_id = $saleId LIMIT 1;";
        command.Parameters.AddWithValue("$saleId", saleId.ToString());
        using var reader = command.ExecuteReader();
        return Task.FromResult<LocalRemoteReceiptReadModel?>(reader.Read() ? ReadRemoteReceipt(reader) : null);
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



    /// <summary>
    /// Apply only snapshots provided by the authenticated authority boundary.
    /// Never overwrite the locally enrolled password or infer privileges from role.
    /// State, grants, revoked sessions, replay guard and audit are atomic.
    /// </summary>
    public Task ApplyVerifiedPermissionSnapshotAsync(
        VerifiedPermissionSnapshot snapshot, DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var binding = connection.CreateCommand())
        {
            binding.Transaction = transaction;
            binding.CommandText = "SELECT tenant_id, store_id, terminal_id FROM terminal_binding WHERE id=1;";
            using var reader = binding.ExecuteReader();
            if (!reader.Read() || reader.GetString(0) != snapshot.TenantId.ToString() ||
                reader.GetString(1) != snapshot.StoreId.ToString() ||
                reader.GetString(2) != snapshot.TerminalId.ToString())
                throw new UnauthorizedAccessException("Snapshot does not match enrolled terminal.");
        }

        string oldRole;
        bool oldActive;
        using (var user = connection.CreateCommand())
        {
            user.Transaction = transaction;
            user.CommandText =
                "SELECT tenant_id, store_id, role_code, is_active FROM local_users WHERE user_id=$id;";
            user.Parameters.AddWithValue("$id", snapshot.UserId.ToString());
            using var reader = user.ExecuteReader();
            if (!reader.Read() || reader.GetString(0) != snapshot.TenantId.ToString() ||
                reader.GetString(1) != snapshot.StoreId.ToString())
                throw new UnauthorizedAccessException("Cannot enroll or reassign a local user using a role snapshot.");
            oldRole = reader.GetString(2);
            oldActive = reader.GetInt32(3) == 1;
        }

        using (var version = connection.CreateCommand())
        {
            version.Transaction = transaction;
            version.CommandText =
                "SELECT authority_version FROM local_authority_snapshot_state WHERE user_id=$id;";
            version.Parameters.AddWithValue("$id", snapshot.UserId.ToString());
            object? last = version.ExecuteScalar();
            if (last is not null && snapshot.AuthorityVersion <= Convert.ToInt64(last))
                throw new InvalidOperationException("Older or duplicate permission authority version.");
        }

        var oldGrants = new HashSet<string>(StringComparer.Ordinal);
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT permission_code FROM local_user_permissions WHERE user_id=$id;";
            read.Parameters.AddWithValue("$id", snapshot.UserId.ToString());
            using var reader = read.ExecuteReader();
            while (reader.Read()) oldGrants.Add(reader.GetString(0));
        }
        bool privilegesChanged = !oldGrants.SetEquals(snapshot.EffectivePermissions) ||
            oldRole != snapshot.RoleCode || oldActive != snapshot.IsActive;
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
UPDATE local_users
SET role_code=$role, is_active=$active, last_synced_at_utc=$synced
WHERE user_id=$id AND tenant_id=$tenant AND store_id=$store;
""";
            update.Parameters.AddWithValue("$role", snapshot.RoleCode);
            update.Parameters.AddWithValue("$active", snapshot.IsActive ? 1 : 0);
            update.Parameters.AddWithValue("$synced", appliedAtUtc.ToUniversalTime().ToString("O"));
            update.Parameters.AddWithValue("$id", snapshot.UserId.ToString());
            update.Parameters.AddWithValue("$tenant", snapshot.TenantId.ToString());
            update.Parameters.AddWithValue("$store", snapshot.StoreId.ToString());
            if (update.ExecuteNonQuery() != 1)
                throw new UnauthorizedAccessException("Lost local user ownership during permission refresh.");
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM local_user_permissions WHERE user_id=$id;";
            delete.Parameters.AddWithValue("$id", snapshot.UserId.ToString());
            delete.ExecuteNonQuery();
        }
        foreach (string permission in snapshot.EffectivePermissions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO local_user_permissions(user_id, permission_code, synced_at_utc) VALUES($id, $code, $at);";
            insert.Parameters.AddWithValue("$id", snapshot.UserId.ToString());
            insert.Parameters.AddWithValue("$code", permission);
            insert.Parameters.AddWithValue("$at", appliedAtUtc.ToUniversalTime().ToString("O"));
            insert.ExecuteNonQuery();
        }

        using (var receipt = connection.CreateCommand())
        {
            receipt.Transaction = transaction;
            receipt.CommandText = """
INSERT INTO local_authority_snapshot_state
 (user_id, tenant_id, store_id, terminal_id, authority_version, issued_at_utc, applied_at_utc)
VALUES ($id, $tenant, $store, $terminal, $version, $issued, $at)
ON CONFLICT(user_id) DO UPDATE SET
 authority_version=excluded.authority_version,
 issued_at_utc=excluded.issued_at_utc,
 applied_at_utc=excluded.applied_at_utc,
 terminal_id=excluded.terminal_id;
""";
            receipt.Parameters.AddWithValue("$id", snapshot.UserId.ToString());
            receipt.Parameters.AddWithValue("$tenant", snapshot.TenantId.ToString());
            receipt.Parameters.AddWithValue("$store", snapshot.StoreId.ToString());
            receipt.Parameters.AddWithValue("$terminal", snapshot.TerminalId.ToString());
            receipt.Parameters.AddWithValue("$version", snapshot.AuthorityVersion);
            receipt.Parameters.AddWithValue("$issued", snapshot.IssuedAtUtc.ToUniversalTime().ToString("O"));
            receipt.Parameters.AddWithValue("$at", appliedAtUtc.ToUniversalTime().ToString("O"));
            receipt.ExecuteNonQuery();
        }
        if (privilegesChanged)
        {
            using var close = connection.CreateCommand();
            close.Transaction = transaction;
            close.CommandText =
                "UPDATE local_sessions SET status='closed' WHERE user_id=$id AND status='active';";
            close.Parameters.AddWithValue("$id", snapshot.UserId.ToString());
            close.ExecuteNonQuery();
        }
        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
INSERT INTO local_audit_events
 (id, tenant_id, store_id, user_id, session_id, event_type, message, occurred_at_utc)
VALUES ($id, $tenant, $store, $user, NULL, $type, $message, $at);
""";
            audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            audit.Parameters.AddWithValue("$tenant", snapshot.TenantId.ToString());
            audit.Parameters.AddWithValue("$store", snapshot.StoreId.ToString());
            audit.Parameters.AddWithValue("$user", snapshot.UserId.ToString());
            audit.Parameters.AddWithValue("$type", privilegesChanged
                ? "local.auth.authority_changed_sessions_closed" : "local.auth.authority_refreshed");
            audit.Parameters.AddWithValue("$message",
                $"Authorized local role snapshot version {snapshot.AuthorityVersion} recorded.");
            audit.Parameters.AddWithValue("$at", appliedAtUtc.ToUniversalTime().ToString("O"));
            audit.ExecuteNonQuery();
        }
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task SaveLocalUserAsync(LocalUser user, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO local_users (user_id, tenant_id, store_id, email, display_name, password_hash, role_code, is_active, last_synced_at_utc, max_offline_hours)
VALUES ($userId, $tenantId, $storeId, $email, $displayName, $passwordHash, $roleCode, $isActive, $lastSyncedAtUtc, $maxOfflineHours)
ON CONFLICT(user_id) DO UPDATE SET
  tenant_id = excluded.tenant_id,
  store_id = excluded.store_id,
  email = excluded.email,
  display_name = excluded.display_name,
  password_hash = excluded.password_hash,
  role_code = excluded.role_code,
  is_active = excluded.is_active,
  last_synced_at_utc = excluded.last_synced_at_utc,
  max_offline_hours = excluded.max_offline_hours;
""";
            command.Parameters.AddWithValue("$userId", user.UserId.ToString());
            command.Parameters.AddWithValue("$tenantId", user.TenantId.ToString());
            command.Parameters.AddWithValue("$storeId", user.StoreId.ToString());
            command.Parameters.AddWithValue("$email", user.Email.ToLowerInvariant());
            command.Parameters.AddWithValue("$displayName", user.DisplayName);
            command.Parameters.AddWithValue("$passwordHash", user.PasswordHash);
            command.Parameters.AddWithValue("$roleCode", user.RoleCode);
            command.Parameters.AddWithValue("$isActive", user.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$lastSyncedAtUtc", user.LastSyncedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$maxOfflineHours", user.MaxOfflineHours);
            command.ExecuteNonQuery();
        }
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM local_user_permissions WHERE user_id = $userId;";
            delete.Parameters.AddWithValue("$userId", user.UserId.ToString());
            delete.ExecuteNonQuery();
        }
        foreach (var permission in permissions.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO local_user_permissions (user_id, permission_code, synced_at_utc) VALUES ($userId, $permissionCode, $syncedAtUtc);";
            command.Parameters.AddWithValue("$userId", user.UserId.ToString());
            command.Parameters.AddWithValue("$permissionCode", permission);
            command.Parameters.AddWithValue("$syncedAtUtc", user.LastSyncedAtUtc.ToString("O"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<LocalUser?> GetLocalUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id, tenant_id, store_id, email, display_name, password_hash, role_code, is_active, last_synced_at_utc, max_offline_hours FROM local_users WHERE lower(email) = lower($email);";
        command.Parameters.AddWithValue("$email", email);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return Task.FromResult<LocalUser?>(null);
        var user = new LocalUser(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7) == 1, DateTimeOffset.Parse(reader.GetString(8)), reader.GetInt32(9));
        return Task.FromResult<LocalUser?>(user);
    }

    public Task CreateLocalSessionAsync(LocalSession session, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO local_sessions (session_id, user_id, tenant_id, store_id, email, display_name, role_code, created_at_utc, expires_at_utc, status)
VALUES ($sessionId, $userId, $tenantId, $storeId, $email, $displayName, $roleCode, $createdAtUtc, $expiresAtUtc, $status);
""";
        command.Parameters.AddWithValue("$sessionId", session.SessionId.ToString());
        command.Parameters.AddWithValue("$userId", session.UserId.ToString());
        command.Parameters.AddWithValue("$tenantId", session.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", session.StoreId.ToString());
        command.Parameters.AddWithValue("$email", session.Email);
        command.Parameters.AddWithValue("$displayName", session.DisplayName);
        command.Parameters.AddWithValue("$roleCode", session.RoleCode);
        command.Parameters.AddWithValue("$createdAtUtc", session.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$expiresAtUtc", session.ExpiresAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", session.Status);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<LocalSession?> GetLocalSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id, user_id, tenant_id, store_id, email, display_name, role_code, created_at_utc, expires_at_utc, status FROM local_sessions WHERE session_id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString());
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return Task.FromResult<LocalSession?>(null);
        return Task.FromResult<LocalSession?>(ReadLocalSession(reader));
    }

    public Task<LocalSession?> GetLatestActiveLocalSessionAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id, user_id, tenant_id, store_id, email, display_name, role_code, created_at_utc, expires_at_utc, status FROM local_sessions WHERE status = 'active' ORDER BY created_at_utc DESC LIMIT 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return Task.FromResult<LocalSession?>(null);
        return Task.FromResult<LocalSession?>(ReadLocalSession(reader));
    }

    public Task CloseLocalSessionAsync(Guid sessionId, DateTimeOffset closedAtUtc, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE local_sessions SET status = 'closed' WHERE session_id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString());
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<bool> LocalUserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM local_user_permissions WHERE user_id = $userId AND permission_code = $permissionCode;";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$permissionCode", permissionCode);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()) > 0);
    }

    public Task LogLocalAuditEventAsync(LocalAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO local_audit_events (id, tenant_id, store_id, user_id, session_id, event_type, message, occurred_at_utc)
VALUES ($id, $tenantId, $storeId, $userId, $sessionId, $eventType, $message, $occurredAtUtc);
""";
        command.Parameters.AddWithValue("$id", auditEvent.Id.ToString());
        command.Parameters.AddWithValue("$tenantId", auditEvent.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", auditEvent.StoreId.ToString());
        command.Parameters.AddWithValue("$userId", auditEvent.UserId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$sessionId", auditEvent.SessionId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$eventType", auditEvent.EventType);
        command.Parameters.AddWithValue("$message", auditEvent.Message);
        command.Parameters.AddWithValue("$occurredAtUtc", auditEvent.OccurredAtUtc.ToString("O"));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<LocalAuthSummary> GetLocalAuthSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        int users = ScalarInt(connection, "SELECT COUNT(1) FROM local_users;");
        int permissions = ScalarInt(connection, "SELECT COUNT(1) FROM local_user_permissions;");
        int sessions = ScalarInt(connection, "SELECT COUNT(1) FROM local_sessions WHERE status = 'active';");
        int audits = ScalarInt(connection, "SELECT COUNT(1) FROM local_audit_events;");
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT max(last_synced_at_utc) FROM local_users;";
        var scalar = command.ExecuteScalar();
        DateTimeOffset? lastSynced = scalar is string text && !string.IsNullOrWhiteSpace(text) ? DateTimeOffset.Parse(text) : null;
        return Task.FromResult(new LocalAuthSummary(users, permissions, sessions, audits, lastSynced));
    }


    public Task SaveReceiptPrintJobAsync(LocalReceiptPrintJob job, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO local_print_jobs (id, tenant_id, store_id, terminal_id, sale_id, receipt_id, receipt_number, content, status, queued_at_utc, printed_at_utc, last_error, attempts)
VALUES ($id, $tenantId, $storeId, $terminalId, $saleId, $receiptId, $receiptNumber, $content, $status, $queuedAtUtc, $printedAtUtc, $lastError, $attempts)
ON CONFLICT(id) DO UPDATE SET
  status = excluded.status,
  printed_at_utc = excluded.printed_at_utc,
  last_error = excluded.last_error,
  attempts = excluded.attempts;
""";
        command.Parameters.AddWithValue("$id", job.Id.ToString());
        command.Parameters.AddWithValue("$tenantId", job.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", job.StoreId.ToString());
        command.Parameters.AddWithValue("$terminalId", job.TerminalId.ToString());
        command.Parameters.AddWithValue("$saleId", job.SaleId.ToString());
        command.Parameters.AddWithValue("$receiptId", job.ReceiptId.ToString());
        command.Parameters.AddWithValue("$receiptNumber", job.ReceiptNumber);
        command.Parameters.AddWithValue("$content", job.Content);
        command.Parameters.AddWithValue("$status", job.Status);
        command.Parameters.AddWithValue("$queuedAtUtc", job.QueuedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$printedAtUtc", job.PrintedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastError", job.LastError ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$attempts", job.Attempts);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<LocalReceiptPrintJob?> GetNextPendingReceiptPrintJobAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, tenant_id, store_id, terminal_id, sale_id, receipt_id, receipt_number, content, status, queued_at_utc, printed_at_utc, last_error, attempts FROM local_print_jobs WHERE status = 'pending' ORDER BY queued_at_utc LIMIT 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return Task.FromResult<LocalReceiptPrintJob?>(null);
        return Task.FromResult<LocalReceiptPrintJob?>(ReadReceiptPrintJob(reader));
    }

    public Task MarkReceiptPrintJobPrintedAsync(Guid jobId, DateTimeOffset printedAtUtc, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE local_print_jobs SET status = 'printed', printed_at_utc = $printedAtUtc, last_error = NULL, attempts = attempts + 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$id", jobId.ToString());
        command.Parameters.AddWithValue("$printedAtUtc", printedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task MarkReceiptPrintJobFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE local_print_jobs SET status = 'failed', last_error = $error, attempts = attempts + 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$id", jobId.ToString());
        command.Parameters.AddWithValue("$error", error);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task SaveHardwareEventAsync(LocalHardwareEvent hardwareEvent, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO local_hardware_events (id, tenant_id, store_id, terminal_id, device_type, event_type, message, occurred_at_utc)
VALUES ($id, $tenantId, $storeId, $terminalId, $deviceType, $eventType, $message, $occurredAtUtc);
""";
        command.Parameters.AddWithValue("$id", hardwareEvent.Id.ToString());
        command.Parameters.AddWithValue("$tenantId", hardwareEvent.TenantId.ToString());
        command.Parameters.AddWithValue("$storeId", hardwareEvent.StoreId.ToString());
        command.Parameters.AddWithValue("$terminalId", hardwareEvent.TerminalId.ToString());
        command.Parameters.AddWithValue("$deviceType", hardwareEvent.DeviceType);
        command.Parameters.AddWithValue("$eventType", hardwareEvent.EventType);
        command.Parameters.AddWithValue("$message", hardwareEvent.Message);
        command.Parameters.AddWithValue("$occurredAtUtc", hardwareEvent.OccurredAtUtc.ToString("O"));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<LocalHardwareSummary> GetHardwareSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        int pending = ScalarInt(connection, "SELECT COUNT(1) FROM local_print_jobs WHERE status = 'pending';");
        int printed = ScalarInt(connection, "SELECT COUNT(1) FROM local_print_jobs WHERE status = 'printed';");
        int failed = ScalarInt(connection, "SELECT COUNT(1) FROM local_print_jobs WHERE status = 'failed';");
        int events = ScalarInt(connection, "SELECT COUNT(1) FROM local_hardware_events;");
        LocalHardwareEvent? latest = null;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, tenant_id, store_id, terminal_id, device_type, event_type, message, occurred_at_utc FROM local_hardware_events ORDER BY occurred_at_utc DESC LIMIT 1;";
        using var reader = command.ExecuteReader();
        if (reader.Read()) latest = ReadHardwareEvent(reader);
        return Task.FromResult(new LocalHardwareSummary(pending, printed, failed, events, latest));
    }



    public Task<(Guid OutboxEventId, Guid PrintJobId, Guid SessionId)> CreateResilienceValidationFixtureAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        var tenantId = Guid.Parse("0ce5bbd0-528b-4aee-9fe3-93df001a4fde");
        var storeId = Guid.Parse("8e446c29-e9ad-41ed-a738-125aff7608b6");
        var terminalId = Guid.NewGuid();
        using (var binding = connection.CreateCommand())
        {
            binding.CommandText = "SELECT tenant_id, store_id, terminal_id FROM terminal_binding WHERE id = 1;";
            using var reader = binding.ExecuteReader();
            if (reader.Read())
            {
                tenantId = Guid.Parse(reader.GetString(0));
                storeId = Guid.Parse(reader.GetString(1));
                terminalId = Guid.Parse(reader.GetString(2));
            }
        }

        var outboxEventId = Guid.NewGuid();
        var printJobId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        using var transaction = connection.BeginTransaction();
        using (var outbox = connection.CreateCommand())
        {
            outbox.Transaction = transaction;
            outbox.CommandText = "INSERT INTO local_outbox_events (id, tenant_id, store_id, terminal_id, event_type, schema_version, sequence_number, payload_json, status, created_at_utc, synced_at_utc, last_error, attempts) VALUES ($id, $tenantId, $storeId, $terminalId, 'pos.validation_recovery', 4, $sequence, '{}', 3, $createdAt, NULL, 'fixture_failed_outbox', 0);";
            outbox.Parameters.AddWithValue("$id", outboxEventId.ToString());
            outbox.Parameters.AddWithValue("$tenantId", tenantId.ToString());
            outbox.Parameters.AddWithValue("$storeId", storeId.ToString());
            outbox.Parameters.AddWithValue("$terminalId", terminalId.ToString());
            outbox.Parameters.AddWithValue("$sequence", now.ToUnixTimeMilliseconds());
            outbox.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            outbox.ExecuteNonQuery();
        }
        using (var print = connection.CreateCommand())
        {
            print.Transaction = transaction;
            print.CommandText = "INSERT INTO local_print_jobs (id, tenant_id, store_id, terminal_id, sale_id, receipt_id, receipt_number, content, status, queued_at_utc, printed_at_utc, last_error, attempts) VALUES ($id, $tenantId, $storeId, $terminalId, $saleId, $receiptId, 'SP-RECOVERY-FIXTURE', 'Recovery fixture receipt', 'failed', $queuedAt, NULL, 'fixture_failed_print', 0);";
            print.Parameters.AddWithValue("$id", printJobId.ToString());
            print.Parameters.AddWithValue("$tenantId", tenantId.ToString());
            print.Parameters.AddWithValue("$storeId", storeId.ToString());
            print.Parameters.AddWithValue("$terminalId", terminalId.ToString());
            print.Parameters.AddWithValue("$saleId", Guid.NewGuid().ToString());
            print.Parameters.AddWithValue("$receiptId", Guid.NewGuid().ToString());
            print.Parameters.AddWithValue("$queuedAt", now.ToString("O"));
            print.ExecuteNonQuery();
        }
        using (var session = connection.CreateCommand())
        {
            session.Transaction = transaction;
            session.CommandText = "INSERT INTO local_sessions (session_id, user_id, tenant_id, store_id, email, display_name, role_code, created_at_utc, expires_at_utc, status) VALUES ($sessionId, $userId, $tenantId, $storeId, 'recovery.fixture@solidpos.local', 'Recovery Fixture', 'cashier', $createdAt, $expiresAt, 'active');";
            session.Parameters.AddWithValue("$sessionId", sessionId.ToString());
            session.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString());
            session.Parameters.AddWithValue("$tenantId", tenantId.ToString());
            session.Parameters.AddWithValue("$storeId", storeId.ToString());
            session.Parameters.AddWithValue("$createdAt", now.AddHours(-2).ToString("O"));
            session.Parameters.AddWithValue("$expiresAt", now.AddHours(-1).ToString("O"));
            session.ExecuteNonQuery();
        }
        transaction.Commit();
        return Task.FromResult((outboxEventId, printJobId, sessionId));
    }

    public Task<LocalIntegrityReport> VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        var sqliteIntegrity = "unknown";
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check;";
            sqliteIntegrity = Convert.ToString(command.ExecuteScalar()) ?? "unknown";
        }

        int pendingOutbox = ScalarInt(connection, "SELECT COUNT(1) FROM local_outbox_events WHERE status = 0;");
        int failedOutbox = ScalarInt(connection, "SELECT COUNT(1) FROM local_outbox_events WHERE status = 3;");
        int deadLetterOutbox = ScalarInt(connection, "SELECT COUNT(1) FROM local_outbox_events WHERE status = 4;");
        int pendingPrintJobs = ScalarInt(connection, "SELECT COUNT(1) FROM local_print_jobs WHERE status = 'pending';");
        int failedPrintJobs = ScalarInt(connection, "SELECT COUNT(1) FROM local_print_jobs WHERE status = 'failed';");
        int openCashShifts = ScalarInt(connection, "SELECT COUNT(1) FROM local_cash_shifts WHERE status = 'open';");
        int activeSessions = ScalarInt(connection, "SELECT COUNT(1) FROM local_sessions WHERE status = 'active';");
        int journalEntries = ScalarInt(connection, "SELECT COUNT(1) FROM local_recovery_journal;");

        var issues = new List<LocalIntegrityIssue>();
        if (!string.Equals(sqliteIntegrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new LocalIntegrityIssue("sqlite.integrity", "critical", $"SQLite integrity_check returned {sqliteIntegrity}.", 1));
        }
        if (deadLetterOutbox > 0)
        {
            issues.Add(new LocalIntegrityIssue("outbox.dead_letter", "critical", "Local outbox has dead-letter events that require operator review.", deadLetterOutbox));
        }
        if (failedOutbox > 0)
        {
            issues.Add(new LocalIntegrityIssue("outbox.failed", "warning", "Local outbox has failed events eligible for controlled retry.", failedOutbox));
        }
        if (failedPrintJobs > 0)
        {
            issues.Add(new LocalIntegrityIssue("print.failed", "warning", "Local print queue has failed jobs eligible for controlled retry.", failedPrintJobs));
        }
        if (openCashShifts > 1)
        {
            issues.Add(new LocalIntegrityIssue("cash_shift.multiple_open", "critical", "More than one local cash shift is open for this terminal database.", openCashShifts));
        }

        bool ok = !issues.Any(x => x.Severity == "critical");
        var report = new LocalIntegrityReport(_database.DatabasePath, ok, sqliteIntegrity, pendingOutbox, failedOutbox, deadLetterOutbox, pendingPrintJobs, failedPrintJobs, openCashShifts, activeSessions, journalEntries, issues);
        return Task.FromResult(report);
    }

    public Task<LocalRecoveryResult> RepairRuntimeAsync(string reason, bool createBackup, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        var journalId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        InsertRecoveryJournal(connection, journalId, "repair", "running", reason, startedAt, null);

        string? backupPath = null;
        bool backupCreated = false;
        if (createBackup)
        {
            var backup = CreateBackupFile(Path.Combine(Path.GetDirectoryName(_database.DatabasePath) ?? Environment.CurrentDirectory, "backups"));
            backupPath = backup.BackupPath;
            backupCreated = true;
        }

        int outboxRepaired;
        int printRepaired;
        int sessionsClosed;
        int cashFlagged = Math.Max(0, ScalarInt(connection, "SELECT COUNT(1) FROM local_cash_shifts WHERE status = 'open';") - 1);
        using (var transaction = connection.BeginTransaction())
        {
            outboxRepaired = ExecuteNonQuery(connection, transaction, "UPDATE local_outbox_events SET status = 0, last_error = 'recovered_for_retry', attempts = attempts + 1 WHERE status IN (3, 5) AND attempts < 5;");
            printRepaired = ExecuteNonQuery(connection, transaction, "UPDATE local_print_jobs SET status = 'pending', last_error = NULL, attempts = attempts + 1 WHERE status = 'failed' AND attempts < 5;");
            sessionsClosed = ExecuteNonQuery(connection, transaction, "UPDATE local_sessions SET status = 'closed' WHERE status = 'active' AND expires_at_utc <= strftime('%Y-%m-%dT%H:%M:%f+00:00','now');");
            transaction.Commit();
        }

        var completedAt = DateTimeOffset.UtcNow;
        var message = $"Local recovery completed. outboxRepaired={outboxRepaired}; printJobsRepaired={printRepaired}; sessionsClosed={sessionsClosed}; cashShiftsFlagged={cashFlagged}";
        UpdateRecoveryJournal(connection, journalId, "completed", message, completedAt);
        return Task.FromResult(new LocalRecoveryResult(journalId, outboxRepaired, printRepaired, sessionsClosed, cashFlagged, backupCreated, backupPath, message));
    }

    public Task<LocalBackupResult> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateBackupFile(destinationDirectory));
    }

    public Task<IReadOnlyList<LocalRecoveryJournalEntry>> GetRecoveryJournalAsync(int limit, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, operation, status, message, started_at_utc, completed_at_utc FROM local_recovery_journal ORDER BY started_at_utc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var entries = new List<LocalRecoveryJournalEntry>();
        while (reader.Read())
        {
            entries.Add(new LocalRecoveryJournalEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5))));
        }

        return Task.FromResult<IReadOnlyList<LocalRecoveryJournalEntry>>(entries);
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


    private static LocalRemoteSaleReadModel ReadRemoteSale(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
        Guid.Parse(reader.GetString(2)),
        Guid.Parse(reader.GetString(3)),
        Guid.Parse(reader.GetString(4)),
        reader.GetString(5),
        reader.GetInt32(6),
        reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
        reader.GetString(8),
        DateTimeOffset.Parse(reader.GetString(9)));

    private static LocalRemoteReceiptReadModel ReadRemoteReceipt(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        DateTimeOffset.Parse(reader.GetString(5)));


    private static LocalReceiptPrintJob ReadReceiptPrintJob(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Guid.Parse(reader.GetString(2)),
        Guid.Parse(reader.GetString(3)),
        Guid.Parse(reader.GetString(4)),
        Guid.Parse(reader.GetString(5)),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        DateTimeOffset.Parse(reader.GetString(9)),
        reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.GetInt32(12));

    private static LocalHardwareEvent ReadHardwareEvent(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Guid.Parse(reader.GetString(2)),
        Guid.Parse(reader.GetString(3)),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        DateTimeOffset.Parse(reader.GetString(7)));

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



    private LocalBackupResult CreateBackupFile(string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var source = _database.DatabasePath;
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var backupPath = Path.Combine(destinationDirectory, $"solidpos-poscore-{timestamp}.sqlite.bak");
        File.Copy(source, backupPath, overwrite: false);
        var file = new FileInfo(backupPath);
        return new LocalBackupResult(backupPath, file.Length, DateTimeOffset.UtcNow);
    }

    private static int ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private static int CountOutboxStatus(SqliteConnection connection, LocalOutboxStatus status)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM local_outbox_events WHERE status = $status;";
        command.Parameters.AddWithValue("$status", (int)status);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static DateTimeOffset? ReadOldestOutboxCreatedAt(SqliteConnection connection, LocalOutboxStatus status)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at_utc FROM local_outbox_events WHERE status = $status ORDER BY created_at_utc LIMIT 1;";
        command.Parameters.AddWithValue("$status", (int)status);
        var value = command.ExecuteScalar();
        return value is null || value == DBNull.Value ? null : DateTimeOffset.Parse((string)value);
    }

    private static DateTimeOffset? ReadOldestClaimedProcessingAt(SqliteConnection connection)
    {
        // Processing age begins when a worker claims a row, not when the
        // offline sale was originally created (possibly days earlier).
        // The fallback supports older InFlight rows without claim metadata.
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COALESCE(c.claimed_at_utc, e.created_at_utc)
FROM local_outbox_events e
LEFT JOIN local_sync_claims c ON c.outbox_event_id = e.id
WHERE e.status = $inFlight
ORDER BY COALESCE(c.claimed_at_utc, e.created_at_utc)
LIMIT 1;
""";
        command.Parameters.AddWithValue("$inFlight", (int)LocalOutboxStatus.InFlight);
        var value = command.ExecuteScalar();
        return value is null || value == DBNull.Value ? null : DateTimeOffset.Parse((string)value);
    }

    private static Guid RequireAuthorizedRecoverySession(
        SqliteConnection connection, SqliteTransaction transaction, Guid sessionId,
        Guid tenantId, Guid storeId, string permission, DateTimeOffset now)
    {
        if (sessionId == Guid.Empty)
            throw new UnauthorizedAccessException("A valid authenticated local session is required.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT s.user_id, s.created_at_utc, s.expires_at_utc,
       u.last_synced_at_utc, u.max_offline_hours
FROM local_sessions s
JOIN local_users u ON u.user_id = s.user_id
JOIN local_user_permissions p ON p.user_id = u.user_id
WHERE s.session_id = $session AND s.status = 'active'
  AND s.tenant_id = $tenant AND s.store_id = $store
  AND u.tenant_id = $tenant AND u.store_id = $store AND u.is_active = 1
  AND p.permission_code = $permission
LIMIT 1;
""";
        command.Parameters.AddWithValue("$session", sessionId.ToString());
        command.Parameters.AddWithValue("$tenant", tenantId.ToString());
        command.Parameters.AddWithValue("$store", storeId.ToString());
        command.Parameters.AddWithValue("$permission", permission);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new UnauthorizedAccessException("Local recovery session or permission is invalid for this scope.");
        var id = Guid.Parse(reader.GetString(0));
        var issued = DateTimeOffset.Parse(reader.GetString(1));
        var expires = DateTimeOffset.Parse(reader.GetString(2));
        if (issued > now || expires <= now)
            throw new UnauthorizedAccessException("Local recovery session has expired or is not yet valid.");

        // A still-active local session must NOT outlive the cached authority.
        // This mirrors LocalAuthService's offline-access window at the moment
        // of each recovery operation, including approval and final commit.
        var cacheSyncedAt = DateTimeOffset.Parse(reader.GetString(3));
        var maxOfflineHours = reader.GetInt32(4);
        if (maxOfflineHours <= 0 || cacheSyncedAt > now ||
            now - cacheSyncedAt > TimeSpan.FromHours(maxOfflineHours))
            throw new UnauthorizedAccessException("Local recovery authorization cache is expired or invalid.");
        return id;
    }

    private static void ConsumeRecoveryApproval(
        SqliteConnection connection, SqliteTransaction transaction,
        LocalSyncRecoveryRequest request, Guid executorId, DateTimeOffset now)
    {
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
SELECT approver_user_id, approver_session_id, approval_reference, expires_at_utc, consumed_at_utc,
       processing_timeout_seconds, max_events, reason
FROM local_sync_recovery_approvals
WHERE recovery_id = $id AND tenant_id = $tenant AND store_id = $store
  AND terminal_id = $terminal;
""";
        select.Parameters.AddWithValue("$id", request.RecoveryId.ToString());
        select.Parameters.AddWithValue("$tenant", request.TenantId.ToString());
        select.Parameters.AddWithValue("$store", request.StoreId.ToString());
        select.Parameters.AddWithValue("$terminal", request.TerminalId.ToString());
        Guid approverId;
        Guid approverSessionId;
        using (var reader = select.ExecuteReader())
        {
            if (!reader.Read())
                throw new UnauthorizedAccessException("No independently recorded approval exists for this recovery.");
            approverId = Guid.Parse(reader.GetString(0));
            approverSessionId = Guid.Parse(reader.GetString(1));
            if (approverId == executorId)
                throw new UnauthorizedAccessException("Recovery executor cannot approve their own operation.");
            if (!string.Equals(reader.GetString(2), request.ApprovalReference, StringComparison.Ordinal) ||
                reader.IsDBNull(3) || DateTimeOffset.Parse(reader.GetString(3)) <= now ||
                !reader.IsDBNull(4) ||
                reader.GetInt64(5) != Convert.ToInt64(request.ProcessingTimeout.TotalSeconds) ||
                reader.GetInt32(6) != request.MaxEvents ||
                !string.Equals(reader.GetString(7), request.Reason, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Recovery approval expired, consumed or does not match the operation.");
        }

        // A revoked approver or closed session invalidates even an unexpired approval.
        // Recheck permission under the same DB transaction before consuming it.
        Guid currentApprover = RequireAuthorizedRecoverySession(
            connection, transaction, approverSessionId, request.TenantId,
            request.StoreId, LocalSyncRecoveryPermissions.Approve, now);
        if (currentApprover != approverId)
            throw new UnauthorizedAccessException("Recovery approval signer no longer matches the active session.");

        using var consume = connection.CreateCommand();
        consume.Transaction = transaction;
        consume.CommandText = """
UPDATE local_sync_recovery_approvals
SET consumed_at_utc = $now
WHERE recovery_id = $id AND consumed_at_utc IS NULL;
""";
        consume.Parameters.AddWithValue("$now", now.ToString("O"));
        consume.Parameters.AddWithValue("$id", request.RecoveryId.ToString());
        if (consume.ExecuteNonQuery() != 1)
            throw new UnauthorizedAccessException("Recovery approval was already consumed.");
    }

    private static void InsertRecoveryJournal(SqliteConnection connection, Guid id, string operation, string status, string message, DateTimeOffset startedAtUtc, DateTimeOffset? completedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO local_recovery_journal (id, operation, status, message, started_at_utc, completed_at_utc) VALUES ($id, $operation, $status, $message, $startedAtUtc, $completedAtUtc);";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$operation", operation);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$startedAtUtc", startedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedAtUtc", completedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void UpdateRecoveryJournal(SqliteConnection connection, Guid id, string status, string message, DateTimeOffset completedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE local_recovery_journal SET status = $status, message = $message, completed_at_utc = $completedAtUtc WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$completedAtUtc", completedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static int ScalarInt(SqliteConnection connection, string sql)    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static LocalSession ReadLocalSession(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Guid.Parse(reader.GetString(2)),
        Guid.Parse(reader.GetString(3)),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        DateTimeOffset.Parse(reader.GetString(7)),
        DateTimeOffset.Parse(reader.GetString(8)),
        reader.GetString(9));

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
