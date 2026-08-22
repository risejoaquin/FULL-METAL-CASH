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

    public async Task<UpdateReleaseWriteResult?> CreateReleaseAsync(Guid tenantId, CreateUpdateReleaseRequest request, CancellationToken cancellationToken)
    {
        Guid[] targetTerminalIds = request.TargetTerminalIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (targetTerminalIds.Length > 0)
        {
            const string validateTargetsSql = """
                SELECT count(*)
                FROM pos.terminals
                WHERE tenant_id = @tenant_id
                  AND id = ANY(@target_terminal_ids)
                  AND status = 'active';
                """;
            await using var validateCommand = new NpgsqlCommand(validateTargetsSql, connection, transaction);
            validateCommand.Parameters.AddWithValue("tenant_id", tenantId);
            validateCommand.Parameters.AddWithValue("target_terminal_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, targetTerminalIds);
            long validTargetCount = (long)(await validateCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (validTargetCount != targetTerminalIds.LongLength)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new UpdateReleaseCreationConflictException(
                    "INVALID_TARGET_TERMINAL",
                    ["targetTerminalIds"]);
            }
        }

        const string insertSql = """
            WITH inserted AS (
              INSERT INTO pos.update_releases (
                tenant_id, version, channel, package_type, artifact_url, artifact_hash,
                signature, rollback_version, mandatory, universal_installer)
              VALUES (
                CASE WHEN @tenant_scoped THEN @tenant_id ELSE NULL END,
                @version, @channel, @package_type, @artifact_url, @artifact_hash,
                @signature, @rollback_version, @mandatory, @universal_installer)
              ON CONFLICT (tenant_id, channel, package_type, version) DO NOTHING
              RETURNING id, tenant_id, version, channel, package_type, artifact_url, artifact_hash,
                signature, rollback_version, mandatory, universal_installer, published_at, revoked_at
            )
            SELECT id, tenant_id, version, channel, package_type, artifact_url, artifact_hash,
              signature, rollback_version, mandatory, universal_installer, published_at, revoked_at, true AS was_created
            FROM inserted
            UNION ALL
            SELECT r.id, r.tenant_id, r.version, r.channel, r.package_type, r.artifact_url, r.artifact_hash,
              r.signature, r.rollback_version, r.mandatory, r.universal_installer, r.published_at, r.revoked_at, false AS was_created
            FROM pos.update_releases r
            WHERE NOT EXISTS (SELECT 1 FROM inserted)
              AND r.tenant_id IS NOT DISTINCT FROM CASE WHEN @tenant_scoped THEN @tenant_id ELSE NULL END
              AND r.channel = @channel
              AND r.package_type = @package_type
              AND r.version = @version
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(insertSql, connection, transaction);
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

        UpdateReleaseWriteRow? writeRow = await ReadSingleReleaseWriteAsync(command, cancellationToken);
        if (writeRow is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        UpdateReleaseResponse release = writeRow.Release;

        bool exactIdentityMatch =
            release.TenantId == (request.TenantScoped ? tenantId : null)
            && string.Equals(release.Version, request.Version, StringComparison.Ordinal)
            && string.Equals(release.Channel, request.Channel, StringComparison.Ordinal)
            && string.Equals(release.PackageType, request.PackageType, StringComparison.Ordinal)
            && string.Equals(release.ArtifactUrl, request.ArtifactUrl, StringComparison.Ordinal)
            && string.Equals(release.ArtifactHash, request.ArtifactHash, StringComparison.Ordinal)
            && string.Equals(release.Signature, request.Signature, StringComparison.Ordinal)
            && string.Equals(release.RollbackVersion, request.RollbackVersion, StringComparison.Ordinal)
            && release.Mandatory == request.Mandatory
            && release.UniversalInstaller == request.UniversalInstaller
            && release.RevokedAt is null;

        if (!exactIdentityMatch)
        {
            var conflicts = new List<string>();
            if (release.TenantId != (request.TenantScoped ? tenantId : null)) conflicts.Add("tenantScope");
            if (!string.Equals(release.Version, request.Version, StringComparison.Ordinal)) conflicts.Add("version");
            if (!string.Equals(release.Channel, request.Channel, StringComparison.Ordinal)) conflicts.Add("channel");
            if (!string.Equals(release.PackageType, request.PackageType, StringComparison.Ordinal)) conflicts.Add("packageType");
            if (!string.Equals(release.ArtifactUrl, request.ArtifactUrl, StringComparison.Ordinal)) conflicts.Add("artifactUrl");
            if (!string.Equals(release.ArtifactHash, request.ArtifactHash, StringComparison.Ordinal)) conflicts.Add("artifactHash");
            if (!string.Equals(release.Signature, request.Signature, StringComparison.Ordinal)) conflicts.Add("signature");
            if (!string.Equals(release.RollbackVersion, request.RollbackVersion, StringComparison.Ordinal)) conflicts.Add("rollbackVersion");
            if (release.Mandatory != request.Mandatory) conflicts.Add("mandatory");
            if (release.UniversalInstaller != request.UniversalInstaller) conflicts.Add("universalInstaller");
            if (release.RevokedAt is not null) conflicts.Add("revokedAt");

            await transaction.RollbackAsync(cancellationToken);
            throw new UpdateReleaseCreationConflictException(
                "RELEASE_IDENTITY_CONFLICT",
                conflicts);
        }

        int insertedTargetCount = 0;
        if (targetTerminalIds.Length > 0)
        {
            const string targetSql = """
                INSERT INTO pos.update_release_targets (release_id, tenant_id, terminal_id)
                SELECT @release_id, @tenant_id, unnest(@target_terminal_ids::uuid[])
                ON CONFLICT (release_id, terminal_id) DO NOTHING;
                """;
            await using var targetCommand = new NpgsqlCommand(targetSql, connection, transaction);
            targetCommand.Parameters.AddWithValue("release_id", release.Id);
            targetCommand.Parameters.AddWithValue("tenant_id", tenantId);
            targetCommand.Parameters.AddWithValue("target_terminal_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, targetTerminalIds);
            insertedTargetCount = await targetCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new UpdateReleaseWriteResult(release, writeRow.WasCreated, insertedTargetCount);
    }

    public async Task<UpdateCheckResponse> CheckForUpdateAsync(Guid tenantId, string currentVersion, string channel, string packageType, Guid? terminalId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.id, r.tenant_id, r.version, r.channel, r.package_type, r.artifact_url, r.artifact_hash,
              r.signature, r.rollback_version, r.mandatory, r.universal_installer, r.published_at, r.revoked_at
            FROM pos.update_releases r
            WHERE (r.tenant_id = @tenant_id OR r.tenant_id IS NULL)
              AND r.channel = @channel
              AND r.package_type = @package_type
              AND r.revoked_at IS NULL
              AND r.version <> @current_version
              AND (
                NOT EXISTS (SELECT 1 FROM pos.update_release_targets rt WHERE rt.release_id = r.id)
                OR (
                  @terminal_id IS NOT NULL
                  AND EXISTS (
                    SELECT 1
                    FROM pos.update_release_targets rt
                    JOIN pos.terminals t ON t.id = rt.terminal_id AND t.tenant_id = rt.tenant_id
                    WHERE rt.release_id = r.id
                      AND rt.tenant_id = @tenant_id
                      AND rt.terminal_id = @terminal_id
                      AND t.status = 'active'
                  )
                )
              )
            ORDER BY r.tenant_id NULLS LAST, r.published_at DESC
            LIMIT 1;
            """;

        await using var connection = await OpenTenantConnectionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("current_version", currentVersion);
        command.Parameters.AddWithValue("channel", channel);
        command.Parameters.AddWithValue("package_type", packageType);
        var terminalParameter = command.Parameters.Add("terminal_id", NpgsqlDbType.Uuid);
        terminalParameter.Value = terminalId.HasValue && terminalId.Value != Guid.Empty ? terminalId.Value : DBNull.Value;

        UpdateReleaseResponse? release = await ReadSingleReleaseAsync(command, cancellationToken);
        return new UpdateCheckResponse(
            release is not null,
            currentVersion,
            channel,
            packageType,
            release,
            release is null ? "already_current_no_release_or_outside_cohort" : "update_available",
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

    private static async Task<UpdateReleaseWriteRow?> ReadSingleReleaseWriteAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var release = new UpdateReleaseResponse(
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
        return new UpdateReleaseWriteRow(release, reader.GetBoolean(13));
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
    private sealed record UpdateReleaseWriteRow(UpdateReleaseResponse Release, bool WasCreated);

}
