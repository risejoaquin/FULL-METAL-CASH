using System.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.AdminManagement;
using SolidPOS.PosServer.Contracts.AdminManagement;
using SolidPOS.PosServer.Contracts.Tenants;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.AdminManagement;

public sealed class PostgreSqlAdminManagementRepository : IAdminManagementRepository
{
    private readonly string _connectionString;

    public PostgreSqlAdminManagementRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<TenantCurrentResponse?> GetCurrentTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              t.id,
              t.name,
              t.legal_name,
              t.status,
              t.timezone,
              t.currency,
              t.created_at,
              t.updated_at,
              c.tenant_id,
              c.business_vertical,
              c.ui_layout,
              c.modules_enabled::text,
              c.branding::text,
              c.receipt_settings::text,
              c.hardware_profile::text,
              c.feature_flags::text,
              c.version,
              c.updated_at
            FROM pos.tenants t
            JOIN pos.tenant_configs c ON c.tenant_id = t.id
            WHERE t.id = @tenant_id
              AND t.deleted_at IS NULL;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        TenantConfigResponse settings = new(
            reader.GetGuid(8),
            reader.GetString(9),
            reader.GetString(10),
            JsonHelpers.Parse(reader.GetString(11)),
            JsonHelpers.Parse(reader.GetString(12)),
            JsonHelpers.Parse(reader.GetString(13)),
            JsonHelpers.Parse(reader.GetString(14)),
            JsonHelpers.Parse(reader.GetString(15)),
            reader.GetInt64(16),
            reader.GetFieldValue<DateTimeOffset>(17));

