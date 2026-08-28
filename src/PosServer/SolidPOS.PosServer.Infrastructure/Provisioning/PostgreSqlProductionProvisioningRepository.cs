using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Provisioning;
using SolidPOS.PosServer.Contracts.Provisioning;

namespace SolidPOS.PosServer.Infrastructure.Provisioning;

public sealed class PostgreSqlProductionProvisioningRepository : IProductionProvisioningRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string DemoOwnerEmail = "owner@solidpos.local";

    private readonly string _connectionString;

    public PostgreSqlProductionProvisioningRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<ProductionTenantBootstrapResponse?> BootstrapTenantAsync(
        ProductionTenantBootstrapRequest request,
        string adminPasswordHash,
        bool disableDemoUser,
        CancellationToken cancellationToken)
    {
        string? idempotencyKey = NormalizeOptional(request.IdempotencyKey);
        string requestHash = ComputeRequestHash(request);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, "SET search_path TO pos, extensions, public;", cancellationToken);

        if (idempotencyKey is not null)
        {
            ExistingBootstrapRun? existing = await TryReadExistingRunAsync(connection, transaction, idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }

                await transaction.CommitAsync(cancellationToken);
                return existing.Response;
            }
        }

        Guid tenantId = request.TenantId ?? Guid.NewGuid();
        await SetTenantSessionAsync(connection, transaction, tenantId, cancellationToken);

        const string tenantSql = """
            INSERT INTO pos.tenants (id, name, legal_name, status, timezone, currency)
            VALUES (@tenant_id, @name, @legal_name, 'active', @timezone, @currency)
            ON CONFLICT (id) DO UPDATE SET
              name = EXCLUDED.name,
              legal_name = EXCLUDED.legal_name,
              status = 'active',
              timezone = EXCLUDED.timezone,
              currency = EXCLUDED.currency,
              updated_at = now()
            RETURNING id;
            """;

        await using (var tenantCommand = new NpgsqlCommand(tenantSql, connection, transaction))
        {
            tenantCommand.Parameters.AddWithValue("tenant_id", tenantId);
            tenantCommand.Parameters.AddWithValue("name", request.TenantName.Trim());
            AddNullableText(tenantCommand, "legal_name", request.LegalName);
            tenantCommand.Parameters.AddWithValue("timezone", request.Timezone.Trim());
            tenantCommand.Parameters.AddWithValue("currency", request.Currency.Trim().ToUpperInvariant());
            await tenantCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string configSql = """
            INSERT INTO pos.tenant_configs (
              tenant_id,
              business_vertical,
              ui_layout,
              modules_enabled,
              branding,
              receipt_settings,
              hardware_profile,
              feature_flags
            )
            VALUES (
              @tenant_id,
              @business_vertical,
              @ui_layout,
              CAST(@modules_enabled AS jsonb),
              CAST(@branding AS jsonb),
              CAST(@receipt_settings AS jsonb),
              CAST(@hardware_profile AS jsonb),
              CAST(@feature_flags AS jsonb)
            )
            ON CONFLICT (tenant_id) DO UPDATE SET
              business_vertical = EXCLUDED.business_vertical,
              ui_layout = EXCLUDED.ui_layout,
              modules_enabled = EXCLUDED.modules_enabled,
              branding = EXCLUDED.branding,
              receipt_settings = EXCLUDED.receipt_settings,
              hardware_profile = EXCLUDED.hardware_profile,
              feature_flags = EXCLUDED.feature_flags,
              version = pos.tenant_configs.version + 1,
              updated_at = now();
            """;

        await using (var configCommand = new NpgsqlCommand(configSql, connection, transaction))
        {
            configCommand.Parameters.AddWithValue("tenant_id", tenantId);
            configCommand.Parameters.AddWithValue("business_vertical", request.BusinessVertical.Trim().ToLowerInvariant());
            configCommand.Parameters.AddWithValue("ui_layout", request.UiLayout.Trim().ToLowerInvariant());
            AddJson(configCommand, "modules_enabled", request.ModulesEnabled, "{\"pos\":true,\"dashboard\":true,\"builder\":true}");
            AddJson(configCommand, "branding", request.Branding, "{}");
            AddJson(configCommand, "receipt_settings", request.ReceiptSettings, "{}");
            AddJson(configCommand, "hardware_profile", request.HardwareProfile, "{}");
            AddJson(configCommand, "feature_flags", request.FeatureFlags, "{\"productionBootstrap\":true}");
            await configCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        Guid storeId;
        const string storeSql = """
            INSERT INTO pos.stores (tenant_id, code, name, address, phone, status)
            VALUES (@tenant_id, @code, @name, @address, @phone, 'active')
            ON CONFLICT (tenant_id, code) DO UPDATE SET
              name = EXCLUDED.name,
              address = EXCLUDED.address,
              phone = EXCLUDED.phone,
              status = 'active',
              updated_at = now()
            RETURNING id;
            """;

        await using (var storeCommand = new NpgsqlCommand(storeSql, connection, transaction))
        {
            storeCommand.Parameters.AddWithValue("tenant_id", tenantId);
            storeCommand.Parameters.AddWithValue("code", request.StoreCode.Trim().ToUpperInvariant());
            storeCommand.Parameters.AddWithValue("name", request.StoreName.Trim());
            AddNullableText(storeCommand, "address", request.StoreAddress);
            AddNullableText(storeCommand, "phone", request.StorePhone);
            object? result = await storeCommand.ExecuteScalarAsync(cancellationToken);
            storeId = result is Guid value ? value : Guid.Empty;
        }

        if (storeId == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using (var seedRolesCommand = new NpgsqlCommand("SELECT pos.seed_mvp_roles(@tenant_id);", connection, transaction))
        {
            seedRolesCommand.Parameters.AddWithValue("tenant_id", tenantId);
            await seedRolesCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        Guid adminUserId;
        const string userSql = """
            INSERT INTO pos.users (tenant_id, email, password_hash, full_name, status, password_reset_required)
            VALUES (@tenant_id, @email, @password_hash, @full_name, 'active', false)
            ON CONFLICT (tenant_id, email) DO UPDATE SET
              password_hash = EXCLUDED.password_hash,
              full_name = EXCLUDED.full_name,
              status = 'active',
              password_reset_required = false,
              login_failed_count = 0,
              locked_until = NULL,
              password_changed_at = now(),
              updated_at = now()
            RETURNING id;
            """;

        await using (var userCommand = new NpgsqlCommand(userSql, connection, transaction))
        {
            userCommand.Parameters.AddWithValue("tenant_id", tenantId);
            userCommand.Parameters.AddWithValue("email", request.AdminEmail.Trim().ToLowerInvariant());
            userCommand.Parameters.AddWithValue("password_hash", adminPasswordHash);
            userCommand.Parameters.AddWithValue("full_name", request.AdminFullName.Trim());
            object? result = await userCommand.ExecuteScalarAsync(cancellationToken);
            adminUserId = result is Guid value ? value : Guid.Empty;
        }

        if (adminUserId == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        const string ownerRoleSql = """
            INSERT INTO pos.user_roles (tenant_id, user_id, role_id)
            SELECT @tenant_id, @user_id, r.id
            FROM pos.roles r
            WHERE r.tenant_id = @tenant_id
              AND r.code = 'owner'
              AND r.deleted_at IS NULL
            ON CONFLICT DO NOTHING;
            """;

        await using (var roleCommand = new NpgsqlCommand(ownerRoleSql, connection, transaction))
        {
            roleCommand.Parameters.AddWithValue("tenant_id", tenantId);
            roleCommand.Parameters.AddWithValue("user_id", adminUserId);
            await roleCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string storeAccessSql = """
            INSERT INTO pos.user_store_access (tenant_id, user_id, store_id)
            VALUES (@tenant_id, @user_id, @store_id)
            ON CONFLICT DO NOTHING;
            """;

        await using (var storeAccessCommand = new NpgsqlCommand(storeAccessSql, connection, transaction))
        {
            storeAccessCommand.Parameters.AddWithValue("tenant_id", tenantId);
            storeAccessCommand.Parameters.AddWithValue("user_id", adminUserId);
            storeAccessCommand.Parameters.AddWithValue("store_id", storeId);
            await storeAccessCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await SeedOperationalDefaultsAsync(connection, transaction, tenantId, request.Currency.Trim().ToUpperInvariant(), cancellationToken);

        bool demoDisabled = false;
        if (disableDemoUser)
        {
            const string disableDemoSql = """
                UPDATE pos.users
                SET status = 'suspended',
                    locked_until = 'infinity'::timestamptz,
                    updated_at = now()
                WHERE tenant_id = @demo_tenant_id
                  AND email = @demo_email;

                UPDATE pos.refresh_tokens
                SET revoked_at = COALESCE(revoked_at, now()),
                    revoked_reason = COALESCE(revoked_reason, 'production_bootstrap_demo_user_disabled')
                WHERE tenant_id = @demo_tenant_id
                  AND user_id IN (
                    SELECT id FROM pos.users WHERE tenant_id = @demo_tenant_id AND email = @demo_email
                  )
                  AND revoked_at IS NULL;
                """;

            await using var disableCommand = new NpgsqlCommand(disableDemoSql, connection, transaction);
            disableCommand.Parameters.AddWithValue("demo_tenant_id", DemoTenantId);
            disableCommand.Parameters.AddWithValue("demo_email", DemoOwnerEmail);
            await disableCommand.ExecuteNonQueryAsync(cancellationToken);
            demoDisabled = true;
        }

        if (idempotencyKey is not null)
        {
            const string runSql = """
                INSERT INTO pos.production_bootstrap_runs (
                  idempotency_key,
                  request_hash,
                  tenant_id,
                  admin_user_id,
                  store_id,
                  status
                )
                VALUES (@idempotency_key, @request_hash, @tenant_id, @admin_user_id, @store_id, 'completed')
                ON CONFLICT (idempotency_key) DO NOTHING;
                """;

            await using var runCommand = new NpgsqlCommand(runSql, connection, transaction);
            runCommand.Parameters.AddWithValue("idempotency_key", idempotencyKey);
            runCommand.Parameters.AddWithValue("request_hash", requestHash);
            runCommand.Parameters.AddWithValue("tenant_id", tenantId);
            runCommand.Parameters.AddWithValue("admin_user_id", adminUserId);
            runCommand.Parameters.AddWithValue("store_id", storeId);
            await runCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await AppendAuditAsync(connection, transaction, tenantId, adminUserId, storeId, request, demoDisabled, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ProductionTenantBootstrapResponse(
            tenantId,
            storeId,
            adminUserId,
            request.TenantName.Trim(),
            request.AdminEmail.Trim().ToLowerInvariant(),
            request.StoreCode.Trim().ToUpperInvariant(),
            WasExisting: false,
            DemoUserDisabled: demoDisabled,
            Message: "Production tenant bootstrap completed.");
    }


    private static async Task SeedOperationalDefaultsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        string currency,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH unit_family AS (
              INSERT INTO pos.unit_families (tenant_id, code, name)
              VALUES (@tenant_id, 'quantity', 'Cantidad')
              ON CONFLICT (tenant_id, code) DO UPDATE SET name = EXCLUDED.name
              RETURNING id
            ), unit_unit AS (
              INSERT INTO pos.units (tenant_id, family_id, code, name, symbol, factor_to_base, is_base)
              SELECT @tenant_id, id, 'unit', 'Unidad', 'u', 1, true
              FROM unit_family
              ON CONFLICT (tenant_id, code) DO UPDATE SET
                name = EXCLUDED.name,
                symbol = EXCLUDED.symbol,
                factor_to_base = EXCLUDED.factor_to_base,
                is_base = EXCLUDED.is_base
              RETURNING id
            ), category AS (
              INSERT INTO pos.categories (tenant_id, name, sort_order, status)
              SELECT @tenant_id, 'Bebidas', 10, 'active'
              WHERE NOT EXISTS (
                SELECT 1 FROM pos.categories
                WHERE tenant_id = @tenant_id
                  AND name = 'Bebidas'
                  AND deleted_at IS NULL
              )
              RETURNING id
            ), selected_category AS (
              SELECT id FROM category
              UNION ALL
              SELECT id FROM pos.categories
              WHERE tenant_id = @tenant_id
                AND name = 'Bebidas'
                AND deleted_at IS NULL
              LIMIT 1
            ), product AS (
              INSERT INTO pos.products (
                tenant_id,
                category_id,
                sku,
                name,
                description,
                product_type,
                sale_unit_id,
                inventory_unit_id,
                is_sellable,
                is_stock_tracked,
                allow_negative_stock,
                tax_mode,
                status
              )
              SELECT
                @tenant_id,
                selected_category.id,
                'QSR-AMERICANO',
                'Americano 12oz',
                'Producto operativo inicial para pruebas productivas de POS.',
                'simple',
                unit_unit.id,
                unit_unit.id,
                true,
                false,
                true,
                'exempt',
                'active'
              FROM selected_category
              CROSS JOIN unit_unit
              ON CONFLICT (tenant_id, sku) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                category_id = EXCLUDED.category_id,
                sale_unit_id = EXCLUDED.sale_unit_id,
                inventory_unit_id = EXCLUDED.inventory_unit_id,
                is_sellable = true,
                is_stock_tracked = false,
                allow_negative_stock = true,
                tax_mode = 'exempt',
                status = 'active',
                deleted_at = NULL,
                updated_at = now()
              RETURNING id
            ), price_list AS (
              INSERT INTO pos.price_lists (tenant_id, code, name, currency, status)
              VALUES (@tenant_id, 'default', 'Lista general', @currency, 'active')
              ON CONFLICT (tenant_id, code) DO UPDATE SET
                name = EXCLUDED.name,
                currency = EXCLUDED.currency,
                status = 'active',
                deleted_at = NULL,
                updated_at = now()
              RETURNING id
            )
            INSERT INTO pos.product_prices (tenant_id, price_list_id, product_id, variant_id, price_cents, currency)
            SELECT @tenant_id, price_list.id, product.id, NULL, 4500, @currency
            FROM price_list
            CROSS JOIN product
            WHERE NOT EXISTS (
              SELECT 1 FROM pos.product_prices pp
              WHERE pp.tenant_id = @tenant_id
                AND pp.price_list_id = price_list.id
                AND pp.product_id = product.id
                AND pp.variant_id IS NULL
                AND pp.deleted_at IS NULL
            );

            INSERT INTO pos.payment_methods (tenant_id, code, name, method_type, status)
            VALUES
              (@tenant_id, 'cash', 'Efectivo', 'cash', 'active'),
              (@tenant_id, 'card_manual', 'Tarjeta manual', 'card', 'active'),
              (@tenant_id, 'transfer', 'Transferencia', 'transfer', 'active')
            ON CONFLICT (tenant_id, code) DO UPDATE SET
              name = EXCLUDED.name,
              method_type = EXCLUDED.method_type,
              status = 'active',
              deleted_at = NULL,
              updated_at = now();
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("currency", currency);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ExistingBootstrapRun?> TryReadExistingRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              r.request_hash,
              r.tenant_id,
              r.admin_user_id,
              r.store_id,
              t.name,
              u.email::text,
              s.code
            FROM pos.production_bootstrap_runs r
            JOIN pos.tenants t ON t.id = r.tenant_id
            JOIN pos.users u ON u.id = r.admin_user_id AND u.tenant_id = r.tenant_id
            JOIN pos.stores s ON s.id = r.store_id AND s.tenant_id = r.tenant_id
            WHERE r.idempotency_key = @idempotency_key
              AND r.status = 'completed';
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var response = new ProductionTenantBootstrapResponse(
            reader.GetGuid(1),
            reader.GetGuid(3),
            reader.GetGuid(2),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            WasExisting: true,
            DemoUserDisabled: false,
            Message: "Production tenant bootstrap already completed for this idempotency key.");

        return new ExistingBootstrapRun(reader.GetString(0), response);
    }

    private static async Task AppendAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid adminUserId,
        Guid storeId,
        ProductionTenantBootstrapRequest request,
        bool demoDisabled,
        CancellationToken cancellationToken)
    {
        var after = new
        {
            tenantId,
            adminUserId,
            storeId,
            tenantName = request.TenantName.Trim(),
            adminEmail = request.AdminEmail.Trim().ToLowerInvariant(),
            storeCode = request.StoreCode.Trim().ToUpperInvariant(),
            demoUserDisabled = demoDisabled
        };

        const string sql = """
            INSERT INTO pos.audit_events (
              tenant_id,
              action,
              entity_type,
              entity_id,
              after_data,
              trace_id
            )
            VALUES (
              @tenant_id,
              'production.bootstrap.completed',
              'tenant',
              @tenant_id,
              CAST(@after_data AS jsonb),
              'production-bootstrap'
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("after_data", JsonSerializer.Serialize(after, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetTenantSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant_id, true);", connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static void AddJson(NpgsqlCommand command, string name, JsonElement? value, string defaultJson)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Jsonb);
        parameter.Value = value.HasValue ? value.Value.GetRawText() : defaultJson;
    }

    private sealed record ExistingBootstrapRun(string RequestHash, ProductionTenantBootstrapResponse Response);

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ComputeRequestHash(ProductionTenantBootstrapRequest request)
    {
        var hashSource = new
        {
            request.TenantId,
            TenantName = request.TenantName.Trim(),
            AdminEmail = request.AdminEmail.Trim().ToLowerInvariant(),
            AdminFullName = request.AdminFullName.Trim(),
            StoreCode = request.StoreCode.Trim().ToUpperInvariant(),
            StoreName = request.StoreName.Trim(),
            LegalName = NormalizeOptional(request.LegalName),
            Timezone = request.Timezone.Trim(),
            Currency = request.Currency.Trim().ToUpperInvariant(),
            BusinessVertical = request.BusinessVertical.Trim().ToLowerInvariant(),
            UiLayout = request.UiLayout.Trim().ToLowerInvariant(),
            Branding = request.Branding?.GetRawText(),
            ReceiptSettings = request.ReceiptSettings?.GetRawText(),
            HardwareProfile = request.HardwareProfile?.GetRawText(),
            FeatureFlags = request.FeatureFlags?.GetRawText(),
            ModulesEnabled = request.ModulesEnabled?.GetRawText(),
            StoreAddress = NormalizeOptional(request.StoreAddress),
            StorePhone = NormalizeOptional(request.StorePhone),
            request.DisableDemoUser
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hashSource, JsonOptions));
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
