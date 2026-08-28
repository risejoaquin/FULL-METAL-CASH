using Npgsql;
using Xunit;

namespace SolidPOS.PosServer.IntegrationTests.PostgreSql;

public sealed class PostgreSqlMigrationTests
{
    [Fact]
    public async Task Migrations_apply_and_create_required_database_objects()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled)
        {
            return;
        }

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);

        object? tableCount = await PostgreSqlIntegrationTestHelper.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'pos' AND table_type = 'BASE TABLE';",
            CancellationToken.None);

        object? digitalReceiptsExists = await PostgreSqlIntegrationTestHelper.ExecuteScalarAsync(
            "SELECT to_regclass('pos.digital_receipts') IS NOT NULL;",
            CancellationToken.None);

        object? syncRuntimeColumnCount = await PostgreSqlIntegrationTestHelper.ExecuteScalarAsync(
            """
            SELECT count(*)
            FROM information_schema.columns
            WHERE table_schema = 'pos'
              AND table_name = 'sync_inbox_events'
              AND column_name IN ('batch_id', 'schema_version', 'sequence_number');
            """,
            CancellationToken.None);

        object? syncProcessingStatusAccepted = await PostgreSqlIntegrationTestHelper.ExecuteScalarAsync(
            """
            SELECT pg_get_constraintdef(oid) LIKE '%processing%'
            FROM pg_constraint
            WHERE conrelid = 'pos.sync_inbox_events'::regclass
              AND conname = 'sync_inbox_events_status_check';
            """,
            CancellationToken.None);

        object? rlsEnabledCount = await PostgreSqlIntegrationTestHelper.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'pos' AND c.relrowsecurity = true;",
            CancellationToken.None);

        object? tenantTablesWithoutRls = await PostgreSqlIntegrationTestHelper.ExecuteScalarAsync(
            """
            SELECT count(*)
            FROM information_schema.columns c
            JOIN pg_class pc ON pc.relname = c.table_name
            JOIN pg_namespace pn ON pn.oid = pc.relnamespace AND pn.nspname = c.table_schema
            WHERE c.table_schema = 'pos'
              AND c.column_name = 'tenant_id'
              AND pc.relkind = 'r'
              AND pc.relrowsecurity = false;
            """,
            CancellationToken.None);

        object? modifierInventoryColumns = await PostgreSqlIntegrationTestHelper.ExecuteScalarAsync(
            """
            SELECT count(*)
            FROM information_schema.columns
            WHERE table_schema = 'pos'
              AND table_name = 'modifiers'
              AND column_name IN ('inventory_behavior', 'consumption_quantity', 'consumption_unit_id', 'replaces_product_id', 'replaces_variant_id');
            """,
            CancellationToken.None);

        Assert.True(Convert.ToInt32(tableCount) >= 50);
        Assert.Equal(true, digitalReceiptsExists);
        Assert.Equal(3, Convert.ToInt32(syncRuntimeColumnCount));
        Assert.Equal(true, syncProcessingStatusAccepted);
        Assert.True(Convert.ToInt32(rlsEnabledCount) >= 50);
        Assert.Equal(0, Convert.ToInt32(tenantTablesWithoutRls));
        Assert.Equal(5, Convert.ToInt32(modifierInventoryColumns));
    }

    [Fact]
    public async Task Rls_policy_isolates_tenant_scoped_store_rows_when_forced()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled)
        {
            return;
        }

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);

        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationTestHelper.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        string setupSql = $"""
            SET search_path TO pos, public;

            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'solidpos_rls_test_reader') THEN
                    DROP OWNED BY solidpos_rls_test_reader;
                    DROP ROLE solidpos_rls_test_reader;
                END IF;
            END $$;

            CREATE ROLE solidpos_rls_test_reader;
            GRANT USAGE ON SCHEMA pos TO solidpos_rls_test_reader;
            GRANT SELECT ON pos.stores TO solidpos_rls_test_reader;

            INSERT INTO tenants (id, name) VALUES ('{tenantA}', 'Tenant A');
            INSERT INTO tenants (id, name) VALUES ('{tenantB}', 'Tenant B');

            INSERT INTO stores (tenant_id, code, name) VALUES ('{tenantA}', 'A-001', 'Store A');
            INSERT INTO stores (tenant_id, code, name) VALUES ('{tenantB}', 'B-001', 'Store B');

            SET ROLE solidpos_rls_test_reader;
            SELECT set_config('app.tenant_id', '{tenantA}', false);
            """;

        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, setupSql, CancellationToken.None);

        await using var command = new NpgsqlCommand("SELECT count(*) FROM pos.stores;", connection);
        object? visibleRows = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal(1, Convert.ToInt32(visibleRows));
    }

    [Fact]
    public async Task Seed_mvp_roles_creates_expected_roles_and_permissions_for_tenant()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled)
        {
            return;
        }

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);

        Guid tenantId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationTestHelper.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        string setupSql = $"""
            SET search_path TO pos, public;

            INSERT INTO tenants (id, name) VALUES ('{tenantId}', 'Seed Tenant');
            SELECT pos.seed_mvp_roles('{tenantId}');
            """;

        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, setupSql, CancellationToken.None);

        await using var command = new NpgsqlCommand($"""
            SELECT count(*)
            FROM roles r
            JOIN role_permissions rp ON rp.role_id = r.id AND rp.tenant_id = r.tenant_id
            WHERE r.tenant_id = '{tenantId}';
            """, connection);

        object? permissionRows = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.True(Convert.ToInt32(permissionRows) > 0);

        await using var excludedPermissionCommand = new NpgsqlCommand(
            "SELECT count(*) FROM permissions WHERE code = 'inventory.purchase';",
            connection);
        object? excludedPermissionRows = await excludedPermissionCommand.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal(0, Convert.ToInt32(excludedPermissionRows));
    }

    [Fact]
    public async Task Inventory_stock_view_sums_append_only_ledger_movements()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled)
        {
            return;
        }

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);

        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationTestHelper.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        string devSeedPath = Path.Combine(PostgreSqlIntegrationTestHelper.RepositoryRoot, "database", "postgresql", "004_seed_dev_auth.sql");
        string devSeedSql = await File.ReadAllTextAsync(devSeedPath, CancellationToken.None);
        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, devSeedSql, CancellationToken.None);

        string sourceEventId = Guid.NewGuid().ToString();
        string referenceId = Guid.NewGuid().ToString();
        string insertLedgerSql = $$"""
            SET search_path TO pos, public;
            SELECT set_config('app.tenant_id', '11111111-1111-1111-1111-111111111111', true);

            INSERT INTO inventory_ledger (
              tenant_id, store_id, product_id, movement_type, quantity_delta, unit_id,
              reference_type, reference_id, source_event_id, local_occurred_at, metadata
            )
            VALUES
              (
                '11111111-1111-1111-1111-111111111111',
                '22222222-2222-2222-2222-222222222222',
                '30000000-0000-0000-0000-000000000004',
                'sale_recipe_component',
                -18,
                '11000000-0000-0000-0000-000000000002',
                'sale',
                '{{referenceId}}',
                '{{sourceEventId}}',
                now(),
                '{"test": true}'::jsonb
              ),
              (
                '11111111-1111-1111-1111-111111111111',
                '22222222-2222-2222-2222-222222222222',
                '30000000-0000-0000-0000-000000000004',
                'sale_recipe_component',
                -18,
                '11000000-0000-0000-0000-000000000002',
                'sale',
                '{{referenceId}}',
                '{{Guid.NewGuid()}}',
                now(),
                '{"test": true}'::jsonb
              );
            """;

        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, insertLedgerSql, CancellationToken.None);

        await using var command = new NpgsqlCommand("""
            SELECT quantity_on_hand
            FROM pos.inventory_stock
            WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
              AND store_id = '22222222-2222-2222-2222-222222222222'
              AND product_id = '30000000-0000-0000-0000-000000000004'
              AND unit_id = '11000000-0000-0000-0000-000000000002';
            """, connection);

        object? quantityOnHand = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.NotNull(quantityOnHand);
        Assert.Equal(-36m, Convert.ToDecimal(quantityOnHand));
    }
    [Fact]
    public async Task Rls_rejects_cross_tenant_background_job_write_for_limited_role()
    {
        if (!PostgreSqlIntegrationTestHelper.IsEnabled)
        {
            return;
        }

        await PostgreSqlIntegrationTestHelper.ResetAndApplyMigrationsAsync(CancellationToken.None);

        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationTestHelper.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        string setupSql = $"""
            SET search_path TO pos, public;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'solidpos_ga08_rls_writer') THEN
                    DROP OWNED BY solidpos_ga08_rls_writer;
                    DROP ROLE solidpos_ga08_rls_writer;
                END IF;
            END $$;
            CREATE ROLE solidpos_ga08_rls_writer;
            GRANT USAGE ON SCHEMA pos TO solidpos_ga08_rls_writer;
            GRANT INSERT, SELECT ON pos.background_jobs TO solidpos_ga08_rls_writer;
            INSERT INTO tenants (id, name) VALUES ('{tenantA}', 'GA08 Tenant A');
            INSERT INTO tenants (id, name) VALUES ('{tenantB}', 'GA08 Tenant B');
            SET ROLE solidpos_ga08_rls_writer;
            SELECT set_config('app.tenant_id', '{tenantA}', false);
            """;

        await PostgreSqlIntegrationTestHelper.ExecuteSqlAsync(connection, setupSql, CancellationToken.None);

        await using var command = new NpgsqlCommand(
            $"INSERT INTO pos.background_jobs (tenant_id, job_type, payload) VALUES ('{tenantB}', 'ga08.cross_tenant_probe', '{{}}'::jsonb);",
            connection);

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

}
