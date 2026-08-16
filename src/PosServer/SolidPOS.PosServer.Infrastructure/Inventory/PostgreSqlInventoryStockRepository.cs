using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Contracts.Inventory;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Inventory;

public sealed class PostgreSqlInventoryStockRepository : IInventoryStockRepository
{
    private readonly string _connectionString;

    public PostgreSqlInventoryStockRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<IReadOnlyCollection<InventoryStockItemResponse>> GetCurrentStockAsync(Guid tenantId, Guid? storeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT tenant_id, store_id, product_id, variant_id, unit_id, quantity_on_hand
            FROM pos.inventory_stock
            WHERE tenant_id = @tenant_id
              AND (@store_id IS NULL OR store_id = @store_id)
            ORDER BY store_id, product_id, variant_id, unit_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.Add("store_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value = storeId.HasValue ? storeId.Value : (object)DBNull.Value;

        List<InventoryStockItemResponse> stock = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            stock.Add(new InventoryStockItemResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetFieldValue<decimal>(5).ToString(CultureInfo.InvariantCulture)));
        }

        return stock;
    }
}

