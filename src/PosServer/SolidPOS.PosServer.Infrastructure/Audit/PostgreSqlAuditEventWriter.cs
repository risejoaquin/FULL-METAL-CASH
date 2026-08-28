using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Audit;

public sealed class PostgreSqlAuditEventWriter : IAuditEventWriter
{
    private readonly string _connectionString;
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PostgreSqlAuditEventWriter> _logger;

    public PostgreSqlAuditEventWriter(
        IConfiguration configuration,
        ITenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PostgreSqlAuditEventWriter> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task AppendAsync(
        Guid tenantId,
        string action,
        string entityType,
        Guid? entityId,
        JsonElement? beforeData,
        JsonElement? afterData,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.audit_events (
              tenant_id,
              actor_user_id,
              terminal_id,
              action,
              entity_type,
              entity_id,
              before_data,
              after_data,
              ip_address,
              user_agent,
              trace_id
            )
            VALUES (
              @tenant_id,
              @actor_user_id,
              @terminal_id,
              @action,
              @entity_type,
              @entity_id,
              CAST(@before_data AS jsonb),
              CAST(@after_data AS jsonb),
              @ip_address,
              @user_agent,
              @trace_id
            );
            """;

        HttpContext? httpContext = _httpContextAccessor.HttpContext;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "actor_user_id", _tenantContext.UserId);
        AddNullableUuid(command, "terminal_id", _tenantContext.TerminalId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        AddNullableUuid(command, "entity_id", entityId);
        AddNullableJson(command, "before_data", beforeData);
        AddNullableJson(command, "after_data", afterData);
        AddNullableIp(command, "ip_address", httpContext?.Connection.RemoteIpAddress);
        object userAgentValue = httpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : DBNull.Value;
        object traceIdValue = httpContext?.TraceIdentifier is { Length: > 0 } traceId ? traceId : DBNull.Value;
        command.Parameters.AddWithValue("user_agent", userAgentValue);
        command.Parameters.AddWithValue("trace_id", traceIdValue);

        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation(
            "Audit event appended for tenant {TenantId}: {Action} {EntityType} {EntityId}",
            tenantId,
            action,
            entityType,
            entityId);
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableJson(NpgsqlCommand command, string name, JsonElement? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Jsonb);
        parameter.Value = value.HasValue ? value.Value.GetRawText() : DBNull.Value;
    }

    private static void AddNullableIp(NpgsqlCommand command, string name, IPAddress? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Inet);
        parameter.Value = value is null ? DBNull.Value : value;
    }
}
