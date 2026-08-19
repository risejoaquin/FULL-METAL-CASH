using Microsoft.Extensions.Configuration;
using Npgsql;

namespace SolidPOS.PosServer.Infrastructure.PostgreSql;

public sealed record PostgreSqlConnectionStringResolution(
    string? ConnectionString,
    string Source,
    bool IsConfigured,
    bool IsValid,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static PostgreSqlConnectionStringResolution Missing()
    {
        return new PostgreSqlConnectionStringResolution(
            null,
            "none",
            false,
            false,
            "POSTGRES_CONNECTION_STRING_MISSING",
            "ConnectionStrings:Postgres or DATABASE_URL must be configured.");
    }

    public static PostgreSqlConnectionStringResolution Invalid(string source, string errorCode, string errorMessage)
    {
        return new PostgreSqlConnectionStringResolution(null, source, true, false, errorCode, errorMessage);
    }

    public static PostgreSqlConnectionStringResolution Valid(string connectionString, string source)
    {
        return new PostgreSqlConnectionStringResolution(connectionString, source, true, true);
    }
}

public static class PostgreSqlConnectionStringResolver
{
    public static PostgreSqlConnectionStringResolution Resolve(IConfiguration configuration)
    {
        string? configuredConnectionString = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return Normalize(configuredConnectionString, "ConnectionStrings:Postgres");
        }

        string? databaseUrl = configuration["DATABASE_URL"];
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            return Normalize(databaseUrl, "DATABASE_URL");
        }

        return PostgreSqlConnectionStringResolution.Missing();
    }

    private static PostgreSqlConnectionStringResolution Normalize(string rawConnectionString, string source)
    {
        string trimmed = rawConnectionString.Trim();
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertPostgresUri(trimmed, source);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(trimmed);
            if (string.IsNullOrWhiteSpace(builder.Host))
            {
                return PostgreSqlConnectionStringResolution.Invalid(
                    source,
                    "POSTGRES_CONNECTION_STRING_INVALID",
                    "PostgreSQL connection string is missing Host.");
            }

            if (string.IsNullOrWhiteSpace(builder.Database))
            {
                return PostgreSqlConnectionStringResolution.Invalid(
                    source,
                    "POSTGRES_CONNECTION_STRING_INVALID",
                    "PostgreSQL connection string is missing Database.");
            }

            if (string.IsNullOrWhiteSpace(builder.Username))
            {
                return PostgreSqlConnectionStringResolution.Invalid(
                    source,
                    "POSTGRES_CONNECTION_STRING_INVALID",
                    "PostgreSQL connection string is missing Username.");
            }

            return PostgreSqlConnectionStringResolution.Valid(builder.ConnectionString, source);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            return PostgreSqlConnectionStringResolution.Invalid(
                source,
                "POSTGRES_CONNECTION_STRING_INVALID",
                ex.Message);
        }
    }

    private static PostgreSqlConnectionStringResolution ConvertPostgresUri(string databaseUrl, string source)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out Uri? uri))
        {
            return PostgreSqlConnectionStringResolution.Invalid(
                source,
                "POSTGRES_DATABASE_URL_INVALID",
                "DATABASE_URL is not a valid absolute URI.");
        }

        if (!uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
        {
            return PostgreSqlConnectionStringResolution.Invalid(
                source,
                "POSTGRES_DATABASE_URL_INVALID",
                "DATABASE_URL must use postgres:// or postgresql:// scheme.");
        }

        string[] userInfoParts = uri.UserInfo.Split(':', 2);
        string username = Uri.UnescapeDataString(userInfoParts.ElementAtOrDefault(0) ?? string.Empty);
        string password = Uri.UnescapeDataString(userInfoParts.ElementAtOrDefault(1) ?? string.Empty);
        string database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return PostgreSqlConnectionStringResolution.Invalid(source, "POSTGRES_DATABASE_URL_INVALID", "DATABASE_URL is missing host.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return PostgreSqlConnectionStringResolution.Invalid(source, "POSTGRES_DATABASE_URL_INVALID", "DATABASE_URL is missing username.");
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            return PostgreSqlConnectionStringResolution.Invalid(source, "POSTGRES_DATABASE_URL_INVALID", "DATABASE_URL is missing database name.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Database = database,
            Username = username,
            Password = password,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Timeout = 15,
            CommandTimeout = 30
        };

        Dictionary<string, string> query = ParseQuery(uri.Query);
        if (query.TryGetValue("sslmode", out string? sslMode) && sslMode.Equals("require", StringComparison.OrdinalIgnoreCase))
        {
            builder.SslMode = SslMode.Require;
        }
        else if (query.TryGetValue("ssl", out string? ssl) && ssl.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            builder.SslMode = SslMode.Require;
        }

        return PostgreSqlConnectionStringResolution.Valid(builder.ConnectionString, source);
    }

    private static Dictionary<string, string> ParseQuery(string queryString)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        string trimmed = queryString.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return values;
        }

        foreach (string pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0]);
            string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            values[key] = value;
        }

        return values;
    }
}
