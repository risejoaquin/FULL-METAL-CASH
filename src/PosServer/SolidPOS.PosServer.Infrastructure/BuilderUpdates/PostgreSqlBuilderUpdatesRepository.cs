using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.BuilderUpdates;
using SolidPOS.PosServer.Contracts.BuilderUpdates;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.BuilderUpdates;

public sealed class PostgreSqlBuilderUpdatesRepository : IBuilderUpdatesRepository
{
    private readonly string _connectionString;

    public PostgreSqlBuilderUpdatesRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<IReadOnlyCollection<BuilderProjectResponse>> ListProjectsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tenant_id, name, target_store_id, package_type, config::text, branding::text, created_at, updated_at
            FROM pos.builder_projects
            WHERE tenant_id = @tenant_id
              AND deleted_at IS NULL
            ORDER BY updated_at DESC, created_at DESC;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return await ReadProjectsAsync(command, cancellationToken);
    }

    public async Task<BuilderProjectResponse?> CreateProjectAsync(Guid tenantId, CreateBuilderProjectRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.builder_projects (tenant_id, name, target_store_id, package_type, config, branding)
            SELECT @tenant_id, @name, @target_store_id, @package_type, @config, @branding
            WHERE @target_store_id IS NULL
               OR EXISTS (
                    SELECT 1
                    FROM pos.stores
                    WHERE id = @target_store_id
                      AND tenant_id = @tenant_id
                      AND deleted_at IS NULL)
            RETURNING id, tenant_id, name, target_store_id, package_type, config::text, branding::text, created_at, updated_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", request.Name);
        AddNullableUuid(command, "target_store_id", request.TargetStoreId);
        command.Parameters.AddWithValue("package_type", request.PackageType);
        command.Parameters.Add("config", NpgsqlDbType.Jsonb).Value = SerializeJson(request.Config);
        command.Parameters.Add("branding", NpgsqlDbType.Jsonb).Value = SerializeJson(request.Branding);
        return await ReadSingleProjectAsync(command, cancellationToken);
    }

    public async Task<BuilderBuildResponse?> CreateBuildAsync(Guid tenantId, Guid projectId, CreateBuilderBuildRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.builder_builds (
              tenant_id, project_id, status, app_version, channel, universal_installer,
              artifact_url, artifact_hash, signature, finished_at)
            SELECT
              @tenant_id, @project_id, 'succeeded', @app_version, @channel, @universal_installer,
              @artifact_url, @artifact_hash, @signature,
              CASE WHEN @artifact_url IS NULL THEN NULL ELSE now() END
            WHERE EXISTS (
                SELECT 1
                FROM pos.builder_projects
                WHERE id = @project_id
                  AND tenant_id = @tenant_id
                  AND deleted_at IS NULL)
            RETURNING id, tenant_id, project_id, status, app_version, channel, universal_installer,
              artifact_url, artifact_hash, signature, error_message, created_at, finished_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("app_version", request.AppVersion);
        command.Parameters.AddWithValue("channel", request.Channel);
        command.Parameters.AddWithValue("universal_installer", request.UniversalInstaller);
        AddNullableText(command, "artifact_url", request.ArtifactUrl);
        AddNullableText(command, "artifact_hash", request.ArtifactHash);
        AddNullableText(command, "signature", request.Signature);
        return await ReadSingleBuildAsync(command, cancellationToken);
    }

    public Task<IReadOnlyCollection<UpdateChannelResponse>> ListChannelsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<UpdateChannelResponse> channels =
        [
            new("stable", "Stable", "production", true, true),
            new("beta", "Beta", "pre-release", true, false),
            new("internal", "Internal", "internal", true, false)
        ];

        return Task.FromResult(channels);
    }

    public async Task<UpdateReleaseResponse?> CreateReleaseAsync(Guid tenantId, CreateUpdateReleaseRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.update_releases (
              tenant_id, version, channel, package_type, artifact_url, artifact_hash,
              signature, rollback_version, mandatory, universal_installer)
            VALUES (
              CASE WHEN @tenant_scoped THEN @tenant_id ELSE NULL END,
              @version, @channel, @package_type, @artifact_url, @artifact_hash,
              @signature, @rollback_version, @mandatory, @universal_installer)
            ON CONFLICT (tenant_id, channel, package_type, version) DO NOTHING
            RETURNING id, tenant_id, version, channel, package_type, artifact_url, artifact_hash,
              signature, rollback_version, mandatory, universal_installer, published_at, revoked_at;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("tenant_scoped", request.TenantScoped);
        command.Parameters.AddWithValue("version", request.Version);
        command.Parameters.AddWithValue("channel", request.Channel);
        command.Parameters.AddWithValue("package_type", request.PackageType);
        command.Parameters.AddWithValue("artifact_url", request.ArtifactUrl);
        command.Parameters.AddWithValue("artifact_hash", request.ArtifactHash);
        command.Parameters.AddWithValue("signature", request.Signature);
        AddNullableText(command, "rollback_version", request.RollbackVersion);
        command.Parameters.AddWithValue("mandatory", request.Mandatory);
        command.Parameters.AddWithValue("universal_installer", request.UniversalInstaller);
        return await ReadSingleReleaseAsync(command, cancellationToken);
    }

    public async Task<UpdateCheckResponse> CheckForUpdateAsync(Guid tenantId, string currentVersion, string channel, string packageType, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tenant_id, version, channel, package_type, artifact_url, artifact_hash,
              signature, rollback_version, mandatory, universal_installer, published_at, revoked_at
            FROM pos.update_releases
            WHERE (tenant_id = @tenant_id OR tenant_id IS NULL)
              AND channel = @channel
              AND package_type = @package_type
              AND revoked_at IS NULL
              AND version <> @current_version
            ORDER BY tenant_id NULLS LAST, published_at DESC
            LIMIT 1;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("current_version", currentVersion);
        command.Parameters.AddWithValue("channel", channel);
        command.Parameters.AddWithValue("package_type", packageType);

        UpdateReleaseResponse? release = await ReadSingleReleaseAsync(command, cancellationToken);
        return new UpdateCheckResponse(
            release is not null,
            currentVersion,
            channel,
            packageType,
            release,
            release is null ? "already_current_or_no_release" : "update_available",
            "preserve_local_branding");
    }

    private async Task<NpgsqlConnection> OpenTenantConnectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);
        return connection;
    }

    private static async Task<IReadOnlyCollection<BuilderProjectResponse>> ReadProjectsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        List<BuilderProjectResponse> projects = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(ReadProject(reader));
        }

        return projects;
    }

    private static async Task<BuilderProjectResponse?> ReadSingleProjectAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProject(reader) : null;
    }

    private static BuilderProjectResponse ReadProject(NpgsqlDataReader reader)
    {
        return new BuilderProjectResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4),
            ParseJson(reader.GetString(5)),
            ParseJson(reader.GetString(6)),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    private static async Task<BuilderBuildResponse?> ReadSingleBuildAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new BuilderBuildResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));
    }

    private static async Task<UpdateReleaseResponse?> ReadSingleReleaseAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UpdateReleaseResponse(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetBoolean(9),
            reader.GetBoolean(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));
    }

    private static JsonElement ParseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string SerializeJson(JsonElement? json)
    {
        return json?.GetRawText() ?? "{}";
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value is null || value == Guid.Empty ? DBNull.Value : value.Value;
    }
}
