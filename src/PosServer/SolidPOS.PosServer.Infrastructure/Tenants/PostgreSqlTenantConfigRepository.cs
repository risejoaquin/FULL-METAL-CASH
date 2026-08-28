using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Tenants;
using SolidPOS.PosServer.Contracts.Tenants;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Tenants;

public sealed class PostgreSqlTenantConfigRepository : ITenantConfigRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public PostgreSqlTenantConfigRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<TenantConfigResponse?> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              tenant_id,
              business_vertical,
              ui_layout,
              modules_enabled::text,
              branding::text,
              receipt_settings::text,
              hardware_profile::text,
              feature_flags::text,
              version,
              updated_at
            FROM pos.tenant_configs
            WHERE tenant_id = @tenant_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadTenantConfig(reader);
    }

    public async Task<TenantConfigResponse?> UpsertAsync(Guid tenantId, UpdateTenantConfigRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH existing AS (
              SELECT version
              FROM pos.tenant_configs
              WHERE tenant_id = @tenant_id
            ),
            upserted AS (
              INSERT INTO pos.tenant_configs (
                tenant_id,
                business_vertical,
                ui_layout,
                modules_enabled,
                branding,
                receipt_settings,
                hardware_profile,
                feature_flags,
                version
              )
              SELECT
                @tenant_id,
                @business_vertical,
                @ui_layout,
                @modules_enabled::jsonb,
                @branding::jsonb,
                @receipt_settings::jsonb,
                @hardware_profile::jsonb,
                @feature_flags::jsonb,
                1
              WHERE @expected_version IS NULL OR EXISTS (SELECT 1 FROM existing WHERE version = @expected_version)
              ON CONFLICT (tenant_id)
              DO UPDATE SET
                business_vertical = EXCLUDED.business_vertical,
                ui_layout = EXCLUDED.ui_layout,
                modules_enabled = EXCLUDED.modules_enabled,
                branding = EXCLUDED.branding,
                receipt_settings = EXCLUDED.receipt_settings,
                hardware_profile = EXCLUDED.hardware_profile,
                feature_flags = EXCLUDED.feature_flags,
                version = pos.tenant_configs.version + 1,
                updated_at = now()
              WHERE @expected_version IS NULL OR pos.tenant_configs.version = @expected_version
              RETURNING
                tenant_id,
                business_vertical,
                ui_layout,
                modules_enabled::text,
                branding::text,
                receipt_settings::text,
                hardware_profile::text,
                feature_flags::text,
                version,
                updated_at
            )
            SELECT * FROM upserted;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("business_vertical", request.BusinessVertical.Trim());
        command.Parameters.AddWithValue("ui_layout", request.UiLayout.Trim());
        command.Parameters.Add("modules_enabled", NpgsqlDbType.Jsonb).Value = ToJson(request.ModulesEnabled);
        command.Parameters.Add("branding", NpgsqlDbType.Jsonb).Value = ToJson(request.Branding);
        command.Parameters.Add("receipt_settings", NpgsqlDbType.Jsonb).Value = ToJson(request.ReceiptSettings);
        command.Parameters.Add("hardware_profile", NpgsqlDbType.Jsonb).Value = ToJson(request.HardwareProfile);
        command.Parameters.Add("feature_flags", NpgsqlDbType.Jsonb).Value = ToJson(request.FeatureFlags);
        command.Parameters.Add("expected_version", NpgsqlDbType.Bigint).Value = request.ExpectedVersion.HasValue ? request.ExpectedVersion.Value : (object)DBNull.Value;

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadTenantConfig(reader);
    }

    private static TenantConfigResponse ReadTenantConfig(NpgsqlDataReader reader)
    {
        return new TenantConfigResponse(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            ParseJson(reader.GetString(3)),
            ParseJson(reader.GetString(4)),
            ParseJson(reader.GetString(5)),
            ParseJson(reader.GetString(6)),
            ParseJson(reader.GetString(7)),
            reader.GetInt64(8),
            reader.GetFieldValue<DateTimeOffset>(9));
    }

    private static JsonElement ParseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string ToJson(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.Undefined
            ? "{}"
            : JsonSerializer.Serialize(element, JsonOptions);
    }
}