        return new TenantCurrentResponse(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            settings,
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7));
    }

    public async Task<IReadOnlyCollection<StoreResponse>> ListStoresAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tenant_id, code, name, address, phone, status, created_at, updated_at
            FROM pos.stores
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY name ASC;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return await ReadStoresAsync(command, cancellationToken);
    }

    public async Task<StoreResponse?> CreateStoreAsync(Guid tenantId, CreateStoreRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.stores (tenant_id, code, name, address, phone, status)
            VALUES (@tenant_id, @code, @name, @address, @phone, @status)
            ON CONFLICT (tenant_id, code) DO NOTHING
            RETURNING id, tenant_id, code, name, address, phone, status, created_at, updated_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("code", request.Code);
        command.Parameters.AddWithValue("name", request.Name);
        AddNullableText(command, "address", request.Address);
        AddNullableText(command, "phone", request.Phone);
        command.Parameters.AddWithValue("status", request.Status);
        return await ReadSingleStoreAsync(command, cancellationToken);
    }

    public async Task<StoreResponse?> UpdateStoreAsync(Guid tenantId, Guid storeId, UpdateStoreRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.stores
            SET
              code = COALESCE(@code, code),
              name = COALESCE(@name, name),
              address = COALESCE(@address, address),
              phone = COALESCE(@phone, phone),
              status = COALESCE(@status, status),
              updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @store_id
              AND deleted_at IS NULL
            RETURNING id, tenant_id, code, name, address, phone, status, created_at, updated_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        AddNullableText(command, "code", request.Code);
        AddNullableText(command, "name", request.Name);
        AddNullableText(command, "address", request.Address);
        AddNullableText(command, "phone", request.Phone);
        AddNullableText(command, "status", request.Status);
        return await ReadSingleStoreAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyCollection<UserResponse>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        string sql = UserSelectSql + "\n" + """
            WHERE u.tenant_id = @tenant_id
              AND u.deleted_at IS NULL
            ORDER BY u.created_at DESC;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return await ReadUsersAsync(command, cancellationToken);
    }

    public async Task<UserResponse?> CreateUserAsync(Guid tenantId, CreateUserRequest request, string passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string insertSql = """
            INSERT INTO pos.users (tenant_id, email, password_hash, full_name, status)
            VALUES (@tenant_id, @email, @password_hash, @full_name, @status)
            ON CONFLICT (tenant_id, email) DO NOTHING
            RETURNING id;
            """;

        await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);
        insertCommand.Parameters.AddWithValue("tenant_id", tenantId);
        insertCommand.Parameters.AddWithValue("email", request.Email);
        insertCommand.Parameters.AddWithValue("password_hash", passwordHash);
        insertCommand.Parameters.AddWithValue("full_name", request.FullName);
        insertCommand.Parameters.AddWithValue("status", request.Status);

        object? scalar = await insertCommand.ExecuteScalarAsync(cancellationToken);
        if (scalar is not Guid userId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        bool accessOk = await ReplaceUserAssignmentsAsync(connection, transaction, tenantId, userId, request.RoleIds, request.RoleCodes, request.StoreIds, cancellationToken);
        if (!accessOk)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        UserResponse? response = await GetUserAsync(connection, transaction, tenantId, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<UserResponse?> UpdateUserAsync(Guid tenantId, Guid userId, UpdateUserRequest request, string? passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string updateSql = """
            UPDATE pos.users
            SET
              email = COALESCE(@email, email),
              password_hash = COALESCE(@password_hash, password_hash),
              full_name = COALESCE(@full_name, full_name),
              status = COALESCE(@status, status),
              updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @user_id
              AND deleted_at IS NULL
            RETURNING id;
            """;

        await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
        updateCommand.Parameters.AddWithValue("tenant_id", tenantId);
        updateCommand.Parameters.AddWithValue("user_id", userId);
        AddNullableText(updateCommand, "email", request.Email);
        AddNullableText(updateCommand, "password_hash", passwordHash);
        AddNullableText(updateCommand, "full_name", request.FullName);
        AddNullableText(updateCommand, "status", request.Status);

        object? updated = await updateCommand.ExecuteScalarAsync(cancellationToken);
        if (updated is not Guid)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (request.RoleIds is not null || request.RoleCodes is not null || request.StoreIds is not null)
        {
            bool accessOk = await ReplaceUserAssignmentsAsync(connection, transaction, tenantId, userId, request.RoleIds, request.RoleCodes, request.StoreIds, cancellationToken);
            if (!accessOk)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        UserResponse? response = await GetUserAsync(connection, transaction, tenantId, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<IReadOnlyCollection<RoleResponse>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              r.id,
              r.tenant_id,
              r.code,
              r.name,
              r.is_system,
              COALESCE(array_agg(rp.permission_code ORDER BY rp.permission_code) FILTER (WHERE rp.permission_code IS NOT NULL), ARRAY[]::text[]) AS permissions,
              r.created_at,
              r.updated_at
            FROM pos.roles r
            LEFT JOIN pos.role_permissions rp ON rp.tenant_id = r.tenant_id AND rp.role_id = r.id
            WHERE r.tenant_id = @tenant_id
              AND r.deleted_at IS NULL
            GROUP BY r.id, r.tenant_id, r.code, r.name, r.is_system, r.created_at, r.updated_at
            ORDER BY r.code ASC;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        List<RoleResponse> roles = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new RoleResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetFieldValue<string[]>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return roles;
    }

    public async Task<IReadOnlyCollection<PermissionResponse>> ListPermissionsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT code, description
            FROM pos.permissions
            ORDER BY code ASC;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        List<PermissionResponse> permissions = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            permissions.Add(new PermissionResponse(reader.GetString(0), reader.GetString(1)));
        }

        return permissions;
    }

    private const string UserSelectSql = """
        SELECT
          u.id,
          u.tenant_id,
          u.email::text,
          u.full_name,
          u.status,
          COALESCE((
            SELECT array_agg(DISTINCT r.id)
            FROM pos.user_roles ur
            INNER JOIN pos.roles r ON r.tenant_id = ur.tenant_id AND r.id = ur.role_id AND r.deleted_at IS NULL
            WHERE ur.tenant_id = u.tenant_id
              AND ur.user_id = u.id
          ), ARRAY[]::uuid[]) AS role_ids,
          COALESCE((
            SELECT array_agg(DISTINCT r.code)
            FROM pos.user_roles ur
            INNER JOIN pos.roles r ON r.tenant_id = ur.tenant_id AND r.id = ur.role_id AND r.deleted_at IS NULL
            WHERE ur.tenant_id = u.tenant_id
              AND ur.user_id = u.id
          ), ARRAY[]::text[]) AS role_codes,
          COALESCE((
            SELECT array_agg(DISTINCT usa.store_id)
            FROM pos.user_store_access usa
            WHERE usa.tenant_id = u.tenant_id
              AND usa.user_id = u.id
          ), ARRAY[]::uuid[]) AS store_ids,
          u.created_at,
          u.updated_at
        FROM pos.users AS u
        """;

    private async Task<UserResponse?> GetUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        string sql = UserSelectSql + "\n" + """
            WHERE u.tenant_id = @tenant_id
              AND u.id = @user_id
              AND u.deleted_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("user_id", userId);

        IReadOnlyCollection<UserResponse> users = await ReadUsersAsync(command, cancellationToken);
        return users.FirstOrDefault();
    }

    private static async Task<IReadOnlyCollection<UserResponse>> ReadUsersAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        List<UserResponse> users = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new UserResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<Guid[]>(5),
                reader.GetFieldValue<string[]>(6),
                reader.GetFieldValue<Guid[]>(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return users;
    }

    private async Task<bool> ReplaceUserAssignmentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid>? roleIds,
        IReadOnlyCollection<string>? roleCodes,
        IReadOnlyCollection<Guid>? storeIds,
        CancellationToken cancellationToken)
    {
        if (roleIds is not null || roleCodes is not null)
        {
            Guid[] resolvedRoleIds = await ResolveRoleIdsAsync(connection, transaction, tenantId, roleIds, roleCodes, cancellationToken);
            if (((roleIds?.Count ?? 0) + (roleCodes?.Count ?? 0)) > 0 && resolvedRoleIds.Length == 0)
            {
                return false;
            }

            await ExecuteAsync(connection, transaction, "DELETE FROM pos.user_roles WHERE tenant_id = @tenant_id AND user_id = @user_id;", tenantId, userId, cancellationToken);

            foreach (Guid roleId in resolvedRoleIds)
            {
                await using var roleCommand = new NpgsqlCommand("INSERT INTO pos.user_roles (tenant_id, user_id, role_id) VALUES (@tenant_id, @user_id, @role_id) ON CONFLICT DO NOTHING;", connection, transaction);
                roleCommand.Parameters.AddWithValue("tenant_id", tenantId);
                roleCommand.Parameters.AddWithValue("user_id", userId);
                roleCommand.Parameters.AddWithValue("role_id", roleId);
                await roleCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (storeIds is not null)
        {
            Guid[] requestedStoreIds = storeIds.Distinct().ToArray();
            if (requestedStoreIds.Length > 0 && !await StoresExistAsync(connection, transaction, tenantId, requestedStoreIds, cancellationToken))
            {
                return false;
            }

            await ExecuteAsync(connection, transaction, "DELETE FROM pos.user_store_access WHERE tenant_id = @tenant_id AND user_id = @user_id;", tenantId, userId, cancellationToken);

            foreach (Guid storeId in requestedStoreIds)
            {
                await using var storeCommand = new NpgsqlCommand("INSERT INTO pos.user_store_access (tenant_id, user_id, store_id) VALUES (@tenant_id, @user_id, @store_id) ON CONFLICT DO NOTHING;", connection, transaction);
                storeCommand.Parameters.AddWithValue("tenant_id", tenantId);
                storeCommand.Parameters.AddWithValue("user_id", userId);
                storeCommand.Parameters.AddWithValue("store_id", storeId);
                await storeCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        return true;
    }

    private static async Task<Guid[]> ResolveRoleIdsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, IReadOnlyCollection<Guid>? roleIds, IReadOnlyCollection<string>? roleCodes, CancellationToken cancellationToken)
    {
        Guid[] ids = roleIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
        string[] codes = roleCodes?.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim().ToLowerInvariant()).Distinct().ToArray() ?? Array.Empty<string>();

        const string sql = """
            SELECT id
            FROM pos.roles
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
              AND ((@role_ids_length > 0 AND id = ANY(@role_ids))
                   OR (@role_codes_length > 0 AND code = ANY(@role_codes)));
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("role_ids_length", ids.Length);
        command.Parameters.Add("role_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids;
        command.Parameters.AddWithValue("role_codes_length", codes.Length);
        command.Parameters.Add("role_codes", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = codes;

        List<Guid> resolved = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resolved.Add(reader.GetGuid(0));
        }

        return resolved.Distinct().ToArray();
    }

    private static async Task<bool> StoresExistAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid[] storeIds, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM pos.stores
            WHERE tenant_id = @tenant_id
              AND id = ANY(@store_ids)
              AND deleted_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.Add("store_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = storeIds;
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        long count = result is long value ? value : 0;
        return count == storeIds.Length;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenTenantConnectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        return connection;
    }

    private static async Task<IReadOnlyCollection<StoreResponse>> ReadStoresAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        List<StoreResponse> stores = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            stores.Add(ReadStore(reader));
        }

        return stores;
    }

    private static async Task<StoreResponse?> ReadSingleStoreAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadStore(reader) : null;
    }

    private static StoreResponse ReadStore(NpgsqlDataReader reader)
    {
        return new StoreResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }
}

file static class JsonHelpers
{
    public static System.Text.Json.JsonElement Parse(string json)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
